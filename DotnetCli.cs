using System.Diagnostics;

namespace ShortHandCreater;

internal static class DotnetCli
{
    internal static bool RunDotnet(string arguments)
    {
        Console.WriteLine($"> dotnet {arguments}");

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = arguments,
            UseShellExecute = false
        };

        using var process = Process.Start(psi);
        process?.WaitForExit();
        return process?.ExitCode == 0;
    }
}
