using Serilog;
using SharpAdbClient;
using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Channels;

namespace ScrcpyClient
{
    internal readonly record struct VideoStreamMetadata(uint CodecId, int Width, int Height);
    internal readonly record struct VideoPacketHeader(bool IsConfigPacket, bool IsKeyFrame, long PresentationTimestampUs, int PacketSize);

    public class Scrcpy
    {
        private const string ToolsDirectoryName = "tools";
        private const string ScrcpyServerFileName = "scrcpy-server";

        public string DeviceName { get; private set; } = "";
        public int Width { get; internal set; }
        public int Height { get; internal set; }
        public long Bitrate { get; set; } = 8000000;
        public string ScrcpyServerFile { get; set; } = GetDefaultScrcpyServerFile(AppContext.BaseDirectory);

        public bool Connected { get; private set; }
        public VideoStreamDecoder VideoStreamDecoder { get; }

        private Thread? videoThread;
        private Thread? controlThread;
        private TcpClient? videoClient;
        private TcpClient? controlClient;
        private TcpListener? listener;
        private CancellationTokenSource? cts;

        private AdbClient adb;
        private readonly DeviceData device;
        private readonly Channel<IControlMessage> controlChannel = Channel.CreateUnbounded<IControlMessage>();
        private static readonly ArrayPool<byte> pool = ArrayPool<byte>.Shared;
        private static readonly ILogger log = Log.ForContext<Scrcpy>();

        private const string DesktopSocketScid = "01234567";
        private const string DesktopSocketName = "scrcpy_" + DesktopSocketScid;
        private const ulong ConfigPacketFlagMask = 1UL << 63;
        private const ulong KeyFrameFlagMask = 1UL << 62;
        private const ulong PresentationTimestampMask = (1UL << 62) - 1;

        public Scrcpy(DeviceData device, VideoStreamDecoder? videoStreamDecoder = null)
        {
            adb = new AdbClient();
            this.device = device;
            DeviceName = device.Serial;
            VideoStreamDecoder = videoStreamDecoder ?? new VideoStreamDecoder();
            VideoStreamDecoder.Scrcpy = this;
        }

        internal static string GetDefaultScrcpyServerFile(string baseDirectory)
        {
            var fullBaseDirectory = Path.GetFullPath(baseDirectory);
            return Path.Combine(fullBaseDirectory, ToolsDirectoryName, ScrcpyServerFileName);
        }

        //public void SetDecoder(VideoStreamDecoder videoStreamDecoder)
        //{
        //    this.videoStreamDecoder = videoStreamDecoder;
        //    this.videoStreamDecoder.Scrcpy = this;
        //}

        public void Start(long timeoutMs = 5000)
        {
            if (Connected)
                throw new Exception("Already connected.");

            AdbServerBootstrap.EnsureRunning(ScrcpyServerFile, log);

            MobileServerSetup();

            listener = new TcpListener(IPAddress.Any, 27183);
            listener.Start();

            MobileServerStart();

            int waitTimeMs = 0;
            while (!listener.Pending())
            {
                Thread.Sleep(10);
                waitTimeMs += 10;

                if (waitTimeMs > timeoutMs)
                    throw new Exception("Timeout while waiting for server to connect.");
            }

            videoClient = listener.AcceptTcpClient();
            log.Information("Video socket connected.");

            if (!listener.Pending())
                throw new Exception("Server is not sending a second connection request. Is 'control' disabled?");

            controlClient = listener.AcceptTcpClient();
            log.Information("Control socket connected.");

            ReadVideoStreamMetadata();

            cts = new CancellationTokenSource();

            videoThread = new Thread(VideoMain) { Name = "ScrcpyNet Video" };
            controlThread = new Thread(ControllerMain) { Name = "ScrcpyNet Controller" };

            videoThread.Start();
            controlThread.Start();

            Connected = true;

            // ADB forward/reverse is not needed anymore.
            MobileServerCleanup();
        }

        public void Stop()
        {
            if (!Connected)
                throw new Exception("Not connected.");

            cts?.Cancel();

            videoThread?.Join();
            controlThread?.Join();
            listener?.Stop();
        }

