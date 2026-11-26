namespace ShortHandCreater;

internal static class LocationResolver
{
    // Resolves a typed --location against the actual folder structure on disk.
    // - If the input is already a real, existing path -> use as-is.
    // - Otherwise, scan existing folders and match the trailing path segments (case-insensitive).
    //   - A typed segment may be a prefix of the real folder name (e.g. "modules/i" -> "modules/item").
    //   - Exact match on all typed segments -> use the matched existing folder.
    //   - Extra trailing segment(s) beyond what matched -> resolve the known part,
    //     then append the new segment(s) as a folder to be created (e.g. "modules/newmodule").
    //   - No match at all -> treat input as a brand-new path to create, unchanged.
    //   - Multiple matches -> list them and ask the user to be more specific.
    internal static string? ResolveLocation(string input, string searchRoot)
    {
        string directCheck = Path.GetFullPath(input);
        if (Directory.Exists(directCheck)) return input;

        var allDirs = EnumerateDirectories(searchRoot);

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

                var tail = relativeParts.Skip(relativeParts.Length - knownParts.Length).ToArray();
                return Matches(tail, knownParts, allowPrefixOnLast: true);
            }).ToList();

            if (matches.Count == 1)
            {
                string resolvedBase = Path.GetRelativePath(searchRoot, matches[0]);
                string result = remainderParts.Length > 0
                    ? Path.Combine(resolvedBase, Path.Combine(remainderParts))
                    : resolvedBase;

                Console.WriteLine(remainderParts.Length > 0
                    ? $"Resolved location \"{input}\" -> \"{result}\" (new subfolder under existing match)"
                    : $"Resolved location \"{input}\" -> \"{result}\"");
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

    // Returns logical (relative) folder paths that complete the typed location.
    // The input is matched against the tail of every folder below searchRoot,
    // allowing the last typed segment to be a prefix of the real folder name.
    internal static IReadOnlyList<string> GetLocationCompletions(string input, string searchRoot)
    {
        var allDirs = EnumerateDirectories(searchRoot);

        string normalizedInput = input.Replace('\\', '/');
        bool trailingSlash = normalizedInput.EndsWith("/", StringComparison.Ordinal);
        var inputParts = normalizedInput.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (inputParts.Length == 0)
        {
            return allDirs
                .Select(d => Path.GetRelativePath(searchRoot, d).Replace('\\', '/'))
                .Where(rel => !rel.Contains('/'))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(rel => rel, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var dir in allDirs)
        {
            var relative = Path.GetRelativePath(searchRoot, dir).Replace('\\', '/');
            var relativeParts = relative.Split('/', StringSplitOptions.RemoveEmptyEntries);

            if (trailingSlash)
            {
                // Input is a folder prefix: the candidate is a child of the matched folder.
                if (relativeParts.Length <= inputParts.Length) continue;

                var parentParts = relativeParts.Take(relativeParts.Length - 1).ToArray();
                if (parentParts.Length < inputParts.Length) continue;

                var parentTail = parentParts.Skip(parentParts.Length - inputParts.Length).ToArray();
                if (!Matches(parentTail, inputParts, allowPrefixOnLast: false)) continue;

                var logicalParts = relativeParts.Skip(relativeParts.Length - inputParts.Length - 1);
                results.Add(string.Join("/", logicalParts));
            }
            else
            {
                // Input is a partial path: the candidate's tail must match it.
                if (relativeParts.Length < inputParts.Length) continue;

                var tail = relativeParts.Skip(relativeParts.Length - inputParts.Length).ToArray();
                if (!Matches(tail, inputParts, allowPrefixOnLast: true)) continue;

                results.Add(string.Join("/", tail));
            }
        }

        return results
            .OrderBy(rel => rel, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool Matches(string[] candidateParts, string[] inputParts, bool allowPrefixOnLast)
    {
        for (int i = 0; i < inputParts.Length; i++)
        {
            bool isLast = i == inputParts.Length - 1;
            if (isLast && allowPrefixOnLast)
            {
                if (!candidateParts[i].StartsWith(inputParts[i], StringComparison.OrdinalIgnoreCase))
                    return false;
            }
            else
            {
                if (!string.Equals(candidateParts[i], inputParts[i], StringComparison.OrdinalIgnoreCase))
                    return false;
            }
        }

        return true;
    }

    private static List<string> EnumerateDirectories(string searchRoot)
    {
        return Directory.GetDirectories(searchRoot, "*", SearchOption.AllDirectories)
            .Where(d => !d.Contains(Path.DirectorySeparatorChar + ".git")
                     && !d.Contains(Path.DirectorySeparatorChar + "bin")
                     && !d.Contains(Path.DirectorySeparatorChar + "obj")
                     && !d.Contains(Path.DirectorySeparatorChar + "node_modules"))
            .ToList();
    }
}
