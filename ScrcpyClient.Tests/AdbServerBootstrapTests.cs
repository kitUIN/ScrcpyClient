using ScrcpyClient;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace ScrcpyClient.Tests;

public class AdbServerBootstrapTests
{
    [Fact]
    public void BuildAdbExecutableCandidates_PrioritizesHintDirectory()
    {
        var candidates = AdbServerBootstrap.BuildAdbExecutableCandidates(
            @"C:\tools\scrcpy\scrcpy-server",
            new Dictionary<string, string?>(),
            pathVariable: null,
            localAppData: null,
            isWindows: true);

        Assert.Equal(Path.Combine(@"C:\tools\scrcpy", "adb.exe"), candidates[0]);
        Assert.Contains(Path.Combine(@"C:\tools\scrcpy\scrcpy-server", "adb.exe"), candidates);
    }

    [Fact]
    public void BuildAdbExecutableCandidates_UsesEnvironmentAndPathLocationsWithoutDuplicates()
    {
        var sdkPath = Path.Combine(@"D:\Android\Sdk", "platform-tools", "adb.exe");
        var candidates = AdbServerBootstrap.BuildAdbExecutableCandidates(
            adbHintPath: null,
            environmentVariables: new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["ADB_PATH"] = @"E:\portable-adb",
                ["ANDROID_SDK_ROOT"] = @"D:\Android\Sdk",
                ["ANDROID_HOME"] = @"D:\Android\Sdk"
            },
            pathVariable: @"C:\tools;D:\Android\Sdk\platform-tools",
            localAppData: @"C:\Users\me\AppData\Local",
            isWindows: true);

        Assert.Contains(Path.Combine(@"E:\portable-adb", "adb.exe"), candidates);
        Assert.Contains(sdkPath, candidates);
        Assert.Contains(Path.Combine(@"C:\tools", "adb.exe"), candidates);
        Assert.Equal(1, candidates.Count(candidate => string.Equals(candidate, sdkPath, StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void BuildAdbExecutableCandidates_IncludesDefaultLocalAppDataSdkLocation()
    {
        var candidates = AdbServerBootstrap.BuildAdbExecutableCandidates(
            adbHintPath: null,
            environmentVariables: new Dictionary<string, string?>(),
            pathVariable: null,
            localAppData: @"C:\Users\me\AppData\Local",
            isWindows: true);

        Assert.Contains(Path.Combine(@"C:\Users\me\AppData\Local", "Android", "Sdk", "platform-tools", "adb.exe"), candidates);
    }
}