        public void SendControlCommand(IControlMessage msg)
        {
            if (controlClient == null)
                log.Warning("SendControlCommand() called, but controlClient is null.");
            else
                controlChannel.Writer.TryWrite(msg);
        }

        private void ReadVideoStreamMetadata()
        {
            if (videoClient == null)
                throw new Exception("Can't read video stream metadata when videoClient is null.");

            var stream = videoClient.GetStream();
            stream.ReadTimeout = 2000;

            var metadataBuffer = pool.Rent(12);
            try
            {
                ReadExactly(stream, metadataBuffer, 0, 12);
                var metadata = ParseVideoStreamMetadata(metadataBuffer.AsSpan(0, 12));

                Width = metadata.Width;
                Height = metadata.Height;

                log.Information("Video stream metadata: codec=0x{CodecId:X8}, initialSize={Width}x{Height}, device={DeviceName}", metadata.CodecId, Width, Height, DeviceName);
            }
            finally
            {
                pool.Return(metadataBuffer);
            }
        }

        internal static VideoStreamMetadata ParseVideoStreamMetadata(ReadOnlySpan<byte> metadata)
        {
            if (metadata.Length != 12)
                throw new ArgumentException("Video stream metadata must be exactly 12 bytes.", nameof(metadata));

            var codecId = BinaryPrimitives.ReadUInt32BigEndian(metadata[..4]);
            var width = checked((int)BinaryPrimitives.ReadUInt32BigEndian(metadata[4..8]));
            var height = checked((int)BinaryPrimitives.ReadUInt32BigEndian(metadata[8..12]));
            return new VideoStreamMetadata(codecId, width, height);
        }

