namespace ShortHandCreater;

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
internal static class ModuleGenerator
{
    internal static void Run(string prefix, string parentLocation, string? slnPath, string sharedRoot, string? entityOverride, bool skipShared, string? hostArg, bool isDryRun)
    {
        string[] layers = { "Domain", "Application", "Infrastructure", "Presentation", "Contracts" };

        var segments = prefix.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 1)
        {
            Console.WriteLine($"Error: \"{prefix}\" is not a valid module name (expected e.g. \"MBPOS.Modules.Products\").");
            return;
        }

        string moduleFolderName = segments[^1];                          // e.g. Products
        string entity = entityOverride ?? StringUtils.Singularize(moduleFolderName); // e.g. Product
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

        var model = entity + "Model";

        // Stub files to create per layer: (relative file path within the layer project, content).
        // Files are placed under the standard sub-folders (Entities, Abstractions, Persistence, ...)
        // rather than the project root. Deletes the default Class1.cs first.
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

        string? hostCsproj = hostArg != null ? HostResolver.ResolveHostCsproj(hostArg, Directory.GetCurrentDirectory()) : null;
        if (hostArg != null && hostCsproj == null && !isDryRun)
        {
            Console.WriteLine("Continuing without host wiring.");
        }

        if (isDryRun)
        {
            PrintDryRun(prefix, moduleRoot, model, entityOverride, moduleFolderName, sharedRoot,
                        slnPath, hostArg, hostCsproj, skipShared, testName, testDir, entity,
                        StubPlan, ExtraDirPlan, LayerDir, LayerCsProj, SharedCsProj);
            return;
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

            if (!DotnetCli.RunDotnet($"new classlib -n \"{name}\" -o \"{outDir}\""))
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
            if (!DotnetCli.RunDotnet($"sln \"{slnPath}\" add \"{LayerCsProj(layer)}\""))
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
        if (!DotnetCli.RunDotnet($"new xunit -n {testName} -o \"{testDir}\""))
        {
            Console.WriteLine($"Failed creating \"{testName}\". Aborting.");
            return;
        }
        if (!DotnetCli.RunDotnet($"sln \"{slnPath}\" add \"{testCsProj}\""))
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

        // ----- forced NuGet packages -----

        var forcedPackages = new (string layer, string package)[]
        {
            ("Application", "FluentValidation"),
            ("Application", "Microsoft.Extensions.DependencyInjection.Abstractions"),
            ("Application", "FluentValidation.DependencyInjectionExtensions"),
            ("Infrastructure", "Microsoft.Extensions.DependencyInjection.Abstractions"),
            ("Presentation", "Microsoft.Extensions.DependencyInjection.Abstractions"),
            ("Presentation", "FluentValidation"),
        };

        foreach (var (layer, package) in forcedPackages)
        {
            if (AddPackage(LayerCsProj(layer), package))
                Console.WriteLine($"  Installed package \"{package}\" into {prefix}.{layer}.");
            else
                Console.WriteLine($"  Warning: failed to install package \"{package}\" into {prefix}.{layer}.");
        }

        if (hostCsproj != null)
        {
            if (AddReference(hostCsproj, LayerCsProj("Presentation"), LayerCsProj("Application"), LayerCsProj("Infrastructure")))
            {
                Console.WriteLine($"  Wired host references: {hostCsproj} -> Presentation, Application, Infrastructure");
                AddServiceInjectionInEntryPoints.PatchProgramCs(hostCsproj, prefix, moduleFolderName, isDryRun);
            }
            else
            {
                Console.WriteLine($"  Warning: failed to wire host references into {hostCsproj}");
            }
        }
    }

    // ----- private helpers -----

    private static bool AddReference(string projectPathOrDir, params string[] references)
    {
        var refs = string.Join(" ", references.Select(r => $"\"{r}\""));
        return DotnetCli.RunDotnet($"add \"{projectPathOrDir}\" reference {refs}");
    }

    // Best-effort: only attempts the reference if the target Shared csproj actually
    // exists on disk. If it doesn't, prints a warning and moves on instead of failing
    // the whole scaffold.
    private static void TryAddSharedReference(string projectPathOrDir, string sharedCsprojPath)
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

    private static bool AddPackage(string projectPathOrDir, string package)
    {
        var parts = package.Split('@', 2); // "Name" or "Name@Version"
        string arg = parts.Length == 2 ? $"{parts[0]} -v {parts[1]}" : parts[0];
        return DotnetCli.RunDotnet($"add \"{projectPathOrDir}\" package {arg}");
    }

    // ----- dry-run output -----

    private static void PrintDryRun(
        string prefix, string moduleRoot, string model, string? entityOverride, string moduleFolderName,
        string sharedRoot, string? slnPath, string? hostArg, string? hostCsproj, bool skipShared,
        string testName, string testDir, string entity,
        Func<(string layer, (string file, string content)[] stubs)[]> stubPlanFactory,
        Func<(string layer, string[] extraDirs)[]> extraDirPlanFactory,
        Func<string, string> layerDir,
        Func<string, string> layerCsProj,
        Func<string, string> sharedCsProj)
    {
        Console.WriteLine("=== DRY RUN (module) — no changes will be made ===");
        Console.WriteLine();
        Console.WriteLine($"Module root: {moduleRoot}");
        Console.WriteLine($"Entity:      {model}  (from {(entityOverride != null ? "--entity" : $"singularized \"{moduleFolderName}\"")})");
        Console.WriteLine($"Shared root: {sharedRoot}  (from {(sharedRoot == "MBPOS" ? "default" : "--shared-root")})");
        Console.WriteLine();

        var stubPlan = stubPlanFactory();
        var extraDirPlan = extraDirPlanFactory();

        foreach (var (layer, stubs) in stubPlan)
        {
            Console.WriteLine($"[WILL CREATE] classlib  {prefix}.{layer}  -> {layerDir(layer)}");
            Console.WriteLine($"[WILL DELETE]           {Path.Combine(layerDir(layer), "Class1.cs")}");
            foreach (var (file, _) in stubs)
                Console.WriteLine($"[WILL CREATE]           {Path.Combine(layerDir(layer), file)}");

            var extraDirs = extraDirPlan.First(p => p.layer == layer).extraDirs;
            foreach (var dir in extraDirs)
                Console.WriteLine($"[WILL CREATE DIR]       {Path.Combine(layerDir(layer), dir)}  (+ README.md)");
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
            Console.WriteLine($"  Domain          -> {sharedCsProj("Domain")}{(File.Exists(sharedCsProj("Domain")) ? "" : "  [NOT FOUND]")}");
            Console.WriteLine($"  Application     -> {sharedCsProj("Application")}{(File.Exists(sharedCsProj("Application")) ? "" : "  [NOT FOUND]")}");
            Console.WriteLine($"  Infrastructure  -> {sharedCsProj("Infrastructure")}{(File.Exists(sharedCsProj("Infrastructure")) ? "" : "  [NOT FOUND]")}");
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
    }
}
