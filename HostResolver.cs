namespace ShortHandCreater;

internal static class HostResolver
{
    // Given a --host value, resolves it to an actual .csproj file:
    // - if it's already a .csproj path -> use as-is (must exist)
    // - if it's a directory -> find a single *.csproj inside it
    internal static string? ResolveHostCsproj(string hostArg, string searchRoot)
    {
        string fullPath = Path.GetFullPath(hostArg);

        if (fullPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            if (File.Exists(fullPath)) return fullPath;
            Console.WriteLine($"Warning: --host project file not found: {fullPath}");
            return null;
        }

        if (Directory.Exists(fullPath))
        {
            var csprojFiles = Directory.GetFiles(fullPath, "*.csproj");
            if (csprojFiles.Length == 1) return csprojFiles[0];

            if (csprojFiles.Length > 1)
            {
                Console.WriteLine($"Warning: --host folder \"{fullPath}\" contains multiple .csproj files:");
                foreach (var f in csprojFiles) Console.WriteLine($"  - {f}");
                Console.WriteLine("Point --host at the specific .csproj file instead.");
                return null;
            }

            Console.WriteLine($"Warning: --host folder \"{fullPath}\" contains no .csproj file.");
            return null;
        }

        Console.WriteLine($"Warning: --host path not found: {fullPath}");
        return null;
    }
}
