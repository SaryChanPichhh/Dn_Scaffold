namespace ShortHandCreater;

internal static class LocationResolver
{
    // Resolves a typed --location against the actual folder structure on disk.
    // - If the input is already a real, existing path -> use as-is.
    // - Otherwise, scan existing folders and match the trailing path segments (case-insensitive).
    //   - Exact match on all typed segments -> use the matched existing folder.
    //   - Extra trailing segment(s) beyond what matched -> resolve the known part,
    //     then append the new segment(s) as a folder to be created (e.g. "modules/newmodule").
    //   - No match at all -> treat input as a brand-new path to create, unchanged.
    //   - Multiple matches -> list them and ask the user to be more specific.
    internal static string? ResolveLocation(string input, string searchRoot)
    {
        string directCheck = Path.GetFullPath(input);
        if (Directory.Exists(directCheck)) return input;

        var allDirs = Directory.GetDirectories(searchRoot, "*", SearchOption.AllDirectories)
            .Where(d => !d.Contains(Path.DirectorySeparatorChar + ".git")
                     && !d.Contains(Path.DirectorySeparatorChar + "bin")
                     && !d.Contains(Path.DirectorySeparatorChar + "obj")
                     && !d.Contains(Path.DirectorySeparatorChar + "node_modules"))
            .ToList();

        string normalizedInput = input.Replace('\\', '/').Trim('/');
        var inputParts = normalizedInput.Split('/', StringSplitOptions.RemoveEmptyEntries);

        for (int take = inputParts.Length; take >= 1; take--)
        {
            var knownParts = inputParts.Take(take).ToArray();
            var remainderParts = inputParts.Skip(take).ToArray();

            var matches = allDirs.Where(d =>
            {
                var relative = Path.GetRelativePath(searchRoot, d).Replace('\\', '/');
                var relativeParts = relative.Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (knownParts.Length > relativeParts.Length) return false;

                var tail = relativeParts.Skip(relativeParts.Length - knownParts.Length);
                return tail.Select(p => p.ToLowerInvariant())
                            .SequenceEqual(knownParts.Select(p => p.ToLowerInvariant()));
            }).ToList();

            if (matches.Count == 1)
            {
                string resolvedBase = Path.GetRelativePath(searchRoot, matches[0]);
                string result = remainderParts.Length > 0
                    ? Path.Combine(resolvedBase, Path.Combine(remainderParts))
                    : resolvedBase;

                Console.WriteLine(remainderParts.Length > 0
                    ? $"Resolved location \"{input}\" → \"{result}\" (new subfolder under existing match)"
                    : $"Resolved location \"{input}\" → \"{result}\"");
                return result;
            }

            if (matches.Count > 1)
            {
                Console.WriteLine($"Warning: \"{string.Join("/", knownParts)}\" matched multiple folders:");
                foreach (var m in matches)
                    Console.WriteLine($"  - {Path.GetRelativePath(searchRoot, m)}");
                Console.WriteLine("Be more specific (include a parent segment) or use the full path.");
                return null;
            }
            // 0 matches at this prefix length -> shrink the known-part window and try again
        }

        // No match at all - treat as a brand-new path, created as typed
        return input;
    }
}
