namespace ShortHandCreater;

internal static class HelpPrinter
{
    internal static void PrintHelp()
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
    usecase        Use‑case scaffold: creates Command (sealed record),
                    Handler (sealed class), and Result (abstract record with
                    Success/ValidationFailed/Failed nested records) inside an
                    existing module's Application/UseCases/<Prefix>/ folder,
                    plus an optional CommandValidator. Auto‑registers the new
                    handler in the module's DI extension file.
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

    MBPOS.Modules.Products.Domain/
      Entities/
        ProductModel.cs
      ValueObjects/        (empty, reserved for value objects)

    MBPOS.Modules.Products.Contracts/
      Criteria/            (empty, reserved for query/filter criteria DTOs)
      Requests/
        CreateProductRequest.cs
        UpdateProductRequest.cs
      Responses/
        ProductResponse.cs

    MBPOS.Modules.Products.Application/
      Abstractions/
        IProductRepository.cs
      DependencyInjection/
        ProductsApplicationServiceCollectionExtensions.cs
      Option/
        ProductsOptions.cs
      Queries/
        GetProductsQuery.cs
      UseCases/
        CreateProduct/
          CreateProductCommand.cs
          CreateProductHandler.cs
          CreateProductResult.cs
          CreateProductReadModel.cs
      Validation/
        CreateProductCommandValidator.cs

    MBPOS.Modules.Products.Infrastructure/
      Configuration/       (empty)
      DependencyInjection/
        ProductsInfrastructureServiceCollectionExtensions.cs
      Persistence/
        Mapping/
          ProductMapping.cs
        Queries/
          ProductQueries.cs
        Records/
          ProductRow.cs
        Repositories/
          ProductRepository.cs
      Services/            (empty, reserved for external/storage services)

    MBPOS.Modules.Products.Presentation/
      Controllers/
        ProductsController.cs
      DependencyInjection/
        ProductsPresentationServiceCollectionExtensions.cs
      Mapping/
        ProductMapping.cs
      Validation/
        CreateProductRequestValidation.cs

  plus UnitTests/MBPOS.Modules.Products.UnitTests/
    Controllers/
      ProductsControllerTests.cs
    UseCases/
      CreateProductHandlerTests.cs

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
  layer's csproj, since the generated {Module}Controller inherits from
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

  Use‑case scaffold (adds Command/Handler/Result/ReadModel + auto‑registers):
    dn g usecase --name GetItemDetailByStoreUseCase --location modules/item/application/usecases/GetItemDetailByStore

  Use‑case with custom result model type:
    dn g usecase --name CreateProductUseCase --location modules/products/application/usecases/CreateProduct --result-model ""Guid""

  Use‑case without a validator:
    dn g usecase --name DeleteItemUseCase --location modules/item/application/usecases/DeleteItem --no-validator

  Preview a use‑case scaffold:
    dn g usecase --name GetItemDetailByStoreUseCase --location modules/item/application/usecases/GetItemDetailByStore --dry-run

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

USE-CASE TYPE (usecase):

  dn g usecase --name GetItemDetailByStoreUseCase --location modules/item/application/usecases/GetItemDetailByStore

  Creates, inside the module's Application/UseCases/<Prefix>/ folder:
    GetItemDetailByStore/
      GetItemDetailByStoreCommand.cs    (sealed record)
      GetItemDetailByStoreHandler.cs    (sealed class)
      GetItemDetailByStoreResult.cs     (abstract record + nested Success / ValidationFailed / Failed)

  The --location is resolved against the real module layout, so fully-qualified
  folders such as src/Modules/Item/MBPOS.Modules.Item.Application/UseCases are
  found automatically even when you type modules/item/application/usecases/<Prefix>.

  Optionally creates (unless --no-validator or FluentValidation is not referenced):
    Application/Validation/GetItemDetailByStoreCommandValidator.cs

  Auto-registration:
    Adds 'services.AddScoped<GetItemDetailByStoreHandler>();' into the module's
    *ApplicationServiceCollectionExtensions.cs and adds the required using directive.
    If the DI file has no AddValidatorsFromAssemblyContaining<...>() scan, the
    validator is registered explicitly as well.

  Options:
    --result-model  (optional)  Type for the Success record's payload.
                                 Defaults to ""IReadOnlyList<{Entity}DetailModel>"" when
                                 that type exists in the module, otherwise
                                 ""IReadOnlyList<{Entity}Model>"".
                                 Example: --result-model ""Guid"" for a create use-case.
    --no-validator  (optional)  Skip creating the CommandValidator file.
    -e, --entity    (optional)  Override the entity name (derived from module name by default).
    --dry-run       (optional)  Preview what would happen without creating anything.

  The --name argument should end in ""UseCase"" (e.g. ""GetItemDetailByStoreUseCase"");
  the suffix is stripped to derive the prefix for file and class names.
");
    }
}
