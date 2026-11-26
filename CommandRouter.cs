namespace ShortHandCreater;

internal static class CommandRouter
{
    internal static void Route(string[] args)
    {
        if (args.Length == 0 || ArgParser.HasFlag(args, "--help") || ArgParser.HasFlag(args, "-h"))
        {
            HelpPrinter.PrintHelp();
            return;
        }

        // Expect: dn <action> [type] --name <name> --location <location>
        // type is optional and defaults to "classlib"
        // e.g.:   dn g --name MBPOS.Modules.Sales.Domain --location modules/sales
        // e.g.:   dn g webapi --name MBPOS.Api --location host
        // e.g.:   dn g m --name MBPOS.Modules.Products --location src/Modules   (full module scaffold)
        if (args.Length < 1)
        {
            Console.WriteLine("Error: expected format: dn <action> [type] --name <name> --location <location>");
            Console.WriteLine("Example: dn g --name MBPOS.Modules.Sales.Domain --location modules/sales");
            Console.WriteLine("Run 'dn --help' for full usage.");
            return;
        }

        string action = args[0].ToLowerInvariant();

        if (action == "link" || action == "l")
        {
            HandleLink(args);
            return;
        }

        if (action != "g" && action != "generate")
        {
            Console.WriteLine($"Error: unknown action \"{action}\". Currently supported: g (generate), link (wire host references into an existing module)");
            return;
        }

        HandleGenerate(args);
    }

    private static void HandleLink(string[] args)
    {
        string? linkName = ArgParser.GetArgValue(args, "--name") ?? ArgParser.GetArgValue(args, "-n");
        string? linkLocation = ArgParser.GetArgValue(args, "--location") ?? ArgParser.GetArgValue(args, "-l");
        string? linkHost = ArgParser.GetArgValue(args, "--host");
        bool linkDryRun = ArgParser.HasFlag(args, "--dry-run");

        if (string.IsNullOrWhiteSpace(linkName) || string.IsNullOrWhiteSpace(linkLocation) || string.IsNullOrWhiteSpace(linkHost))
        {
            Console.WriteLine("Error: --name, --location, and --host are all required for the link action.");
            Console.WriteLine("Example: dn link --name MBPOS.Modules.Products --location src/Modules --host src/Host/MBPOS.Api");
            return;
        }

        string? resolvedLinkLocation = LocationResolver.ResolveLocation(linkLocation, Directory.GetCurrentDirectory());
        if (resolvedLinkLocation == null) return; // ambiguous match - options already printed

        LinkHostAction.Run(linkName, Path.GetFullPath(resolvedLinkLocation), linkHost, linkDryRun);
    }

    private static void HandleGenerate(string[] args)
    {
        // type is positional at args[1] IF it exists and isn't a flag; otherwise default to classlib
        string projectType = "classlib";
        if (args.Length > 1 && !args[1].StartsWith("-"))
        {
            projectType = args[1];
        }

        bool isModule = string.Equals(projectType, "m", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(projectType, "module", StringComparison.OrdinalIgnoreCase);

        bool isUseCase = string.Equals(projectType, "usecase", StringComparison.OrdinalIgnoreCase)
                      || string.Equals(projectType, "f", StringComparison.OrdinalIgnoreCase)
                      || string.Equals(projectType, "feature", StringComparison.OrdinalIgnoreCase);

        bool isDryRun = ArgParser.HasFlag(args, "--dry-run");
        string? nameArg = ArgParser.GetArgValue(args, "--name") ?? ArgParser.GetArgValue(args, "-n");
        string? location = ArgParser.GetArgValue(args, "--location") ?? ArgParser.GetArgValue(args, "-l");
        string? slnFlag = ArgParser.GetArgValue(args, "--sln") ?? ArgParser.GetArgValue(args, "-s");
        string? entityFlag = ArgParser.GetArgValue(args, "--entity") ?? ArgParser.GetArgValue(args, "-e");
        string sharedRoot = ArgParser.GetArgValue(args, "--shared-root") ?? ArgParser.GetArgValue(args, "-r") ?? "MBPOS";
        bool skipShared = ArgParser.HasFlag(args, "--no-shared");
        string? hostFlag = ArgParser.GetArgValue(args, "--host");

        if (string.IsNullOrWhiteSpace(nameArg) || string.IsNullOrWhiteSpace(location))
        {
            Console.WriteLine("Error: --name and --location are required.");
            Console.WriteLine("Run 'dn --help' to see full usage and examples.");
            return;
        }

        if (!isModule && !isUseCase && !TemplateValidator.IsValidTemplate(projectType))
        {
            Console.WriteLine($"Error: \"{projectType}\" is not a recognized dotnet template.");
            Console.WriteLine("Run 'dotnet new list' to see all available templates on this machine.");
            return;
        }

        string currentDir = Directory.GetCurrentDirectory();

        string? resolvedLocation = LocationResolver.ResolveLocation(location, currentDir);
        if (resolvedLocation == null) return; // ambiguous match - options already printed
        location = resolvedLocation;

        string fullLocation = Path.GetFullPath(location);

        string? slnPath = slnFlag != null
            ? Path.GetFullPath(slnFlag)
            : Directory.GetFiles(currentDir, "*.sln")
                .Concat(Directory.GetFiles(currentDir, "*.slnx"))
                .FirstOrDefault();

        // ============================================================
        // MODULE MODE — "dn g m --name MBPOS.Modules.Products --location src/Modules"
        // ============================================================
        if (isModule)
        {
            ModuleGenerator.Run(nameArg, fullLocation, slnPath, sharedRoot, entityFlag, skipShared, hostFlag, isDryRun);
            return;
        }

        // ============================================================
        // USE‑CASE MODE — "dn g usecase --name <Prefix>UseCase --location modules/item/application/usecases/<Prefix>"
        // ============================================================
        if (isUseCase)
        {
            string? resultModelFlag = ArgParser.GetArgValue(args, "--result-model");
            bool skipValidator = ArgParser.HasFlag(args, "--no-validator");
            UseCaseGenerator.Run(nameArg, fullLocation, entityFlag, resultModelFlag, skipValidator, isDryRun);
            return;
        }

        // ============================================================
        // SINGLE PROJECT MODE — original behavior
        // ============================================================
        SingleProjectGenerator.Run(projectType, nameArg, fullLocation, slnPath, isDryRun);
    }
}
