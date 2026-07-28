# dn — .NET Module & Project Scaffolder

`dn` is a command-line tool that generates new .NET projects — or a full, layered module — into your existing solution, wires up the project references between them, and registers everything in your `.sln`/`.slnx`. Locations are resolved against your actual folder structure, so there's no config file to maintain.

---

## Quick Start

```bash
# Scaffold a single class library
dn g --name MBPOS.Modules.Sales.Domain --location modules/sales

# Scaffold a full layered module
dn g m --name MBPOS.Modules.Products --location src/Modules

# Wire an already-scaffolded module into your API host
dn link --name MBPOS.Modules.Products --location src/Modules --host src/Host/MBPOS.Api
```

Run `dn --help` any time for the full built-in reference.

---

## Actions

| Action | Aliases | What it does |
|---|---|---|
| `generate` | `g` | Creates a new project, or a full module (`m` / `module` type) |
| `link` | `l` | Wires an entry-point/host project's references into a module that's **already** been scaffolded |

---

## Basic Usage

```bash
dn <action> [type] --name <name> --location <location> [options]
```

- **`action`** — `g`/`generate` or `link`/`l`
- **`type`** (optional, `generate` only) — a dotnet template shortname (`classlib`, `webapi`, `console`, `worker`, `xunit`, …) or `m`/`module` for a full module scaffold. Defaults to `classlib` if omitted.

---

## Options

| Flag | Required | Applies to | Description |
|---|---|---|---|
| `-n`, `--name` | ✅ | all | Project name, or for `module` type, the module prefix (e.g. `MBPOS.Modules.Products`) |
| `-l`, `--location` | ✅ | all | Folder path, or a partial/fuzzy path matched against existing folders. For `module` type, this is the **parent** folder the module gets created under |
| `-s`, `--sln` | optional | all | Path to a specific `.sln`/`.slnx`. Auto-detected in the current directory otherwise. **Required** for module scaffolds |
| `-e`, `--entity` | optional | `module` | Overrides the auto-singularized entity name (e.g. `Products` → `Product`) |
| `-r`, `--shared-root` | optional | `module` | Prefix for `Shared.*` references. Defaults to `MBPOS` → `src/Shared/MBPOS.Shared.<Layer>` |
| `--no-shared` | optional | `module` | Skip wiring `Shared.*` references entirely |
| `--host` | optional | `module`, `link` | A host `.csproj` path, or a folder containing exactly one `.csproj`. Wires the module's Presentation/Application/Infrastructure into it |
| `--dry-run` | optional | all | Preview everything that would happen — no files, folders, or references are created |
| `-h`, `--help` | | | Show full usage |

---

## How `--location` Resolution Works

You don't need to type exact paths — `dn` scans your existing folders and matches from the end of the path.

Say your disk already has `src/Modules/Identity`:

| You type | What happens |
|---|---|
| `--location identity` | Matches `src/Modules/Identity` exactly → uses it |
| `--location modules/vendors` | `modules` matches `src/Modules`, `vendors` doesn't exist yet → creates `src/Modules/vendors` |
| `--location brandnewpath/here` | No match anywhere → creates exactly as typed |
| `--location domain` | Matches **multiple** folders (every module has a `Domain` folder) → lists them and asks you to be more specific |

💡 **Tip:** Always try `--dry-run` first if you're unsure how a location will resolve.

---

## Module Scaffold (`m` / `module`)

```bash
dn g m --name MBPOS.Modules.Products --location src/Modules
```

Creates a full 5-project module under `src/Modules/Products`, each with real stub files (not the default `Class1.cs`), the standard sub-folder layout, and references wired between layers automatically.

```
MBPOS.Modules.Products.Domain/
  Entities/
    ProductModel.cs
  Services/            (empty — reserved for domain services)
  ValueObjects/         (empty)

MBPOS.Modules.Products.Contracts/
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

UnitTests/MBPOS.Modules.Products.UnitTests/
  Controllers/
    ProductControllerTests.cs
```

