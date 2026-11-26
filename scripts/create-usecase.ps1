<#
.SYNOPSIS
    Scaffold a Clean Architecture use case (Command / Handler / Result / ReadModel) and
    auto-register its handler in the module's Application DI extension file.

.DESCRIPTION
    This is a thin wrapper around:

        dn g usecase --name <Prefix>UseCase --location <Module>/application/usecases/<Prefix>

    The CLI creates the use-case folder and its four files, then registers the
    handler in the module's *ApplicationServiceCollectionExtensions.cs.

.EXAMPLE
    .\scripts\create-usecase.ps1 -Name GetItemDetailByStoreUseCase -Location modules/item/application/usecases/GetItemDetailByStore

.EXAMPLE
    .\scripts\create-usecase.ps1 -Name CreateProductUseCase -Location modules/products/application/usecases/CreateProduct -ResultModel Guid -NoValidator

.EXAMPLE
    .\scripts\create-usecase.ps1 -Name DeleteItemUseCase -Location modules/item/application/usecases/DeleteItem -DryRun

.PARAMETER Name
    Use-case name ending in "UseCase", e.g. GetItemDetailByStoreUseCase.

.PARAMETER Location
    Module use-cases path ending in the <Prefix> folder, e.g.
    modules/item/application/usecases/GetItemDetailByStore.

.PARAMETER ResultModel
    Optional type used by the Result.Success record.
    Defaults to IReadOnlyList<{Entity}DetailModel> when that type exists in the
    module, otherwise IReadOnlyList<{Entity}Model>.

.PARAMETER Entity
    Optional entity-name override (defaults to the module name singularized).

.PARAMETER NoValidator
    Skip creating the FluentValidation command validator.

.PARAMETER DryRun
    Preview the changes without writing files or updating DI.

.PARAMETER DnPath
    Optional path to a specific dn executable. Defaults to the local build
    (bin/Debug or bin/Release) when this script lives inside the DnScaffold
    repository, otherwise the globally installed 'dn' tool.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $Name,

    [Parameter(Mandatory = $true)]
    [ArgumentCompleter({
        param($commandName, $parameterName, $wordToComplete, $commandAst, $fakeBoundParameters)

        $separator = [System.IO.Path]::DirectorySeparatorChar
        $alternateSeparator = [System.IO.Path]::AltDirectorySeparatorChar
        $inputPath = if ([string]::IsNullOrWhiteSpace($wordToComplete)) { "." } else { $wordToComplete }
        $hasTrailingSeparator = $inputPath.EndsWith($separator) -or $inputPath.EndsWith($alternateSeparator)
        $trimmedPath = $inputPath.TrimEnd($separator, $alternateSeparator)
        $leafPrefix = if ($hasTrailingSeparator) { "" } else { [System.IO.Path]::GetFileName($trimmedPath) }
        $parentPath = if ($hasTrailingSeparator) { $trimmedPath } else { [System.IO.Path]::GetDirectoryName($trimmedPath) }

        if ([string]::IsNullOrWhiteSpace($parentPath)) {
            $parentPath = "."
        }

        try {
            $resolvedParent = [System.IO.Path]::GetFullPath($parentPath, (Get-Location).Path)
            $directories = Get-ChildItem -LiteralPath $resolvedParent -Directory -ErrorAction SilentlyContinue |
                Where-Object { $_.Name.StartsWith($leafPrefix, [System.StringComparison]::OrdinalIgnoreCase) }

            foreach ($directory in $directories) {
                $completion = if ($parentPath -eq ".") {
                    $directory.Name
                } else {
                    Join-Path $parentPath $directory.Name
                }

                [System.Management.Automation.CompletionResult]::new(
                    $completion,
                    $completion,
                    [System.Management.Automation.CompletionResultType]::ProviderItem,
                    $directory.FullName)
            }
        } catch {
            # Ignore invalid or inaccessible paths during tab completion.
        }
    })]
    [string] $Location,

    [string] $ResultModel,
    [string] $Entity,
    [switch] $NoValidator,
    [switch] $DryRun,
    [string] $DnPath
)

$ErrorActionPreference = "Stop"

function Resolve-DnCommand {
    param([string] $ExplicitPath)

    if (-not [string]::IsNullOrWhiteSpace($ExplicitPath)) {
        return (Resolve-Path $ExplicitPath -ErrorAction Stop).Path
    }

    $repoRoot = Split-Path $PSScriptRoot -Parent
    foreach ($configuration in @("Debug", "Release")) {
        $candidate = Join-Path $repoRoot "bin\$configuration\net10.0\ShortHandCreater.exe"
        if (Test-Path $candidate) {
            return (Resolve-Path $candidate).Path
        }
    }

    $dn = Get-Command dn -ErrorAction SilentlyContinue
    if ($null -eq $dn) {
        throw "The 'dn' global tool was not found. Install it with: dotnet tool install --global DnScaffold"
    }

    return $dn.Source
}

$dnCommand = Resolve-DnCommand -ExplicitPath $DnPath
$arguments = @("g", "usecase", "--name", $Name, "--location", $Location)

if (-not [string]::IsNullOrWhiteSpace($ResultModel)) {
    $arguments += @("--result-model", $ResultModel)
}

if (-not [string]::IsNullOrWhiteSpace($Entity)) {
    $arguments += @("--entity", $Entity)
}

if ($NoValidator) {
    $arguments += "--no-validator"
}

if ($DryRun) {
    $arguments += "--dry-run"
}

& $dnCommand @arguments
exit $LASTEXITCODE
