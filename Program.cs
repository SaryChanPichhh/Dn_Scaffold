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
// e.g.:   dn g m --name MBPOS.Modules.Products --location src/Modules   (full module scaffold)
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

bool isModule = string.Equals(projectType, "m", StringComparison.OrdinalIgnoreCase)
             || string.Equals(projectType, "module", StringComparison.OrdinalIgnoreCase);

bool isDryRun = HasFlag(args, "--dry-run");
string? nameArg = GetArgValue(args, "--name") ?? GetArgValue(args, "-n");
string? location = GetArgValue(args, "--location") ?? GetArgValue(args, "-l");
string? slnFlag = GetArgValue(args, "--sln") ?? GetArgValue(args, "-s");
string? entityFlag = GetArgValue(args, "--entity") ?? GetArgValue(args, "-e");
string sharedRoot = GetArgValue(args, "--shared-root") ?? GetArgValue(args, "-r") ?? "MBPOS";
bool skipShared = HasFlag(args, "--no-shared");

if (string.IsNullOrWhiteSpace(nameArg) || string.IsNullOrWhiteSpace(location))
{
    Console.WriteLine("Error: --name and --location are required.");
    Console.WriteLine("Run 'dn --help' to see full usage and examples.");
    return;
}

if (!isModule && !IsValidTemplate(projectType))
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

string? slnPath = slnFlag != null
    ? Path.GetFullPath(slnFlag)
    : Directory.GetFiles(currentDir, "*.sln")
        .Concat(Directory.GetFiles(currentDir, "*.slnx"))
        .FirstOrDefault();

// ============================================================
// MODULE MODE — "dn g m --name MBPOS.Modules.Products --location src/Modules"
// ============================================================
if (isModule)
{
    GenerateModule(nameArg, fullLocation, slnPath, sharedRoot, entityFlag, skipShared, isDryRun);
    return;
}

// ============================================================
// SINGLE PROJECT MODE — original behavior
// ============================================================
string folderName = nameArg;
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

return;

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

// Very simple English singularizer, good enough for typical entity names
// (Products -> Product, Categories -> Category, Addresses -> Address).
// Falls back to returning the word unchanged if no rule applies.
static string Singularize(string word)
{
    if (word.EndsWith("ies", StringComparison.OrdinalIgnoreCase) && word.Length > 3)
        return word[..^3] + "y";
    if (word.EndsWith("ses", StringComparison.OrdinalIgnoreCase) || word.EndsWith("xes", StringComparison.OrdinalIgnoreCase))
        return word[..^2];
    if (word.EndsWith("s", StringComparison.OrdinalIgnoreCase) && !word.EndsWith("ss", StringComparison.OrdinalIgnoreCase))
        return word[..^1];
    return word;
}

