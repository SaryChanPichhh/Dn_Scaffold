namespace ShortHandCreater;

internal static class StringUtils
{
    /// <summary>
    /// Simple English singularizer for typical module/entity names
    /// (Products -> Product, Categories -> Category, Addresses -> Address).
    /// </summary>
    internal static string Singularize(string word)
    {
        if (string.IsNullOrWhiteSpace(word))
            return word;

        if (word.EndsWith("ies", StringComparison.OrdinalIgnoreCase) && word.Length > 3)
            return word[..^3] + "y";

        if (word.EndsWith("sses", StringComparison.OrdinalIgnoreCase)
            || word.EndsWith("xes", StringComparison.OrdinalIgnoreCase)
            || word.EndsWith("zes", StringComparison.OrdinalIgnoreCase)
            || word.EndsWith("ches", StringComparison.OrdinalIgnoreCase)
            || word.EndsWith("shes", StringComparison.OrdinalIgnoreCase))
            return word[..^2];

        if (word.EndsWith("s", StringComparison.OrdinalIgnoreCase)
            && !word.EndsWith("ss", StringComparison.OrdinalIgnoreCase)
            && !word.EndsWith("us", StringComparison.OrdinalIgnoreCase)
            && !word.EndsWith("is", StringComparison.OrdinalIgnoreCase))
            return word[..^1];

        return word;
    }

    /// <summary>
    /// Simple English pluralizer for entity names
    /// (Product -> Products, Item -> Items, Category -> Categories, Address -> Addresses).
    /// </summary>
    internal static string Pluralize(string word)
    {
        if (string.IsNullOrWhiteSpace(word))
            return word;

        if (word.EndsWith("y", StringComparison.OrdinalIgnoreCase)
            && word.Length > 1
            && !IsVowel(word[^2]))
            return word[..^1] + "ies";

        if (word.EndsWith("s", StringComparison.OrdinalIgnoreCase)
            || word.EndsWith("x", StringComparison.OrdinalIgnoreCase)
            || word.EndsWith("z", StringComparison.OrdinalIgnoreCase)
            || word.EndsWith("ch", StringComparison.OrdinalIgnoreCase)
            || word.EndsWith("sh", StringComparison.OrdinalIgnoreCase))
            return word + "es";

        return word + "s";
    }

    internal static string ToCamelCase(string value)
    {
        if (string.IsNullOrEmpty(value) || char.IsLower(value[0]))
            return value;

        return char.ToLowerInvariant(value[0]) + value[1..];
    }

    private static bool IsVowel(char ch) =>
        ch is 'a' or 'e' or 'i' or 'o' or 'u' or 'A' or 'E' or 'I' or 'O' or 'U';
}
