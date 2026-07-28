# DnScaffold

Every time you add a new module to a modular .NET monolith, you repeat the same tedious steps — create 5+ class libraries, delete the default `Class1.cs` from each, set up the folder structure, wire `ProjectReference`s between layers, add NuGet packages, register everything in the solution, and hook it into your host's `Program.cs`. **DnScaffold** (`dn`) eliminates all of that.

It's a single `dotnet` CLI tool that takes a module name and generates a complete **Clean Architecture** module — Domain, Contracts, Application, Infrastructure, Presentation, and UnitTests — with real starter files, the correct inter-layer references, required NuGet packages, and solution registration, all in one command. No config files, no templates to maintain, no copy-pasting from an existing module.

---

## Quick Install

```bash
dotnet tool install --global DnScaffold
```

Once installed, the `dn` command is available globally. For full CLI usage, flags, and examples, see [`USAGE.md`](USAGE.md).

---

## What Gets Generated

When you scaffold a module (e.g. `MBPOS.Modules.Products`), DnScaffold creates **5 class library projects + 1 test project**, each with the standard sub-folder layout and real starter files — not the default `Class1.cs`.

```
src/Modules/Products/
│
├── MBPOS.Modules.Products.Domain/
│   ├── Entities/
│   │   └── ProductModel.cs
│   └── ValueObjects/
│
├── MBPOS.Modules.Products.Contracts/
│   ├── Requests/
│   │   ├── CreateProductRequest.cs
│   │   └── UpdateProductRequest.cs
│   └── Responses/
│       └── ProductResponse.cs
│
├── MBPOS.Modules.Products.Application/
│   ├── Abstractions/
│   │   └── IProductRepository.cs
│   ├── DependencyInjection/
│   │   └── ProductsApplicationServiceCollectionExtensions.cs
│   ├── UseCases/
│   │   └── Create/
│   │       ├── CreateProductsCommand.cs
│   │       ├── CreateProductsHandler.cs
│   │       └── CreateProductsResult.cs
│   └── Validation/
│       └── CreateProductsCommandValidator.cs
│
├── MBPOS.Modules.Products.Infrastructure/
│   ├── Persistence/
│   │   ├── Repositories/
│   │   │   └── ProductRepository.cs
│   │   ├── Queries/
│   │   │   └── ProductsQueries.cs
│   │   └── Records/
│   ├── DependencyInjection/
│   │   └── ProductsInfrastructureServiceCollectionExtensions.cs
│   ├── Configuration/
│   └── Services/
│
├── MBPOS.Modules.Products.Presentation/
│   ├── Controllers/
│   │   └── ProductController.cs
│   └── DependencyInjection/
│       └── ProductsPresentationServiceCollectionExtensions.cs
│
└── UnitTests/MBPOS.Modules.Products.UnitTests/
    └── Controllers/
        └── ProductControllerTests.cs
```

---

## Layer Responsibilities

| Layer              | Purpose                                                                                                    |
|--------------------|------------------------------------------------------------------------------------------------------------|
| **Domain**         | Core business entities and value objects. No dependencies on other layers — this is the innermost circle.  |
| **Contracts**      | DTOs only — `Requests` (input) and `Responses` (output). Shared between layers that need a common shape.   |
| **Application**    | Use cases, repository abstractions (`I{Entity}Repository`), validators, and DI registration. Orchestrates domain logic without knowing how data is stored. |
| **Infrastructure** | Concrete implementations — repositories, database queries, external service clients. Depends on Application for the abstractions it implements. |
| **Presentation**   | API controllers and the `AddApplicationPart` DI extension. The only layer that touches ASP.NET Core directly. |
| **UnitTests**      | xUnit test project targeting Application and Presentation.                                                 |

> The repository interface (`I{Entity}Repository`) lives in **Application/Abstractions**, not Contracts. Contracts is strictly for DTOs. Infrastructure implements the interface directly.

---

## Reference Wiring

DnScaffold automatically wires all `ProjectReference`s between layers following the Clean Architecture dependency rule — **inner layers never reference outer layers**.

