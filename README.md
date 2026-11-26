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
│   ├── Criteria/
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
│   ├── Option/
│   │   └── ProductsOptions.cs
│   ├── Queries/
│   │   └── GetProductsQuery.cs
│   ├── UseCases/
│   │   └── CreateProduct/
│   │       ├── CreateProductCommand.cs
│   │       ├── CreateProductHandler.cs
│   │       ├── CreateProductResult.cs
│   │       └── CreateProductReadModel.cs
│   └── Validation/
│       └── CreateProductCommandValidator.cs
│
├── MBPOS.Modules.Products.Infrastructure/
│   ├── Configuration/
│   ├── DependencyInjection/
│   │   └── ProductsInfrastructureServiceCollectionExtensions.cs
│   ├── Persistence/
│   │   ├── Mapping/
│   │   │   └── ProductMapping.cs
│   │   ├── Queries/
│   │   │   └── ProductQueries.cs
│   │   ├── Records/
│   │   │   └── ProductRow.cs
│   │   └── Repositories/
│   │       └── ProductRepository.cs
│   └── Services/
│
├── MBPOS.Modules.Products.Presentation/
│   ├── Controllers/
│   │   └── ProductsController.cs
│   ├── DependencyInjection/
│   │   └── ProductsPresentationServiceCollectionExtensions.cs
│   ├── Mapping/
│   │   └── ProductMapping.cs
│   └── Validation/
│       └── CreateProductRequestValidation.cs
│
└── UnitTests/MBPOS.Modules.Products.UnitTests/
    ├── Controllers/
    │   └── ProductsControllerTests.cs
    └── UseCases/
        └── CreateProductHandlerTests.cs
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


  # `dn` — Command-Line Usage Guide

A quick-reference guide for the **`dn`** CLI tool — a .NET project & module scaffolder.

---

## 📦 Installation

```bash
# Pack the tool locally
dotnet pack

# Install as a global dotnet tool
dotnet tool install --global --add-source ./nupkg DnScaffold

# Or update to the latest version
dotnet tool update --global --add-source ./nupkg DnScaffold
```

After installation, the `dn` command is available globally in your terminal.

---

## 🧭 Command Syntax

```
dn <action> [type] --name <name> --location <location> [options]
```

| Part           | Required | Description                                                                 |
|----------------|----------|-----------------------------------------------------------------------------|
| `<action>`     | ✅       | `g` / `generate` — create a project or module, `l` / `link` — wire a host |
| `[type]`       | optional | Template name or `m` / `module`. Defaults to `classlib`                    |
| `--name`, `-n` | ✅       | Project name or module prefix (e.g. `MBPOS.Modules.Products`)              |
| `--location`, `-l` | ✅   | Target folder path (supports fuzzy matching)                               |

---

## ⚡ Actions at a Glance

### 1. `generate` (`g`) — Create a Project or Module

**Single project:**

```bash
dn g classlib --name MBPOS.Modules.Sales.Domain --location modules/sales
```

**Full module scaffold:**

```bash
dn g m --name MBPOS.Modules.Products --location src/Modules
```

**Module + wire into host:**

```bash
dn g m --name MBPOS.Modules.Products --location src/Modules --host src/Host/MBPOS.Api
```

### 2. `link` (`l`) — Wire Host to an Existing Module

```bash
dn link --name MBPOS.Modules.Products --location src/Modules --host src/Host/MBPOS.Api
```

Adds `ProjectReference`s from the host `.csproj` to the module's **Presentation**, **Application**, and **Infrastructure** projects. Does not create or modify the module itself.

---

## 🔧 All Options

| Flag                  | Short | Applies to       | Default      | Description                                                        |
|-----------------------|-------|------------------|--------------|--------------------------------------------------------------------|
| `--name`              | `-n`  | all              | *(required)* | Project or module name / prefix                                    |
| `--location`          | `-l`  | all              | *(required)* | Target folder path (fuzzy-matched)                                 |
| `--sln`               | `-s`  | all              | auto-detect  | Path to `.sln` / `.slnx` file                                     |
| `--entity`            | `-e`  | `module`         | auto-singular| Override entity name (e.g. `InventoryItem` instead of `Inventory`) |
| `--shared-root`       | `-r`  | `module`         | `MBPOS`      | Root prefix for `Shared.*` references                              |
| `--no-shared`         |       | `module`         | off          | Skip `Shared.*` reference wiring                                   |
| `--host`              |       | `module`, `link` | *(none)*     | Host `.csproj` path or folder with one `.csproj`                   |
| `--dry-run`           |       | all              | off          | Preview changes — nothing is created                               |
| `--help`              | `-h`  | all              |              | Show help text                                                     |