        private void VideoMain()
        {
            // Both of these should never happen.
            if (videoClient == null) throw new Exception("videoClient is null.");
            if (cts == null) throw new Exception("cts is null.");

            var videoStream = videoClient.GetStream();
            videoStream.ReadTimeout = 2000;

            var metaBuf = pool.Rent(12);

            Stopwatch sw = new();

            while (!cts.Token.IsCancellationRequested)
            {
                try
                {
                    ReadExactly(videoStream, metaBuf, 0, 12);
                }
                catch (IOException ex)
                {
                    if (ex.InnerException is SocketException x && x.SocketErrorCode == SocketError.TimedOut)
                        continue;
                    throw;
                }

                sw.Restart();

                var header = ParseVideoPacketHeader(metaBuf.AsSpan(0, 12));
                var presentationTimeUs = header.PresentationTimestampUs;
                var packetSize = header.PacketSize;

                var packetBuf = pool.Rent(packetSize);
                try
                {
                    ReadExactly(videoStream, packetBuf, 0, packetSize, cts.Token);

                    if (!cts.Token.IsCancellationRequested)
                    {
                        VideoStreamDecoder?.Decode(packetBuf[..packetSize], presentationTimeUs);
                        log.Verbose("Received {PacketKind} packet ({PacketSize} bytes, keyFrame={IsKeyFrame}) and decoded it in {ElapsedMilliseconds} ms", header.IsConfigPacket ? "config" : "video", packetSize, header.IsKeyFrame, sw.ElapsedMilliseconds);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                finally
                {
                    pool.Return(packetBuf);
                }

                sw.Stop();
            }
        }

        private async void ControllerMain()
        {
            if (controlClient == null) throw new Exception("controlClient is null.");
            if (cts == null) throw new Exception("cts is null.");

            var stream = controlClient.GetStream();

            try
            {
                await foreach (var cmd in controlChannel.Reader.ReadAllAsync(cts.Token))
                {
                    ControllerSend(stream, cmd);
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        private void ControllerSend(NetworkStream stream, IControlMessage cmd)
        {
            log.Debug("Sending control message: {@ControlMessage}", cmd.Type);
            var bytes = cmd.ToBytes();
            stream.Write(bytes);
        }

        internal static VideoPacketHeader ParseVideoPacketHeader(ReadOnlySpan<byte> header)
        {
            if (header.Length != 12)
                throw new ArgumentException("Video packet header must be exactly 12 bytes.", nameof(header));

            var ptsAndFlags = BinaryPrimitives.ReadUInt64BigEndian(header[..8]);
            var isConfigPacket = (ptsAndFlags & ConfigPacketFlagMask) != 0;
            var isKeyFrame = (ptsAndFlags & KeyFrameFlagMask) != 0;
            var presentationTimestampUs = checked((long)(ptsAndFlags & PresentationTimestampMask));
            var packetSize = checked((int)BinaryPrimitives.ReadUInt32BigEndian(header[8..12]));

            return new VideoPacketHeader(isConfigPacket, isKeyFrame, presentationTimestampUs, packetSize);
        }

        internal static void ReadExactly(Stream stream, byte[] buffer, int offset, int count, CancellationToken cancellationToken = default)
        {
            var totalRead = 0;
            while (totalRead < count)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var bytesRead = stream.Read(buffer, offset + totalRead, count - totalRead);
                if (bytesRead == 0)
                    throw new EndOfStreamException($"Expected to read {count} bytes but reached end of stream after {totalRead} bytes.");

                totalRead += bytesRead;
            }
        }

        private void MobileServerSetup()
        {
            MobileServerCleanup();

            // Push scrcpy-server.jar
            UploadMobileServer();

            // Create port reverse rule
            adb.CreateReverseForward(device, $"localabstract:{DesktopSocketName}", "tcp:27183", true);
        }

        /// <summary>
        /// Remove ADB forwards/reverses.
        /// </summary>
        private void MobileServerCleanup()
        {
            // Remove any existing network stuff.
            adb.RemoveAllForwards(device);
            adb.RemoveAllReverseForwards(device);
        }

        /// <summary>
        /// Start the scrcpy server on the android device.
        /// </summary>
        /// <param name="bitrate"></param>
        private void MobileServerStart()
        {
            log.Information("Starting scrcpy server...");

            var receiver = new SerilogOutputReceiver();

            string version = "3.3.4";
            int maxFramerate = 0;
            ScrcpyLockVideoOrientation orientation = ScrcpyLockVideoOrientation.Unlocked; // -1 means allow rotate

            var cmds = new List<string>
            {
                "CLASSPATH=/data/local/tmp/scrcpy-server",
                "app_process",

                // Unused
                "/",

                // App entry point, or something like that.
                "com.genymobile.scrcpy.Server",

                version,
                // Use the current scrcpy socket metadata protocol: 12-byte video metadata, then 12-byte packet headers.
                $"scid={DesktopSocketScid}",
                // $"bit_rate={Bitrate}"
            };

            if (maxFramerate != 0)
                cmds.Add($"max_fps={maxFramerate}");

            if (orientation != ScrcpyLockVideoOrientation.Unlocked)
                cmds.Add($"lock_video_orientation={(int)orientation}");

            cmds.Add("send_device_meta=false");
            cmds.Add("send_codec_meta=true");
            cmds.Add("send_frame_meta=true");
            cmds.Add("tunnel_forward=false");
            //cmds.Add("crop=-");
            cmds.Add($"control=true");
            cmds.Add("audio=false");
            // cmds.Add("display_id=0");
            // cmds.Add($"show_touches={showTouches}");
            // cmds.Add($"stay_awake={stayAwake}");
            // cmds.Add("power_off_on_close=false");
            // cmds.Add("downsize_on_error=true");
            // cmds.Add("scid=12345678");
            cmds.Add("cleanup=true");

            string command = string.Join(" ", cmds);

            log.Information("Start command: " + command);
            // _ = adb.ExecuteRemoteCommandAsync(command, device, receiver, cts.Token);
            Task.Run(() => {
                try {
                    adb.ExecuteRemoteCommand(command, device, receiver);
                } catch (Exception ex) {
                    Console.WriteLine($"Server 退出: {ex.Message}");
                }
            });
        }

        private void UploadMobileServer()
        {
            using SyncService service = new(adb, device);
            using Stream stream = File.OpenRead(ScrcpyServerFile);
            service.Push(stream, "/data/local/tmp/scrcpy-server", 777, DateTime.Now, null, CancellationToken.None);
        }
    }
}
