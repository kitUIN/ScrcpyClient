using SharpAdbClient;

namespace ScrcpyClient.Demo;

internal static class DeviceSelector
{
    public static DeviceData Select(AdbClient adbClient, string? serial)
    {
        ArgumentNullException.ThrowIfNull(adbClient);

        var devices = adbClient.GetDevices().Where(IsOnline).ToList();

        if (!string.IsNullOrWhiteSpace(serial))
        {
            var device = devices.FirstOrDefault(d => string.Equals(d.Serial, serial, StringComparison.OrdinalIgnoreCase));
            return device ?? throw new InvalidOperationException($"Unable to find online adb device with serial '{serial}'.");
        }

        return devices.Count switch
        {
            0 => throw new InvalidOperationException("No online adb devices were found."),
            1 => devices[0],
            _ => throw new InvalidOperationException("Multiple online adb devices were found. Please pass --serial <deviceSerial>.")
        };
    }

    private static bool IsOnline(DeviceData device)
    {
        return device.State.ToString().Equals("Online", StringComparison.OrdinalIgnoreCase);
    }
}

