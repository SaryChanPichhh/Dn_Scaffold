<#
.SYNOPSIS
    Automatically scans .NET Clean Architecture modules and registers dependencies
    and FluentValidation validators in each layer's *ServiceCollectionExtensions.cs.

.DESCRIPTION
    Scans Application, Infrastructure, and Presentation projects inside one or more
    module folders (for example, src/Modules/Products) and updates their
    DependencyInjection/*ServiceCollectionExtensions.cs files:

      - Application:
          * UseCase / Query handlers (*Handler)
          * Application services (*Service, *Client, *Provider, *Gateway, *Adapter)
          * FluentValidation validators (AbstractValidator<T>) via
            AddValidatorsFromAssemblyContaining<T>() or explicit IValidator<T>
          * Options (*Options) when -IncludeOptions is specified and IConfiguration is accepted
      - Infrastructure:
          * Repositories (*Repository -> matching interface or concrete)
          * Infrastructure services (*Service, *Client, *Provider, *Gateway, *Adapter)
          * Options (*Options) when -IncludeOptions is specified and IConfiguration is accepted
      - Presentation:
          * Request-level FluentValidation validators (AbstractValidator<TRequest>)
            via <mvcBuilder>.Services.Add<Lifetime><IValidator<T>, TValidator>()

    Existing manual registrations and manual using directives outside the managed
    "// <auto-registered>" / "// <auto-usings>" blocks are preserved and never
    duplicated. Re-running the script updates both blocks idempotently.

.EXAMPLE
    .\scripts\register-di.ps1

.EXAMPLE
    .\scripts\register-di.ps1 -ModuleName Products -DryRun

.EXAMPLE
    .\scripts\register-di.ps1 -ModulePath src/Modules/Item -ExplicitValidators -Lifetime Scoped
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$ModulePath,
    [string]$ModuleName,
    [string]$ModulesRoot = "src/Modules",

    [ValidateSet("Scoped", "Transient", "Singleton")]
    [string]$Lifetime = "Scoped",

    [switch]$ExplicitValidators,
    [switch]$IncludeOptions,
    [switch]$DryRun
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# --- C# parsing helpers ---

function Remove-CSharpComments {
    param([string]$Text)
    # Strip block comments and line comments while keeping line structure intact.
    $withoutBlock = [regex]::Replace($Text, '/\*[\s\S]*?\*/', '')
    return [regex]::Replace($withoutBlock, '(?m)//.*$', '')
}

function Split-TopLevelCommas {
    param([string]$Text)
    $parts = [System.Collections.Generic.List[string]]::new()
    if ([string]::IsNullOrWhiteSpace($Text)) { return $parts }

    $depthAngle = 0
    $depthParen = 0
    $start = 0

    for ($i = 0; $i -lt $Text.Length; $i++) {
        switch ($Text[$i]) {
            '<' { $depthAngle++ }
            '>' { if ($depthAngle -gt 0) { $depthAngle-- } }
            '(' { $depthParen++ }
            ')' { if ($depthParen -gt 0) { $depthParen-- } }
            ',' {
                if ($depthAngle -eq 0 -and $depthParen -eq 0) {
                    $segment = $Text.Substring($start, $i - $start).Trim()
                    if ($segment) { $parts.Add($segment) }
                    $start = $i + 1
                }
            }
        }
    }

    $tail = $Text.Substring($start).Trim()
    if ($tail) { $parts.Add($tail) }
    return $parts
}

