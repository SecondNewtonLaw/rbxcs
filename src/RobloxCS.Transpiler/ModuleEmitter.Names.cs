using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RobloxCS.Luau;

namespace RobloxCS.Transpiler;

internal sealed partial class ModuleEmitter
{
    private string RequireLocalName(INamedTypeSymbol type)
    {
        if (_thisModuleTypes.Contains(type.Name) || IsRobloxNative(type))
            return type.Name; // native Roblox globals (Vector3, game, ...) need no require
        if (map.TryGetType(type, out var _))
        {
            _externalRequires[type.Name] = map.TypeRequire(type);
            return type.Name;
        }
        return type.Name;
    }

    // Roblox print/warn (Globals) and Console.WriteLine/Write -> the Luau logging fn, else null.
    private static string? LoggingLuauFn(IMethodSymbol? sym)
    {
        var ct = sym?.ContainingType?.ToDisplayString();
        if (ct == "Roblox.Globals")
            return sym!.Name switch { "print" => "print", "warn" => "warn", _ => null };
        if (ct == "System.Console" && sym!.Name is "WriteLine" or "Write")
            return "print";
        return null;
    }

    // "[File.cs:42]" source tag prepended to logging output so stray prints are traceable.
    private static string SourceLocation(Microsoft.CodeAnalysis.SyntaxNode node)
    {
        var span = node.GetLocation().GetLineSpan();
        var file = System.IO.Path.GetFileName(span.Path);
        return $"[{file}:{span.StartLinePosition.Line + 1}]";
    }

    // Bound Luau module for a type, or null. [LuauImport("path")] gives an explicit path;
    // [WallyPackage("alias")] resolves to <PackagesMount>.<alias> (alias defaults to the type name).
    private string? ImportModule(INamedTypeSymbol? type)
    {
        if (type is null)
            return null;
        foreach (var a in type.GetAttributes())
        {
            if (a.AttributeClass?.Name == "LuauImportAttribute")
                return a.ConstructorArguments.FirstOrDefault().Value as string;
            if (a.AttributeClass?.Name == "LuauModuleAttribute")
                return map.ContentModulePath(a.ConstructorArguments.FirstOrDefault().Value as string ?? "");
            if (a.AttributeClass?.Name == "WallyPackageAttribute")
            {
                var alias = a.ConstructorArguments.FirstOrDefault().Value as string;
                return $"{map.PackagesMount}.{(string.IsNullOrEmpty(alias) ? type.Name : alias)}";
            }
        }
        return null;
    }

    // Registers `local <TypeName> = require(<module>)` and returns the local name.
    private string RequireImport(INamedTypeSymbol type, string module)
    {
        _externalRequires[type.Name] = $"require({ModulePathExpr(module)})";
        return type.Name;
    }

    // [LuauName("x")] override, else the C# member name.
    private static string LuauMemberName(ISymbol? sym) =>
        sym?.GetAttributes().FirstOrDefault(a => a.AttributeClass?.Name == "LuauNameAttribute")
            ?.ConstructorArguments.FirstOrDefault().Value as string ?? sym?.Name ?? "";

    // Dotted DataModel path -> Luau: "ReplicatedStorage.Packages.ZString" -> game:GetService("ReplicatedStorage").Packages.ZString
    private static string ModulePathExpr(string dotted)
    {
        var segs = dotted.Split('.');
        var expr = $"game:GetService(\"{segs[0]}\")";
        for (var i = 1; i < segs.Length; i++)
            expr += "." + segs[i];
        return expr;
    }

    private readonly HashSet<string> _warnedTypes = new();

    // Containing types the transpiler maps to Luau natives/runtime (BCL surface). Anything else
    // external + unbound gets a detection warning.
    private static readonly HashSet<string> MappedBcl = new()
    {
        "System.Math", "System.Console", "System.Object", "System.ValueType", "System.Enum",
        "System.Collections.Generic.List<T>", "System.Collections.Generic.Dictionary<TKey, TValue>",
        "System.Collections.Generic.HashSet<T>", "System.Collections.Generic.IEnumerable<T>",
        "System.Collections.Generic.KeyValuePair<TKey, TValue>",
        "System.Threading.Tasks.Task", "System.Threading.Tasks.Task<TResult>",
        "System.Linq.Enumerable", "System.Nullable<T>", "System.Exception",
    };