// ============================================================
// Module scaffold: creates Domain / Application / Infrastructure /
// Presentation / Contracts class libs + a UnitTests project, replaces
// the default Class1.cs in each with real stub files for the given
// entity, wires up the standard layer references (Shared.* paths are
// fixed to {sharedRoot}.Shared.<Layer>, not derived from --name), and
// registers everything in the solution.
//
//   prefix            e.g. "MBPOS.Modules.Products"
//   moduleFolderName  e.g. "Products"       (derived: last segment of prefix)
//   moduleRoot        e.g. "{location}/Products"
//   entity            e.g. "Product"        (derived: singularized moduleFolderName, or --entity)
// ============================================================
static void GenerateModule(string prefix, string parentLocation, string? slnPath, string sharedRoot, string? entityOverride, bool skipShared, bool isDryRun)
{
    string[] layers = { "Domain", "Application", "Infrastructure", "Presentation", "Contracts" };

    var segments = prefix.Split('.', StringSplitOptions.RemoveEmptyEntries);
    if (segments.Length < 1)
    {
        Console.WriteLine($"Error: \"{prefix}\" is not a valid module name (expected e.g. \"MBPOS.Modules.Products\").");
        return;
    }

    string moduleFolderName = segments[^1];                          // e.g. Products
    string entity = entityOverride ?? Singularize(moduleFolderName); // e.g. Product
    string moduleRoot = Path.Combine(parentLocation, moduleFolderName);

    string LayerDir(string layer) => Path.Combine(moduleRoot, $"{prefix}.{layer}");
    string LayerCsProj(string layer) => Path.Combine(LayerDir(layer), $"{prefix}.{layer}.csproj");
    string LayerNamespace(string layer) => $"{prefix}.{layer}";

    // Fixed shared-project convention: {sharedRoot}.Shared.<Layer> under src/Shared.
    // Not derived from --name, so a mistyped module prefix can't point references
    // at the wrong shared projects.
    string SharedCsProj(string layer) =>
        Path.Combine("src", "Shared", $"{sharedRoot}.Shared.{layer}", $"{sharedRoot}.Shared.{layer}.csproj");

    string testName = $"{prefix}.UnitTests";
    string testDir = Path.Combine("UnitTests", testName);
    string testCsProj = Path.Combine(testDir, $"{testName}.csproj");

    // Stub files to create per layer: (relative file name, content).
    // Deletes the default Class1.cs first.
    (string layer, (string file, string content)[] stubs)[] StubPlan() => new[]
    {
        ("Domain", new[]
        {
            ($"{entity}.cs", $$"""
                namespace {{LayerNamespace("Domain")}};

                public class {{entity}}
                {
                    public Guid Id { get; private set; }

                    // TODO: add {{entity}} properties and domain behavior
                }
                """),
            ($"I{entity}Repository.cs", $$"""
                namespace {{LayerNamespace("Domain")}};

                public interface I{{entity}}Repository
                {
                    Task<{{entity}}?> GetByIdAsync(Guid id);
                    Task AddAsync({{entity}} entity);
                    Task UpdateAsync({{entity}} entity);
                    Task DeleteAsync(Guid id);
                }
                """)
        }),
        ("Application", new[]
        {
            ($"Create{entity}.cs", $$"""
                namespace {{LayerNamespace("Application")}};

                // TODO: implement as a command/handler pair (e.g. MediatR) as needed
                public class Create{{entity}}
                {
                }
                """),
            ($"Update{entity}.cs", $$"""
                namespace {{LayerNamespace("Application")}};

                // TODO: implement as a command/handler pair (e.g. MediatR) as needed
                public class Update{{entity}}
                {
                }
                """),
            ($"{entity}Dto.cs", $$"""
                namespace {{LayerNamespace("Application")}};

                // Internal application-layer DTO (distinct from the public Contracts request/response types)
                public class {{entity}}Dto
                {
                    public Guid Id { get; set; }

                    // TODO: add {{entity}} fields
                }
                """)
        }),
        ("Infrastructure", new[]
        {
            ($"{entity}Repository.cs", $$"""
                using {{LayerNamespace("Domain")}};

                namespace {{LayerNamespace("Infrastructure")}};

                public class {{entity}}Repository : I{{entity}}Repository
                {
                    public Task<{{entity}}?> GetByIdAsync(Guid id)
                        => throw new NotImplementedException();

                    public Task AddAsync({{entity}} entity)
                        => throw new NotImplementedException();

                    public Task UpdateAsync({{entity}} entity)
                        => throw new NotImplementedException();

                    public Task DeleteAsync(Guid id)
                        => throw new NotImplementedException();
                }
                """)
        }),
        ("Presentation", new[]
        {
            ($"{entity}Controller.cs", $$"""
                using Microsoft.AspNetCore.Mvc;

                namespace {{LayerNamespace("Presentation")}};

                [ApiController]
                [Route("api/[controller]")]
                public class {{entity}}Controller : ControllerBase
                {
                    // TODO: add endpoints (Get, Create, Update, Delete)
                }
                """)
        }),
        ("Contracts", new[]
        {
            (Path.Combine("Request", $"Create{entity}Request.cs"), $$"""
                namespace {{LayerNamespace("Contracts")}}.Request;

                public class Create{{entity}}Request
                {
                    // TODO: add fields required to create a {{entity}}
                }
                """),
            (Path.Combine("Response", $"Create{entity}Response.cs"), $$"""
                namespace {{LayerNamespace("Contracts")}}.Response;

                public class Create{{entity}}Response
                {
                    public Guid Id { get; set; }
                }
                """),
            (Path.Combine("Request", $"Update{entity}Request.cs"), $$"""
                namespace {{LayerNamespace("Contracts")}}.Request;

                public class Update{{entity}}Request
                {
                    public Guid Id { get; set; }

                    // TODO: add fields that can be updated on a {{entity}}
                }
                """),
            (Path.Combine("Response", $"Update{entity}Response.cs"), $$"""
                namespace {{LayerNamespace("Contracts")}}.Response;

                public class Update{{entity}}Response
                {
                    public bool Success { get; set; }
                }
                """),
            (Path.Combine("Response", $"{entity}Response.cs"), $$"""
                namespace {{LayerNamespace("Contracts")}}.Response;

                // Returned when fetching a {{entity}}
                public class {{entity}}Response
                {
                    public Guid Id { get; set; }

                    // TODO: add {{entity}} fields returned to callers
                }
                """)
        })
    };

    if (isDryRun)
    {
        Console.WriteLine("=== DRY RUN (module) — no changes will be made ===");
        Console.WriteLine();
        Console.WriteLine($"Module root: {moduleRoot}");
        Console.WriteLine($"Entity:      {entity}  (from {(entityOverride != null ? "--entity" : $"singularized \"{moduleFolderName}\"")})");
        Console.WriteLine($"Shared root: {sharedRoot}  (from {(GetShownSharedRootSource())})");
        Console.WriteLine();

        foreach (var (layer, stubs) in StubPlan())
        {
            Console.WriteLine($"[WILL CREATE] classlib  {prefix}.{layer}  -> {LayerDir(layer)}");
            Console.WriteLine($"[WILL DELETE]           {Path.Combine(LayerDir(layer), "Class1.cs")}");
            foreach (var (file, _) in stubs)
                Console.WriteLine($"[WILL CREATE]           {Path.Combine(LayerDir(layer), file)}");
        }
        Console.WriteLine($"[WILL CREATE] xunit     {testName}  -> {testDir}");
        Console.WriteLine($"[WILL DELETE]           {Path.Combine(testDir, "UnitTest1.cs")}");
        Console.WriteLine($"[WILL CREATE]           {Path.Combine(testDir, "Controllers", $"{entity}ControllerTests.cs")}");
        Console.WriteLine($"[WILL CREATE]           {Path.Combine(testDir, "Services", $"{entity}ServiceTests.cs")}");
        Console.WriteLine();
        Console.WriteLine("[WILL WIRE INTERNAL REFERENCES] (always applied)");
        Console.WriteLine($"  Application     -> Domain, Contracts");
        Console.WriteLine($"  Infrastructure  -> Domain, Application");
        Console.WriteLine($"  Presentation    -> Application, Contracts");
        Console.WriteLine($"  UnitTests       -> Application, Presentation");
        Console.WriteLine();
        Console.WriteLine("[WILL ADD FRAMEWORK REFERENCE]");
        Console.WriteLine($"  Presentation    -> Microsoft.AspNetCore.App  (for ControllerBase)");
        Console.WriteLine();
        if (skipShared)
        {
            Console.WriteLine("[SKIP] Shared.* references skipped (--no-shared)");
        }
        else
        {
            Console.WriteLine("[WILL WIRE SHARED REFERENCES] (best-effort — skipped with a warning if the project isn't found)");
            Console.WriteLine($"  Domain          -> {SharedCsProj("Domain")}{(File.Exists(SharedCsProj("Domain")) ? "" : "  [NOT FOUND]")}");
            Console.WriteLine($"  Application     -> {SharedCsProj("Application")}{(File.Exists(SharedCsProj("Application")) ? "" : "  [NOT FOUND]")}");
            Console.WriteLine($"  Infrastructure  -> {SharedCsProj("Infrastructure")}{(File.Exists(SharedCsProj("Infrastructure")) ? "" : "  [NOT FOUND]")}");
        }
        Console.WriteLine();
        Console.WriteLine(slnPath != null
            ? $"[WILL REGISTER] All projects above added to solution: {slnPath}"
            : "[SKIP] No solution file found or specified — projects will NOT be added to a solution.");
        Console.WriteLine();
        Console.WriteLine("Run the same command without --dry-run to apply these changes.");
        return;

        string GetShownSharedRootSource() => sharedRoot == "MBPOS" ? "default" : "--shared-root";
    }

    if (Directory.Exists(moduleRoot))
    {
        Console.WriteLine($"Error: module folder already exists: \"{moduleRoot}\". Aborting to avoid overwrite.");
        return;
    }

    if (slnPath == null)
    {
        Console.WriteLine("Error: no solution file found or specified (use --sln). A module needs a solution to register its projects in.");
        return;
    }

    Directory.CreateDirectory(moduleRoot);
    Console.WriteLine($"Created module root: {moduleRoot}");
    Console.WriteLine($"Entity name: {entity}");

    // 1. Create each layer's class library, strip the default Class1.cs, add stub files
    foreach (var (layer, stubs) in StubPlan())
    {
        string name = $"{prefix}.{layer}";
        string outDir = LayerDir(layer);

        if (!RunDotnet($"new classlib -n \"{name}\" -o \"{outDir}\""))
        {
            Console.WriteLine($"Failed creating \"{name}\". Aborting.");
            return;
        }

        string defaultClassFile = Path.Combine(outDir, "Class1.cs");
        if (File.Exists(defaultClassFile))
        {
            File.Delete(defaultClassFile);
            Console.WriteLine($"  removed default {defaultClassFile}");
        }

        foreach (var (file, content) in stubs)
        {
            string filePath = Path.Combine(outDir, file);
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            File.WriteAllText(filePath, content + Environment.NewLine);
            Console.WriteLine($"  created {filePath}");
        }
    }

    // 2. Add each layer to the solution
    foreach (var layer in layers)
    {
        if (!RunDotnet($"sln \"{slnPath}\" add \"{LayerCsProj(layer)}\""))
        {
            Console.WriteLine($"Failed adding \"{prefix}.{layer}\" to solution. Aborting.");
            return;
        }
    }

    // 3. Wire internal module references FIRST, unconditionally.
    // These never depend on Shared projects existing, so Infrastructure/Application/
    // Presentation always get Domain/Contracts wired even if Shared.* isn't there yet.
    if (!AddReference(LayerCsProj("Application"),
            LayerCsProj("Domain"), LayerCsProj("Contracts"))) return;

    if (!AddReference(LayerCsProj("Infrastructure"),
            LayerCsProj("Domain"), LayerCsProj("Application"))) return;

    if (!AddReference(LayerCsProj("Presentation"),
            LayerCsProj("Application"), LayerCsProj("Contracts"))) return;

    // 3a. Presentation needs the ASP.NET Core framework reference for ControllerBase.
    string presentationCsproj = LayerCsProj("Presentation");
    if (File.Exists(presentationCsproj))
    {
        string csprojContent = File.ReadAllText(presentationCsproj);
        if (!csprojContent.Contains("Microsoft.AspNetCore.App"))
        {
            csprojContent = csprojContent.Replace("</Project>", "  <ItemGroup>\n    <FrameworkReference Include=\"Microsoft.AspNetCore.App\" />\n  </ItemGroup>\n</Project>");
            File.WriteAllText(presentationCsproj, csprojContent);
        }
    }

    // 3b. Wire Shared.* references as best-effort. Skipped (with a warning, not an
    // abort) if the Shared project doesn't exist on disk yet, or if --no-shared was passed.
    if (!skipShared)
    {
        TryAddSharedReference(LayerCsProj("Domain"), SharedCsProj("Domain"));
        TryAddSharedReference(LayerCsProj("Application"), SharedCsProj("Application"));
        TryAddSharedReference(LayerCsProj("Infrastructure"), SharedCsProj("Infrastructure"));
    }
    else
    {
        Console.WriteLine("Skipped Shared.* references (--no-shared).");
    }

    // 4. Unit test project
    if (!RunDotnet($"new xunit -n {testName} -o \"{testDir}\""))
    {
        Console.WriteLine($"Failed creating \"{testName}\". Aborting.");
        return;
    }
    if (!RunDotnet($"sln \"{slnPath}\" add \"{testCsProj}\""))
    {
        Console.WriteLine($"Failed adding \"{testName}\" to solution. Aborting.");
        return;
    }
    if (!AddReference(testDir, LayerCsProj("Application"), LayerCsProj("Presentation"))) return;

    string defaultTestFile = Path.Combine(testDir, "UnitTest1.cs");
    if (File.Exists(defaultTestFile))
    {
        File.Delete(defaultTestFile);
        Console.WriteLine($"  removed default {defaultTestFile}");
    }

    string controllerTestPath = Path.Combine(testDir, "Controllers", $"{entity}ControllerTests.cs");
    Directory.CreateDirectory(Path.GetDirectoryName(controllerTestPath)!);
    File.WriteAllText(controllerTestPath, $$"""
        using Xunit;
        using {{LayerNamespace("Presentation")}};

        namespace {{testName}}.Controllers;

        public class {{entity}}ControllerTests
        {
            [Fact]
            public void Controller_Can_Be_Instantiated()
            {
                var controller = new {{entity}}Controller();

                Assert.NotNull(controller);
            }

            // TODO: add tests for each {{entity}}Controller endpoint once implemented
        }

        """);
    Console.WriteLine($"  created {controllerTestPath}");

    string serviceTestPath = Path.Combine(testDir, "Services", $"{entity}ServiceTests.cs");
    Directory.CreateDirectory(Path.GetDirectoryName(serviceTestPath)!);
    File.WriteAllText(serviceTestPath, $$"""
        using Xunit;
        using {{LayerNamespace("Application")}};

        namespace {{testName}}.Services;

        public class {{entity}}ServiceTests
        {
            [Fact]
            public void Create{{entity}}_Can_Be_Instantiated()
            {
                var command = new Create{{entity}}();

                Assert.NotNull(command);
            }

            [Fact]
            public void Update{{entity}}_Can_Be_Instantiated()
            {
                var command = new Update{{entity}}();

                Assert.NotNull(command);
            }

            // TODO: add real behavioral tests once Create{{entity}}/Update{{entity}} are implemented
        }

        """);
    Console.WriteLine($"  created {serviceTestPath}");

    Console.WriteLine();
    Console.WriteLine("============================================================");
    Console.WriteLine($" Module \"{moduleFolderName}\" ({prefix}) scaffolded successfully.");
    Console.WriteLine("============================================================");

    // ----- local helpers scoped to module generation -----

    bool AddReference(string projectPathOrDir, params string[] references)
    {
        var refs = string.Join(" ", references.Select(r => $"\"{r}\""));
        return RunDotnet($"add \"{projectPathOrDir}\" reference {refs}");
    }

    // Best-effort: only attempts the reference if the target Shared csproj actually
    // exists on disk. If it doesn't, prints a warning and moves on instead of failing
    // the whole scaffold.
    void TryAddSharedReference(string projectPathOrDir, string sharedCsprojPath)
    {
        if (!File.Exists(sharedCsprojPath))
        {
            Console.WriteLine($"  Warning: shared project not found, skipped: {sharedCsprojPath}");
            return;
        }

        if (!AddReference(projectPathOrDir, sharedCsprojPath))
        {
            Console.WriteLine($"  Warning: failed to add shared reference: {sharedCsprojPath}");
        }
    }



    static bool RunDotnet(string arguments)
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

