using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

if (args.Length == 0 || HasFlag(args, "--help") || HasFlag(args, "-h"))
{
    PrintHelp();
    return;
}

// Expect: dn <action> [type] --name <name> --location <location>
// type is optional and defaults to "classlib"
// e.g.:   dn g --name MBPOS.Modules.Sales.Domain --location modules/sales
// e.g.:   dn g webapi --name MBPOS.Api --location host
if (args.Length < 1)
{
    Console.WriteLine("Error: expected format: dn <action> [type] --name <name> --location <location>");
    Console.WriteLine("Example: dn g --name MBPOS.Modules.Sales.Domain --location modules/sales");
    Console.WriteLine("Run 'dn --help' for full usage.");
    return;
}

string action = args[0].ToLowerInvariant();

if (action != "g" && action != "generate")
{
    Console.WriteLine($"Error: unknown action \"{action}\". Currently supported: g (generate)");
    return;
}

// type is positional at args[1] IF it exists and isn't a flag; otherwise default to classlib
string projectType = "classlib";
if (args.Length > 1 && !args[1].StartsWith("-"))
{
    projectType = args[1];
}

bool isDryRun = HasFlag(args, "--dry-run");
string? folderName = GetArgValue(args, "--name") ?? GetArgValue(args, "-n");
string? location = GetArgValue(args, "--location") ?? GetArgValue(args, "-l");
string? slnFlag = GetArgValue(args, "--sln") ?? GetArgValue(args, "-s");

if (string.IsNullOrWhiteSpace(folderName) || string.IsNullOrWhiteSpace(location))
{
    Console.WriteLine("Error: --name and --location are required.");
    Console.WriteLine("Run 'dn --help' to see full usage and examples.");
    return;
}

if (!IsValidTemplate(projectType))
{
    Console.WriteLine($"Error: \"{projectType}\" is not a recognized dotnet template.");
    Console.WriteLine("Run 'dotnet new list' to see all available templates on this machine.");
    return;
}

string currentDir = Directory.GetCurrentDirectory();

string? resolvedLocation = ResolveLocation(location, currentDir);
if (resolvedLocation == null) return; // ambiguous match - options already printed
location = resolvedLocation;

string fullLocation = Path.GetFullPath(location);
string fullProjectPath = Path.Combine(fullLocation, folderName);
bool locationExists = Directory.Exists(fullLocation);
bool projectExists = Directory.Exists(fullProjectPath);

string? slnPath = slnFlag != null
    ? Path.GetFullPath(slnFlag)
    : Directory.GetFiles(currentDir, "*.sln")
        .Concat(Directory.GetFiles(currentDir, "*.slnx"))
        .FirstOrDefault();

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

// ---------- Helpers ----------

static string? GetArgValue(string[] args, string key)
{
    for (int i = 0; i < args.Length - 1; i++)
        if (string.Equals(args[i], key, StringComparison.OrdinalIgnoreCase))
            return args[i + 1];
    return null;
}

static bool HasFlag(string[] args, string key) =>
    args.Any(a => string.Equals(a, key, StringComparison.OrdinalIgnoreCase));

static bool IsValidTemplate(string type)
{
    string[] known =
    {
        "classlib", "console", "webapi", "web", "mvc", "razor", "blazorwasm", "blazorserver",
        "worker", "xunit", "nunit", "mstest", "wpf", "winforms", "grpc"
    };
    return known.Contains(type.ToLowerInvariant());
}

