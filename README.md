# dn — .NET Project Scaffolder

CLI tool to scaffold a new .NET project into your existing solution/folder structure.
Locations are resolved dynamically against your existing folders on disk — **no config file needed**.

## Install

```
dotnet tool install --global DnScaffold
```

Update later with:

```
dotnet tool update --global DnScaffold
```

## Quick Start

```
dn g --name MyProject.Domain --location modules/myproject
dn g webapi --name MyProject.Api --location host
```

## Usage

```
dn <action> [type] --name <name> --location <location> [--sln <path>] [--dry-run]
```

### Actions

| Action | Description |
|---|---|
| `g`, `generate` | Generate a new project (currently the only supported action) |

### Type

Optional positional argument — any valid `dotnet new` template shortname. **Defaults to `classlib`** if omitted.

Common values:

```
classlib   console   webapi   web   mvc   razor
blazorwasm blazorserver   worker   xunit   nunit   mstest
wpf   winforms   grpc
```

Run `dotnet new list` to see every template installed on your machine — any valid template shortname works with `dn`, not just the ones listed above.

### Options

| Flag | Required | Description |
|---|---|---|
| `-n`, `--name` | Yes | Name of the project to create. |
| `-l`, `--location` | Yes | Folder path or partial/fuzzy path. Matches existing folders on disk (fully or partially, from the end of the path); creates a new path if nothing matches. |
| `-s`, `--sln` | No | Path to a specific `.sln`/`.slnx` file. Auto-detected from the current directory if omitted. |
| `--dry-run` | No | Preview what would happen without creating anything. |
| `-h`, `--help` | No | Show the built-in help message. |

## How Location Resolution Works

Say your disk already has: `src/Modules/Identity`

```
dn g classlib --name X --location identity
```
→ matches existing folder `src/Modules/Identity` exactly → uses it

```
dn g classlib --name X --location modules/identity
```
→ matches existing folder `src/Modules/Identity` exactly → uses it

```
dn g classlib --name X --location modules/vendors
```
→ `modules` matches `src/Modules`, `vendors` doesn't exist yet
→ resolves to `src/Modules/vendors` (new subfolder, created fresh)

```
dn g classlib --name X --location brandnewpath/here
```
→ no match anywhere on disk → created exactly as typed: `brandnewpath/here`

```
dn g classlib --name X --location domain
```
→ matches **multiple** existing folders (e.g. every module has a `Domain` folder)
→ tool lists all matches and asks you to be more specific instead of guessing

## Examples

Class library, using an existing folder by partial name:
```
dn g classlib --name MBPOS.Modules.Identity.Presentation --location identity
```

Full explicit path (always works, no resolution needed):
```
dn g classlib --name MBPOS.Modules.Sales.Domain --location src/Modules/Sales
```

New module folder under an existing parent:
```
dn g classlib --name MBPOS.Modules.Vendors.Domain --location modules/vendors
```

Shared project:
```
dn g classlib --name MBPOS.Shared.Infrastructure --location shared
```

Web API host:
```
dn g webapi --name MBPOS.Api --location host
```

xUnit test project:
```
dn g xunit --name MBPOS.Modules.Payments.UnitTests --location tests/unittests
```

Preview before creating anything:
```
dn g classlib --name MBPOS.Modules.Sales.Domain --location modules/sales --dry-run
```

Point to a specific solution file explicitly:
```
dn g classlib --name MBPOS.Modules.Sales.Domain --location modules/sales --sln MbPos.Backend.sln
```

## Notes

- If `--location` resolves to an existing folder, the tool reuses it instead of failing.
- If the target project folder itself already exists, the tool aborts to avoid overwriting.
- The tool auto-detects `.sln` or `.slnx` files in the current directory; use `--sln` to override.
- Run this tool from your solution root so folder scanning and `.sln` detection work correctly.

## License

MIT