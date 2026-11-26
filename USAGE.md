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

### 3. `generate usecase` (`g usecase`) — Add a Use Case to an Existing Module

```bash
dn g usecase --name GetItemDetailByStoreUseCase --location modules/item/application/usecases/GetItemDetailByStore
```

Creates a `GetItemDetailByStore/` folder under the module's `Application/UseCases/` folder with `Command`, `Handler`, `Result`, and `ReadModel` files, then auto-registers the handler in the module's `*ApplicationServiceCollectionExtensions.cs`.

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
| `--result-model`      |       | `usecase`        | auto         | Success payload type (defaults to `IReadOnlyList<{Entity}DetailModel>` when it exists, else `IReadOnlyList<{Entity}Model>`) |
| `--no-validator`      |       | `usecase`        | off          | Skip creating the FluentValidation command validator               |
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
| `usecase`     |              | Use-case scaffold inside an existing module's Application layer |

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

## 🧩 Use-Case (Feature) Structure

Running:

```bash
dn g usecase --name GetItemDetailByStoreUseCase --location modules/item/application/usecases/GetItemDetailByStore
```

creates:

```
MBPOS.Modules.Item.Application/
└── UseCases/
    └── GetItemDetailByStore/
        ├── GetItemDetailByStoreCommand.cs    (sealed record)
        ├── GetItemDetailByStoreHandler.cs    (sealed class)
        └── GetItemDetailByStoreResult.cs     (abstract record)
```

The `Result` file contains the standard nested records:

```csharp
public abstract record GetItemDetailByStoreResult
{
    public sealed record Success(
        IReadOnlyList<ItemDetailModel> Items) : GetItemDetailByStoreResult;

    public sealed record ValidationFailed(
        IReadOnlyDictionary<string, string[]> Errors) : GetItemDetailByStoreResult;

    public sealed record Failed(string Message) : GetItemDetailByStoreResult;
}
```

When FluentValidation is referenced by the Application project, a validator is also created at:

```
MBPOS.Modules.Item.Application/Validation/GetItemDetailByStoreCommandValidator.cs
```

The handler is auto-registered in `DependencyInjection/*ApplicationServiceCollectionExtensions.cs`:

```csharp
using MBPOS.Modules.Item.Application.UseCases.GetItemDetailByStore;

// ...

services.AddScoped<GetItemDetailByStoreHandler>();
```

The location is resolved against the real module layout, so `modules/item/application/usecases/<Prefix>` also finds fully-qualified folders such as `src/Modules/Item/MBPOS.Modules.Item.Application/UseCases`.

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

### Workflow 9: Add a Use Case to an Existing Module

```bash
# Preview first
dn g usecase --name GetItemDetailByStoreUseCase --location modules/item/application/usecases/GetItemDetailByStore --dry-run

# Create Command / Handler / Result / ReadModel and auto-register the handler
dn g usecase --name GetItemDetailByStoreUseCase --location modules/item/application/usecases/GetItemDetailByStore

# Custom Success payload and no validator
dn g usecase --name CreateItemUseCase --location modules/item/application/usecases/CreateItem --result-model "Guid" --no-validator
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