    // Warns when a used symbol comes from an external assembly with no Luau path: not Roblox, not
    // a mapped BCL type, not [LuauImport]. Tells the user exactly what needs a binding.
    private void MaybeWarnExternal(ISymbol? sym, Microsoft.CodeAnalysis.SyntaxNode node) =>
        MaybeWarnExternalType(sym?.ContainingType, node);

    private void MaybeWarnExternalType(INamedTypeSymbol? ct, Microsoft.CodeAnalysis.SyntaxNode node)
    {
        if (ct is null
            || ct.SpecialType != SpecialType.None         // primitives / string / object
            || ct.DeclaringSyntaxReferences.Length != 0    // has source -> transpiled
            || IsRobloxNative(ct)
            || ImportModule(ct) is not null
            || MappedBcl.Contains(ct.OriginalDefinition.ToDisplayString()))
            return;

        var key = ct.ToDisplayString();
        if (!_warnedTypes.Add(key))
            return;
        var pos = node.GetLocation().GetLineSpan();
        diagnostics.Add(new TranspileDiagnostic(
            $"'{key}' has no Luau binding — it won't run in Roblox. Add [LuauImport(\"...\")] or use a supported type.",
            pos.Path, pos.StartLinePosition.Line + 1, pos.StartLinePosition.Character + 1));
    }

    // A Roblox API type (namespace Roblox, from a referenced assembly, not user source).
    private static bool IsRobloxNative(INamedTypeSymbol type) =>
        type.ContainingNamespace?.Name == "Roblox" && type.DeclaringSyntaxReferences.Length == 0;

    // A Roblox Instance-derived class -> constructed via Instance.new("ClassName").
    private static bool DerivesFromInstance(INamedTypeSymbol type)
    {
        for (var b = type.BaseType; b is not null; b = b.BaseType)
            if (b.Name == "Instance")
                return true;
        return false;
    }

    private bool IsUserType(INamedTypeSymbol? type)
    {
        if (type is null || type.SpecialType == SpecialType.System_Object || type.TypeKind != TypeKind.Class)
            return false;
        return _thisModuleTypes.Contains(type.Name) || map.TryGetType(type, out _);
    }

    // Luau reserved words (Lua 5.1 baseline). A C# identifier colliding with one is suffixed `_`;
    // applied at both declaration and use so it stays consistent. ponytail: a user with both `end`
    // and `end_` would collide — accepted, vanishingly rare.
    private static readonly HashSet<string> LuauReserved = new()
    {
        "and", "break", "do", "else", "elseif", "end", "false", "for", "function", "if", "in",
        "local", "nil", "not", "or", "repeat", "return", "then", "true", "until", "while",
    };

    private static string LuauId(string name) => LuauReserved.Contains(name) ? name + "_" : name;

    // Strips C# numeric suffixes (f/d/m/u/l) and digit-separator underscores; Luau has one number
    // type and no suffixes. Hex/binary keep their digits (only integer suffixes trimmed).
    private static string NormalizeNumber(string t)
    {
        t = t.Replace("_", "");
        var hexOrBin = t.StartsWith("0x") || t.StartsWith("0X") || t.StartsWith("0b") || t.StartsWith("0B");
        return hexOrBin ? t.TrimEnd('u', 'U', 'l', 'L') : t.TrimEnd('f', 'F', 'd', 'D', 'm', 'M', 'u', 'U', 'l', 'L');
    }

    // Escapes a raw string value into a Luau double-quoted literal. Built from the parsed token value,
    // so C# verbatim strings and \uXXXX escapes are handled uniformly.
    private static string LuauString(string s)
    {
        var sb = new System.Text.StringBuilder("\"");
        foreach (var c in s)
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20) sb.Append("\\x").Append(((int)c).ToString("x2"));
                    else sb.Append(c);
                    break;
            }
        sb.Append('"');
        return sb.ToString();
    }

    private int InheritanceDepth(INamedTypeSymbol sym)
    {
        var d = 0;
        var b = sym.BaseType;
        while (b is not null && _thisModuleTypes.Contains(b.Name))
        {
            d++;
            b = b.BaseType;
        }
        return d;
    }
}
