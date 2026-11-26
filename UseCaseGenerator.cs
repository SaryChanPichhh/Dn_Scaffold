using System.Text.RegularExpressions;

namespace ShortHandCreater;

// ============================================================
// Use-case scaffold: creates Command / Handler / Result / ReadModel files
// inside an EXISTING module's Application layer, then auto-registers
// the new handler (and optionally a validator) in the module's
// DependencyInjection extension files.
//
// Invoked via:
//   dn g usecase --name <Prefix>UseCase --location modules/item/application/usecases/<Prefix>
//
// Example:
//   dn g usecase --name GetItemDetailByStoreUseCase --location modules/item/application/usecases/GetItemDetailByStore
//
// Produces (inside the resolved UseCases folder):
//   GetItemDetailByStore/
//     GetItemDetailByStoreCommand.cs    (sealed record)
//     GetItemDetailByStoreHandler.cs    (sealed class)
//     GetItemDetailByStoreResult.cs     (abstract record + nested Success / ValidationFailed / Failed)
//     GetItemDetailByStoreReadModel.cs  (sealed record)
//
// The location is resolved against the real module layout, so it works even
// when the on-disk folder names differ (e.g. src/Modules/Item/MBPOS.Modules.Item.Application/UseCases).
// ============================================================
internal static class UseCaseGenerator
{
    private static readonly string[] ExcludedDirectoryNames =
        ["bin", "obj", ".git", ".vs", ".idea", "node_modules"];

    private static readonly string[] StructuralSegments =
        ["application", "usecases", "domain", "infrastructure", "presentation", "contracts", "unittests"];

    private sealed record TargetResolution(string AppDir, string ModuleRoot);

    private sealed record UseCaseContext(
        string Prefix,          // e.g. "MBPOS.Modules.Item"
        string ModuleName,      // e.g. "Item"
        string Entity,          // e.g. "Item"
        string UseCaseName,     // e.g. "GetItemDetailByStore"
        string FolderName,      // the use-case folder name (usually == UseCaseName)
        string UseCaseDir,      // full path to the use-case folder
        string AppDir,          // full path to the Application project
        string ModuleRoot,      // full path to the module root (parent of Application project)
        string ResultModelType, // the type the success record wraps — e.g. "IReadOnlyList<ItemDetailModel>"
        string? ResultModelUsing,
        bool SkipValidator,
        bool IsDryRun);