// Resolves a typed --location against the actual folder structure on disk.
// - If the input is already a real, existing path -> use as-is.
// - Otherwise, scan existing folders and match the trailing path segments (case-insensitive).
//   - Exact match on all typed segments -> use the matched existing folder.
//   - Extra trailing segment(s) beyond what matched -> resolve the known part,
//     then append the new segment(s) as a folder to be created (e.g. "modules/newmodule").
//   - No match at all -> treat input as a brand-new path to create, unchanged.
//   - Multiple matches -> list them and ask the user to be more specific.
static string? ResolveLocation(string input, string searchRoot)
{
    string directCheck = Path.GetFullPath(input);
    if (Directory.Exists(directCheck)) return input;

    var allDirs = Directory.GetDirectories(searchRoot, "*", SearchOption.AllDirectories)
        .Where(d => !d.Contains(Path.DirectorySeparatorChar + ".git")
                 && !d.Contains(Path.DirectorySeparatorChar + "bin")
                 && !d.Contains(Path.DirectorySeparatorChar + "obj")
                 && !d.Contains(Path.DirectorySeparatorChar + "node_modules"))
        .ToList();

    string normalizedInput = input.Replace('\\', '/').Trim('/');
    var inputParts = normalizedInput.Split('/', StringSplitOptions.RemoveEmptyEntries);

    for (int take = inputParts.Length; take >= 1; take--)
    {
        var knownParts = inputParts.Take(take).ToArray();
        var remainderParts = inputParts.Skip(take).ToArray();

        var matches = allDirs.Where(d =>
        {
            var relative = Path.GetRelativePath(searchRoot, d).Replace('\\', '/');
            var relativeParts = relative.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (knownParts.Length > relativeParts.Length) return false;

            var tail = relativeParts.Skip(relativeParts.Length - knownParts.Length);
            return tail.Select(p => p.ToLowerInvariant())
                        .SequenceEqual(knownParts.Select(p => p.ToLowerInvariant()));
        }).ToList();

        if (matches.Count == 1)
        {
            string resolvedBase = Path.GetRelativePath(searchRoot, matches[0]);
            string result = remainderParts.Length > 0
                ? Path.Combine(resolvedBase, Path.Combine(remainderParts))
                : resolvedBase;

            Console.WriteLine(remainderParts.Length > 0
                ? $"Resolved location \"{input}\" → \"{result}\" (new subfolder under existing match)"
                : $"Resolved location \"{input}\" → \"{result}\"");
            return result;
        }

        if (matches.Count > 1)
        {
            Console.WriteLine($"Warning: \"{string.Join("/", knownParts)}\" matched multiple folders:");
            foreach (var m in matches)
                Console.WriteLine($"  - {Path.GetRelativePath(searchRoot, m)}");
            Console.WriteLine("Be more specific (include a parent segment) or use the full path.");
            return null;
        }
        // 0 matches at this prefix length -> shrink the known-part window and try again
    }

    // No match at all - treat as a brand-new path, created as typed
    return input;
}

static void PrintHelp()
{
    Console.WriteLine(@"
dn — scaffold a new .NET project into your solution structure.
Locations are resolved dynamically against your existing folder structure — no config file needed.

USAGE:
  dn <action> <type> --name <name> --location <location> [--sln <path>] [--dry-run]

ACTIONS:
  g, generate    Generate a new project (currently the only supported action)

TYPE:
  Positional argument — any valid dotnet template shortname:
  classlib   console   webapi   web   mvc   razor
  blazorwasm blazorserver   worker   xunit   nunit   mstest
  wpf   winforms   grpc

  Run 'dotnet new list' to see every template installed on your machine.

OPTIONS:
  -n, --name        (required)  Name of the project to create.
  -l, --location    (required)  Folder path or partial/fuzzy path. Matches existing
                                 folders on disk (fully or partially, from the end of
                                 the path); creates a new path if nothing matches.
  -s, --sln         (optional)  Path to a specific .sln/.slnx file. Auto-detected
                                 from the current directory if omitted.
      --dry-run     (optional)  Preview what would happen without creating anything.
  -h, --help                    Show this help message.

HOW LOCATION RESOLUTION WORKS:

  Say your disk already has: src/Modules/Identity

  dn g classlib --name X --location identity
    → matches existing folder ""src/Modules/Identity"" exactly → uses it

  dn g classlib --name X --location modules/identity
    → matches existing folder ""src/Modules/Identity"" exactly → uses it

  dn g classlib --name X --location modules/vendors
    → ""modules"" matches ""src/Modules"", ""vendors"" doesn't exist yet
    → resolves to ""src/Modules/vendors"" (new subfolder, created fresh)

  dn g classlib --name X --location brandnewpath/here
    → no match anywhere on disk → created exactly as typed: ""brandnewpath/here""

  dn g classlib --name X --location domain
    → matches MULTIPLE existing folders (e.g. every module has a Domain folder)
    → tool lists all matches and asks you to be more specific instead of guessing

EXAMPLES:

  Class library, using an existing folder by partial name:
    dn g classlib --name MBPOS.Modules.Identity.Presentation --location identity

  Full explicit path (always works, no resolution needed):
    dn g classlib --name MBPOS.Modules.Sales.Domain --location src/Modules/Sales

  New module folder under an existing parent:
    dn g classlib --name MBPOS.Modules.Vendors.Domain --location modules/vendors

  Shared project:
    dn g classlib --name MBPOS.Shared.Infrastructure --location shared

  Web API host:
    dn g webapi --name MBPOS.Api --location host

  xUnit test project:
    dn g xunit --name MBPOS.Modules.Payments.UnitTests --location tests/unittests

  Preview before creating anything:
    dn g classlib --name MBPOS.Modules.Sales.Domain --location modules/sales --dry-run

  Point to a specific solution file explicitly:
    dn g classlib --name MBPOS.Modules.Sales.Domain --location modules/sales --sln MbPos.Backend.sln

NOTES:
  - If --location resolves to an existing folder, the tool reuses it instead of failing.
  - If the target project folder itself already exists, the tool aborts to avoid overwriting.
  - The tool auto-detects .sln or .slnx files in the current directory; use --sln to override.
  - Run this tool from your solution root so folder scanning and .sln detection work correctly.
");
}