function Get-FileMetadata {
    param([string]$FilePath)

    $raw = [System.IO.File]::ReadAllText($FilePath)
    $clean = Remove-CSharpComments -Text $raw

    $nsMatch = [regex]::Match($clean, '(?m)^\s*namespace\s+([A-Za-z_][\w.]*)\s*[;{]')
    $namespace = if ($nsMatch.Success) { $nsMatch.Groups[1].Value } else { "" }

    $usings = [System.Collections.Generic.List[string]]::new()
    foreach ($m in [regex]::Matches($clean, '(?m)^\s*using\s+(?:global\s+)?([A-Za-z_][\w.]*)\s*;')) {
        $usings.Add($m.Groups[1].Value)
    }

    $types = [System.Collections.Generic.List[psobject]]::new()

    $typeDeclPattern = '(?ms)^[ \t]*((?:(?:public|internal|private|protected|sealed|abstract|static|partial|readonly)\s+)*)(class|interface|record(?:\s+class|\s+struct)?|struct)\s+([A-Za-z_]\w*)(?:\s*<([^>]+)>)?(?:\s*\([^)]*\))?(?:\s*:\s*([^{;]+?))?\s*(?:where\b[^{]+)?(?:\{|;)'
    foreach ($m in [regex]::Matches($clean, $typeDeclPattern)) {
        $modifiers = $m.Groups[1].Value
        $kind = $m.Groups[2].Value
        $name = $m.Groups[3].Value
        $genericParams = $m.Groups[4].Value
        $baseListRaw = $m.Groups[5].Value

        $baseTypes = @(Split-TopLevelCommas -Text $baseListRaw | ForEach-Object {
            ($_ -replace '\([^)]*\)\s*$', '').Trim()
        })

        $types.Add([pscustomobject]@{
            FilePath          = $FilePath
            Namespace         = $namespace
            Usings            = @($usings)
            Modifiers         = $modifiers
            Kind              = $kind
            Name              = $name
            IsGeneric         = -not [string]::IsNullOrWhiteSpace($genericParams)
            IsAbstract        = ($modifiers -match '\babstract\b')
            IsStatic          = ($modifiers -match '\bstatic\b')
            IsNonPublicNested = ($modifiers -match '\b(private|protected)\b')
            BaseTypes         = $baseTypes
            CleanSource       = $clean
        })
    }

    return [pscustomobject]@{
        FilePath  = $FilePath
        Namespace = $namespace
        Usings    = @($usings)
        Types     = @($types)
    }
}

function Get-ModuleSourceIndex {
    param([string]$ModuleDir)

    $typeMap = @{} # TypeName -> HashSet[Namespace]
    $csFiles = @(Get-ChildItem -Path $ModuleDir -Filter "*.cs" -Recurse -File | Where-Object {
        $_.FullName -notmatch '[\\/](bin|obj|\.git)[\\/]'
    })

    $parsedFiles = [System.Collections.Generic.List[psobject]]::new()
    foreach ($f in $csFiles) {
        $meta = Get-FileMetadata -FilePath $f.FullName
        $parsedFiles.Add($meta)
        foreach ($t in $meta.Types) {
            if ([string]::IsNullOrWhiteSpace($t.Namespace)) { continue }
            if (-not $typeMap.ContainsKey($t.Name)) {
                $typeMap[$t.Name] = [System.Collections.Generic.HashSet[string]]::new()
            }
            [void]$typeMap[$t.Name].Add($t.Namespace)
        }
    }

    return [pscustomobject]@{
        Files   = @($parsedFiles)
        TypeMap = $typeMap
    }
}

function Resolve-TypeNamespace {
    param(
        [string]$TypeName,
        [string]$CurrentNamespace,
        [string[]]$FileUsings,
        [hashtable]$TypeMap
    )

    if ([string]::IsNullOrWhiteSpace($TypeName)) { return $null }

    $lookup = ($TypeName -replace '^global::', '' -replace '\?$', '').Trim()
    if ($lookup.Contains('.')) {
        $lookup = $lookup.Split('.')[-1]
    }

    if (-not $TypeMap.ContainsKey($lookup)) { return $null }
    $candidates = @($TypeMap[$lookup])
    if ($candidates.Count -eq 1) { return $candidates[0] }

    foreach ($u in $FileUsings) {
        if ($candidates -contains $u) { return $u }
    }
    if ($candidates -contains $CurrentNamespace) { return $CurrentNamespace }

    return $candidates[0]
}

function Find-MatchingInterface {
    param(
        [psobject]$TypeInfo,
        [hashtable]$TypeMap
    )

    $ignoredFrameworkInterfaces = @(
        "IDisposable", "IAsyncDisposable", "IEquatable", "IComparable",
        "ICloneable", "IFormattable", "IValidatableObject"
    )

    $interfaces = [System.Collections.Generic.List[string]]::new()
    foreach ($b in $TypeInfo.BaseTypes) {
        $simple = ($b -replace '<.*$', '').Split('.')[-1].Trim()
        if ($simple -match '^I[A-Z]' -and ($ignoredFrameworkInterfaces -notcontains $simple)) {
            $interfaces.Add($b.Trim())
        }
    }

    if ($interfaces.Count -eq 0) { return $null }

    $exact = "I$($TypeInfo.Name)"
    foreach ($iface in $interfaces) {
        $short = ($iface -replace '<.*$', '').Split('.')[-1]
        if ($short -eq $exact) { return $iface }
    }

    foreach ($iface in $interfaces) {
        $short = ($iface -replace '<.*$', '').Split('.')[-1]
        $ns = Resolve-TypeNamespace -TypeName $short -CurrentNamespace $TypeInfo.Namespace -FileUsings $TypeInfo.Usings -TypeMap $TypeMap
        if ($null -ne $ns) { return $iface }
    }

    return $interfaces[0]
}

