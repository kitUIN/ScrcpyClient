using System;
using System.Buffers.Binary;
using System.Text;

namespace ScrcpyClient
{
    public enum ControlMessageType : byte
    {
        InjectKeycode,
        InjectText,
        InjectTouchEvent,
        InjectScrollEvent,
        BackOrScreenOn,
        ExpandNotificationPanel,
        ExpandSettingsPanel,
        CollapsePanels,
        GetClipboard,
        SetClipboard,
        SetScreenPowerMode,
        RotateDevice,
    }

    public record ScreenSize
    {
        public ushort Width;
        public ushort Height;
    }

    public record Point
    {
        public int X;
        public int Y;
    }

    // Not sure whether to use struct, record, or class for this.
    public record Position
    {
        public ScreenSize ScreenSize = new();
        public Point Point = new();

        public Span<byte> ToBytes()
        {
            Span<byte> b = new byte[12];
            BinaryPrimitives.WriteInt32BigEndian(b[0..], Point.X);
            BinaryPrimitives.WriteInt32BigEndian(b[4..], Point.Y);
            BinaryPrimitives.WriteUInt16BigEndian(b[8..], ScreenSize.Width);
            BinaryPrimitives.WriteUInt16BigEndian(b[10..], ScreenSize.Height);
            return b;
        }
    }

    public interface IControlMessage
    {
        public ControlMessageType Type { get; }

        Span<byte> ToBytes();
    }

    public class KeycodeControlMessage : IControlMessage
    {
        public ControlMessageType Type => ControlMessageType.InjectKeycode;
        public AndroidKeyEventAction Action { get; set; }
        public AndroidKeycode KeyCode { get; set; }
        public uint Repeat { get; set; }
        public AndroidMetastate Metastate { get; set; }

        public Span<byte> ToBytes()
        {            Span<byte> b = new byte[14];
            b[0] = (byte)Type;
            b[1] = (byte)Action;
            BinaryPrimitives.WriteInt32BigEndian(b[2..], (int)KeyCode);
            BinaryPrimitives.WriteInt32BigEndian(b[6..], (int)Repeat);
            BinaryPrimitives.WriteInt32BigEndian(b[10..], (int)Metastate);
            return b;
        }
    }

    public class BackOrScreenOnControlMessage : IControlMessage    {
        public ControlMessageType Type => ControlMessageType.BackOrScreenOn;
        public AndroidKeyEventAction Action { get; set; }

        public Span<byte> ToBytes()
        {
            Span<byte> b = new byte[2];
            b[0] = (byte)Type;
            b[1] = (byte)Action;
            return b;
        }
    }

    /// <summary>Injects UTF-8 text directly, bypassing keycode mapping on the device.</summary>
    public class InjectTextControlMessage : IControlMessage
    {
        private const int MaxTextBytes = 300;

        public ControlMessageType Type => ControlMessageType.InjectText;
        public string Text { get; set; } = "";

        public Span<byte> ToBytes()
        {
            // type(1) + length(4, uint32 big-endian) + text(N, UTF-8)
            var textBytes = Encoding.UTF8.GetBytes(Text);
            if (textBytes.Length > MaxTextBytes)
                textBytes = textBytes[..MaxTextBytes];
            var b = new byte[5 + textBytes.Length];
            b[0] = (byte)Type;
            BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(1), (uint)textBytes.Length);
            textBytes.CopyTo(b, 5);
            return b;
        }
    }

    public class TouchEventControlMessage : IControlMessage
    {
        public ControlMessageType Type => ControlMessageType.InjectTouchEvent;
        public AndroidMotionEventAction Action { get; set; }
        /// <summary>The button that triggered this action (e.g. DOWN/UP).</summary>
        public AndroidMotionEventButtons ActionButton { get; set; } = AndroidMotionEventButtons.AMOTION_EVENT_BUTTON_PRIMARY;
        /// <summary>All currently pressed buttons. Should be 0 on UP events.</summary>
        public AndroidMotionEventButtons Buttons { get; set; } = AndroidMotionEventButtons.AMOTION_EVENT_BUTTON_PRIMARY;
        public ulong PointerId { get; set; } = 0xFFFFFFFFFFFFFFFF;
        public Position Position { get; set; } = new();
        //public float Pressure { get; set; }

        public Span<byte> ToBytes()
        {
            // type(1) + action(1) + pointer_id(8) + x(4) + y(4) + w(2) + h(2) + pressure(2) + action_button(4) + buttons(4) = 32
            Span<byte> b = new byte[32];
            b[0] = (byte)Type;
            b[1] = (byte)Action;
            BinaryPrimitives.WriteUInt64BigEndian(b[2..], PointerId);

            // Position
            BinaryPrimitives.WriteInt32BigEndian(b[10..], Position.Point.X);
            BinaryPrimitives.WriteInt32BigEndian(b[14..], Position.Point.Y);
            BinaryPrimitives.WriteUInt16BigEndian(b[18..], Position.ScreenSize.Width);
            BinaryPrimitives.WriteUInt16BigEndian(b[20..], Position.ScreenSize.Height);

            // Pressure (max = 0xFFFF)
            b[22] = 0xFF;
            b[23] = 0xFF;

            BinaryPrimitives.WriteInt32BigEndian(b[24..], (int)ActionButton);
            BinaryPrimitives.WriteInt32BigEndian(b[28..], (int)Buttons);

            return b;
        }
    }

    public class ScrollEventControlMessage : IControlMessage
    {
        public ControlMessageType Type => ControlMessageType.InjectScrollEvent;
        public Position Position { get; set; } = new();
        public int HorizontalScroll { get; set; }
        public int VerticalScroll { get; set; }
        /// <summary>Currently pressed mouse buttons.</summary>
        public AndroidMotionEventButtons Buttons { get; set; } = 0;

        public Span<byte> ToBytes()
        {
            // type(1) + x(4) + y(4) + w(2) + h(2) + hscroll(2,int16) + vscroll(2,int16) + buttons(4) = 21
            Span<byte> b = new byte[21];
            b[0] = (byte)Type;
            Position.ToBytes().CopyTo(b[1..]);
            BinaryPrimitives.WriteInt16BigEndian(b[13..], (short)HorizontalScroll);
            BinaryPrimitives.WriteInt16BigEndian(b[15..], (short)VerticalScroll);
            BinaryPrimitives.WriteInt32BigEndian(b[17..], (int)Buttons);
            return b;
        }
    }
}