---

## 📂 Supported Project Types

When using `dn g <type>`, any of these dotnet template shortnames work:

| Type           | Description               |
|----------------|---------------------------|
| `classlib`     | Class library *(default)* |
| `console`      | Console application       |
| `webapi`       | ASP.NET Web API           |
| `web`          | ASP.NET Empty web         |
| `mvc`          | ASP.NET MVC               |
| `razor`        | Razor Pages               |
| `blazorwasm`   | Blazor WebAssembly        |
| `blazorserver` | Blazor Server             |
| `worker`       | Worker Service            |
| `xunit`        | xUnit Test Project        |
| `nunit`        | NUnit Test Project        |
| `mstest`       | MSTest Project            |
| `wpf`          | WPF Application           |
| `winforms`     | Windows Forms Application |
| `grpc`         | gRPC Service              |

Special types for `generate` only:

| Type          | Alias | Description                            |
|---------------|-------|----------------------------------------|
| `module`      | `m`   | Full layered module scaffold (5 projects + tests) |

---

## 🗂️ Module Structure

Running `dn g m --name MBPOS.Modules.Products --location src/Modules` creates:

```
src/Modules/Products/
├── MBPOS.Modules.Products.Domain/
│   ├── Entities/
│   │   └── ProductModel.cs
│   └── ValueObjects/
│
├── MBPOS.Modules.Products.Contracts/
│   ├── Criteria/
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
│   ├── Option/
│   │   └── ProductsOptions.cs
│   ├── Queries/
│   │   └── GetProductsQuery.cs
│   ├── UseCases/
│   │   └── CreateProduct/
│   │       ├── CreateProductCommand.cs
│   │       ├── CreateProductHandler.cs
│   │       ├── CreateProductResult.cs
│   │       └── CreateProductReadModel.cs
│   └── Validation/
│       └── CreateProductCommandValidator.cs
│
├── MBPOS.Modules.Products.Infrastructure/
│   ├── Configuration/
│   ├── DependencyInjection/
│   │   └── ProductsInfrastructureServiceCollectionExtensions.cs
│   ├── Persistence/
│   │   ├── Mapping/
│   │   │   └── ProductMapping.cs
│   │   ├── Queries/
│   │   │   └── ProductQueries.cs
│   │   ├── Records/
│   │   │   └── ProductRow.cs
│   │   └── Repositories/
│   │       └── ProductRepository.cs
│   └── Services/
│
├── MBPOS.Modules.Products.Presentation/
│   ├── Controllers/
│   │   └── ProductsController.cs
│   ├── DependencyInjection/
│   │   └── ProductsPresentationServiceCollectionExtensions.cs
│   ├── Mapping/
│   │   └── ProductMapping.cs
│   └── Validation/
│       └── CreateProductRequestValidation.cs
│
└── UnitTests/MBPOS.Modules.Products.UnitTests/
    ├── Controllers/
    │   └── ProductsControllerTests.cs
    └── UseCases/
        └── CreateProductHandlerTests.cs
```

### Auto-Wired References

```
Contracts       → Domain
Application     → Contracts, Domain
Infrastructure  → Application, Contracts, Domain
Presentation    → Application, Contracts
UnitTests       → Application, Presentation
```

### Auto-Installed NuGet Packages

| Layer            | Package                                                |
|------------------|--------------------------------------------------------|
| Application      | `FluentValidation`                                     |
| Application      | `FluentValidation.DependencyInjectionExtensions`       |
| Application      | `Microsoft.Extensions.DependencyInjection.Abstractions`|
| Infrastructure   | `Microsoft.Extensions.DependencyInjection.Abstractions`|
| Presentation     | `Microsoft.Extensions.DependencyInjection.Abstractions`|
| Presentation     | `FluentValidation`                                     |