# --- Layer scanning & registration planning ---

function New-RegistrationEntry {
    param(
        [string]$Line,
        [string[]]$Usings
    )
    return [pscustomobject]@{
        Line   = $Line
        Usings = @($Usings | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique)
    }
}

function Get-LayerPlan {
    param(
        [string]$Layer,
        [string]$LayerProjectDir,
        [psobject]$ModuleIndex,
        [string]$DiFilePath,
        [string]$Lifetime,
        [switch]$ExplicitValidators,
        [switch]$IncludeOptions
    )

    $diClean = Remove-CSharpComments -Text ([System.IO.File]::ReadAllText($DiFilePath))

    $paramName = "services"
    $serviceTarget = "services"
    $configParam = $null

    $sigMatch = [regex]::Match($diClean, 'this\s+(IServiceCollection|IMvcBuilder)\s+([A-Za-z_]\w*)(?:\s*,\s*([^)]+))?')
    if ($sigMatch.Success) {
        $paramType = $sigMatch.Groups[1].Value
        $paramName = $sigMatch.Groups[2].Value
        $serviceTarget = if ($paramType -eq "IMvcBuilder") { "$paramName.Services" } else { $paramName }

        $extraParams = $sigMatch.Groups[3].Value
        if ($extraParams -match '\bIConfiguration\s+([A-Za-z_]\w*)') {
            $configParam = $Matches[1]
        }
    }

    $layerFullPrefix = [System.IO.Path]::GetFullPath($LayerProjectDir)
    $layerFiles = @($ModuleIndex.Files | Where-Object {
        [System.IO.Path]::GetFullPath($_.FilePath).StartsWith($layerFullPrefix, [System.StringComparison]::OrdinalIgnoreCase)
    })

    $entries = [System.Collections.Generic.List[psobject]]::new()
    $validators = [System.Collections.Generic.List[psobject]]::new()

    foreach ($file in $layerFiles) {
        foreach ($type in $file.Types) {
            if ($type.Kind -ne "class" -or $type.IsAbstract -or $type.IsStatic -or $type.IsGeneric -or $type.IsNonPublicNested) {
                continue
            }

            if ($type.Name -like "*ServiceCollectionExtensions" -or $type.Name -like "*Controller") {
                continue
            }

            # 1. FluentValidation validator (AbstractValidator<T>)
            $validatorBase = $type.BaseTypes | Where-Object { $_ -match '^AbstractValidator<(.+)>$' } | Select-Object -First 1
            if ($null -ne $validatorBase -and $validatorBase -match '^AbstractValidator<(.+)>$') {
                $validators.Add([pscustomobject]@{
                    ValidatorType = $type
                    TargetType    = $Matches[1].Trim()
                })
                continue
            }

            # 2. Options classes (requires IConfiguration parameter on the DI extension method)
            if ($type.Name -like "*Options") {
                if ($IncludeOptions -and $null -ne $configParam) {
                    $sectionExpr = if ($type.CleanSource -match 'const\s+string\s+SectionName\s*=') {
                        "$($type.Name).SectionName"
                    }
                    else {
                        "`"$($type.Name -replace 'Options$', '')`""
                    }

                    $entries.Add((New-RegistrationEntry `
                        -Line "$serviceTarget.Configure<$($type.Name)>($configParam.GetSection($sectionExpr));" `
                        -Usings @($type.Namespace)))
                }
                continue
            }

            # 3. Services / Handlers / Repositories by layer convention
            $isRegistrable = switch ($Layer) {
                "Application"    { $type.Name -match '(Handler|Service|Client|Provider|Gateway|Adapter)$' }
                "Infrastructure" { $type.Name -match '(Repository|Service|Client|Provider|Gateway|Adapter)$' }
                default          { $false }
            }
            if (-not $isRegistrable) { continue }

            $iface = Find-MatchingInterface -TypeInfo $type -TypeMap $ModuleIndex.TypeMap
            $usings = [System.Collections.Generic.List[string]]::new()
            $usings.Add($type.Namespace)

            if ($null -ne $iface) {
                $ifaceShort = ($iface -replace '<.*$', '').Split('.')[-1]
                $ifaceNs = Resolve-TypeNamespace -TypeName $ifaceShort -CurrentNamespace $type.Namespace -FileUsings $type.Usings -TypeMap $ModuleIndex.TypeMap
                if ($null -ne $ifaceNs) { $usings.Add($ifaceNs) }

                $entries.Add((New-RegistrationEntry `
                    -Line "$serviceTarget.Add$Lifetime<$iface, $($type.Name)>();" `
                    -Usings $usings.ToArray()))
            }
            else {
                $entries.Add((New-RegistrationEntry `
                    -Line "$serviceTarget.Add$Lifetime<$($type.Name)>();" `
                    -Usings $usings.ToArray()))
            }
        }
    }

    # 4. FluentValidation registrations
    if ($validators.Count -gt 0) {
        if ($Layer -eq "Application" -and -not $ExplicitValidators) {
            # Application installs FluentValidation.DependencyInjectionExtensions, so assembly scanning registers all validators.
            $firstValidator = $validators[0].ValidatorType
            $entries.Add((New-RegistrationEntry `
                -Line "$serviceTarget.AddValidatorsFromAssemblyContaining<$($firstValidator.Name)>();" `
                -Usings @("FluentValidation", $firstValidator.Namespace)))
        }
        else {
            foreach ($v in $validators) {
                $vt = $v.ValidatorType
                $usings = [System.Collections.Generic.List[string]]::new()
                $usings.Add("FluentValidation")
                $usings.Add($vt.Namespace)

                $targetNs = Resolve-TypeNamespace -TypeName $v.TargetType -CurrentNamespace $vt.Namespace -FileUsings $vt.Usings -TypeMap $ModuleIndex.TypeMap
                if ($null -ne $targetNs) { $usings.Add($targetNs) }

                $entries.Add((New-RegistrationEntry `
                    -Line "$serviceTarget.Add$Lifetime<IValidator<$($v.TargetType)>, $($vt.Name)>();" `
                    -Usings $usings.ToArray()))
            }
        }
    }

    # Deduplicate entries by generated statement while keeping stable order
    $seenLines = [System.Collections.Generic.HashSet[string]]::new()
    $uniqueEntries = [System.Collections.Generic.List[psobject]]::new()
    foreach ($e in $entries) {
        if ($seenLines.Add($e.Line)) {
            $uniqueEntries.Add($e)
        }
    }

    return [pscustomobject]@{
        ReturnParam = $paramName
        Entries     = @($uniqueEntries)
    }
}

# --- File patching ---

function Update-DiExtensionFile {
    param(
        [string]$FilePath,
        [psobject]$Plan,
        [switch]$DryRun
    )

    $raw = [System.IO.File]::ReadAllText($FilePath)
    $newline = if ($raw.Contains("`r`n")) { "`r`n" } else { "`n" }

    $regStart = "// <auto-registered>"
    $regEnd = "// </auto-registered>"
    $usingStart = "// <auto-usings>"
    $usingEnd = "// </auto-usings>"

    # Inspect manual code outside the managed blocks so we never duplicate manual registrations or usings.
    $outsideAuto = [regex]::Replace($raw, "(?ms)[ \t]*$([regex]::Escape($regStart)).*?$([regex]::Escape($regEnd))\r?\n?", '')
    $outsideAuto = [regex]::Replace($outsideAuto, "(?ms)[ \t]*$([regex]::Escape($usingStart)).*?$([regex]::Escape($usingEnd))\r?\n?", '')
    $outsideClean = Remove-CSharpComments -Text $outsideAuto

    $existingGenericArgs = [System.Collections.Generic.HashSet[string]]::new()
    foreach ($m in [regex]::Matches($outsideClean, '\.(?:Add(?:Scoped|Transient|Singleton)|Configure)<([^;()]+)>\s*\(')) {
        [void]$existingGenericArgs.Add(($m.Groups[1].Value -replace '\s+', ''))
    }
    $hasManualAssemblyValidatorScan = ($outsideClean -match 'AddValidatorsFromAssemblyContaining<')

    $manualUsings = [System.Collections.Generic.HashSet[string]]::new()
    foreach ($m in [regex]::Matches($outsideClean, '(?m)^\s*using\s+(?:global\s+)?([A-Za-z_][\w.]*)\s*;')) {
        [void]$manualUsings.Add($m.Groups[1].Value)
    }

    $toEmit = [System.Collections.Generic.List[psobject]]::new()
    foreach ($entry in $Plan.Entries) {
        $line = $entry.Line
        if ($line -match 'AddValidatorsFromAssemblyContaining<') {
            if (-not $hasManualAssemblyValidatorScan) {
                $toEmit.Add($entry)
            }
            continue
        }

        if ($line -match '\.(?:Add(?:Scoped|Transient|Singleton)|Configure)<([^;()]+)>\s*\(') {
            $normalizedArgs = ($Matches[1] -replace '\s+', '')
            if (-not $existingGenericArgs.Contains($normalizedArgs)) {
                $toEmit.Add($entry)
            }
        }
    }

    $updated = $raw
    $hasRegBlock = $updated.Contains($regStart) -and $updated.Contains($regEnd)
    $hasUsingBlock = $updated.Contains($usingStart) -and $updated.Contains($usingEnd)

    $indent = "        "
    if ($updated -match "(?m)^([ \t]+)return\s+$([regex]::Escape($Plan.ReturnParam))\s*;") {
        $indent = $Matches[1]
    }

    # 1. Update // <auto-registered> ... // </auto-registered>
    if ($toEmit.Count -gt 0) {
        $blockLines = [System.Collections.Generic.List[string]]::new()
        $blockLines.Add("$indent$regStart")
        foreach ($entry in $toEmit) {
            $blockLines.Add("$indent$($entry.Line)")
        }
        $blockLines.Add("$indent$regEnd")
        $newRegBlock = ($blockLines -join $newline)

        if ($hasRegBlock) {
            $updated = [regex]::Replace(
                $updated,
                "(?ms)^[ \t]*$([regex]::Escape($regStart)).*?$([regex]::Escape($regEnd))",
                { param($m) $newRegBlock })
        }
        else {
            $returnPattern = "(?m)^([ \t]*)(return\s+$([regex]::Escape($Plan.ReturnParam))\s*;)"
            if ([regex]::IsMatch($updated, $returnPattern)) {
                $updated = ([regex]$returnPattern).Replace(
                    $updated,
                    { param($m) "$newRegBlock$newline$newline$($m.Groups[1].Value)$($m.Groups[2].Value)" },
                    1)
            }
            else {
                Write-Warning "Could not locate 'return $($Plan.ReturnParam);' in $FilePath — skipped."
                return
            }
        }
    }
    elseif ($hasRegBlock) {
        $updated = [regex]::Replace(
            $updated,
            "(?ms)[ \t]*$([regex]::Escape($regStart)).*?$([regex]::Escape($regEnd))(\r?\n){1,2}",
            "")
    }

    # Updated script with missing namespace guard for Services
# (Only adding using directives if corresponding folder contains .cs files)
# The modification is inserted near the end where neededUsings are processed.


    if ($toEmit.Count -gt 0) {
        # Collect unique usings from the entries, excluding those already present manually.
        $rawUsings = @($toEmit | ForEach-Object { $_.Usings } | Sort-Object -Unique)
        # Guard against adding usings for empty service folders (e.g., Infrastructure.Services) to avoid compile errors.
        $guardedUsings = @()
        foreach ($ns in $rawUsings) {
            if ($ns -match '^MBPOS\.Modules\.(?<mod>[^.]+)\.Infrastructure\.Services$') {
                $moduleName = $Matches['mod']
                $serviceFolder = Join-Path $ModulesRoot $moduleName "Infrastructure" "Services"
                if (Test-Path (Join-Path $serviceFolder "*.cs")) {
                    $guardedUsings += $ns
                }
            } else {
                $guardedUsings += $ns
            }
        }
        $neededUsings = @($guardedUsings | Where-Object { -not $manualUsings.Contains($_) })
    } else {
        $neededUsings = @()
    }

    if ($neededUsings.Count -gt 0) {
        $usingBlockLines = [System.Collections.Generic.List[string]]::new()
        $usingBlockLines.Add($usingStart)
        foreach ($ns in $neededUsings) {
            $usingBlockLines.Add("using $ns;")
        }
        $usingBlockLines.Add($usingEnd)
        $newUsingBlock = ($usingBlockLines -join $newline)

        if ($hasUsingBlock) {
            $updated = [regex]::Replace(
                $updated,
                "(?ms)^[ \t]*$([regex]::Escape($usingStart)).*?$([regex]::Escape($usingEnd))",
                { param($m) $newUsingBlock })
        }
        else {
            $lastUsingMatches = [regex]::Matches($updated, '(?m)^[ \t]*using\s+[^;]+;[ \t]*$')
            if ($lastUsingMatches.Count -gt 0) {
                $last = $lastUsingMatches[$lastUsingMatches.Count - 1]
                $insertPos = $last.Index + $last.Length
                $updated = $updated.Insert($insertPos, $newline + $newUsingBlock)
            }
            else {
                $updated = $newUsingBlock + $newline + $newline + $updated
            }
        }
    }
    elseif ($hasUsingBlock) {
        $updated = [regex]::Replace(
            $updated,
            "(?ms)[ \t]*$([regex]::Escape($usingStart)).*?$([regex]::Escape($usingEnd))\r?\n?",
            "")
    }

    if ($updated -eq $raw) {
        Write-Host "  [OK]      $FilePath (up to date)"
        return
    }

    if ($DryRun -or -not $PSCmdlet.ShouldProcess($FilePath, "Update DI registrations")) {
        Write-Host "  [DRYRUN]  Would update $FilePath"
        foreach ($entry in $toEmit) {
            Write-Host "            + $($entry.Line)"
        }
        return
    }

    [System.IO.File]::WriteAllText($FilePath, $updated, [System.Text.UTF8Encoding]::new($false))
    Write-Host "  [UPDATED] $FilePath (+$($toEmit.Count) auto-registered entries)"
}

# --- Target module resolution & main loop ---

$targetModules = [System.Collections.Generic.List[string]]::new()
if (-not [string]::IsNullOrWhiteSpace($ModulePath)) {
    $resolved = Resolve-Path $ModulePath -ErrorAction Stop
    $targetModules.Add($resolved.Path)
}
elseif (-not [string]::IsNullOrWhiteSpace($ModuleName)) {
    $candidate = Join-Path $ModulesRoot $ModuleName
    $resolved = Resolve-Path $candidate -ErrorAction Stop
    $targetModules.Add($resolved.Path)
}
else {
    if (-not (Test-Path $ModulesRoot)) {
        throw "Modules root folder not found: '$ModulesRoot'. Pass -ModulePath or -ModulesRoot."
    }
    foreach ($dir in Get-ChildItem -Path $ModulesRoot -Directory) {
        $targetModules.Add($dir.FullName)
    }
}

if ($targetModules.Count -eq 0) {
    Write-Host "No module folders found."
    exit 0
}

foreach ($modDir in $targetModules) {
    $modName = Split-Path $modDir -Leaf
    Write-Host "Scanning module: $modName ($modDir)"

    $moduleIndex = Get-ModuleSourceIndex -ModuleDir $modDir

    foreach ($layer in @("Application", "Infrastructure", "Presentation")) {
        $layerDir = Get-ChildItem -Path $modDir -Directory |
            Where-Object { $_.Name -like "*.$layer" } |
            Select-Object -First 1

        if ($null -eq $layerDir) { continue }

        $diDir = Join-Path $layerDir.FullName "DependencyInjection"
        if (-not (Test-Path $diDir)) { continue }

        $diFile = Get-ChildItem -Path $diDir -Filter "*ServiceCollectionExtensions.cs" -File |
            Select-Object -First 1

        if ($null -eq $diFile) { continue }

        $plan = Get-LayerPlan `
            -Layer $layer `
            -LayerProjectDir $layerDir.FullName `
            -ModuleIndex $moduleIndex `
            -DiFilePath $diFile.FullName `
            -Lifetime $Lifetime `
            -ExplicitValidators:$ExplicitValidators `
            -IncludeOptions:$IncludeOptions

        Update-DiExtensionFile -FilePath $diFile.FullName -Plan $plan -DryRun:$DryRun
    }
}
