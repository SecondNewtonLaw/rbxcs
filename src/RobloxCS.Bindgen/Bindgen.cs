using System.Text;

namespace RobloxCS.Bindgen;

public sealed class BindgenResult
{
    public string Code = "";
    public readonly List<string> Unknowns = new();       // Luau type names with no C# mapping
    public readonly List<string> Notes = new();          // resolution warnings
}

// Turns a Luau module/package into a C# binding file. Entry can be a single .luau, or a folder with
// init.luau; an index (init.luau `return { X = require(script.Parent.X) }`) is followed one level so
// each re-exported sibling becomes a nested static class bound to its own file.
public static class Bindgen
{
    public static BindgenResult Generate(string path, string ns, string moduleBase, bool wally, string? wallyAlias)
    {
        var result = new BindgenResult();
        var entryFile = ResolveEntry(path, result);
        if (entryFile is null)
            return result;

        var entryDir = Path.GetDirectoryName(entryFile) ?? ".";
        var rootCs = RootName(path, wallyAlias);
        // Wally: entry is [WallyPackage]; nested submodules chain off the package require, so their
        // base is "Packages/<alias>" -> ContentModulePath maps Packages -> PackagesMount.
        var effectiveBase = wally ? "Packages/" + (wallyAlias ?? rootCs) : moduleBase;
        var module = BuildModule(entryFile, entryDir, rootCs, effectiveBase, result, depth: 0);
        if (wally)
        {
            module.Kind = BindingKind.Wally;
            module.WallyAlias = wallyAlias ?? module.CsName;
            module.BindingPath = null;
        }

        result.Code = BindingEmitter.Emit(ns, new[] { module });
        result.Unknowns.Sort();
        return result;
    }

    private static LuauModule BuildModule(string file, string dir, string csName, string bindingBase,
        BindgenResult result, int depth)
    {
        var extract = SurfaceExtractor.Extract(File.ReadAllText(file), file, csName);
        var module = extract.Module;
        module.BindingPath = bindingBase;
        foreach (var u in extract.Unknowns)
            if (!result.Unknowns.Contains(u))
                result.Unknowns.Add(u);

        if (depth >= 2)
        {
            if (extract.ReExports.Count > 0)
                result.Notes.Add($"{csName}: re-export nesting stopped at depth 2");
            return module;
        }

        foreach (var re in extract.ReExports)
        {
            var subFile = ResolveRequire(dir, re.RequireArg);
            if (subFile is null)
            {
                result.Notes.Add($"{csName}.{re.FieldName}: could not resolve require({re.RequireArg})");
                continue;
            }
            var subDir = Path.GetDirectoryName(subFile) ?? dir;
            var subBase = bindingBase + "/" + re.FieldName;
            module.Nested.Add(BuildModule(subFile, subDir, SafeType(re.FieldName), subBase, result, depth + 1));
        }
        return module;
    }

    // "script.Parent.Signal" / "script.Parent.util.Foo" -> a sibling file under `dir`.
    private static string? ResolveRequire(string dir, string arg)
    {
        var segs = arg.Split('.').Select(s => s.Trim()).ToList();
        var rel = segs.SkipWhile(s => s is "script" or "Parent").ToList();
        if (rel.Count == 0)
            return null;
        var basePath = Path.Combine(new[] { dir }.Concat(rel).ToArray());
        if (File.Exists(basePath + ".luau")) return basePath + ".luau";
        if (File.Exists(basePath + ".lua")) return basePath + ".lua";
        if (File.Exists(Path.Combine(basePath, "init.luau"))) return Path.Combine(basePath, "init.luau");
        if (File.Exists(Path.Combine(basePath, "init.lua"))) return Path.Combine(basePath, "init.lua");
        return null;
    }

    private static string? ResolveEntry(string path, BindgenResult result)
    {
        if (File.Exists(path))
            return path;
        if (Directory.Exists(path))
        {
            foreach (var init in new[] { "init.luau", "init.lua" })
                if (File.Exists(Path.Combine(path, init)))
                    return Path.Combine(path, init);
            var luaus = Directory.GetFiles(path, "*.luau");
            if (luaus.Length == 1)
                return luaus[0];
            result.Notes.Add($"no init.luau and {luaus.Length} .luau files in {path}; pass a specific file");
            return null;
        }
        result.Notes.Add($"path not found: {path}");
        return null;
    }

    private static string RootName(string path, string? wallyAlias)
    {
        if (!string.IsNullOrEmpty(wallyAlias))
            return SafeType(wallyAlias!);
        var name = File.Exists(path) && Path.GetFileNameWithoutExtension(path) is var f && f != "init"
            ? f
            : new DirectoryInfo(Directory.Exists(path) ? path : Path.GetDirectoryName(path) ?? ".").Name;
        return SafeType(name);
    }

    private static string SafeType(string s)
    {
        var sb = new StringBuilder();
        foreach (var c in s)
            sb.Append(char.IsLetterOrDigit(c) || c == '_' ? c : '_');
        var r = sb.ToString();
        if (r.Length == 0 || char.IsDigit(r[0])) r = "_" + r;
        return char.ToUpperInvariant(r[0]) + r.Substring(1);
    }
}
