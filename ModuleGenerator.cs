namespace ShortHandCreater;

// ============================================================
// Module scaffold: creates Domain / Application / Infrastructure /
// Presentation / Contracts class libs + a UnitTests project, laid out
// with the standard sub-folder structure per layer, replaces the default
// Class1.cs in each with real starter files, wires up the standard
// layer references (Shared.* paths are fixed to {sharedRoot}.Shared.<Layer>,
// not derived from --name), optionally wires the module's Presentation /
// Application / Infrastructure projects into a host/entry-point project
// via --host, and registers everything in the solution.
//
//   prefix            e.g. "MBPOS.Modules.Products"
//   moduleFolderName  e.g. "Products"       (derived: last segment of prefix)
//   moduleRoot        e.g. "{location}/Products"
//   entity            e.g. "Product"        (derived: singularized moduleFolderName, or --entity)
//   entities          e.g. "Products"       (derived: pluralized entity)
//   useCaseName       e.g. "CreateProduct"  (derived: Create{entity})
// ============================================================
internal static class ModuleGenerator
{
    private static readonly string[] Layers =
        { "Domain", "Application", "Infrastructure", "Presentation", "Contracts" };

    private static readonly (string Layer, string Package)[] ForcedPackages =
    {
        ("Application", "FluentValidation"),
        ("Application", "Microsoft.Extensions.DependencyInjection.Abstractions"),
        ("Application", "FluentValidation.DependencyInjectionExtensions"),
        ("Infrastructure", "Microsoft.Extensions.DependencyInjection.Abstractions"),
        ("Presentation", "Microsoft.Extensions.DependencyInjection.Abstractions"),
        ("Presentation", "FluentValidation"),
        ("Presentation", "Microsoft.AspNetCore.Http.Abstractions"),
    };

    private sealed record StubFile(string RelativePath, string Content);

    private sealed record ModuleContext(
        string Prefix,
        string ModuleName,
        string Entity,
        string Entities,
        string Model,
        string UseCaseName,
        string UseCaseCamel,
        string ModuleRoot,
        string SharedRoot,
        bool SkipShared,
        bool HasSharedInfrastructure,
        string TestName,
        string TestDir,
        string TestCsProj)
    {
        public string LayerDir(string layer) => Path.Combine(ModuleRoot, $"{Prefix}.{layer}");
        public string LayerCsProj(string layer) => Path.Combine(LayerDir(layer), $"{Prefix}.{layer}.csproj");
        public string LayerNamespace(string layer) => $"{Prefix}.{layer}";
        public string SharedCsProj(string layer) => GetSharedCsProjPath(SharedRoot, layer);
    }

    internal static void Run(
        string prefix,
        string parentLocation,
        string? slnPath,
        string sharedRoot,
        string? entityOverride,
        bool skipShared,
        string? hostArg,
        bool isDryRun)
    {
        var segments = prefix.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 1)
        {
            Console.WriteLine($"Error: \"{prefix}\" is not a valid module name (expected e.g. \"MBPOS.Modules.Products\").");
            return;
        }

        string moduleName = segments[^1];
        string entity = entityOverride ?? StringUtils.Singularize(moduleName);
        string entities = StringUtils.Pluralize(entity);
        string useCaseName = $"Create{entity}";
        string sharedInfraCsproj = GetSharedCsProjPath(sharedRoot, "Infrastructure");
        string testName = $"{prefix}.UnitTests";
        string testDir = Path.Combine("UnitTests", testName);

        var ctx = new ModuleContext(
            Prefix: prefix,
            ModuleName: moduleName,
            Entity: entity,
            Entities: entities,
            Model: $"{entity}Model",
            UseCaseName: useCaseName,
            UseCaseCamel: StringUtils.ToCamelCase(useCaseName),
            ModuleRoot: Path.Combine(parentLocation, moduleName),
            SharedRoot: sharedRoot,
            SkipShared: skipShared,
            HasSharedInfrastructure: !skipShared && File.Exists(sharedInfraCsproj),
            TestName: testName,
            TestDir: testDir,
            TestCsProj: Path.Combine(testDir, $"{testName}.csproj"));

        var stubPlan = BuildStubPlan(ctx);
        var extraDirPlan = BuildExtraDirPlan();
        var unitTestFiles = BuildUnitTestFiles(ctx);

        string? hostCsproj = hostArg != null
            ? HostResolver.ResolveHostCsproj(hostArg, Directory.GetCurrentDirectory())
            : null;
        if (hostArg != null && hostCsproj == null && !isDryRun)
        {
            Console.WriteLine("Continuing without host wiring.");
        }

        if (isDryRun)
        {
            PrintDryRun(ctx, entityOverride, slnPath, hostArg, hostCsproj, stubPlan, extraDirPlan, unitTestFiles);
            return;
        }

        if (Directory.Exists(ctx.ModuleRoot))
        {
            Console.WriteLine($"Error: module folder already exists: \"{ctx.ModuleRoot}\". Aborting to avoid overwrite.");
            return;
        }