> Entity name (`Product`) is auto-singularized from the module folder name (`Products`). Override with `--entity` for irregular cases.

### Where the repository contract lives

The repository interface (`I{Entity}Repository`) lives in **`Application/Abstractions`**, not Contracts. `Contracts` is reserved for DTOs only (`Requests` / `Responses`). Infrastructure implements the interface directly, and Presentation's controller depends on it too — no separate `{Entity}Service` layer.

### Reference wiring (always applied)

```
Contracts       -> Domain
Application     -> Contracts, Domain
Infrastructure  -> Application, Contracts, Domain
Presentation    -> Application, Contracts
UnitTests       -> Application, Presentation
```

Presentation also gets a `FrameworkReference` to `Microsoft.AspNetCore.App`, since the generated controller inherits `ControllerBase` and the generated `{ModuleName}PresentationServiceCollectionExtensions` exposes:

```csharp
public static IMvcBuilder Add{ModuleName}Presentation(this IMvcBuilder mvcBuilder)
{
    ArgumentNullException.ThrowIfNull(mvcBuilder);
    mvcBuilder.AddApplicationPart(typeof({Entity}Controller).Assembly);
    return mvcBuilder;
}
```

Call it from your host's `AddControllers()` chain to register the module's controllers.

### Shared project references (best-effort)

Unless `--no-shared` is passed, `dn` also tries to wire:

```
Domain          -> {SharedRoot}.Shared.Domain
Application     -> {SharedRoot}.Shared.Application
Infrastructure  -> {SharedRoot}.Shared.Infrastructure
```

expected at `src/Shared/{SharedRoot}.Shared.<Layer>`. If a Shared project isn't found on disk, `dn` just prints a warning and moves on — it never blocks the rest of the scaffold.

### Wiring into a host, at scaffold time

Pass `--host` to also add `ProjectReference`s from your API/host project to the module's Presentation, Application, and Infrastructure projects:

```bash
dn g m --name MBPOS.Modules.Products --location src/Modules --host src/Host/MBPOS.Api
```

---

## Wiring a Host After the Fact — `link`

If the module already exists (scaffolded earlier without `--host`, or in general), use `link` instead of regenerating it:

```bash
dn link --name MBPOS.Modules.Products --location src/Modules --host src/Host/MBPOS.Api
```

`--name` and `--location` must match what you originally passed to `dn g m`. This only runs `dotnet add reference` against Presentation/Application/Infrastructure — it never touches Domain, Contracts, or the module's files, and doesn't require a `.sln`.

---

## More Examples

```bash
# Class library, using an existing folder by partial name
dn g classlib --name MBPOS.Modules.Identity.Presentation --location identity

# Full explicit path (no resolution needed)
dn g classlib --name MBPOS.Modules.Sales.Domain --location src/Modules/Sales

# New module folder under an existing parent
dn g classlib --name MBPOS.Modules.Vendors.Domain --location modules/vendors

# Explicit entity name (irregular plural)
dn g m --name MBPOS.Modules.Inventory --location src/Modules --entity InventoryItem

# Preview a module scaffold before creating anything
dn g m --name MBPOS.Modules.Products --location src/Modules --dry-run

# Web API host project
dn g webapi --name MBPOS.Api --location host

# Point to a specific solution file explicitly
dn g m --name MBPOS.Modules.Products --location src/Modules --sln MBPOS.slnx
```

---

## Notes

- If `--location` resolves to an existing folder, `dn` reuses it instead of failing.
- If the target project (or module) folder already exists, `dn` aborts to avoid overwriting anything.
- `.sln`/`.slnx` files are auto-detected in the current directory; use `--sln` to override.
- Run `dn` from your solution root so folder scanning, `.sln` detection, and `--host` resolution all work correctly.
- The generated `Queries` stub in Infrastructure is a placeholder for read-side query methods (e.g. Dapper or raw SQL) — it doesn't pull in any ORM by default.
- Always test with `--dry-run` before running for real on an unfamiliar location or module name.