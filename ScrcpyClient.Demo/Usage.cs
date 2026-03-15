namespace ScrcpyClient.Demo;

internal static class Usage
{
    public static void Print()
    {
        Console.WriteLine("ScrcpyClient.Demo usage:");
        Console.WriteLine("  mock");
        Console.WriteLine("  scrcpy [--serial <deviceSerial>] [--server <pathToScrcpyServer>] [--bitrate <bitsPerSecond>] [--processor <name>] [--processing-fps <fps>] [--processor-template <path>]");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  dotnet run --project .\\ScrcpyClient.Demo\\ScrcpyClient.Demo.csproj -- mock");
        Console.WriteLine("  dotnet run --project .\\ScrcpyClient.Demo\\ScrcpyClient.Demo.csproj -- scrcpy --serial R58M1234567");
        Console.WriteLine("  dotnet run --project .\\ScrcpyClient.Demo\\ScrcpyClient.Demo.csproj -- scrcpy --serial R58M1234567 --server .\\tools\\scrcpy-server");
        Console.WriteLine("  dotnet run --project .\\ScrcpyClient.Demo\\ScrcpyClient.Demo.csproj -- scrcpy --processor farm-test --processing-fps 5");
    }
}