        if (Directory.Exists(ctx.TestDir))
        {
            Console.WriteLine($"Error: unit test folder already exists: \"{ctx.TestDir}\". Aborting to avoid overwrite.");
            return;
        }

        if (slnPath == null)
        {
            Console.WriteLine("Error: no solution file found or specified (use --sln). A module needs a solution to register its projects in.");
            return;
        }

        Directory.CreateDirectory(ctx.ModuleRoot);
        Console.WriteLine($"Created module root: {ctx.ModuleRoot}");
        Console.WriteLine($"Entity name: {ctx.Entity}");

        if (!CreateLayerProjects(ctx, slnPath, stubPlan, extraDirPlan)) return;
        if (!WireProjectReferences(ctx)) return;
        if (!CreateUnitTestProject(ctx, slnPath, unitTestFiles)) return;

        InstallForcedPackages(ctx);
        WireHostProject(ctx, hostArg, hostCsproj, isDryRun);

        Console.WriteLine();
        Console.WriteLine("============================================================");
        Console.WriteLine($" Module \"{ctx.ModuleName}\" ({ctx.Prefix}) scaffolded successfully.");
        Console.WriteLine("============================================================");
    }

    // ----- stub & folder plans -----

    private static (string Layer, StubFile[] Stubs)[] BuildStubPlan(ModuleContext ctx) =>
    [
        ("Domain", BuildDomainStubs(ctx)),
        ("Contracts", BuildContractsStubs(ctx)),
        ("Application", BuildApplicationStubs(ctx)),
        ("Infrastructure", BuildInfrastructureStubs(ctx)),
        ("Presentation", BuildPresentationStubs(ctx)),
    ];

    private static StubFile[] BuildDomainStubs(ModuleContext ctx) =>
    [
        new(Path.Combine("Entities", $"{ctx.Model}.cs"), $$"""
            namespace {{ctx.LayerNamespace("Domain")}}.Entities;

            public class {{ctx.Model}}
            {
                public Guid Id { get; private set; }
                public string Name { get; init; } = string.Empty;

                // TODO: add {{ctx.Model}} properties and domain behavior
            }
            """)
    ];

    private static StubFile[] BuildContractsStubs(ModuleContext ctx) =>
    [
        new(Path.Combine("Requests", $"Create{ctx.Entity}Request.cs"), $$"""
            namespace {{ctx.LayerNamespace("Contracts")}}.Requests;

            public class Create{{ctx.Entity}}Request
            {
                public string Name { get; set; } = string.Empty;

                // TODO: add fields required to create a {{ctx.Entity}}
            }
            """),
        new(Path.Combine("Requests", $"Update{ctx.Entity}Request.cs"), $$"""
            namespace {{ctx.LayerNamespace("Contracts")}}.Requests;

            public class Update{{ctx.Entity}}Request
            {
                public Guid Id { get; set; }
                public string Name { get; set; } = string.Empty;

                // TODO: add fields that can be updated on a {{ctx.Entity}}
            }
            """),
        new(Path.Combine("Responses", $"{ctx.Entity}Response.cs"), $$"""
            namespace {{ctx.LayerNamespace("Contracts")}}.Responses;

            // Returned when fetching, creating, or updating a {{ctx.Entity}}
            public class {{ctx.Entity}}Response
            {
                public Guid Id { get; set; }
                public string Name { get; set; } = string.Empty;

                // TODO: add {{ctx.Entity}} fields returned to callers
            }
            """)
    ];

    private static StubFile[] BuildApplicationStubs(ModuleContext ctx) =>
    [
        new(Path.Combine("Abstractions", $"I{ctx.Entity}Repository.cs"), $$"""
            using {{ctx.LayerNamespace("Domain")}}.Entities;

            namespace {{ctx.LayerNamespace("Application")}}.Abstractions;

            public interface I{{ctx.Entity}}Repository
            {
                Task<{{ctx.Model}}?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
                Task AddAsync({{ctx.Model}} entity, CancellationToken cancellationToken = default);
                Task UpdateAsync({{ctx.Model}} entity, CancellationToken cancellationToken = default);
                Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
            }
            """),
        new(Path.Combine("DependencyInjection", $"{ctx.ModuleName}ApplicationServiceCollectionExtensions.cs"), $$"""
            using FluentValidation;
            using Microsoft.Extensions.DependencyInjection;
            using {{ctx.LayerNamespace("Application")}}.UseCases.{{ctx.UseCaseName}};
            using {{ctx.LayerNamespace("Application")}}.Validation;

            namespace {{ctx.LayerNamespace("Application")}}.DependencyInjection;

            public static class {{ctx.ModuleName}}ApplicationServiceCollectionExtensions
            {
                public static IServiceCollection Add{{ctx.ModuleName}}Application(this IServiceCollection services)
                {
                    ArgumentNullException.ThrowIfNull(services);

                    services.AddValidatorsFromAssemblyContaining<{{ctx.UseCaseName}}CommandValidator>();
                    services.AddScoped<{{ctx.UseCaseName}}Handler>();

                    // TODO: register additional application-layer services and handlers

                    return services;
                }
            }
            """),
        new(Path.Combine("Option", $"{ctx.ModuleName}Options.cs"), $$"""
            namespace {{ctx.LayerNamespace("Application")}}.Option;

            public sealed class {{ctx.ModuleName}}Options
            {
                public const string SectionName = "{{ctx.ModuleName}}";

                // TODO: add {{ctx.ModuleName}} module configuration options
            }
            """),
        new(Path.Combine("Queries", $"Get{ctx.Entities}Query.cs"), $$"""
            namespace {{ctx.LayerNamespace("Application")}}.Queries;

            public sealed record Get{{ctx.Entities}}Query
            {
                // TODO: add filter and pagination parameters for querying {{ctx.Entities}}
            }
            """),
        new(Path.Combine("UseCases", ctx.UseCaseName, $"{ctx.UseCaseName}Command.cs"), $$"""
            namespace {{ctx.LayerNamespace("Application")}}.UseCases.{{ctx.UseCaseName}};

            public sealed record {{ctx.UseCaseName}}Command(
                Guid Id,
                string Name
            );
            """),
        new(Path.Combine("UseCases", ctx.UseCaseName, $"{ctx.UseCaseName}Handler.cs"), $$"""
            using {{ctx.LayerNamespace("Application")}}.Abstractions;

            namespace {{ctx.LayerNamespace("Application")}}.UseCases.{{ctx.UseCaseName}};

            public sealed class {{ctx.UseCaseName}}Handler
            {
                private readonly I{{ctx.Entity}}Repository _repository;

                public {{ctx.UseCaseName}}Handler(I{{ctx.Entity}}Repository repository)
                {
                    _repository = repository ?? throw new ArgumentNullException(nameof(repository));
                }

                public async Task<{{ctx.UseCaseName}}Result> HandleAsync(
                    {{ctx.UseCaseName}}Command command,
                    CancellationToken cancellationToken = default)
                {
                    ArgumentNullException.ThrowIfNull(command);

                    // TODO: implement {{ctx.UseCaseName}} use case logic using _repository
                    await Task.CompletedTask;
                    return new {{ctx.UseCaseName}}Result.Created(command.Id);
                }
            }
            """),
        new(Path.Combine("UseCases", ctx.UseCaseName, $"{ctx.UseCaseName}Result.cs"), $$"""
            namespace {{ctx.LayerNamespace("Application")}}.UseCases.{{ctx.UseCaseName}};

            public abstract record {{ctx.UseCaseName}}Result
            {
                public sealed record Created(Guid Id) : {{ctx.UseCaseName}}Result;
                public sealed record ValidationFailed(IReadOnlyDictionary<string, string[]> Errors) : {{ctx.UseCaseName}}Result;
            }
            """),
        new(Path.Combine("UseCases", ctx.UseCaseName, $"{ctx.UseCaseName}ReadModel.cs"), $$"""
            namespace {{ctx.LayerNamespace("Application")}}.UseCases.{{ctx.UseCaseName}};

            public sealed record {{ctx.UseCaseName}}ReadModel(
                Guid Id,
                string Name
            );
            """),
        new(Path.Combine("Validation", $"{ctx.UseCaseName}CommandValidator.cs"), $$"""
            using FluentValidation;
            using {{ctx.LayerNamespace("Application")}}.UseCases.{{ctx.UseCaseName}};

            namespace {{ctx.LayerNamespace("Application")}}.Validation;

            public sealed class {{ctx.UseCaseName}}CommandValidator
                : AbstractValidator<{{ctx.UseCaseName}}Command>
            {
                public {{ctx.UseCaseName}}CommandValidator()
                {
                    RuleFor(command => command.Id)
                        .NotEmpty()
                        .WithMessage("Id is required.");

                    RuleFor(command => command.Name)
                        .NotEmpty().WithMessage("Name is required.")
                        .MaximumLength(100).WithMessage("Name must not exceed 100 characters.");
                }
            }
            """)
    ];

    private static StubFile[] BuildInfrastructureStubs(ModuleContext ctx)
    {
        string repositoryContent = ctx.HasSharedInfrastructure
            ? $$"""
                using {{ctx.LayerNamespace("Application")}}.Abstractions;
                using {{ctx.LayerNamespace("Domain")}}.Entities;
                using {{ctx.SharedRoot}}.Shared.Infrastructure.Database;

                namespace {{ctx.LayerNamespace("Infrastructure")}}.Persistence.Repositories;

                public class {{ctx.Entity}}Repository : I{{ctx.Entity}}Repository
                {
                    private readonly ISqlDataAccess _sqlDataAccess;

                    public {{ctx.Entity}}Repository(ISqlDataAccess sqlDataAccess)
                    {
                        ArgumentNullException.ThrowIfNull(sqlDataAccess);
                        _sqlDataAccess = sqlDataAccess;
                    }

                    public Task<{{ctx.Model}}?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
                        => throw new NotImplementedException();

                    public Task AddAsync({{ctx.Model}} entity, CancellationToken cancellationToken = default)
                        => throw new NotImplementedException();

                    public Task UpdateAsync({{ctx.Model}} entity, CancellationToken cancellationToken = default)
                        => throw new NotImplementedException();

                    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
                        => throw new NotImplementedException();
                }
                """
            : $$"""
                using {{ctx.LayerNamespace("Application")}}.Abstractions;
                using {{ctx.LayerNamespace("Domain")}}.Entities;

                namespace {{ctx.LayerNamespace("Infrastructure")}}.Persistence.Repositories;

                public class {{ctx.Entity}}Repository : I{{ctx.Entity}}Repository
                {
                    // TODO: inject ISqlDataAccess (from {{ctx.SharedRoot}}.Shared.Infrastructure.Database) or DbConnection when available

                    public Task<{{ctx.Model}}?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
                        => throw new NotImplementedException();

                    public Task AddAsync({{ctx.Model}} entity, CancellationToken cancellationToken = default)
                        => throw new NotImplementedException();

                    public Task UpdateAsync({{ctx.Model}} entity, CancellationToken cancellationToken = default)
                        => throw new NotImplementedException();

                    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
                        => throw new NotImplementedException();
                }
                """;

        return
        [
            new(Path.Combine("DependencyInjection", $"{ctx.ModuleName}InfrastructureServiceCollectionExtensions.cs"), $$"""
                using Microsoft.Extensions.DependencyInjection;
                using {{ctx.LayerNamespace("Application")}}.Abstractions;
                using {{ctx.LayerNamespace("Infrastructure")}}.Persistence.Repositories;

                namespace {{ctx.LayerNamespace("Infrastructure")}}.DependencyInjection;

                public static class {{ctx.ModuleName}}InfrastructureServiceCollectionExtensions
                {
                    public static IServiceCollection Add{{ctx.ModuleName}}Infrastructure(this IServiceCollection services)
                    {
                        ArgumentNullException.ThrowIfNull(services);

                        services.AddScoped<I{{ctx.Entity}}Repository, {{ctx.Entity}}Repository>();

                        return services;
                    }
                }
                """),
            new(Path.Combine("Persistence", "Mapping", $"{ctx.Entity}Mapping.cs"), $$"""
                using {{ctx.LayerNamespace("Domain")}}.Entities;
                using {{ctx.LayerNamespace("Infrastructure")}}.Persistence.Records;

                namespace {{ctx.LayerNamespace("Infrastructure")}}.Persistence.Mapping;

                internal static class {{ctx.Entity}}Mapping
                {
                    internal static {{ctx.Model}} ToDomain(this {{ctx.Entity}}Row row)
                    {
                        ArgumentNullException.ThrowIfNull(row);

                        // TODO: map {{ctx.Entity}}Row columns to {{ctx.Model}}
                        throw new NotImplementedException();
                    }

                    internal static {{ctx.Entity}}Row ToRow(this {{ctx.Model}} model)
                    {
                        ArgumentNullException.ThrowIfNull(model);

                        return new {{ctx.Entity}}Row
                        {
                            Id = model.Id,
                            Name = model.Name
                        };
                    }
                }
                """),
            new(Path.Combine("Persistence", "Queries", $"{ctx.Entity}Queries.cs"), $$""""
                namespace {{ctx.LayerNamespace("Infrastructure")}}.Persistence.Queries;

                // TODO: add read-only SQL queries for {{ctx.Entity}} (e.g. via Dapper or raw SQL)
                internal static class {{ctx.Entity}}Queries
                {
                    internal const string Insert{{ctx.Entity}} = """
                        -- TODO: add SQL query
                        """;
                }
                """"),
            new(Path.Combine("Persistence", "Records", $"{ctx.Entity}Row.cs"), $$"""
                namespace {{ctx.LayerNamespace("Infrastructure")}}.Persistence.Records;

                internal sealed class {{ctx.Entity}}Row
                {
                    public Guid Id { get; init; }
                    public string Name { get; init; } = string.Empty;

                    // TODO: match columns in the {{ctx.Entity}} database table
                }
                """),
            new(Path.Combine("Persistence", "Repositories", $"{ctx.Entity}Repository.cs"), repositoryContent)
        ];
    }

    private static StubFile[] BuildPresentationStubs(ModuleContext ctx) =>
    [
        new(Path.Combine("Controllers", $"{ctx.ModuleName}Controller.cs"), $$"""
            using FluentValidation;
            using Microsoft.AspNetCore.Authorization;
            using Microsoft.AspNetCore.Mvc;
            using {{ctx.LayerNamespace("Application")}}.UseCases.{{ctx.UseCaseName}};

            namespace {{ctx.LayerNamespace("Presentation")}}.Controllers;

            [ApiController]
            [AllowAnonymous]
            [Route("api/v1/{{ctx.ModuleName}}")]
            [Produces("application/json")]
            public class {{ctx.ModuleName}}Controller : ControllerBase
            {
                private readonly {{ctx.UseCaseName}}Handler _{{ctx.UseCaseCamel}}Handler;
                private readonly IValidator<{{ctx.UseCaseName}}Command> _{{ctx.UseCaseCamel}}Validator;

                public {{ctx.ModuleName}}Controller(
                    {{ctx.UseCaseName}}Handler {{ctx.UseCaseCamel}}Handler,
                    IValidator<{{ctx.UseCaseName}}Command> {{ctx.UseCaseCamel}}Validator)
                {
                    _{{ctx.UseCaseCamel}}Handler = {{ctx.UseCaseCamel}}Handler
                        ?? throw new ArgumentNullException(nameof({{ctx.UseCaseCamel}}Handler));
                    _{{ctx.UseCaseCamel}}Validator = {{ctx.UseCaseCamel}}Validator
                        ?? throw new ArgumentNullException(nameof({{ctx.UseCaseCamel}}Validator));
                }

                // TODO: add endpoints (Get, Create, Update, Delete)
            }
            """),
        new(Path.Combine("DependencyInjection", $"{ctx.ModuleName}PresentationServiceCollectionExtensions.cs"), $$"""
            using Microsoft.Extensions.DependencyInjection;
            using {{ctx.LayerNamespace("Presentation")}}.Controllers;

            namespace {{ctx.LayerNamespace("Presentation")}}.DependencyInjection;

            public static class {{ctx.ModuleName}}PresentationServiceCollectionExtensions
            {
                public static IMvcBuilder Add{{ctx.ModuleName}}Presentation(
                    this IMvcBuilder mvcBuilder)
                {
                    ArgumentNullException.ThrowIfNull(mvcBuilder);

                    mvcBuilder.AddApplicationPart(
                        typeof({{ctx.ModuleName}}Controller).Assembly);

                    return mvcBuilder;
                }
            }
            """),
        new(Path.Combine("Mapping", $"{ctx.Entity}Mapping.cs"), $$"""
            using {{ctx.LayerNamespace("Application")}}.UseCases.{{ctx.UseCaseName}};
            using {{ctx.LayerNamespace("Contracts")}}.Requests;
            using {{ctx.LayerNamespace("Contracts")}}.Responses;

            namespace {{ctx.LayerNamespace("Presentation")}}.Mapping;

            internal static class {{ctx.Entity}}Mapping
            {
                internal static {{ctx.UseCaseName}}Command ToCommand(this Create{{ctx.Entity}}Request request, Guid? id = null)
                {
                    ArgumentNullException.ThrowIfNull(request);

                    return new {{ctx.UseCaseName}}Command(
                        id ?? Guid.NewGuid(),
                        request.Name);
                }

                internal static {{ctx.Entity}}Response ToResponse(this {{ctx.UseCaseName}}ReadModel readModel)
                {
                    ArgumentNullException.ThrowIfNull(readModel);

                    return new {{ctx.Entity}}Response
                    {
                        Id = readModel.Id,
                        Name = readModel.Name
                    };
                }
            }
            """),
        new(Path.Combine("Validation", $"Create{ctx.Entity}RequestValidation.cs"), $$"""
            using FluentValidation;
            using {{ctx.LayerNamespace("Contracts")}}.Requests;

            namespace {{ctx.LayerNamespace("Presentation")}}.Validation;

            public sealed class Create{{ctx.Entity}}RequestValidation
                : AbstractValidator<Create{{ctx.Entity}}Request>
            {
                public Create{{ctx.Entity}}RequestValidation()
                {
                    RuleFor(request => request.Name)
                        .NotEmpty().WithMessage("Name is required.")
                        .MaximumLength(100).WithMessage("Name must not exceed 100 characters.");
                }
            }
            """)
    ];

    private static StubFile[] BuildUnitTestFiles(ModuleContext ctx) =>
    [
        new(Path.Combine("Controllers", $"{ctx.ModuleName}ControllerTests.cs"), $$"""
            using Xunit;
            using {{ctx.LayerNamespace("Application")}}.Abstractions;
            using {{ctx.LayerNamespace("Application")}}.UseCases.{{ctx.UseCaseName}};
            using {{ctx.LayerNamespace("Application")}}.Validation;
            using {{ctx.LayerNamespace("Domain")}}.Entities;
            using {{ctx.LayerNamespace("Presentation")}}.Controllers;

            namespace {{ctx.TestName}}.Controllers;

            public class {{ctx.ModuleName}}ControllerTests
            {
                [Fact]
                public void Controller_Can_Be_Instantiated()
                {
                    var handler = new {{ctx.UseCaseName}}Handler(new Fake{{ctx.Entity}}Repository());
                    var validator = new {{ctx.UseCaseName}}CommandValidator();
                    var controller = new {{ctx.ModuleName}}Controller(handler, validator);

                    Assert.NotNull(controller);
                }

                // TODO: add tests for each {{ctx.ModuleName}}Controller endpoint once implemented

                private sealed class Fake{{ctx.Entity}}Repository : I{{ctx.Entity}}Repository
                {
                    public Task<{{ctx.Model}}?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
                        => Task.FromResult<{{ctx.Model}}?>(null);

                    public Task AddAsync({{ctx.Model}} entity, CancellationToken cancellationToken = default)
                        => Task.CompletedTask;

                    public Task UpdateAsync({{ctx.Model}} entity, CancellationToken cancellationToken = default)
                        => Task.CompletedTask;

                    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
                        => Task.CompletedTask;
                }
            }
            """),
        new(Path.Combine("UseCases", $"{ctx.UseCaseName}HandlerTests.cs"), $$"""
            using Xunit;
            using {{ctx.LayerNamespace("Application")}}.Abstractions;
            using {{ctx.LayerNamespace("Application")}}.UseCases.{{ctx.UseCaseName}};
            using {{ctx.LayerNamespace("Domain")}}.Entities;

            namespace {{ctx.TestName}}.UseCases;

            public class {{ctx.UseCaseName}}HandlerTests
            {
                [Fact]
                public void Constructor_Throws_When_Repository_Is_Null()
                {
                    Assert.Throws<ArgumentNullException>(() => new {{ctx.UseCaseName}}Handler(null!));
                }

                [Fact]
                public async Task HandleAsync_Returns_Created_Result()
                {
                    var handler = new {{ctx.UseCaseName}}Handler(new Fake{{ctx.Entity}}Repository());
                    var command = new {{ctx.UseCaseName}}Command(Guid.NewGuid(), "Sample {{ctx.Entity}}");

                    var result = await handler.HandleAsync(command);

                    var created = Assert.IsType<{{ctx.UseCaseName}}Result.Created>(result);
                    Assert.Equal(command.Id, created.Id);
                }

                // TODO: add tests for {{ctx.UseCaseName}}Handler business rules and edge cases

                private sealed class Fake{{ctx.Entity}}Repository : I{{ctx.Entity}}Repository
                {
                    public Task<{{ctx.Model}}?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
                        => Task.FromResult<{{ctx.Model}}?>(null);

                    public Task AddAsync({{ctx.Model}} entity, CancellationToken cancellationToken = default)
                        => Task.CompletedTask;

                    public Task UpdateAsync({{ctx.Model}} entity, CancellationToken cancellationToken = default)
                        => Task.CompletedTask;

                    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
                        => Task.CompletedTask;
                }
            }
            """)
    ];

    // Empty sub-folders that don't get a stub file yet but should still exist
    // on disk to match the standard module layout.
    private static (string Layer, string[] ExtraDirs)[] BuildExtraDirPlan() =>
    [
        ("Domain", ["ValueObjects"]),
        ("Contracts", ["Criteria"]),
        ("Application", []),
        ("Infrastructure", ["Configuration", "Services"]),
        ("Presentation", []),
    ];

    // ----- generation steps -----

    private static bool CreateLayerProjects(
        ModuleContext ctx,
        string slnPath,
        (string Layer, StubFile[] Stubs)[] stubPlan,
        (string Layer, string[] ExtraDirs)[] extraDirPlan)
    {
        foreach (var (layer, stubs) in stubPlan)
        {
            string name = $"{ctx.Prefix}.{layer}";
            string outDir = ctx.LayerDir(layer);

            if (!DotnetCli.RunDotnet($"new classlib -n \"{name}\" -o \"{outDir}\""))
            {
                Console.WriteLine($"Failed creating \"{name}\". Aborting.");
                return false;
            }

            string defaultClassFile = Path.Combine(outDir, "Class1.cs");
            if (File.Exists(defaultClassFile))
            {
                File.Delete(defaultClassFile);
                Console.WriteLine($"  removed default {defaultClassFile}");
            }

            foreach (var stub in stubs)
            {
                string filePath = Path.Combine(outDir, stub.RelativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
                File.WriteAllText(filePath, stub.Content + Environment.NewLine);
                Console.WriteLine($"  created {filePath}");
            }

            var extraDirs = extraDirPlan.First(p => p.Layer == layer).ExtraDirs;
            foreach (var dir in extraDirs)
            {
                string dirPath = Path.Combine(outDir, dir);
                Directory.CreateDirectory(dirPath);

                // SDK-style projects exclude dot-files (**/.*) from implicit globs, so ".gitkeep"
                // won't surface empty folders in Visual Studio / Rider Solution Explorer.
                string dirLabel = dir.Replace(Path.DirectorySeparatorChar, '/').Split('/').Last();
                string keepFilePath = Path.Combine(dirPath, "README.md");
                File.WriteAllText(keepFilePath, $"Reserved for {dirLabel}. Safe to delete this file once real content lives here.{Environment.NewLine}");

                Console.WriteLine($"  created dir {dirPath}  (+ README.md)");
            }
        }

        foreach (var layer in Layers)
        {
            if (!DotnetCli.RunDotnet($"sln \"{slnPath}\" add \"{ctx.LayerCsProj(layer)}\""))
            {
                Console.WriteLine($"Failed adding \"{ctx.Prefix}.{layer}\" to solution. Aborting.");
                return false;
            }
        }

        return true;
    }

    private static bool WireProjectReferences(ModuleContext ctx)
    {
        if (!AddReference(ctx.LayerCsProj("Contracts"),
                ctx.LayerCsProj("Domain"))) return false;

        if (!AddReference(ctx.LayerCsProj("Application"),
                ctx.LayerCsProj("Contracts"), ctx.LayerCsProj("Domain"))) return false;

        if (!AddReference(ctx.LayerCsProj("Infrastructure"),
                ctx.LayerCsProj("Application"), ctx.LayerCsProj("Contracts"), ctx.LayerCsProj("Domain"))) return false;

        if (!AddReference(ctx.LayerCsProj("Presentation"),
                ctx.LayerCsProj("Application"), ctx.LayerCsProj("Contracts"))) return false;

        // Presentation needs the ASP.NET Core framework reference for ControllerBase / IMvcBuilder.
        string presentationCsproj = ctx.LayerCsProj("Presentation");
        if (File.Exists(presentationCsproj))
        {
            string csprojContent = File.ReadAllText(presentationCsproj);
            if (!csprojContent.Contains("Microsoft.AspNetCore.App"))
            {
                csprojContent = csprojContent.Replace(
                    "</Project>",
                    "  <ItemGroup>\n    <FrameworkReference Include=\"Microsoft.AspNetCore.App\" />\n  </ItemGroup>\n</Project>");
                File.WriteAllText(presentationCsproj, csprojContent);
            }
        }

        if (!ctx.SkipShared)
        {
            TryAddSharedReference(ctx.LayerCsProj("Domain"), ctx.SharedCsProj("Domain"));
            TryAddSharedReference(ctx.LayerCsProj("Application"), ctx.SharedCsProj("Application"));
            TryAddSharedReference(ctx.LayerCsProj("Infrastructure"), ctx.SharedCsProj("Infrastructure"));
        }
        else
        {
            Console.WriteLine("Skipped Shared.* references (--no-shared).");
        }

        return true;
    }

    private static bool CreateUnitTestProject(ModuleContext ctx, string slnPath, StubFile[] unitTestFiles)
    {
        if (!DotnetCli.RunDotnet($"new xunit -n \"{ctx.TestName}\" -o \"{ctx.TestDir}\""))
        {
            Console.WriteLine($"Failed creating \"{ctx.TestName}\". Aborting.");
            return false;
        }

        if (!DotnetCli.RunDotnet($"sln \"{slnPath}\" add \"{ctx.TestCsProj}\""))
        {
            Console.WriteLine($"Failed adding \"{ctx.TestName}\" to solution. Aborting.");
            return false;
        }

        if (!AddReference(ctx.TestCsProj, ctx.LayerCsProj("Application"), ctx.LayerCsProj("Presentation")))
            return false;

        string defaultTestFile = Path.Combine(ctx.TestDir, "UnitTest1.cs");
        if (File.Exists(defaultTestFile))
        {
            File.Delete(defaultTestFile);
            Console.WriteLine($"  removed default {defaultTestFile}");
        }

        foreach (var file in unitTestFiles)
        {
            string fullPath = Path.Combine(ctx.TestDir, file.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, file.Content + Environment.NewLine);
            Console.WriteLine($"  created {fullPath}");
        }

        return true;
    }

    private static void InstallForcedPackages(ModuleContext ctx)
    {
        foreach (var (layer, package) in ForcedPackages)
        {
            if (AddPackage(ctx.LayerCsProj(layer), package))
                Console.WriteLine($"  Installed package \"{package}\" into {ctx.Prefix}.{layer}.");
            else
                Console.WriteLine($"  Warning: failed to install package \"{package}\" into {ctx.Prefix}.{layer}.");
        }
    }

    private static void WireHostProject(ModuleContext ctx, string? hostArg, string? hostCsproj, bool isDryRun)
    {
        if (hostCsproj != null)
        {
            if (AddReference(hostCsproj, ctx.LayerCsProj("Presentation"), ctx.LayerCsProj("Application"), ctx.LayerCsProj("Infrastructure")))
            {
                Console.WriteLine($"  Wired host references: {hostCsproj} -> Presentation, Application, Infrastructure");
                AddServiceInjectionInEntryPoints.PatchProgramCs(hostCsproj, ctx.Prefix, ctx.ModuleName, isDryRun);
            }
            else
            {
                Console.WriteLine($"  Warning: failed to wire host references into {hostCsproj}");
            }
        }
        else if (hostArg != null)
        {
            Console.WriteLine("Skipped host wiring (could not resolve --host to a single .csproj).");
        }
    }

    // ----- private helpers -----

    // Fixed shared-project convention: {sharedRoot}.Shared.<Layer> under src/Shared.
    // Not derived from --name, so a mistyped module prefix can't point references
    // at the wrong shared projects.
    private static string GetSharedCsProjPath(string sharedRoot, string layer) =>
        Path.Combine("src", "Shared", $"{sharedRoot}.Shared.{layer}", $"{sharedRoot}.Shared.{layer}.csproj");

    private static bool AddReference(string projectPathOrDir, params string[] references)
    {
        var refs = string.Join(" ", references.Select(r => $"\"{r}\""));
        return DotnetCli.RunDotnet($"add \"{projectPathOrDir}\" reference {refs}");
    }

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
        ModuleContext ctx,
        string? entityOverride,
        string? slnPath,
        string? hostArg,
        string? hostCsproj,
        (string Layer, StubFile[] Stubs)[] stubPlan,
        (string Layer, string[] ExtraDirs)[] extraDirPlan,
        StubFile[] unitTestFiles)
    {
        Console.WriteLine("=== DRY RUN (module) — no changes will be made ===");
        Console.WriteLine();
        Console.WriteLine($"Module root: {ctx.ModuleRoot}");
        Console.WriteLine($"Entity:      {ctx.Model}  (from {(entityOverride != null ? "--entity" : $"singularized \"{ctx.ModuleName}\"")})");
        Console.WriteLine($"UseCase:     {ctx.UseCaseName}");
        Console.WriteLine($"Shared root: {ctx.SharedRoot}  (from {(ctx.SharedRoot == "MBPOS" ? "default" : "--shared-root")})");
        Console.WriteLine();

        foreach (var (layer, stubs) in stubPlan)
        {
            string layerDir = ctx.LayerDir(layer);
            Console.WriteLine($"[WILL CREATE] classlib  {ctx.Prefix}.{layer}  -> {layerDir}");
            Console.WriteLine($"[WILL DELETE]           {Path.Combine(layerDir, "Class1.cs")}");
            foreach (var stub in stubs)
                Console.WriteLine($"[WILL CREATE]           {Path.Combine(layerDir, stub.RelativePath)}");

            var extraDirs = extraDirPlan.First(p => p.Layer == layer).ExtraDirs;
            foreach (var dir in extraDirs)
                Console.WriteLine($"[WILL CREATE DIR]       {Path.Combine(layerDir, dir)}  (+ README.md)");
        }

        Console.WriteLine($"[WILL CREATE] xunit     {ctx.TestName}  -> {ctx.TestDir}");
        Console.WriteLine($"[WILL DELETE]           {Path.Combine(ctx.TestDir, "UnitTest1.cs")}");
        foreach (var file in unitTestFiles)
            Console.WriteLine($"[WILL CREATE]           {Path.Combine(ctx.TestDir, file.RelativePath)}");
        Console.WriteLine();

        Console.WriteLine("[WILL WIRE INTERNAL REFERENCES] (always applied)");
        Console.WriteLine("  Contracts       -> Domain");
        Console.WriteLine("  Application     -> Contracts, Domain");
        Console.WriteLine("  Infrastructure  -> Application, Contracts, Domain");
        Console.WriteLine("  Presentation    -> Application, Contracts");
        Console.WriteLine("  UnitTests       -> Application, Presentation");
        Console.WriteLine();

        Console.WriteLine("[WILL ADD FRAMEWORK REFERENCE]");
        Console.WriteLine("  Presentation    -> Microsoft.AspNetCore.App  (for ControllerBase / IMvcBuilder)");
        Console.WriteLine();

        Console.WriteLine("[WILL INSTALL NUGET PACKAGES]");
        foreach (var (layer, package) in ForcedPackages)
            Console.WriteLine($"  {layer,-15} -> {package}");
        Console.WriteLine();

        if (ctx.SkipShared)
        {
            Console.WriteLine("[SKIP] Shared.* references skipped (--no-shared)");
        }
        else
        {
            Console.WriteLine("[WILL WIRE SHARED REFERENCES] (best-effort — skipped with a warning if the project isn't found)");
            Console.WriteLine($"  Domain          -> {ctx.SharedCsProj("Domain")}{(File.Exists(ctx.SharedCsProj("Domain")) ? "" : "  [NOT FOUND]")}");
            Console.WriteLine($"  Application     -> {ctx.SharedCsProj("Application")}{(File.Exists(ctx.SharedCsProj("Application")) ? "" : "  [NOT FOUND]")}");
            Console.WriteLine($"  Infrastructure  -> {ctx.SharedCsProj("Infrastructure")}{(File.Exists(ctx.SharedCsProj("Infrastructure")) ? "" : "  [NOT FOUND]")}");
        }
        Console.WriteLine();

        if (hostArg != null)
        {
            if (hostCsproj != null)
            {
                Console.WriteLine($"[WILL WIRE HOST REFERENCES] {hostCsproj}\n" +
                                  $"  <- Presentation, Application, Infrastructure");
                AddServiceInjectionInEntryPoints.PatchProgramCs(hostCsproj, ctx.Prefix, ctx.ModuleName, isDryRun: true);
            }
            else
            {
                Console.WriteLine($"[SKIP] --host \"{hostArg}\" could not be resolved to a single .csproj — host wiring skipped.");
            }
            Console.WriteLine();
        }

        Console.WriteLine(slnPath != null
            ? $"[WILL REGISTER] All projects above added to solution: {slnPath}"
            : "[SKIP] No solution file found or specified — projects will NOT be added to a solution.");
        Console.WriteLine();
        Console.WriteLine("Run the same command without --dry-run to apply these changes.");
    }
}
