using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace ShortHandCreater
{
    internal static class AddServiceInjectionInEntryPoints
    {
        // ============================================================
        // Best-effort Program.cs patcher — appends this module's
        // .Add{Module}Infrastructure() / .Add{Module}Presentation() calls
        // into the existing builder.Services chains, e.g. turning:
        //
        //   builder.Services.AddIdentityInfrastructure(builder.Configuration)
        //       .AddSaleInfrastructure();
        //
        //   builder.Services
        //       .AddControllers()
        //       .AddIdentityPresentation()
        //       .AddSalePresentation();
        //
        // into the same chains with .AddCustomerInfrastructure() /
        // .AddCustomerPresentation() appended. Purely textual — if the
        // expected shape isn't found, it prints the snippet to add manually
        // instead of guessing and corrupting the file.
        // ============================================================
        internal static void PatchProgramCs(string hostCsprojPath, string prefix, string moduleFolderName, bool isDryRun)
        {
            string? hostDir = Path.GetDirectoryName(hostCsprojPath);
            if (hostDir == null) return;

            string programCsPath = Path.Combine(hostDir, "Program.cs");
            if (!File.Exists(programCsPath))
            {
                Console.WriteLine($"  Note: {programCsPath} not found — skipped Program.cs wiring.");
                return;
            }

            string content = File.ReadAllText(programCsPath);
            string original = content;

            string infraUsing = $"using {prefix}.Infrastructure.DependencyInjection;";
            string presUsing = $"using {prefix}.Presentation.DependencyInjection;";
            string infraCall = $"Add{moduleFolderName}Infrastructure";
            string presCall = $"Add{moduleFolderName}Presentation";

            bool changed = false;

            // 1. Add using directives if missing (right after the last existing `using` line).
            content = EnsureUsing(content, infraUsing, ref changed);
            content = EnsureUsing(content, presUsing, ref changed);

            // 2. Append .Add{Module}Infrastructure() to a statement that already calls
            //    some *Infrastructure(...) extension on builder.Services.
            content = AppendToChain(content, callNameHint: "Infrastructure", newCall: infraCall, ref changed,
                onNotFound: () => PrintManualSnippet("Infrastructure", infraCall));

            // 3. Append .Add{Module}Presentation() to a statement that already calls
            //    .AddControllers() / some *Presentation(...) extension.
            content = AppendToChain(content, callNameHint: "Presentation", newCall: presCall, ref changed,
                onNotFound: () => PrintManualSnippet("Presentation", presCall));

            if (!changed)
            {
                Console.WriteLine("  No changes made to Program.cs (nothing matched or already wired).");
                return;
            }

            if (isDryRun)
            {
                Console.WriteLine($"[WILL PATCH] {programCsPath}");
                Console.WriteLine("  --- preview ---");
                foreach (var line in Diff(original, content)) Console.WriteLine("  " + line);
                Console.WriteLine("  --- end preview ---");
                return;
            }

            File.WriteAllText(programCsPath, content);
            Console.WriteLine($"  Patched {programCsPath} (added {infraCall}/{presCall} wiring).");

            static string EnsureUsing(string src, string usingLine, ref bool changed)
            {
                if (src.Contains(usingLine)) return src;
                int lastUsingIdx = src.LastIndexOf("\nusing ", StringComparison.Ordinal);
                if (lastUsingIdx < 0)
                {
                    changed = true;
                    return usingLine + "\n" + src;
                }
                int lineEnd = src.IndexOf('\n', lastUsingIdx + 1);
                if (lineEnd < 0) lineEnd = src.Length - 1;
                changed = true;
                return src.Insert(lineEnd + 1, usingLine + "\n");
            }

            // Finds a `builder.Services...` statement whose fluent chain already contains a call
            // matching *callNameHint (e.g. an existing "...Infrastructure(...)" or "AddControllers()"
            // for Presentation), and inserts ".New{Call}()" right before the statement's closing ';'.
            // Statement boundary is found by simple paren/brace-depth tracking, not regex, so it's
            // robust to multiline chains and arguments like builder.Configuration.
            static string AppendToChain(string src, string callNameHint, string newCall, ref bool changed, Action onNotFound)
            {
                if (Regex.IsMatch(src, $@"\.{Regex.Escape(newCall)}\s*\(")) return src; // already wired

                // Only look inside `builder.Services` statements — find every one, then pick the
                // first whose chain contains the anchor keyword, instead of matching the keyword
                // anywhere in the whole file.
                foreach (Match stmtStart in Regex.Matches(src, @"builder\.Services\b"))
                {
                    int start = stmtStart.Index;

                    // Walk forward to find this statement's terminating ';' at paren/brace depth 0.
                    int depth = 0, i = start;
                    for (; i < src.Length; i++)
                    {
                        char c = src[i];
                        if (c is '(' or '{') depth++;
                        else if (c is ')' or '}') depth--;
                        else if (c == ';' && depth == 0) break;
                    }
                    if (i >= src.Length) continue;

                    string statement = src[start..i];

                    bool matchesHint = callNameHint == "Presentation"
                        ? statement.Contains(".AddControllers()") || statement.Contains("Presentation(")
                        : statement.Contains("Infrastructure(");

                    if (!matchesHint) continue;

                    string insertion = $"\n    .{newCall}()";
                    changed = true;
                    return src.Insert(i, insertion);
                }

                onNotFound();
                return src;
            }

            static int FindFirstOf(string src, string[] needles)
            {
                foreach (var n in needles)
                {
                    int idx = src.IndexOf(n, StringComparison.Ordinal);
                    if (idx >= 0) return idx;
                }
                return -1;
            }

            static IEnumerable<string> Diff(string before, string after)
            {
                // Minimal line-level diff for preview purposes only.
                var beforeLines = before.Split('\n');
                var afterLines = after.Split('\n');
                foreach (var line in afterLines)
                    if (!beforeLines.Contains(line)) yield return "+ " + line;
            }

            void PrintManualSnippet(string kind, string call)
            {
                Console.WriteLine($"  Warning: couldn't confidently locate the {kind} chain in Program.cs.");
                Console.WriteLine($"  Add this manually: .{call}()");
            }
        }
    }
}
