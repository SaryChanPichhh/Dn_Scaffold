namespace ShortHandCreater;

internal static class ArgParser
{
    internal static string? GetArgValue(string[] args, string key)
    {
        for (int i = 0; i < args.Length - 1; i++)
            if (string.Equals(args[i], key, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        return null;
    }

    internal static bool HasFlag(string[] args, string key) =>
        args.Any(a => string.Equals(a, key, StringComparison.OrdinalIgnoreCase));
}
