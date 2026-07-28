using System.Diagnostics;

namespace ShortHandCreater;

internal static class SingleProjectGenerator
{
    internal static void Run(
        string projectType,
        string folderName,
        string fullLocation,
        string? slnPath,
        bool isDryRun)
    {
        string fullProjectPath = Path.Combine(fullLocation, folderName);
        bool locationExists = Directory.Exists(fullLocation);
        bool projectExists = Directory.Exists(fullProjectPath);

        // ---------- DRY RUN ----------
        if (isDryRun)
        {
            Console.WriteLine("=== DRY RUN — no changes will be made ===");
            Console.WriteLine();
            Console.WriteLine(locationExists
                ? $"[OK]   Location already exists: {fullLocation}"
                : $"[WILL CREATE] Location folder: {fullLocation}");

            if (projectExists)
            {
                Console.WriteLine($"[BLOCKED] Project folder already exists: {fullProjectPath}");
            }
            else
            {
                Console.WriteLine($"[WILL CREATE] Project: \"{folderName}\" (type: {projectType})");
                Console.WriteLine($"              at: {fullProjectPath}");
            }

            Console.WriteLine(slnPath != null
                ? $"[WILL REGISTER] Add to solution: {slnPath}"
                : "[SKIP] No solution file found or specified.");

            Console.WriteLine();
            Console.WriteLine("Run the same command without --dry-run to apply these changes.");
            return;
        }

        // ---------- REAL RUN ----------

        if (!locationExists)
        {
            Directory.CreateDirectory(fullLocation);
            Console.WriteLine($"Location \"{fullLocation}\" did not exist — created it.");
        }
        else
        {
            Console.WriteLine($"Location \"{fullLocation}\" already exists — using it.");
        }

        if (projectExists)
        {
            Console.WriteLine($"Error: \"{fullProjectPath}\" already exists. Aborting to avoid overwrite.");
            return;
        }

        var newProjectPsi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"new {projectType} -n \"{folderName}\" -o \"{fullProjectPath}\"",
            WorkingDirectory = fullLocation,
            UseShellExecute = false
        };

        using (var process = Process.Start(newProjectPsi))
        {
            process?.WaitForExit();
            if (process?.ExitCode != 0)
            {
                Console.WriteLine($"Failed to create project of type \"{projectType}\".");
                return;
            }
        }

        Console.WriteLine($"Project \"{folderName}\" (type: {projectType}) created at {fullProjectPath}");

        if (slnPath != null && File.Exists(slnPath))
        {
            string csprojPath = Path.Combine(fullProjectPath, folderName + ".csproj");

            var addToSlnPsi = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"sln \"{slnPath}\" add \"{csprojPath}\"",
                UseShellExecute = false
            };

            using var slnProcess = Process.Start(addToSlnPsi);
            slnProcess?.WaitForExit();

            Console.WriteLine(slnProcess?.ExitCode == 0
                ? $"Added \"{folderName}\" to solution: {slnPath}"
                : $"Warning: could not add project to solution \"{slnPath}\".");
        }
        else
        {
            Console.WriteLine("No solution file found or specified — skipped sln registration.");
        }
    }
}
