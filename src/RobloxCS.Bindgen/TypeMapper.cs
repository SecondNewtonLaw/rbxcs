using Loretta.CodeAnalysis;
using Loretta.CodeAnalysis.Lua.Syntax;

namespace RobloxCS.Bindgen;

// Maps a Luau type annotation to a C# type. Structure (optional/array/dict/function/union) is read
// from the AST node kind; leaf names from a small table. Anything it can't place becomes `object`
// and is recorded in `unknowns` so the caller can report what needs a hand-written binding.
internal static class TypeMapper
{
    // ponytail: curated Roblox surface. Bindgen has no access to the API dump; an unknown capitalized
    // name stays `object` rather than guessing `Roblox.X`. Extend this set (or wire the dump) if needed.
    private static readonly HashSet<string> RobloxTypes = new()
    {
        "Instance", "Vector3", "Vector2", "CFrame", "Color3", "UDim", "UDim2", "Rect", "Ray",
        "Region3", "BrickColor", "TweenInfo", "NumberRange", "NumberSequence", "ColorSequence",
        "DateTime", "Random", "RaycastParams", "RaycastResult", "PhysicalProperties", "Enum",
        "RBXScriptSignal", "RBXScriptConnection",
    };

    public static string Map(TypeSyntax? t, ISet<string> unknowns, bool returnPos = false,
        IReadOnlyDictionary<string, string>? aliases = null)
    {
        if (t is null)
            return "object";

        switch (t.GetType().Name)
        {
            case "NilableTypeSyntax":
                return Nullable(Map(Inner(t), unknowns, aliases: aliases));
            case "ArrayTypeSyntax":
                return Map(Inner(t), unknowns, aliases: aliases) + "[]";
            case "UnionTypeSyntax":
            {
                var parts = t.ChildNodes().OfType<TypeSyntax>().ToList();
                var nonNil = parts.Where(p => p.ToString().Trim() != "nil").ToList();
                if (nonNil.Count == 1 && nonNil.Count < parts.Count)
                    return Nullable(Map(nonNil[0], unknowns, aliases: aliases));
                return "object";
            }
            case "FunctionTypeSyntax":
                return MapFunction(t, unknowns, aliases);
            case "TableTypeSyntax":
                return MapTable(t, unknowns, aliases);
            default:
                return MapLeaf(t.ToString().Trim(), unknowns, returnPos, aliases);
        }
    }

    private static TypeSyntax? Inner(TypeSyntax t) => t.ChildNodes().OfType<TypeSyntax>().FirstOrDefault();

    private static string Nullable(string cs) => cs.EndsWith("?") || cs == "object" ? cs : cs + "?";

    private static string MapLeaf(string name, ISet<string> unknowns, bool returnPos,
        IReadOnlyDictionary<string, string>? aliases)
    {
        switch (name)
        {
            case "string": return "string";
            case "number": return "double";
            case "boolean": case "bool": return "bool";
            case "nil": case "void": case "()": return returnPos ? "void" : "object";
            case "any": case "unknown": case "table": case "{}": return "object";
            case "thread": case "userdata": case "buffer": return "object";
        }
        if (aliases is not null && aliases.TryGetValue(name, out var cs))
            return cs;
        if (RobloxTypes.Contains(name))
            return "Roblox." + name;
        unknowns.Add(name);
        return "object";
    }

    // {[K]: V} -> Dictionary<K,V>; named-field table -> object (a generated struct is a later upgrade).
    private static string MapTable(TypeSyntax t, ISet<string> unknowns, IReadOnlyDictionary<string, string>? aliases)
    {
        var text = t.ToString();
        var open = text.IndexOf("[", StringComparison.Ordinal);
        var close = text.IndexOf("]", StringComparison.Ordinal);
        var colon = text.IndexOf(":", close < 0 ? 0 : close, StringComparison.Ordinal);
        if (open >= 0 && close > open && colon > close)
        {
            var key = MapLeaf(text.Substring(open + 1, close - open - 1).Trim(), unknowns, false, aliases);
            var val = MapLeaf(text.Substring(colon + 1).TrimEnd('}', ' ', '\t', '\r', '\n').Trim(), unknowns, false, aliases);
            return $"System.Collections.Generic.Dictionary<{key}, {val}>";
        }
        return "object";
    }

    // (A, B) -> R  ->  Func<A,B,R> / Action<A,B>. ponytail: single-return, non-nested only; hairy
    // signatures fall back to System.Delegate rather than mis-mapping.
    private static string MapFunction(TypeSyntax t, ISet<string> unknowns, IReadOnlyDictionary<string, string>? aliases)
    {
        var text = t.ToString();
        var arrow = TopLevelArrow(text);
        if (arrow < 0)
            return "System.Delegate";
        var ret = text.Substring(arrow + 2).Trim();
        var paramsPart = text.Substring(0, arrow).Trim().TrimStart('(').TrimEnd(')');
        var ps = paramsPart.Length == 0
            ? new List<string>()
            : SplitTopLevel(paramsPart).Select(p => MapLeaf(StripParamName(p), unknowns, false, aliases)).ToList();
        var retCs = ret is "()" or "nil" or "void" ? "void" : MapLeaf(ret, unknowns, true, aliases);
        if (retCs == "void")
            return ps.Count == 0 ? "System.Action" : $"System.Action<{string.Join(", ", ps)}>";
        ps.Add(retCs);
        return $"System.Func<{string.Join(", ", ps)}>";
    }

    private static string StripParamName(string p)
    {
        var c = p.IndexOf(':');
        var t = (c >= 0 ? p.Substring(c + 1) : p).Trim();
        return t.StartsWith("...") ? t.Substring(3).Trim() : t; // vararg `...T` -> element type
    }

    private static int TopLevelArrow(string s)
    {
        var depth = 0;
        for (var i = 0; i < s.Length - 1; i++)
        {
            var ch = s[i];
            if (ch is '(' or '{' or '<') depth++;
            else if (ch is ')' or '}' or '>') depth--;
            else if (depth == 0 && ch == '-' && s[i + 1] == '>') return i;
        }
        return -1;
    }

    private static List<string> SplitTopLevel(string s)
    {
        var parts = new List<string>();
        var depth = 0;
        var start = 0;
        for (var i = 0; i < s.Length; i++)
        {
            var ch = s[i];
            if (ch is '(' or '{' or '<') depth++;
            else if (ch is ')' or '}' or '>') depth--;
            else if (ch == ',' && depth == 0)
            {
                parts.Add(s.Substring(start, i - start));
                start = i + 1;
            }
        }
        parts.Add(s.Substring(start));
        return parts.Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
    }
}
