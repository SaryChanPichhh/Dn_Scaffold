using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Intrinsics.X86;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;

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

if (action == "link" || action == "l")
{
    string? linkName = GetArgValue(args, "--name") ?? GetArgValue(args, "-n");
    string? linkLocation = GetArgValue(args, "--location") ?? GetArgValue(args, "-l");
    string? linkHost = GetArgValue(args, "--host");
    bool linkDryRun = HasFlag(args, "--dry-run");

    if (string.IsNullOrWhiteSpace(linkName) || string.IsNullOrWhiteSpace(linkLocation) || string.IsNullOrWhiteSpace(linkHost))
    {
        Console.WriteLine("Error: --name, --location, and --host are all required for the link action.");
        Console.WriteLine("Example: dn link --name MBPOS.Modules.Products --location src/Modules --host src/Host/MBPOS.Api");
        return;
    }

    string? resolvedLinkLocation = ResolveLocation(linkLocation, Directory.GetCurrentDirectory());
    if (resolvedLinkLocation == null) return; // ambiguous match - options already printed

    LinkHost(linkName, Path.GetFullPath(resolvedLinkLocation), linkHost, linkDryRun);
    return;
}

if (action != "g" && action != "generate")
{
    Console.WriteLine($"Error: unknown action \"{action}\". Currently supported: g (generate), link (wire host references into an existing module)");
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
string? hostFlag = GetArgValue(args, "--host");

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
    GenerateModule(nameArg, fullLocation, slnPath, sharedRoot, entityFlag, skipShared, hostFlag, isDryRun);
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

// Given a --host value, resolves it to an actual .csproj file:
// - if it's already a .csproj path -> use as-is (must exist)
// - if it's a directory -> find a single *.csproj inside it
static string? ResolveHostCsproj(string hostArg, string searchRoot)
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

// ============================================================
// LINK action — "dn link --name MBPOS.Modules.Products --location src/Modules --host src/Host/MBPOS.Api"
//
// For a module that's already been scaffolded (via `dn g m`), wires the
// entry-point/host project's references to that module's Presentation,
// Application, and Infrastructure projects — the same three references
// shown in your example:
//
//   <ProjectReference Include="...MBPOS.Modules.Products.Presentation.csproj" />
//   <ProjectReference Include="...MBPOS.Modules.Products.Application.csproj" />
//   <ProjectReference Include="...MBPOS.Modules.Products.Infrastructure.csproj" />
//
// Does not touch Domain/Contracts/UnitTests, does not modify the module
// itself, and does not require a .sln (it only runs `dotnet add reference`).
// ============================================================
static void LinkHost(string prefix, string parentLocation, string hostArg, bool isDryRun)
{
    var segments = prefix.Split('.', StringSplitOptions.RemoveEmptyEntries);
    if (segments.Length < 1)
    {
        Console.WriteLine($"Error: \"{prefix}\" is not a valid module name (expected e.g. \"MBPOS.Modules.Products\").");
        return;
    }

    string moduleFolderName = segments[^1];
    string moduleRoot = Path.Combine(parentLocation, moduleFolderName);

    string LayerCsProj(string layer) =>
        Path.Combine(moduleRoot, $"{prefix}.{layer}", $"{prefix}.{layer}.csproj");

    string[] layersToLink = { "Presentation", "Application", "Infrastructure" };

    var missing = layersToLink.Where(l => !File.Exists(LayerCsProj(l))).ToList();
    if (missing.Count > 0)
    {
        Console.WriteLine($"Error: couldn't find the following project(s) under \"{moduleRoot}\":");
        foreach (var l in missing) Console.WriteLine($"  - {LayerCsProj(l)}");
        Console.WriteLine("Check --name/--location match the module you already scaffolded with 'dn g m'.");
        return;
    }

    string? hostCsproj = ResolveHostCsproj(hostArg, Directory.GetCurrentDirectory());
    if (hostCsproj == null)
    {
        Console.WriteLine("Aborting: could not resolve --host to a single .csproj.");
        return;
    }

    if (isDryRun)
    {
        Console.WriteLine("=== DRY RUN (link) — no changes will be made ===");
        Console.WriteLine();
        Console.WriteLine($"Host project: {hostCsproj}");
        Console.WriteLine();
        Console.WriteLine("[WILL WIRE HOST REFERENCES]");
        foreach (var l in layersToLink)
            Console.WriteLine($"  {hostCsproj}\n    <- {LayerCsProj(l)}");
        Console.WriteLine();
        Console.WriteLine("Run the same command without --dry-run to apply this change.");
        return;
    }

    var refs = layersToLink.Select(LayerCsProj).ToArray();
    var refArgs = string.Join(" ", refs.Select(r => $"\"{r}\""));

    if (RunDotnet($"add \"{hostCsproj}\" reference {refArgs}"))
    {
        Console.WriteLine($"Wired host references: {hostCsproj} -> Presentation, Application, Infrastructure");
    }
    else
    {
        Console.WriteLine($"Error: failed to add references to {hostCsproj}.");
    }
}

// ============================================================
// Module scaffold: creates Domain / Application / Infrastructure /
// Presentation / Contracts class libs + a UnitTests project, laid out
// with the standard sub-folder structure per layer (Entities,
// Abstractions, Requests/Responses, Persistence, Controllers,
// DependencyInjection, ...), replaces the default Class1.cs in each
// with real stub files for the given entity, wires up the standard
// layer references (Shared.* paths are fixed to {sharedRoot}.Shared.<Layer>,
// not derived from --name), optionally wires the module's Presentation /
// Application / Infrastructure projects into a host/entry-point project
// via --host, and registers everything in the solution.
//
//   prefix            e.g. "MBPOS.Modules.Products"
//   moduleFolderName  e.g. "Products"       (derived: last segment of prefix)
//   moduleRoot        e.g. "{location}/Products"
//   entity            e.g. "Product"        (derived: singularized moduleFolderName, or --entity)
// ============================================================
static void GenerateModule(string prefix, string parentLocation, string? slnPath, string sharedRoot, string? entityOverride, bool skipShared, string? hostArg, bool isDryRun)
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

    // Stub files to create per layer: (relative file path within the layer project, content).
    // Files are placed under the standard sub-folders (Entities, Abstractions, Persistence, ...)
    // rather than the project root. Deletes the default Class1.cs first.
    var model = entity + "Model";
    (string layer, (string file, string content)[] stubs)[] StubPlan() => new[]
    {
        ("Domain", new[]
        {
            (Path.Combine("Entities", $"{model}.cs"), $$"""
                namespace {{LayerNamespace("Domain")}}.Entities;

                public class {{model}}
                {
                    public Guid Id { get; private set; }
                    public string Name { get; init; }

                    // TODO: add {{model}} properties and domain behavior
                }
                """)
        }),
        ("Contracts", new[]
        {
            (Path.Combine("Requests", $"Create{entity}Request.cs"), $$"""
                namespace {{LayerNamespace("Contracts")}}.Requests;

                public class Create{{entity}}Request
                {
                    // TODO: add fields required to create a {{entity}}
                }
                """),
            (Path.Combine("Requests", $"Update{entity}Request.cs"), $$"""
                namespace {{LayerNamespace("Contracts")}}.Requests;

                public class Update{{entity}}Request
                {
                    public Guid Id { get; set; }

                    // TODO: add fields that can be updated on a {{entity}}
                }
                """),
            (Path.Combine("Responses", $"{entity}Response.cs"), $$"""
                namespace {{LayerNamespace("Contracts")}}.Responses;

                // Returned when fetching, creating, or updating a {{entity}}
                public class {{entity}}Response
                {
                    public Guid Id { get; set; }

                    // TODO: add {{entity}} fields returned to callers
                }
                """)
        }),
        ("Application", new[]
        {
            (Path.Combine("Abstractions", $"I{entity}Repository.cs"), $$"""
                using {{LayerNamespace("Domain")}}.Entities;

                namespace {{LayerNamespace("Application")}}.Abstractions;

                public interface I{{entity}}Repository
                {
                    Task<{{model}}?> GetByIdAsync(Guid id);
                    Task AddAsync({{model}} entity);
                    Task UpdateAsync({{model}} entity);
                    Task DeleteAsync(Guid id);
                }
                """),
            (Path.Combine("DependencyInjection", $"{moduleFolderName}ApplicationServiceCollectionExtensions.cs"), $$"""
                using Microsoft.Extensions.DependencyInjection;
                using FluentValidation;
                using {{LayerNamespace("Application")}}.Validation;
                using {{LayerNamespace("Application")}}.UseCases.Create;
                
                namespace {{LayerNamespace("Application")}}.DependencyInjection;

                public static class {{moduleFolderName}}ApplicationServiceCollectionExtensions
                {
                    public static IServiceCollection Add{{moduleFolderName}}Application(this IServiceCollection services)
                    {
                        // TODO: register application-layer services
                        ArgumentNullException.ThrowIfNull(services);
                
                        services.AddValidatorsFromAssemblyContaining<
                            Create{{moduleFolderName}}Command>();
                        
                        services.AddScoped<Create{{moduleFolderName}}Handler>();
                        return services;
                    }
                }
                """),
            (Path.Combine("UseCases","Create", $"Create{moduleFolderName}Command.cs"), $$"""
                  namespace {{LayerNamespace("Application")}}.UseCases.Create;

                  public sealed record Create{{moduleFolderName}}Command
                  (
                      Guid Id,
                      string Name
                  );
                  """),
            (Path.Combine("UseCases","Create", $"Create{moduleFolderName}Handler.cs"), $$"""
                  using {{LayerNamespace("Application")}}.Abstractions;
                  namespace {{LayerNamespace("Application")}}.UseCases.Create;

                  public sealed class Create{{moduleFolderName}}Handler
                  {
                      private readonly I{{entity}}Repository _repository;
                      public Create{{moduleFolderName}}Handler(I{{entity}}Repository repository){
                            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
                      }
                  }
                  """),
            (Path.Combine("UseCases","Create", $"Create{moduleFolderName}Result.cs"), $$"""
                  namespace {{LayerNamespace("Application")}}.UseCases.Create;
                  public abstract record Create{{moduleFolderName}}Result;
                  """),
            (Path.Combine("Validation", $"Create{moduleFolderName}CommandValidator.cs"), $$"""
                  using FluentValidation;
                  using {{LayerNamespace("Application")}}.UseCases.Create;
                  namespace {{LayerNamespace("Application")}}.Validation;
                  public sealed class Create{{moduleFolderName}}CommandValidator
                  : AbstractValidator<Create{{moduleFolderName}}Command>
                  {
                      public Create{{moduleFolderName}}CommandValidator()
                      {
                          RuleFor(command => command.Id)
                          .Must(value => !string.IsNullOrWhiteSpace(value.ToString()))
                          .WithMessage("Id is required");
                          
                          RuleFor(x => x.Name)
                              .NotNull().WithMessage("Name cannot be null.")
                              .NotEmpty().WithMessage("Name is required.")
                              .MaximumLength(100).WithMessage("Name must not exceed 100 characters.");
                      }
                  }
                  """)
        }),
        ("Infrastructure", new[]
        {
            (Path.Combine("Persistence", "Repositories", $"{entity}Repository.cs"), $$"""
                using {{LayerNamespace("Application")}}.Abstractions;
                using {{LayerNamespace("Domain")}}.Entities;
                using MBPOS.Shared.Infrastructure.Database;
                
                namespace {{LayerNamespace("Infrastructure")}}.Persistence.Repositories;

                public class {{entity}}Repository : I{{entity}}Repository
                {
                    private readonly ISqlDataAccess _sqlDataAccess;
                    public {{entity}}Repository(
                    ISqlDataAccess sqlDataAccess)
                    {
                        ArgumentNullException.ThrowIfNull(sqlDataAccess);
                        _sqlDataAccess = sqlDataAccess;
                    }
                    
                    public Task<{{model}}?> GetByIdAsync(Guid id)
                        => throw new NotImplementedException();

                    public Task AddAsync({{model}} entity)
                        => throw new NotImplementedException();

                    public Task UpdateAsync({{model}} entity)
                        => throw new NotImplementedException();

                    public Task DeleteAsync(Guid id)
                        => throw new NotImplementedException();
                }
                """),
            (Path.Combine("Persistence", "Queries", $"{moduleFolderName}Queries.cs"), $$""""
                namespace {{LayerNamespace("Infrastructure")}}.Persistence.Queries;

                // TODO: add read-only query methods for {{moduleFolderName}} (e.g. via Dapper or raw SQL)
                internal static class {{moduleFolderName}}Queries
                {
                    internal const string Insert{{moduleFolderName}} = """
                
                    """;  
                }
                """"),
            (Path.Combine("DependencyInjection", $"{moduleFolderName}InfrastructureServiceCollectionExtensions.cs"), $$"""
                using Microsoft.Extensions.DependencyInjection;
                using {{LayerNamespace("Application")}}.Abstractions;
                using {{LayerNamespace("Infrastructure")}}.Persistence.Repositories;

                namespace {{LayerNamespace("Infrastructure")}}.DependencyInjection;

                public static class {{moduleFolderName}}InfrastructureServiceCollectionExtensions
                {
                    public static IServiceCollection Add{{moduleFolderName}}Infrastructure(this IServiceCollection services)
                    {
                        services.AddScoped<I{{entity}}Repository, {{entity}}Repository>();

                        return services;
                    }
                }
                """)
        }),
        ("Presentation", new[]
        {
            (Path.Combine("Controllers", $"{entity}Controller.cs"), $$$"""
                using Microsoft.AspNetCore.Mvc;
                using Microsoft.AspNetCore.Authorization;
                using System.Diagnostics;
                using FluentValidation;
                using {{{LayerNamespace("Application")}}}.UseCases.Create;

                namespace {{{LayerNamespace("Presentation")}}}.Controllers;

                [ApiController]
                [AllowAnonymous]
                [Route("api/v1/{{{entity}}}")]
                [Produces("application/json")]
                public class {{{entity}}}Controller : ControllerBase
                {
                    private readonly Create{{{moduleFolderName}}}Handler _create{{{moduleFolderName}}}Handler;
                    private readonly IValidator<Create{{{moduleFolderName}}}Command> _create{{{moduleFolderName}}}Validator;
                    public {{{entity}}}Controller(Create{{{moduleFolderName}}}Handler create{{{moduleFolderName}}}Handler, IValidator<Create{{{moduleFolderName}}}Command> create{{{moduleFolderName}}}Validator)
                    {
                        //TODO : Inject Handle From UseCase
                        _create{{{moduleFolderName}}}Handler = create{{{moduleFolderName}}}Handler;
                        _create{{{moduleFolderName}}}Validator = create{{{moduleFolderName}}}Validator;
                    }

                    // TODO: add endpoints (Get, Create, Update, Delete)
                }
                """),
            (Path.Combine("DependencyInjection", $"{moduleFolderName}PresentationServiceCollectionExtensions.cs"), $$"""
                using Microsoft.Extensions.DependencyInjection;
                using {{LayerNamespace("Presentation")}}.Controllers;

                namespace {{LayerNamespace("Presentation")}}.DependencyInjection;

                public static class {{moduleFolderName}}PresentationServiceCollectionExtensions
                {
                    public static IMvcBuilder Add{{moduleFolderName}}Presentation(
                        this IMvcBuilder mvcBuilder)
                    {
                        ArgumentNullException.ThrowIfNull(mvcBuilder);

                        mvcBuilder.AddApplicationPart(
                            typeof({{entity}}Controller).Assembly);

                        return mvcBuilder;
                    }
                }
                """)
        })
    };

    // Empty sub-folders that don't get a stub file yet but should still exist
    // on disk to match the standard module layout.
    (string layer, string[] extraDirs)[] ExtraDirPlan() => new[]
    {
        ("Domain", [ "ValueObjects"]),
        ("Application", new[] { "UseCases", "Validation" }),
        ("Infrastructure", new[] { "Configuration", Path.Combine("Persistence", "Records"), "Services" }),
        ("Presentation", Array.Empty<string>()),
        ("Contracts", Array.Empty<string>())
    };

    string? hostCsproj = hostArg != null ? ResolveHostCsproj(hostArg, Directory.GetCurrentDirectory()) : null;
    if (hostArg != null && hostCsproj == null && !isDryRun)
    {
        Console.WriteLine("Continuing without host wiring.");
    }

    if (isDryRun)
    {
        Console.WriteLine("=== DRY RUN (module) — no changes will be made ===");
        Console.WriteLine();
        Console.WriteLine($"Module root: {moduleRoot}");
        Console.WriteLine($"Entity:      {model}  (from {(entityOverride != null ? "--entity" : $"singularized \"{moduleFolderName}\"")})");
        Console.WriteLine($"Shared root: {sharedRoot}  (from {(GetShownSharedRootSource())})");
        Console.WriteLine();

        var stubPlan = StubPlan();
        var extraDirPlan = ExtraDirPlan();

        foreach (var (layer, stubs) in stubPlan)
        {
            Console.WriteLine($"[WILL CREATE] classlib  {prefix}.{layer}  -> {LayerDir(layer)}");
            Console.WriteLine($"[WILL DELETE]           {Path.Combine(LayerDir(layer), "Class1.cs")}");
            foreach (var (file, _) in stubs)
                Console.WriteLine($"[WILL CREATE]           {Path.Combine(LayerDir(layer), file)}");

            var extraDirs = extraDirPlan.First(p => p.layer == layer).extraDirs;
            foreach (var dir in extraDirs)
                Console.WriteLine($"[WILL CREATE DIR]       {Path.Combine(LayerDir(layer), dir)}  (+ README.md)");
        }
        Console.WriteLine($"[WILL CREATE] xunit     {testName}  -> {testDir}");
        Console.WriteLine($"[WILL DELETE]           {Path.Combine(testDir, "UnitTest1.cs")}");
        Console.WriteLine($"[WILL CREATE]           {Path.Combine(testDir, "Controllers", $"{entity}ControllerTests.cs")}");
        Console.WriteLine();
        Console.WriteLine("[WILL WIRE INTERNAL REFERENCES] (always applied)");
        Console.WriteLine($"  Contracts       -> Domain");
        Console.WriteLine($"  Application     -> Contracts, Domain");
        Console.WriteLine($"  Infrastructure  -> Application, Contracts, Domain");
        Console.WriteLine($"  Presentation    -> Application, Contracts");
        Console.WriteLine($"  UnitTests       -> Application, Presentation");
        Console.WriteLine();
        Console.WriteLine("[WILL ADD FRAMEWORK REFERENCE]");
        Console.WriteLine($"  Presentation    -> Microsoft.AspNetCore.App  (for ControllerBase / IMvcBuilder)");
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
        if (hostArg != null)
        {
            Console.WriteLine(hostCsproj != null
                ? $"[WILL WIRE HOST REFERENCES] {hostCsproj}\n" +
                  $"  <- Presentation, Application, Infrastructure"
                : $"[SKIP] --host \"{hostArg}\" could not be resolved to a single .csproj — host wiring skipped.");
            Console.WriteLine();
        }
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

    // 1. Create each layer's class library, strip the default Class1.cs, add stub files,
    //    and create the standard (currently empty) sub-folders for that layer.
    var stubPlanReal = StubPlan();
    var extraDirPlanReal = ExtraDirPlan();

    foreach (var (layer, stubs) in stubPlanReal)
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

        var extraDirs = extraDirPlanReal.First(p => p.layer == layer).extraDirs;
        foreach (var dir in extraDirs)
        {
            string dirPath = Path.Combine(outDir, dir);
            Directory.CreateDirectory(dirPath);

            // Empty folders aren't tracked by git and won't show up in Visual Studio /
            // Rider's Solution Explorer, since SDK-style projects only surface folders
            // that contain at least one item matched by the implicit glob — and that
            // glob explicitly EXCLUDES dot-files (**/.*), so ".gitkeep" doesn't work.
            // Use a plain, non-dot filename instead so it's picked up as a <None> item.
            string dirLabel = dir.Replace(Path.DirectorySeparatorChar, '/').Split('/').Last();
            string keepFilePath = Path.Combine(dirPath, "README.md");
            File.WriteAllText(keepFilePath, $"Reserved for {dirLabel}. Safe to delete this file once real content lives here.{Environment.NewLine}");

            Console.WriteLine($"  created dir {dirPath}  (+ README.md)");
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
    // These never depend on Shared projects existing, so Contracts/Application/
    // Infrastructure/Presentation always get their internal references wired
    // even if Shared.* isn't there yet.
    if (!AddReference(LayerCsProj("Contracts"),
            LayerCsProj("Domain"))) return;

    if (!AddReference(LayerCsProj("Application"),
            LayerCsProj("Contracts"), LayerCsProj("Domain"))) return;

    if (!AddReference(LayerCsProj("Infrastructure"),
            LayerCsProj("Application"), LayerCsProj("Contracts"), LayerCsProj("Domain"))) return;

    if (!AddReference(LayerCsProj("Presentation"),
            LayerCsProj("Application"), LayerCsProj("Contracts"))) return;

    // 3a. Presentation needs the ASP.NET Core framework reference for ControllerBase / IMvcBuilder.
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

    // 3c. Wire the module's entry-point-facing projects into a host project, if --host was given.
    // Best-effort: a missing/ambiguous host never blocks the rest of the module scaffold.
    if (hostCsproj != null)
    {
        if (AddReference(hostCsproj, LayerCsProj("Presentation"), LayerCsProj("Application"), LayerCsProj("Infrastructure")))
            Console.WriteLine($"  Wired host references: {hostCsproj} -> Presentation, Application, Infrastructure");
        else
            Console.WriteLine($"  Warning: failed to wire host references into {hostCsproj}");
    }
    else if (hostArg != null)
    {
        Console.WriteLine("Skipped host wiring (could not resolve --host to a single .csproj).");
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
        using {{LayerNamespace("Presentation")}}.Controllers;

        namespace {{testName}}.Controllers;

        public class {{entity}}ControllerTests
        {
            [Fact]
            public void Controller_Can_Be_Instantiated()
            {
                var controller = new {{entity}}Controller(null!);

                Assert.NotNull(controller);
            }

            // TODO: add tests for each {{entity}}Controller endpoint once implemented
        }

        """);
    Console.WriteLine($"  created {controllerTestPath}");

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
}

static void PrintHelp()
{
    Console.WriteLine(@"
dn — scaffold new .NET projects (or a full module) into your solution structure.
Locations are resolved dynamically against your existing folder structure — no config file needed.

USAGE:
  dn <action> <type> --name <name> --location <location> [options]

ACTIONS:
  g, generate    Generate a new project or module
  l, link        Wire an entry-point/host project's references to an
                  ALREADY-SCAFFOLDED module's Presentation, Application,
                  and Infrastructure projects. Use this when the module
                  already exists and you just need to hook it into a host
                  (e.g. MBPOS.Api) without regenerating anything.

                  dn link --name MBPOS.Modules.Products --location src/Modules --host src/Host/MBPOS.Api

                  Adds, to the host's .csproj:
                    <ProjectReference Include=""...MBPOS.Modules.Products.Presentation.csproj"" />
                    < ProjectReference Include = ""...MBPOS.Modules.Products.Application.csproj"" />
                    < ProjectReference Include = ""...MBPOS.Modules.Products.Infrastructure.csproj"" />

                  --name and--location must match what you originally passed
                  to 'dn g m'(same module prefix, same parent location).
                  --host works the same way as in 'g m' — a.csproj path, or
                  a folder containing exactly one.csproj.Supports--dry - run.

TYPE:
  Positional argument. Either:
m, module Full module scaffold: Domain, Application, Infrastructure,
                Presentation, Contracts +a UnitTests project, laid out with
                    the standard sub-folder structure per layer, with real stub
                    files (not the default Class1.cs) and the standard layer
                    references wired up, all added to your solution.
    <template>     Any valid dotnet template shortname for a single project:
                    classlib console   webapi   web   mvc   razor
                    blazorwasm blazorserver   worker   xunit   nunit   mstest
                    wpf   winforms   grpc

  Run 'dotnet new list' to see every template installed on your machine.

OPTIONS:
  -n, --name(required)  Project name, OR for module type, the module
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
                                   folder name, singularized (""Products""-> ""Product"").
  -r, --shared-root    (optional) Root prefix for Shared project references
                                   (module type only). Defaults to ""MBPOS"", producing
                                   src/Shared/MBPOS.Shared.<Layer>. This is fixed and
                                   NOT derived from --name, so a differently-prefixed
                                   --name (e.g. one starting with ""tests"") won't point
                                   references at the wrong shared projects.
      --no-shared     (optional)  Skip wiring Shared.* references entirely (module type
                                   only). Internal module references are ALWAYS wired
                                   regardless of this flag or whether Shared projects
                                   exist — Shared references are best-effort and just
                                   get skipped with a warning if the target Shared
                                   project isn't found on disk.
      --host          (optional)  Path to an entry-point (host) .csproj, or a folder
                                   containing exactly one .csproj (module type only).
                                   Adds ProjectReferences from the host to the module's
                                   Presentation, Application, and Infrastructure
                                   projects. Best-effort — skipped with a warning if
                                   the host can't be resolved; never blocks the rest
                                   of the module scaffold.
      --dry-run       (optional)  Preview what would happen without creating anything.
  -h, --help                      Show this help message.

HOW LOCATION RESOLUTION WORKS:

  Say your disk already has: src / Modules / Identity

  dn g classlib--name X --location identity
    → matches existing folder ""src/Modules/Identity"" exactly → uses it

  dn g classlib --name X --location modules/vendors
    → ""modules"" matches ""src/Modules"", ""vendors"" doesn't exist yet
    → resolves to ""src/Modules/vendors"" (new subfolder, created fresh)

  dn g classlib --name X --location brandnewpath/here
    → no match anywhere on disk → created exactly as typed: ""brandnewpath / here""

  dn g classlib--name X --location domain
    → matches MULTIPLE existing folders (e.g. every module has a Domain folder)
    → tool lists all matches and asks you to be more specific instead of guessing

MODULE TYPE (m / module):

  dn g m--name MBPOS.Modules.Products --location src/Modules

  Creates, under src/Modules/Products:

    MBPOS.Modules.Products.Domain /
      Entities /
        Product.cs
      Services / (empty, reserved for domain services)
    ValueObjects / (empty, reserved for value objects)

    MBPOS.Modules.Products.Contracts /
      Requests /
        CreateProductRequest.cs
        UpdateProductRequest.cs
      Responses/
        ProductResponse.cs

    MBPOS.Modules.Products.Application/
      Abstractions/
        IProductRepository.cs
      DependencyInjection/
        ProductsApplicationServiceCollectionExtensions.cs
      UseCases/              (empty)
      Validation/            (empty)

    MBPOS.Modules.Products.Infrastructure/
      Persistence/
        Repositories/
          ProductRepository.cs
        Queries/
          ProductsQueries.cs
        Records/                (empty)
      DependencyInjection/
        ProductsInfrastructureServiceCollectionExtensions.cs
      Configuration/           (empty)
      Services/                 (empty)

    MBPOS.Modules.Products.Presentation/
      Controllers/
        ProductController.cs
      DependencyInjection/
        ProductsPresentationServiceCollectionExtensions.cs

  plus UnitTests/MBPOS.Modules.Products.UnitTests/
    Controllers/
      ProductControllerTests.cs

  (Entity name ""Product"" is singularized automatically from ""Products"";
override with --entity if the automatic singular is wrong, e.g.irregular
plurals like ""Categories"" -> ""Category"" is handled, but exotic cases may
   need --entity explicitly.)

  Wires internal references (always, regardless of Shared projects):
    Contracts->Domain
    Application->Contracts, Domain
    Infrastructure  -> Application, Contracts, Domain
    Presentation    -> Application, Contracts
    UnitTests       -> Application, Presentation

  Also adds a FrameworkReference to Microsoft.AspNetCore.App to the Presentation
  layer's csproj, since the generated {Entity}Controller inherits from
  ControllerBase, and the generated {ModuleName}PresentationServiceCollectionExtensions
  exposes an Add{ModuleName}Presentation(this IMvcBuilder) extension that registers
  the controller's assembly as an application part.

  Then wires Shared references as best-effort (skipped with a warning if the
  Shared project isn't found, so a missing Shared project never blocks the
  internal module wiring):
    Domain->MBPOS.Shared.Domain
    Application->MBPOS.Shared.Application
    Infrastructure->MBPOS.Shared.Infrastructure

  Shared projects are expected at src/Shared/MBPOS.Shared.<Layer> by default;
  override the root with --shared-root if your Shared projects use a
  different prefix.

  If --host is given, also wires the host project's references:
    <host>.csproj -> Presentation, Application, Infrastructure

EXAMPLES:

  Class library, using an existing folder by partial name:
    dn g classlib--name MBPOS.Modules.Identity.Presentation --location identity

  Full explicit path (always works, no resolution needed):
    dn g classlib--name MBPOS.Modules.Sales.Domain --location src/Modules/Sales

  New module folder under an existing parent:
    dn g classlib--name MBPOS.Modules.Vendors.Domain --location modules/vendors

  Full module scaffold with stub files:
    dn g m--name MBPOS.Modules.Products --location src/Modules

  Same, with an explicit entity name (irregular plural, or just to be explicit):
    dn g m--name MBPOS.Modules.Inventory --location src/Modules --entity InventoryItem

  Module scaffold that also wires into the API host:
    dn g m--name MBPOS.Modules.Products --location src/Modules --host src/Host/MBPOS.Api

  Wire an ALREADY-SCAFFOLDED module into the API host (module already exists):
    dn link --name MBPOS.Modules.Products --location src/Modules --host src/Host/MBPOS.Api

  Preview a module scaffold before creating anything:
    dn g m--name MBPOS.Modules.Products --location src/Modules --dry-run

  Web API host:
    dn g webapi--name MBPOS.Api --location host

  Point to a specific solution file explicitly:
    dn g m--name MBPOS.Modules.Products --location src/Modules --sln MBPOS.slnx

NOTES:
  -If--location resolves to an existing folder, the tool reuses it instead of failing.
  - If the target project (or module) folder already exists, the tool aborts to avoid overwriting.
  - The tool auto-detects .sln or .slnx files in the current directory; use--sln to override.
  - Run this tool from your solution root so folder scanning, .sln detection, and --host
    resolution work correctly.
  - The generated Infrastructure Queries stub is a placeholder for read-side query methods
    (e.g. Dapper / raw SQL); it does not depend on any ORM out of the box.
");
}