    internal static void Run(
        string nameArg,
        string location,
        string? entityFlag,
        string? resultModelFlag,
        bool skipValidator,
        bool isDryRun)
    {
        // --- parse & resolve ---

        // Strip the trailing "UseCase" suffix from --name to get the use-case prefix
        string useCaseName = nameArg.Trim();
        if (useCaseName.EndsWith("UseCase", StringComparison.OrdinalIgnoreCase))
            useCaseName = useCaseName[..^"UseCase".Length];

        if (string.IsNullOrWhiteSpace(useCaseName))
        {
            Console.WriteLine("Error: --name must contain a prefix before the \"UseCase\" suffix.");
            return;
        }

        string currentDir = Directory.GetCurrentDirectory();
        string typedFull = Path.GetFullPath(location, currentDir);
        string typedFolder = Path.GetFileName(typedFull.TrimEnd(
            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        TargetResolution? target = ResolveTarget(typedFull, currentDir);
        if (target == null)
        {
            Console.WriteLine($"Error: could not locate an *.Application project for \"{typedFull}\".");
            Console.WriteLine("Make sure --location points inside an existing module, e.g. modules/item/application/usecases/GetItemDetailByStore");
            return;
        }

        string prefix = DerivePrefix(target.AppDir, target.ModuleRoot);
        string moduleName = prefix.Split('.')[^1]; // last segment e.g. "Item"
        string entity = entityFlag ?? StringUtils.Singularize(moduleName);

        // The location's last segment is normally the <Prefix> folder. If it is a
        // structural folder name (application/usecases/...) or the module name
        // itself, fall back to --name.
        string folderName = useCaseName;
        if (!string.IsNullOrWhiteSpace(typedFolder)
            && !IsStructuralSegment(typedFolder)
            && !string.Equals(typedFolder, moduleName, StringComparison.OrdinalIgnoreCase))
        {
            folderName = typedFolder;
        }

        string useCasesDir = FindChildDirectory(target.AppDir, "UseCases")
                             ?? Path.Combine(target.AppDir, "UseCases");
        string useCaseDir = Path.Combine(useCasesDir, folderName);

        string resultModel = string.IsNullOrWhiteSpace(resultModelFlag)
            ? PickDefaultResultModel(target.ModuleRoot, entity)
            : resultModelFlag.Trim();

        string? resultModelUsing = ResolveUsingForType(target.ModuleRoot, resultModel, prefix);

        var ctx = new UseCaseContext(
            Prefix: prefix,
            ModuleName: moduleName,
            Entity: entity,
            UseCaseName: useCaseName,
            FolderName: folderName,
            UseCaseDir: useCaseDir,
            AppDir: target.AppDir,
            ModuleRoot: target.ModuleRoot,
            ResultModelType: resultModel,
            ResultModelUsing: resultModelUsing,
            SkipValidator: skipValidator,
            IsDryRun: isDryRun);

        if (isDryRun)
        {
            PrintDryRun(ctx);
            return;
        }

        // --- create files ---
        if (Directory.Exists(ctx.UseCaseDir) && Directory.GetFiles(ctx.UseCaseDir, "*.cs").Length > 0)
        {
            Console.WriteLine($"Error: use-case folder already contains .cs files: \"{ctx.UseCaseDir}\". Aborting to avoid overwrite.");
            return;
        }

        Directory.CreateDirectory(ctx.UseCaseDir);

        WriteStub(ctx, $"{useCaseName}Command.cs", BuildCommandFile(ctx));
        WriteStub(ctx, $"{useCaseName}Handler.cs", BuildHandlerFile(ctx));
        WriteStub(ctx, $"{useCaseName}Result.cs", BuildResultFile(ctx));
        WriteStub(ctx, $"{useCaseName}ReadModel.cs", BuildReadModelFile(ctx));

        string? validatorPath = null;
        if (!ctx.SkipValidator)
        {
            if (!HasFluentValidation(ctx))
            {
                Console.WriteLine($"  Note: FluentValidation is not referenced by {ctx.AppDir}. Skipped validator.");
            }
            else
            {
                // Validator goes into Application/Validation/
                string validationDir = FindChildDirectory(ctx.AppDir, "Validation")
                                       ?? Path.Combine(ctx.AppDir, "Validation");
                Directory.CreateDirectory(validationDir);
                validatorPath = Path.Combine(validationDir, $"{useCaseName}CommandValidator.cs");
                File.WriteAllText(validatorPath, BuildValidatorFile(ctx) + Environment.NewLine);
                Console.WriteLine($"  created {validatorPath}");
            }
        }

        // --- auto-register in DI ---
        AutoRegisterInDi(ctx, validatorPath);

        Console.WriteLine();
        Console.WriteLine("============================================================");
        Console.WriteLine($" Use-case \"{ctx.UseCaseName}\" scaffolded and registered.");
        Console.WriteLine("============================================================");
    }

    // ===== file templates =====

    private static string BuildCommandFile(UseCaseContext ctx) => $$"""
        namespace {{ctx.Prefix}}.Application.UseCases.{{ctx.FolderName}};

        public sealed record {{ctx.UseCaseName}}Command(
            // TODO: add command parameters
        );
        """;

    private static string BuildHandlerFile(UseCaseContext ctx)
    {
        string? abstractionsDir = FindChildDirectory(ctx.AppDir, "Abstractions");
        bool hasAbstractions = abstractionsDir != null
            && Directory.GetFiles(abstractionsDir, "*.cs", SearchOption.AllDirectories).Length > 0;

        string abstractionsUsing = hasAbstractions
            ? $"using {ctx.Prefix}.Application.Abstractions;{Environment.NewLine}{Environment.NewLine}"
            : "";

        return $$"""
            {{abstractionsUsing}}namespace {{ctx.Prefix}}.Application.UseCases.{{ctx.FolderName}};

            public sealed class {{ctx.UseCaseName}}Handler
            {
                public {{ctx.UseCaseName}}Handler()
                {
                    // TODO: inject repositories/services via constructor
                }

                public async Task<{{ctx.UseCaseName}}Result> HandleAsync(
                    {{ctx.UseCaseName}}Command command,
                    CancellationToken cancellationToken = default)
                {
                    ArgumentNullException.ThrowIfNull(command);

                    // TODO: implement {{ctx.UseCaseName}} use case logic
                    await Task.CompletedTask;
                    throw new NotImplementedException();
                }
            }
            """;
    }

    private static string BuildResultFile(UseCaseContext ctx)
    {
        string usingBlock = ctx.ResultModelUsing is null
            ? ""
            : $"using {ctx.ResultModelUsing};{Environment.NewLine}{Environment.NewLine}";

        return $$"""
            {{usingBlock}}namespace {{ctx.Prefix}}.Application.UseCases.{{ctx.FolderName}};

            public abstract record {{ctx.UseCaseName}}Result
            {
                public sealed record Success(
                    {{ctx.ResultModelType}} Items) :
                    {{ctx.UseCaseName}}Result;

                public sealed record ValidationFailed(
                    IReadOnlyDictionary<string, string[]> Errors) :
                    {{ctx.UseCaseName}}Result;

                public sealed record Failed(string Message) :
                    {{ctx.UseCaseName}}Result;
            }
            """;
    }

    private static string BuildReadModelFile(UseCaseContext ctx) => $$"""
        namespace {{ctx.Prefix}}.Application.UseCases.{{ctx.FolderName}};

        public sealed record {{ctx.UseCaseName}}ReadModel(
            Guid Id,
            string Name
        );
        """;

    private static string BuildValidatorFile(UseCaseContext ctx) => $$"""
        using FluentValidation;
        using {{ctx.Prefix}}.Application.UseCases.{{ctx.FolderName}};

        namespace {{ctx.Prefix}}.Application.Validation;

        public sealed class {{ctx.UseCaseName}}CommandValidator
            : AbstractValidator<{{ctx.UseCaseName}}Command>
        {
            public {{ctx.UseCaseName}}CommandValidator()
            {
                // TODO: add validation rules
            }
        }
        """;

    // ===== auto-register in DI =====

    private static void AutoRegisterInDi(UseCaseContext ctx, string? validatorPath)
    {
        string? diFile = FindApplicationDiFile(ctx.AppDir);
        if (diFile == null)
        {
            Console.WriteLine($"  Warning: no *ApplicationServiceCollectionExtensions.cs found under {ctx.AppDir}. Skipped auto-registration.");
            return;
        }

        string content = File.ReadAllText(diFile);
        string original = content;
        string newline = content.Contains("\r\n") ? "\r\n" : "\n";

        string serviceParam = FindServiceCollectionParamName(content);

        // --- add using directive ---
        content = EnsureUsing(content, $"using {ctx.Prefix}.Application.UseCases.{ctx.FolderName};", newline);

        var registrations = new List<string>();

        // --- handler registration ---
        string handlerPattern = $@"\.(?:AddScoped|AddTransient|AddSingleton)\s*<\s*{Regex.Escape(ctx.UseCaseName)}Handler\s*>\s*\(";
        if (!Regex.IsMatch(content, handlerPattern))
            registrations.Add($"{serviceParam}.AddScoped<{ctx.UseCaseName}Handler>();");

        // --- validator registration (only when the DI file has no assembly scan) ---
        if (validatorPath != null)
        {
            string validatorName = Path.GetFileNameWithoutExtension(validatorPath);
            bool hasAssemblyScan = Regex.IsMatch(content, @"\bAddValidatorsFromAssemblyContaining\s*<");
            string validatorPattern = $@"\.(?:AddScoped|AddTransient|AddSingleton)\s*<\s*IValidator\s*<\s*{Regex.Escape(ctx.UseCaseName)}Command\s*>\s*,\s*{Regex.Escape(validatorName)}\s*>\s*\(";

            if (!hasAssemblyScan && !Regex.IsMatch(content, validatorPattern))
            {
                content = EnsureUsing(content, "using FluentValidation;", newline);
                content = EnsureUsing(content, $"using {ctx.Prefix}.Application.Validation;", newline);
                registrations.Add($"{serviceParam}.AddScoped<IValidator<{ctx.UseCaseName}Command>, {validatorName}>();");
            }
        }

        // --- insert registrations before "return <services>;" ---
        if (registrations.Count > 0)
        {
            var returnMatch = Regex.Match(content, @"(?m)^([ \t]*)return\s+" + Regex.Escape(serviceParam) + @"\s*;");
            if (!returnMatch.Success)
            {
                Console.WriteLine($"  Warning: could not locate 'return {serviceParam};' in {diFile}. Add manually:");
                foreach (var registration in registrations)
                    Console.WriteLine($"    {registration}");
            }
            else
            {
                string indent = returnMatch.Groups[1].Value;
                string block = string.Join(newline, registrations.Select(r => indent + r)) + newline + newline;
                content = content.Insert(returnMatch.Index, block);
            }
        }

        if (content != original)
        {
            File.WriteAllText(diFile, content);
            Console.WriteLine($"  [REGISTERED] {ctx.UseCaseName}Handler in {diFile}");
            if (validatorPath != null && registrations.Any(r => r.Contains("IValidator<")))
                Console.WriteLine($"  [REGISTERED] {Path.GetFileNameWithoutExtension(validatorPath)} in {diFile}");
        }
        else
        {
            Console.WriteLine($"  [OK] {ctx.UseCaseName}Handler already registered in {diFile}");
        }
    }

    private static string? FindApplicationDiFile(string appDir)
    {
        string? diDir = FindChildDirectory(appDir, "DependencyInjection");
        if (diDir == null) return null;

        return Directory.GetFiles(diDir, "*ApplicationServiceCollectionExtensions.cs").FirstOrDefault()
            ?? Directory.GetFiles(diDir, "*ServiceCollectionExtensions.cs").FirstOrDefault();
    }

    private static string FindServiceCollectionParamName(string content)
    {
        // Prefer the "return <param>;" statement.
        var returnMatch = Regex.Match(content, @"(?m)^[ \t]*return\s+([A-Za-z_]\w*)\s*;");
        if (returnMatch.Success)
            return returnMatch.Groups[1].Value;

        // Fall back to the extension method signature.
        var signatureMatch = Regex.Match(content, @"this\s+IServiceCollection\s+([A-Za-z_]\w*)");
        return signatureMatch.Success ? signatureMatch.Groups[1].Value : "services";
    }

    private static string EnsureUsing(string src, string usingLine, string newline)
    {
        string ns = usingLine.Replace("using ", "").TrimEnd(';').Trim();

        // Match any existing using for the same namespace (global or not).
        if (Regex.IsMatch(src, $@"(?m)^\s*using\s+(?:global\s+)?{Regex.Escape(ns)}\s*;"))
            return src;

        // Insert after the last existing `using` line.
        var usingMatches = Regex.Matches(src, @"(?m)^[ \t]*using\s+(?:global\s+)?[^;]+;[ \t]*$");
        if (usingMatches.Count == 0)
            return usingLine + newline + newline + src;

        var lastUsing = usingMatches[usingMatches.Count - 1];
        int insertPos = lastUsing.Index + lastUsing.Length;
        return src.Insert(insertPos, newline + usingLine);
    }

    // ===== target resolution =====

    /// <summary>
    /// Resolves the Application project and module root for a location such as
    /// "modules/item/application/usecases/GetItemDetailByStore", even when the
    /// on-disk folders use fully-qualified names (MBPOS.Modules.Item.Application).
    /// </summary>
    private static TargetResolution? ResolveTarget(string typedFull, string currentDir)
    {
        // 1. Walk up from the deepest existing part of the typed path.
        string? existing = typedFull;
        while (existing != null && !Directory.Exists(existing))
            existing = Path.GetDirectoryName(existing);

        if (existing != null)
        {
            string? appDir = FindApplicationAncestor(existing);
            if (appDir != null)
                return new TargetResolution(appDir, Path.GetDirectoryName(appDir)!);

            string? appChild = FindApplicationChild(existing);
            if (appChild != null)
                return new TargetResolution(appChild, existing);
        }

        // 2. Match the typed segments against an existing module folder.
        List<string> typedParts = GetPathParts(typedFull, currentDir);
        int structuralIdx = typedParts.FindIndex(IsStructuralSegment);
        if (structuralIdx < 0)
            structuralIdx = typedParts.Count; // assume the last segment is the module name

        if (typedParts.Count > 0)
        {
            string? moduleRoot = FindModuleDirectory(currentDir, typedParts, structuralIdx);
            if (moduleRoot != null)
            {
                string? appDir = FindApplicationChild(moduleRoot);
                if (appDir != null)
                    return new TargetResolution(appDir, moduleRoot);
            }
        }

        return null;
    }

    private static string? FindApplicationAncestor(string startDir)
    {
        string? dir = startDir;
        while (dir != null && dir.Length > 3) // stop at drive root
        {
            if (IsApplicationDirectory(dir))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }

    private static string? FindApplicationChild(string moduleRoot)
    {
        if (IsApplicationDirectory(moduleRoot))
            return moduleRoot;

        List<string> children;
        try
        {
            children = Directory.GetDirectories(moduleRoot)
                .Where(IsApplicationDirectory)
                .ToList();
        }
        catch
        {
            return null;
        }

        if (children.Count == 1)
            return children[0];

        if (children.Count > 1)
        {
            // Prefer the project whose name contains the module folder name.
            string moduleName = Path.GetFileName(moduleRoot);
            var preferred = children
                .Where(d => Path.GetFileName(d).Contains(moduleName, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (preferred.Count == 1)
                return preferred[0];

            // Prefer *.Application over a bare "Application" folder.
            var dotted = children
                .Where(d => Path.GetFileName(d).EndsWith(".Application", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (dotted.Count == 1)
                return dotted[0];
        }

        return null;
    }

    private static bool IsApplicationDirectory(string dir)
    {
        string name = Path.GetFileName(dir);
        if (name.Equals("Application", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".Application", StringComparison.OrdinalIgnoreCase))
            return true;

        try
        {
            foreach (string csproj in Directory.GetFiles(dir, "*.csproj"))
            {
                string csprojName = Path.GetFileNameWithoutExtension(csproj);
                if (csprojName.Equals("Application", StringComparison.OrdinalIgnoreCase)
                    || csprojName.EndsWith(".Application", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        catch
        {
            // Ignore inaccessible folders.
        }

        return false;
    }

    private static string? FindModuleDirectory(string currentDir, List<string> typedParts, int structuralIdx)
    {
        int moduleIdx = structuralIdx > 0 ? structuralIdx - 1 : typedParts.Count - 1;
        if (moduleIdx < 0 || moduleIdx >= typedParts.Count)
            return null;

        string moduleSegment = typedParts[moduleIdx];
        string? parentSegment = moduleIdx > 0 ? typedParts[moduleIdx - 1] : null;
        string normalizedModule = NormalizeSegment(moduleSegment);

        // Targeted search: parent segment (e.g. "modules") -> module segment (e.g. "item").
        if (parentSegment != null)
        {
            string normalizedParent = NormalizeSegment(parentSegment);
            var matches = new List<string>();

            foreach (string dir in EnumerateDirectories(currentDir))
            {
                if (NormalizeSegment(Path.GetFileName(dir)) != normalizedParent)
                    continue;

                try
                {
                    foreach (string child in Directory.GetDirectories(dir))
                    {
                        if (NormalizeSegment(Path.GetFileName(child)) == normalizedModule)
                            matches.Add(child);
                    }
                }
                catch
                {
                    // Ignore inaccessible folders.
                }
            }

            var withApplication = matches.Where(d => FindApplicationChild(d) != null).ToList();
            if (withApplication.Count == 1)
                return withApplication[0];

            if (withApplication.Count > 1)
            {
                PrintAmbiguousModules(withApplication);
                return null;
            }

            if (matches.Count == 1)
                return matches[0];

            if (matches.Count > 1)
            {
                PrintAmbiguousModules(matches);
                return null;
            }
        }

        // Fallback: any folder matching the module name that contains an Application project.
        var candidates = EnumerateDirectories(currentDir)
            .Where(d => NormalizeSegment(Path.GetFileName(d)) == normalizedModule)
            .Where(d => FindApplicationChild(d) != null)
            .ToList();

        if (candidates.Count == 1)
            return candidates[0];

        if (candidates.Count > 1)
            PrintAmbiguousModules(candidates);

        return null;
    }

    private static void PrintAmbiguousModules(IEnumerable<string> matches)
    {
        Console.WriteLine("Warning: the module name matched multiple folders:");
        foreach (string match in matches)
            Console.WriteLine($"  - {match}");
        Console.WriteLine("Be more specific (include a parent segment) or use the full path.");
    }

    private static List<string> GetPathParts(string fullPath, string currentDir)
    {
        string relative = Path.GetRelativePath(currentDir, fullPath);
        if (!relative.StartsWith("..", StringComparison.Ordinal)
            && !Path.IsPathRooted(relative))
        {
            return relative
                .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries)
                .ToList();
        }

        return fullPath
            .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries)
            .ToList();
    }

    private static IEnumerable<string> EnumerateDirectories(string root)
    {
        var stack = new Stack<string>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            string dir = stack.Pop();
            string[] children;
            try
            {
                children = Directory.GetDirectories(dir);
            }
            catch
            {
                continue;
            }

            foreach (string child in children)
            {
                string name = Path.GetFileName(child);
                if (ExcludedDirectoryNames.Contains(name, StringComparer.OrdinalIgnoreCase))
                    continue;

                yield return child;
                stack.Push(child);
            }
        }
    }

    private static string? FindChildDirectory(string parent, string name)
    {
        try
        {
            return Directory.GetDirectories(parent)
                .FirstOrDefault(d => string.Equals(Path.GetFileName(d), name, StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return null;
        }
    }

    private static bool IsStructuralSegment(string segment)
    {
        return StructuralSegments.Contains(NormalizeSegment(segment), StringComparer.OrdinalIgnoreCase);
    }

    private static string NormalizeSegment(string segment)
    {
        // "MBPOS.Modules.Item.Application" -> "application"
        string trimmed = segment.Trim();
        int dot = trimmed.LastIndexOf('.');
        return (dot >= 0 ? trimmed[(dot + 1)..] : trimmed).ToLowerInvariant();
    }

    private static string DerivePrefix(string appDir, string moduleRoot)
    {
        string appName = Path.GetFileName(appDir);
        if (appName.EndsWith(".Application", StringComparison.OrdinalIgnoreCase))
            return appName[..^".Application".Length];

        string[] csprojFiles = Directory.GetFiles(appDir, "*.csproj");

        // Prefer an explicit <RootNamespace> when the project declares one.
        if (csprojFiles.Length == 1)
        {
            string csprojText = File.ReadAllText(csprojFiles[0]);
            var rootNsMatch = Regex.Match(csprojText, @"<RootNamespace>\s*([^<]+?)\s*</RootNamespace>");
            if (rootNsMatch.Success)
            {
                string rootNs = rootNsMatch.Groups[1].Value.Trim();
                if (!string.IsNullOrWhiteSpace(rootNs) && !rootNs.Contains("$("))
                {
                    return rootNs.EndsWith(".Application", StringComparison.OrdinalIgnoreCase)
                        ? rootNs[..^".Application".Length]
                        : rootNs;
                }
            }
        }

        if (csprojFiles.Length == 1)
        {
            string csprojName = Path.GetFileNameWithoutExtension(csprojFiles[0]);
            if (csprojName.EndsWith(".Application", StringComparison.OrdinalIgnoreCase))
                return csprojName[..^".Application".Length];
            if (!csprojName.Equals("Application", StringComparison.OrdinalIgnoreCase))
                return csprojName;
        }

        foreach (string layer in new[] { ".Domain", ".Contracts", ".Infrastructure", ".Presentation" })
        {
            string? sibling = Directory.GetDirectories(moduleRoot)
                .FirstOrDefault(d => Path.GetFileName(d).EndsWith(layer, StringComparison.OrdinalIgnoreCase));
            if (sibling != null)
                return Path.GetFileName(sibling)[..^layer.Length];
        }

        return Path.GetFileName(moduleRoot);
    }

    // ===== result model helpers =====

    private static string PickDefaultResultModel(string moduleRoot, string entity)
    {
        string detailType = $"{entity}DetailModel";
        if (FindTypeNamespace(moduleRoot, detailType) != null)
            return $"IReadOnlyList<{detailType}>";

        return $"IReadOnlyList<{entity}Model>";
    }

    private static string? ResolveUsingForType(string moduleRoot, string resultModelType, string prefix)
    {
        foreach (Match match in Regex.Matches(resultModelType, @"\b[A-Z][A-Za-z0-9_]*\b"))
        {
            string typeName = match.Value;
            string? ns = FindTypeNamespace(moduleRoot, typeName);
            if (ns != null)
                return ns;
        }

        // The model may not exist yet; point at the conventional Domain.Entities namespace.
        if (Regex.IsMatch(resultModelType, @"\b[A-Z][A-Za-z0-9_]*Model\b"))
            return $"{prefix}.Domain.Entities";

        return null;
    }

    private static string? FindTypeNamespace(string moduleRoot, string typeName)
    {
        string pattern = $@"\b(?:record(?:\s+class|\s+struct)?|class|struct|interface)\s+{Regex.Escape(typeName)}\b";

        foreach (string file in EnumerateSourceFiles(moduleRoot))
        {
            string text;
            try
            {
                text = File.ReadAllText(file);
            }
            catch
            {
                continue;
            }

            if (!Regex.IsMatch(text, pattern))
                continue;

            var nsMatch = Regex.Match(text, @"\bnamespace\s+([A-Za-z_][\w.]*)");
            return nsMatch.Success ? nsMatch.Groups[1].Value : null;
        }

        return null;
    }

    private static IEnumerable<string> EnumerateSourceFiles(string root)
    {
        var stack = new Stack<string>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            string dir = stack.Pop();
            string[] files;
            string[] children;

            try
            {
                files = Directory.GetFiles(dir, "*.cs");
                children = Directory.GetDirectories(dir);
            }
            catch
            {
                continue;
            }

            foreach (string file in files)
                yield return file;

            foreach (string child in children)
            {
                string name = Path.GetFileName(child);
                if (ExcludedDirectoryNames.Contains(name, StringComparer.OrdinalIgnoreCase))
                    continue;
                stack.Push(child);
            }
        }
    }

    private static bool HasFluentValidation(UseCaseContext ctx)
    {
        string? csproj = Directory.GetFiles(ctx.AppDir, "*.csproj").FirstOrDefault();
        if (csproj == null)
            return false;

        return File.ReadAllText(csproj)
            .Contains("FluentValidation", StringComparison.OrdinalIgnoreCase);
    }

    // ===== helpers =====

    private static void WriteStub(UseCaseContext ctx, string fileName, string content)
    {
        string path = Path.Combine(ctx.UseCaseDir, fileName);
        File.WriteAllText(path, content + Environment.NewLine);
        Console.WriteLine($"  created {path}");
    }

    // ===== dry-run =====

    private static void PrintDryRun(UseCaseContext ctx)
    {
        Console.WriteLine("=== DRY RUN (use-case) — no changes will be made ===");
        Console.WriteLine();
        Console.WriteLine($"Module:       {ctx.Prefix}");
        Console.WriteLine($"Entity:       {ctx.Entity}");
        Console.WriteLine($"Use-case:     {ctx.UseCaseName}");
        Console.WriteLine($"App project:  {ctx.AppDir}");
        Console.WriteLine($"Use-case dir: {ctx.UseCaseDir}");
        Console.WriteLine($"Result model: {ctx.ResultModelType}");
        Console.WriteLine();

        Console.WriteLine("[WILL CREATE]");
        Console.WriteLine($"  {Path.Combine(ctx.UseCaseDir, $"{ctx.UseCaseName}Command.cs")}");
        Console.WriteLine($"  {Path.Combine(ctx.UseCaseDir, $"{ctx.UseCaseName}Handler.cs")}");
        Console.WriteLine($"  {Path.Combine(ctx.UseCaseDir, $"{ctx.UseCaseName}Result.cs")}");
        Console.WriteLine($"  {Path.Combine(ctx.UseCaseDir, $"{ctx.UseCaseName}ReadModel.cs")}");
        if (!ctx.SkipValidator)
        {
            Console.WriteLine($"  {Path.Combine(ctx.AppDir, "Validation", $"{ctx.UseCaseName}CommandValidator.cs")}");
        }
        Console.WriteLine();

        string? diFile = FindApplicationDiFile(ctx.AppDir);
        Console.WriteLine("[WILL REGISTER]");
        if (diFile == null)
        {
            Console.WriteLine("  (no Application DI extension file found — registration will be skipped)");
        }
        else
        {
            string serviceParam = FindServiceCollectionParamName(File.ReadAllText(diFile));
            Console.WriteLine($"  {diFile}");
            Console.WriteLine($"  using {ctx.Prefix}.Application.UseCases.{ctx.FolderName};");
            Console.WriteLine($"  {serviceParam}.AddScoped<{ctx.UseCaseName}Handler>();");
        }
        Console.WriteLine();

        Console.WriteLine("Run the same command without --dry-run to apply these changes.");
    }
}