### Shared References (best-effort)

Unless `--no-shared` is passed:

```
Domain          → {SharedRoot}.Shared.Domain
Application     → {SharedRoot}.Shared.Application
Infrastructure  → {SharedRoot}.Shared.Infrastructure
```

Skipped with a warning if the shared project is not found on disk.

---

## 🔍 Location Resolution

`dn` fuzzy-matches `--location` against your folder tree — no need to type exact paths.

| You type                              | Disk has `src/Modules/Identity` | Result                                                     |
|---------------------------------------|----------------------------------|-------------------------------------------------------------|
| `--location identity`                 | ✅ match                         | Resolves to `src/Modules/Identity`                          |
| `--location modules/vendors`          | `modules` matches                | Resolves to `src/Modules/vendors` (new subfolder)           |
| `--location brandnewpath/here`        | ❌ no match                      | Creates as typed: `brandnewpath/here`                       |
| `--location domain`                   | ⚠️ multiple matches              | Lists all matches — asks you to be more specific            |

> **Tip:** Use `--dry-run` to see exactly how a location resolves before committing.

---

## 🧪 Dry Run

Always preview first with `--dry-run`:

```bash
# Preview a module scaffold
dn g m --name MBPOS.Modules.Orders --location src/Modules --dry-run

# Preview a link action
dn link --name MBPOS.Modules.Orders --location src/Modules --host src/Host/MBPOS.Api --dry-run
```

Output shows `[WILL CREATE]`, `[WILL DELETE]`, `[WILL WIRE]`, and `[SKIP]` tags for every operation.

---

## 🛠️ Common Workflows

### Workflow 1: Scaffold a New Module from Scratch

```bash
# 1. Preview what will happen
dn g m --name MBPOS.Modules.Customers --location src/Modules --dry-run

# 2. Scaffold for real
dn g m --name MBPOS.Modules.Customers --location src/Modules

# 3. Wire into the API host
dn link --name MBPOS.Modules.Customers --location src/Modules --host src/Host/MBPOS.Api
```

### Workflow 2: Scaffold + Wire in One Command

```bash
dn g m --name MBPOS.Modules.Customers --location src/Modules --host src/Host/MBPOS.Api
```

This creates the module **and** wires the host references + patches the host's `Program.cs` in a single step.

### Workflow 3: Add a Single Class Library to an Existing Module

```bash
dn g classlib --name MBPOS.Modules.Identity.Presentation --location identity
```

### Workflow 4: Create a Web API Host Project

```bash
dn g webapi --name MBPOS.Api --location host
```

### Workflow 5: Override Entity Name for Irregular Plurals

```bash
# "Inventory" wouldn't auto-singularize well → use --entity
dn g m --name MBPOS.Modules.Inventory --location src/Modules --entity InventoryItem
```

### Workflow 6: Skip Shared Project References

```bash
dn g m --name MBPOS.Modules.Payments --location src/Modules --no-shared
```

### Workflow 7: Use a Custom Shared Root

```bash
dn g m --name MyApp.Modules.Billing --location src/Modules --shared-root MyApp
# References will point to MyApp.Shared.Domain, MyApp.Shared.Application, etc.
```

### Workflow 8: Specify a Solution File Explicitly

```bash
dn g m --name MBPOS.Modules.Products --location src/Modules --sln MyProject.slnx
```

---

## ⚠️ Important Notes

- **Run from your solution root** — folder scanning, `.sln` detection, and `--host` resolution all depend on the current working directory.
- **Existing folder = safe** — if `--location` resolves to a folder that already exists, `dn` reuses it.
- **Existing project = blocked** — if the project/module folder already exists, `dn` aborts to prevent overwriting.
- **Solution auto-detection** — `.sln` / `.slnx` files in the current directory are detected automatically. Use `--sln` to override.
- **Host wiring is best-effort** — a missing or ambiguous `--host` prints a warning but never blocks the rest of the scaffold.
- **`Program.cs` patching** — when `--host` is used with module generation, `dn` also patches the host's `Program.cs` to add the module's `Add{Module}Infrastructure()` and `Add{Module}Presentation()` calls into the existing service chains.

---

## 🆘 Getting Help

```bash
dn --help
dn -h
```
