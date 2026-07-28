namespace ShortHandCreater;

internal static class StringUtils
{
    // Very simple English singularizer, good enough for typical entity names
    // (Products -> Product, Categories -> Category, Addresses -> Address).
    // Falls back to returning the word unchanged if no rule applies.
    internal static string Singularize(string word)
    {
        if (word.EndsWith("ies", StringComparison.OrdinalIgnoreCase) && word.Length > 3)
            return word[..^3] + "y";
        if (word.EndsWith("ses", StringComparison.OrdinalIgnoreCase) || word.EndsWith("xes", StringComparison.OrdinalIgnoreCase))
            return word[..^2];
        if (word.EndsWith("s", StringComparison.OrdinalIgnoreCase) && !word.EndsWith("ss", StringComparison.OrdinalIgnoreCase))
            return word[..^1];
        return word;
    }
}
