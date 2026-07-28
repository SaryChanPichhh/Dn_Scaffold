namespace ShortHandCreater;

internal static class TemplateValidator
{
    private static readonly string[] KnownTemplates =
    {
        "classlib", "console", "webapi", "web", "mvc", "razor", "blazorwasm", "blazorserver",
        "worker", "xunit", "nunit", "mstest", "wpf", "winforms", "grpc"
    };

    internal static bool IsValidTemplate(string type) =>
        KnownTemplates.Contains(type.ToLowerInvariant());
}
