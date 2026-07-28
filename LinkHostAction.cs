namespace ShortHandCreater;

// ============================================================
// LINK action — "dn link --name MBPOS.Modules.Products --location src/Modules --host src/Host/MBPOS.Api"
//
// For a module that's already been scaffolded (via `dn g m`), wires the
// entry-point/host project's references to that module's Presentation,
// Application, and Infrastructure projects — the same three references
// shown in your example:
//
//   <ProjectReference Include="...MBPOS.Modules.Products.Presentation.csproj" />
//   <ProjectReference Include="...MBPOS.Modules.Products.Application.csproj" />
//   <ProjectReference Include="...MBPOS.Modules.Products.Infrastructure.csproj" />
//
// Does not touch Domain/Contracts/UnitTests, does not modify the module
// itself, and does not require a .sln (it only runs `dotnet add reference`).
// ============================================================
internal static class LinkHostAction
{
    internal static void Run(string prefix, string parentLocation, string hostArg, bool isDryRun)
    {
        var segments = prefix.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 1)
        {
            Console.WriteLine($"Error: \"{prefix}\" is not a valid module name (expected e.g. \"MBPOS.Modules.Products\").");
            return;
        }

        string moduleFolderName = segments[^1];
        string moduleRoot = Path.Combine(parentLocation, moduleFolderName);

        string LayerCsProj(string layer) =>
            Path.Combine(moduleRoot, $"{prefix}.{layer}", $"{prefix}.{layer}.csproj");

        string[] layersToLink = { "Presentation", "Application", "Infrastructure" };

        var missing = layersToLink.Where(l => !File.Exists(LayerCsProj(l))).ToList();
        if (missing.Count > 0)
        {
            Console.WriteLine($"Error: couldn't find the following project(s) under \"{moduleRoot}\":");
            foreach (var l in missing) Console.WriteLine($"  - {LayerCsProj(l)}");
            Console.WriteLine("Check --name/--location match the module you already scaffolded with 'dn g m'.");
            return;
        }

        string? hostCsproj = HostResolver.ResolveHostCsproj(hostArg, Directory.GetCurrentDirectory());
        if (hostCsproj == null)
        {
            Console.WriteLine("Aborting: could not resolve --host to a single .csproj.");
            return;
        }

        if (isDryRun)
        {
            Console.WriteLine("=== DRY RUN (link) — no changes will be made ===");
            Console.WriteLine();
            Console.WriteLine($"Host project: {hostCsproj}");
            Console.WriteLine();
            Console.WriteLine("[WILL WIRE HOST REFERENCES]");
            foreach (var l in layersToLink)
                Console.WriteLine($"  {hostCsproj}\n    <- {LayerCsProj(l)}");
            Console.WriteLine();
            Console.WriteLine("Run the same command without --dry-run to apply this change.");
            return;
        }

        var refs = layersToLink.Select(LayerCsProj).ToArray();
        var refArgs = string.Join(" ", refs.Select(r => $"\"{r}\""));

        if (DotnetCli.RunDotnet($"add \"{hostCsproj}\" reference {refArgs}"))
        {
            Console.WriteLine($"Wired host references: {hostCsproj} -> Presentation, Application, Infrastructure");
        }
        else
        {
            Console.WriteLine($"Error: failed to add references to {hostCsproj}.");
        }
    }
}
