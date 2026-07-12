using System.Text.RegularExpressions;
using RobloxCS.Bindgen;

namespace RobloxCS.Cli;

// Reads wally.toml and, for each dependency, generates a typed C# binding from the installed Luau
// source (via bindgen). Falls back to an empty [WallyPackage] stub only when the package isn't
// installed yet (run `wally install` first).
internal static class AddPackage
{
    public static int Run(string[] args)
    {
        string? only = null, output = "Bindings", ns = "Packages", packages = "Packages";
        var force = false;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--output" when i + 1 < args.Length: output = args[++i]; break;
                case "--namespace" when i + 1 < args.Length: ns = args[++i]; break;
                case "--packages" when i + 1 < args.Length: packages = args[++i]; break;
                case "--force": force = true; break;
                default:
                    if (args[i].StartsWith('-'))
                        return Fail($"unknown option '{args[i]}'");
                    only = args[i];
                    break;
            }
        }

        var wally = Path.Combine(Directory.GetCurrentDirectory(), "wally.toml");
        if (!File.Exists(wally))
            return Fail("no wally.toml in the current directory");

        var deps = ParseDependencies(File.ReadAllLines(wally));
        if (only is not null)
            deps = deps.Where(d => d.Alias.Equals(only, StringComparison.OrdinalIgnoreCase)).ToList();
        if (deps.Count == 0)
            return Fail(only is null ? "no dependencies found in wally.toml" : $"no dependency '{only}' in wally.toml");

        Directory.CreateDirectory(output);
        int bound = 0, stubbed = 0;
        foreach (var dep in deps)
        {
            var path = Path.Combine(output, dep.Alias + ".cs");
            if (File.Exists(path) && !force)
            {
                Console.WriteLine($"skip {dep.Alias} (exists — use --force to overwrite)");
                continue;
            }

            var source = ResolveInstalled(packages, dep);
            if (source is not null)
            {
                var result = Bindgen.Bindgen.Generate(source, ns, moduleBase: "", wally: true, wallyAlias: dep.Alias);
                if (result.Code.Length > 0)
                {
                    File.WriteAllText(path, result.Code);
                    var extra = result.Unknowns.Count > 0 ? $"  ({result.Unknowns.Count} unmapped -> object)" : "";
                    Console.WriteLine($"bound {dep.Alias} from {source}{extra}");
                    foreach (var note in result.Notes)
                        Console.WriteLine($"  note: {note}");
                    bound++;
                    continue;
                }
            }

            File.WriteAllText(path, Stub(dep, ns));
            Console.WriteLine($"stub {dep.Alias} (not installed under {packages}/ — run `wally install`, then re-run)");
            stubbed++;
        }
        Console.WriteLine($"rbxcs: {bound} bound, {stubbed} stubbed in {output}/.");
        return 0;
    }

    private readonly record struct Dep(string Alias, string Spec);

    // Finds a dependency's installed Luau root. Wally lays out Packages/<Alias>.luau as a redirect to
    // Packages/_Index/<scope_name@ver>/<name>/; parse that redirect, else derive from the spec.
    private static string? ResolveInstalled(string packagesDir, Dep dep)
    {
        var redirect = Path.Combine(packagesDir, dep.Alias + ".luau");
        if (File.Exists(redirect))
        {
            var keys = IndexKeys(File.ReadAllText(redirect));
            if (keys.Count >= 1)
            {
                var p = Path.Combine(new[] { packagesDir, "_Index" }.Concat(keys).ToArray());
                if (Directory.Exists(p)) return p;
                if (File.Exists(p + ".luau")) return p + ".luau";
            }
        }

        // Fallback: spec "scope/name@ver" -> _Index/scope_name@ver/name
        var slash = dep.Spec.IndexOf('/');
        var at = dep.Spec.IndexOf('@');
        if (slash > 0 && at > slash)
        {
            var name = dep.Spec.Substring(slash + 1, at - slash - 1);
            var idxKey = dep.Spec.Substring(0, at).Replace('/', '_');
            var p = Path.Combine(packagesDir, "_Index", idxKey, name);
            if (Directory.Exists(p)) return p;
        }
        return null;
    }

    // Accessors chained off `_Index` in a redirect: `_Index["a"]["b"]` / `_Index["a"].b` -> ["a","b"].
    private static List<string> IndexKeys(string src)
    {
        var keys = new List<string>();
        var at = src.IndexOf("_Index", StringComparison.Ordinal);
        if (at < 0) return keys;
        foreach (Match m in Regex.Matches(src.Substring(at + 6), @"\[\s*""([^""]+)""\s*\]|\.([A-Za-z_]\w*)"))
        {
            var key = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
            if (key == "Parent") continue;
            keys.Add(key);
        }
        return keys;
    }

    // Minimal wally.toml parse: collect `Alias = "scope/name@ver"` under [dependencies]/[dev-dependencies].
    private static List<Dep> ParseDependencies(string[] lines)
    {
        var deps = new List<Dep>();
        var inDeps = false;
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                var section = line.Trim('[', ']').Trim();
                inDeps = section is "dependencies" or "dev-dependencies" or "server-dependencies";
                continue;
            }
            if (!inDeps || line.Length == 0 || line.StartsWith('#'))
                continue;
            var eq = line.IndexOf('=');
            if (eq <= 0)
                continue;
            var alias = line[..eq].Trim();
            var spec = line[(eq + 1)..].Trim().Trim('"');
            if (alias.Length > 0)
                deps.Add(new Dep(alias, spec));
        }
        return deps;
    }

    private static string Stub(Dep dep, string ns) =>
        $$"""
        using RobloxCS;
        using Roblox;

        namespace {{ns}};

        // Binding for wally package {{dep.Spec}} — not installed, so no surface could be read.
        // Run `wally install`, then `rbxcs add-package {{dep.Alias}} --force` to generate the real API.
        [WallyPackage]
        public class {{dep.Alias}}
        {
        }
        """;

    private static int Fail(string message)
    {
        Console.Error.WriteLine($"rbxcs add-package: {message}");
        return 1;
    }
}