static void PrintHelp()
{
    Console.WriteLine(@"
dn — scaffold new .NET projects (or a full module) into your solution structure.
Locations are resolved dynamically against your existing folder structure — no config file needed.

USAGE:
  dn <action> <type> --name <name> --location <location> [options]

ACTIONS:
  g, generate    Generate a new project or module (currently the only supported action)

TYPE:
  Positional argument. Either:
    m, module      Full module scaffold: Domain, Application, Infrastructure,
                    Presentation, Contracts + a UnitTests project, with real
                    stub files (not the default Class1.cs) and the standard
                    layer references wired up, all added to your solution.
    <template>     Any valid dotnet template shortname for a single project:
                    classlib   console   webapi   web   mvc   razor
                    blazorwasm blazorserver   worker   xunit   nunit   mstest
                    wpf   winforms   grpc

  Run 'dotnet new list' to see every template installed on your machine.

OPTIONS:
  -n, --name          (required)  Project name, OR for module type, the module
                                   prefix, e.g. ""MBPOS.Modules.Products"".
  -l, --location      (required)  Folder path or partial/fuzzy path. Matches existing
                                   folders on disk (fully or partially, from the end of
                                   the path); creates a new path if nothing matches.
                                   For module type, this is the PARENT folder the
                                   module's own folder gets created under.
  -s, --sln           (optional)  Path to a specific .sln/.slnx file. Auto-detected
                                   from the current directory if omitted. Required
                                   for module type.
  -e, --entity        (optional)  Entity name used for generated stub files
                                   (module type only). Defaults to the module's
                                   folder name, singularized (""Products"" -> ""Product"").
  -r, --shared-root    (optional) Root prefix for Shared project references
                                   (module type only). Defaults to ""MBPOS"", producing
                                   src/Shared/MBPOS.Shared.<Layer>. This is fixed and
                                   NOT derived from --name, so a differently-prefixed
                                   --name (e.g. one starting with ""tests"") won't point
                                   references at the wrong shared projects.
      --no-shared     (optional)  Skip wiring Shared.* references entirely (module type
                                   only). Internal module references (Application->Domain,
                                   Infrastructure->Domain/Application, etc.) are ALWAYS
                                   wired regardless of this flag or whether Shared
                                   projects exist — Shared references are best-effort
                                   and just get skipped with a warning if the target
                                   Shared project isn't found on disk.
      --dry-run       (optional)  Preview what would happen without creating anything.
  -h, --help                      Show this help message.

HOW LOCATION RESOLUTION WORKS:

  Say your disk already has: src/Modules/Identity

  dn g classlib --name X --location identity
    → matches existing folder ""src/Modules/Identity"" exactly → uses it

  dn g classlib --name X --location modules/vendors
    → ""modules"" matches ""src/Modules"", ""vendors"" doesn't exist yet
    → resolves to ""src/Modules/vendors"" (new subfolder, created fresh)

  dn g classlib --name X --location brandnewpath/here
    → no match anywhere on disk → created exactly as typed: ""brandnewpath/here""

  dn g classlib --name X --location domain
    → matches MULTIPLE existing folders (e.g. every module has a Domain folder)
    → tool lists all matches and asks you to be more specific instead of guessing

MODULE TYPE (m / module):

  dn g m --name MBPOS.Modules.Products --location src/Modules

  Creates, under src/Modules/Products:
    MBPOS.Modules.Products.Domain/
      Product.cs
      IProductRepository.cs
    MBPOS.Modules.Products.Application/
      CreateProduct.cs
      UpdateProduct.cs
      ProductDto.cs
    MBPOS.Modules.Products.Infrastructure/
      ProductRepository.cs
    MBPOS.Modules.Products.Presentation/
      ProductController.cs
    MBPOS.Modules.Products.Contracts/
      Request/
        CreateProductRequest.cs
        UpdateProductRequest.cs
      Response/
        CreateProductResponse.cs
        UpdateProductResponse.cs
        ProductResponse.cs
  plus UnitTests/MBPOS.Modules.Products.UnitTests/
    Controllers/
      ProductControllerTests.cs
    Services/
      ProductServiceTests.cs

  (Entity name ""Product"" is singularized automatically from ""Products"";
   override with --entity if the automatic singular is wrong, e.g. irregular
   plurals like ""Categories"" -> ""Category"" is handled, but exotic cases may
   need --entity explicitly.)

  Wires internal references (always, regardless of Shared projects):
    Application     -> Domain, Contracts
    Infrastructure  -> Domain, Application
    Presentation    -> Application, Contracts
    UnitTests       -> Application, Presentation

  Also adds a FrameworkReference to Microsoft.AspNetCore.App to the Presentation
  layer's csproj, since the generated {Entity}Controller inherits from
  ControllerBase for ASP.NET Core Web API.

  Then wires Shared references as best-effort (skipped with a warning if the
  Shared project isn't found, so a missing Shared project never blocks the
  internal module wiring):
    Domain          -> MBPOS.Shared.Domain
    Application     -> MBPOS.Shared.Application
    Infrastructure  -> MBPOS.Shared.Infrastructure

  Shared projects are expected at src/Shared/MBPOS.Shared.<Layer> by default;
  override the root with --shared-root if your Shared projects use a
  different prefix.

EXAMPLES:

  Class library, using an existing folder by partial name:
    dn g classlib --name MBPOS.Modules.Identity.Presentation --location identity

  Full explicit path (always works, no resolution needed):
    dn g classlib --name MBPOS.Modules.Sales.Domain --location src/Modules/Sales

  New module folder under an existing parent:
    dn g classlib --name MBPOS.Modules.Vendors.Domain --location modules/vendors

  Full module scaffold with stub files:
    dn g m --name MBPOS.Modules.Products --location src/Modules

  Same, with an explicit entity name (irregular plural, or just to be explicit):
    dn g m --name MBPOS.Modules.Inventory --location src/Modules --entity InventoryItem

  Preview a module scaffold before creating anything:
    dn g m --name MBPOS.Modules.Products --location src/Modules --dry-run

  Web API host:
    dn g webapi --name MBPOS.Api --location host

  Point to a specific solution file explicitly:
    dn g m --name MBPOS.Modules.Products --location src/Modules --sln MBPOS.slnx

NOTES:
  - If --location resolves to an existing folder, the tool reuses it instead of failing.
  - If the target project (or module) folder already exists, the tool aborts to avoid overwriting.
  - The tool auto-detects .sln or .slnx files in the current directory; use --sln to override.
  - Run this tool from your solution root so folder scanning and .sln detection work correctly.
");
}