```
                    ┌─────────────┐
                    │   Domain    │  ← innermost, no dependencies
                    └──────▲──────┘
                           │
                    ┌──────┴──────┐
                    │  Contracts  │  → Domain
                    └──────▲──────┘
                           │
                    ┌──────┴──────┐
                    │ Application │  → Contracts, Domain
                    └──────▲──────┘
                           │
              ┌────────────┼────────────┐
              │                         │
    ┌─────────┴─────────┐   ┌──────────┴──────────┐
    │  Infrastructure   │   │    Presentation     │
    │                   │   │                     │
    │ → Application     │   │ → Application       │
    │ → Contracts       │   │ → Contracts         │
    │ → Domain          │   │                     │
    └───────────────────┘   └─────────────────────┘

              ┌─────────────────────┐
              │     UnitTests       │
              │                     │
              │ → Application       │
              │ → Presentation      │
              └─────────────────────┘
```

### Summary Table

| Project          | References                          |
|------------------|-------------------------------------|
| Domain           | *(none)*                            |
| Contracts        | Domain                              |
| Application      | Contracts, Domain                   |
| Infrastructure   | Application, Contracts, Domain      |
| Presentation     | Application, Contracts              |
| UnitTests        | Application, Presentation           |

### Shared Project References (best-effort)

Each module also attempts to wire references to shared, cross-cutting projects:

```
Domain          → {SharedRoot}.Shared.Domain
Application     → {SharedRoot}.Shared.Application
Infrastructure  → {SharedRoot}.Shared.Infrastructure
```

These are expected at `src/Shared/{SharedRoot}.Shared.<Layer>` (defaults to `MBPOS`). If a shared project is not found on disk, DnScaffold prints a warning and continues — **a missing shared project never blocks the scaffold**.

---

## Framework Reference

The **Presentation** layer receives a `FrameworkReference` to `Microsoft.AspNetCore.App` because:

- The generated `{Entity}Controller` inherits from `ControllerBase`
- The generated `{ModuleName}PresentationServiceCollectionExtensions` exposes:

```csharp
public static IMvcBuilder Add{ModuleName}Presentation(this IMvcBuilder mvcBuilder)
{
    ArgumentNullException.ThrowIfNull(mvcBuilder);
    mvcBuilder.AddApplicationPart(typeof({Entity}Controller).Assembly);
    return mvcBuilder;
}
```

This registers the module's controllers as an application part — call it from your host's `AddControllers()` chain.

---

## NuGet Packages

DnScaffold installs these packages automatically during scaffold:

| Layer            | Package                                                  |
|------------------|----------------------------------------------------------|
| Application      | `FluentValidation`                                       |
| Application      | `FluentValidation.DependencyInjectionExtensions`         |
| Application      | `Microsoft.Extensions.DependencyInjection.Abstractions`  |
| Infrastructure   | `Microsoft.Extensions.DependencyInjection.Abstractions`  |
| Presentation     | `Microsoft.Extensions.DependencyInjection.Abstractions`  |
| Presentation     | `FluentValidation`                                       |

---

## Host Wiring & Program.cs Patching

When a host project is specified, DnScaffold:

1. **Adds `ProjectReference`s** from the host `.csproj` to the module's Presentation, Application, and Infrastructure projects.
2. **Patches the host's `Program.cs`** — appends `.Add{Module}Infrastructure()` and `.Add{Module}Presentation()` into the existing `builder.Services` chains, and adds the required `using` directives.

If the expected chain shape isn't found in `Program.cs`, it prints the snippet to add manually instead of guessing.

---

## Entity Name Singularization

The entity name is derived automatically from the module's folder name by singularizing it:

| Module Folder Name | Entity Name |
|--------------------|-------------|
| `Products`         | `Product`   |
| `Categories`       | `Category`  |
| `Addresses`        | `Address`   |
| `Inventory`        | `Inventory` |

For irregular plurals or custom names, an explicit entity override is available.

---

## Notes

- **Run from your solution root** — folder scanning, `.sln` detection, and host resolution all depend on the current working directory.
- **Existing project folders are never overwritten** — if the target module or project folder already exists, DnScaffold aborts to prevent data loss.
- **Solution files are auto-detected** — `.sln` / `.slnx` files in the current directory are found automatically. A module scaffold requires a solution to register its projects in.
- **Host wiring is best-effort** — a missing or ambiguous host prints a warning but never blocks the rest of the scaffold.
- **The Infrastructure `Queries` stub** is a placeholder for read-side query methods (e.g. Dapper or raw SQL) — it does not depend on any ORM out of the box.
- **Empty folders include a `README.md`** — SDK-style projects don't track empty folders or dot-files like `.gitkeep`, so a plain `README.md` is used instead to make the folder visible in IDEs and version control.
- **For command-line usage, flags, and examples** — see [`USAGE.md`](USAGE.md).