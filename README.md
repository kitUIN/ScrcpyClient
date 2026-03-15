# ScrcpyClient

ScrcpyClient 是一个基于 .NET 8 的 [scrcpy](https://github.com/Genymobile/scrcpy) 客户端解决方案，用来连接 Android 设备、接收视频流、解码画面，并在不同宿主中展示或进一步处理图像数据。

仓库当前包含几个主要部分：

- `ScrcpyClient`：核心库，负责 scrcpy 连接、ADB 启动、控制消息发送、视频流解码和帧分发。
- `ScrcpyClient.Demo`：控制台示例程序，可用 SDL2 本地窗口展示设备画面，也支持 mock 模式。
- `ScrcpyClient.React`：ASP.NET Core Web 宿主，通过 HTTP 和 WebSocket 提供浏览器预览。
- `ScrcpyClient.SDL2`：SDL2 渲染相关实现。
- `ScrcpyClient.Tests`：基础测试项目。

## 构建

在仓库根目录执行：

```powershell
dotnet build .\ScrcpyClient.sln
```

## 运行示例

本地 SDL2 mock 预览：

```powershell
dotnet run --project .\ScrcpyClient.Demo\ScrcpyClient.Demo.csproj -- mock
```

连接真实设备的 Web 预览：

```powershell
dotnet run --project .\ScrcpyClient.React\ScrcpyClient.React.csproj -- scrcpy --serial <deviceSerial>
```

## 运行前准备

- 需要可用的 `adb`。程序会从 `PATH`、`ANDROID_SDK_ROOT`、`ANDROID_HOME` 或 `ADB_PATH` 中查找。
- 需要可用的 `scrcpy-server` 文件。项目已将 [ScrcpyClient/tools](ScrcpyClient/tools) 下的内容复制到输出目录。
- 需要可用的 FFmpeg 原生 DLL。程序会优先从环境变量 `FFMPEG_ROOT` 或 `FFMPEG_PATH` 指向的目录查找，然后检查环境变量 `PATH` 中的目录；如果仍未找到，再回退到输出目录下的 `tools` 文件夹。

如果使用 `FFMPEG_ROOT` 或 `FFMPEG_PATH`，它们应指向包含 FFmpeg DLL 的目录；也可以配置多个目录，使用系统路径分隔符分开。也可以直接把 FFmpeg 所在目录加入 `PATH`。如果这些位置都未命中，至少需要在输出目录下的 `./tools` 放入这类文件：

- `avcodec-62.dll`
- `avformat-62.dll`
- `avutil-60.dll`
- `swscale-9.dll`
- `swresample-6.dll`

也可以使用与你的 FFmpeg 构建版本匹配的等效版本号 DLL，但它们必须位于程序输出目录的 `tools` 文件夹中。

对 `ScrcpyClient.Demo` 和 `ScrcpyClient.React` 来说，仓库中的 `ffmpeg` 目录会在构建时复制到输出目录并映射为 `tools`，因此请确保相应项目目录下提供了 FFmpeg DLL。

