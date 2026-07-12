using Loretta.CodeAnalysis;
using Loretta.CodeAnalysis.Lua;
using Loretta.CodeAnalysis.Lua.Syntax;

namespace RobloxCS.Bindgen;

internal sealed class ExtractResult
{
    public readonly LuauModule Module = new();
    public readonly List<ReExport> ReExports = new();   // index fields -> require(...) submodules
    public readonly HashSet<string> Unknowns = new();
}

internal sealed class ReExport
{
    public string FieldName = "";
    public string RequireArg = "";   // e.g. "script.Parent.Signal"
}

// Parses one Luau module and reads the public surface off the value it returns:
//   return M                              -> members declared on M (function M.x, M.x = ...)
//   return { A = require(...), B = ... }   -> index: re-exports (recursed) + inline members
// A metatable-OOP module (M.__index = M, `:` methods, or M.new returning setmetatable) is emitted
// as a real class: M.new -> constructor, `:`/self methods -> instance methods, M.x = lit -> static.
internal static class SurfaceExtractor
{
    private static readonly HashSet<string> CsKeywords = new()
    {
        "abstract","as","base","bool","break","byte","case","catch","char","checked","class","const",
        "continue","decimal","default","delegate","do","double","else","enum","event","explicit",
        "extern","false","finally","fixed","float","for","foreach","goto","if","implicit","in","int",
        "interface","internal","is","lock","long","namespace","new","null","object","operator","out",
        "override","params","private","protected","public","readonly","ref","return","sbyte","sealed",
        "short","sizeof","stackalloc","static","string","struct","switch","this","throw","true","try",
        "typeof","uint","ulong","unchecked","unsafe","ushort","using","virtual","void","volatile","while",
    };

    public static ExtractResult Extract(string source, string path, string csName)
    {
        var result = new ExtractResult();
        result.Module.CsName = csName;
        var tree = LuaSyntaxTree.ParseText(source, new LuaParseOptions(LuaSyntaxOptions.Luau), path: path);
        var cu = (CompilationUnitSyntax)tree.GetRoot();

        var top = cu.DescendantNodes()
            .Where(n => n.Parent is StatementListSyntax sl && sl.Parent is CompilationUnitSyntax)
            .ToList();

        var ret = top.OfType<ReturnStatementSyntax>().LastOrDefault();
        var returned = ret?.Expressions.FirstOrDefault();

        if (returned is TableConstructorExpressionSyntax table)
        {
            foreach (var field in table.Fields)
            {
                var (name, value) = FieldNameValue(field);
                if (name is null || value is null)
                    continue;
                if (value is FunctionCallExpressionSyntax call && CalleeIs(call, "require"))
                    result.ReExports.Add(new ReExport { FieldName = name, RequireArg = RequireArg(call) });
                else if (value.GetType().Name.Contains("Function")) // anon function field: rare, skip
                    continue;
                else
                    result.Module.Members.Add(Property(name, value, null, result.Unknowns));
            }
            return result;
        }

        var moduleVar = (returned as IdentifierNameSyntax)?.Name ?? "M";
        var isClass = DetectClass(top, moduleVar);
        result.Module.IsClass = isClass;

        // Class self-types (the module var + top-level `export type`s) resolve to the C# class name.
        IReadOnlyDictionary<string, string>? aliases = null;
        if (isClass)
        {
            var map = new Dictionary<string, string> { [moduleVar] = csName };
            foreach (var td in top.OfType<TypeDeclarationStatementSyntax>())
                map[td.Name.Text] = csName;
            aliases = map;
        }

        foreach (var stmt in top)
        {
            switch (stmt)
            {
                case FunctionDeclarationStatementSyntax f
                    when f.Name is MemberFunctionNameSyntax mfn && BaseText(mfn.BaseName) == moduleVar:
                    result.Module.Members.Add(DotFunction(mfn.Name.Text, f, isClass, aliases, result.Unknowns));
                    break;
                case FunctionDeclarationStatementSyntax f2
                    when f2.Name is MethodFunctionNameSyntax mtn && BaseText(mtn.BaseName) == moduleVar:
                    result.Module.Members.Add(Method(mtn.Name.Text, f2, isInstance: true, aliases, result.Unknowns));
                    break;
                case AssignmentStatementSyntax a:
                    foreach (var (member, rhs) in AssignedMembers(a, moduleVar))
                        result.Module.Members.Add(Property(member, rhs, aliases, result.Unknowns));
                    break;
            }
        }
        return result;
    }

    private static bool DetectClass(IReadOnlyList<SyntaxNode> top, string moduleVar)
    {
        foreach (var stmt in top)
        {
            if (stmt is AssignmentStatementSyntax a
                && a.Variables.Any(v => v is MemberAccessExpressionSyntax ma
                    && (ma.Expression as IdentifierNameSyntax)?.Name == moduleVar
                    && ma.MemberName.Text is "__index" or "__newindex"))
                return true;
            if (stmt is FunctionDeclarationStatementSyntax cf
                && cf.Name is MethodFunctionNameSyntax mtn && BaseText(mtn.BaseName) == moduleVar)
                return true;
            if (stmt is FunctionDeclarationStatementSyntax nf
                && nf.Name is MemberFunctionNameSyntax mfn && BaseText(mfn.BaseName) == moduleVar
                && mfn.Name.Text == "new" && nf.Body.ToString().Contains("setmetatable"))
                return true;
        }
        return false;
    }

    private static LuauMember DotFunction(string luauName, FunctionDeclarationStatementSyntax f,
        bool isClass, IReadOnlyDictionary<string, string>? aliases, ISet<string> unknowns)
    {
        if (isClass && luauName == "new")
        {
            var ctor = new LuauMember { Kind = MemberKind.Constructor, LuauName = luauName, CsName = "new" };
            AddParams(ctor, f, dropSelf: false, unknowns, aliases);
            return ctor;
        }
        var firstIsSelf = f.Parameters.Parameters.FirstOrDefault() is NamedParameterSyntax { Identifier: { Text: "self" } };
        return Method(luauName, f, isInstance: firstIsSelf, aliases, unknowns);
    }

    private static LuauMember Method(string luauName, FunctionDeclarationStatementSyntax f,
        bool isInstance, IReadOnlyDictionary<string, string>? aliases, ISet<string> unknowns)
    {
        var m = new LuauMember { Kind = MemberKind.Method, LuauName = luauName, IsStatic = !isInstance };
        Name(m, luauName);
        m.CsType = TypeMapper.Map(f.TypeBinding?.Type, unknowns, returnPos: true, aliases: aliases);
        AddParams(m, f, dropSelf: isInstance, unknowns, aliases);
        return m;
    }

    private static void AddParams(LuauMember m, FunctionDeclarationStatementSyntax f, bool dropSelf,
        ISet<string> unknowns, IReadOnlyDictionary<string, string>? aliases)
    {
        foreach (var p in f.Parameters.Parameters)
        {
            if (p is NamedParameterSyntax np)
            {
                if (dropSelf && np.Identifier.Text == "self")
                    continue;
                m.Params.Add(new LuauParam
                {
                    Name = SafeParam(np.Identifier.Text),
                    CsType = TypeMapper.Map(np.TypeBinding?.Type, unknowns, aliases: aliases),
                });
            }
            else if (p is VarArgParameterSyntax)
            {
                m.Params.Add(new LuauParam { Name = "args", CsType = "object[]", IsParams = true });
            }
        }
    }

    private static LuauMember Property(string luauName, ExpressionSyntax value,
        IReadOnlyDictionary<string, string>? aliases, ISet<string> unknowns)
    {
        var m = new LuauMember { Kind = MemberKind.Property, LuauName = luauName };
        Name(m, luauName);
        m.CsType = LiteralType(value);
        return m;
    }

    private static string LiteralType(ExpressionSyntax value) => value switch
    {
        LiteralExpressionSyntax lit => lit.Token.Text switch
        {
            "true" or "false" => "bool",
            _ when lit.Token.Value is string => "string",
            _ when lit.Token.Value is double or long or int => "double",
            _ => "object",
        },
        _ => "object",
    };

    private static void Name(LuauMember m, string luauName)
    {
        if (IsIdentifier(luauName) && !CsKeywords.Contains(luauName))
        {
            m.CsName = luauName;
        }
        else if (IsIdentifier(luauName) && CsKeywords.Contains(luauName))
        {
            m.CsName = "@" + luauName; // @new -> luau `new`, transpiler drops the @
        }
        else
        {
            m.CsName = Sanitize(luauName);
            m.LuauName = luauName; // differs -> emitter adds [LuauName]
        }
    }

    private static IEnumerable<(string, ExpressionSyntax)> AssignedMembers(AssignmentStatementSyntax a, string moduleVar)
    {
        var vars = a.Variables.ToList();
        var vals = a.EqualsValues.Values.ToList();
        for (var i = 0; i < vars.Count; i++)
        {
            if (vars[i] is MemberAccessExpressionSyntax ma
                && (ma.Expression as IdentifierNameSyntax)?.Name == moduleVar
                && ma.MemberName.Text is var name && name != "__index" && name != "__newindex"
                && i < vals.Count)
                yield return (name, vals[i]);
        }
    }

    private static (string?, ExpressionSyntax?) FieldNameValue(TableFieldSyntax field)
    {
        if (field is IdentifierKeyedTableFieldSyntax id)
            return (id.Identifier.Text, id.Value);
        return (null, null);
    }

    private static bool CalleeIs(FunctionCallExpressionSyntax call, string name) =>
        (call.Expression as IdentifierNameSyntax)?.Name == name;

    private static string RequireArg(FunctionCallExpressionSyntax call) =>
        call.Argument.ToString().Trim().TrimStart('(').TrimEnd(')').Trim();

    private static string BaseText(FunctionNameSyntax n) => n.ToString().Trim();

    private static bool IsIdentifier(string s)
    {
        if (s.Length == 0 || !(char.IsLetter(s[0]) || s[0] == '_'))
            return false;
        foreach (var c in s)
            if (!(char.IsLetterOrDigit(c) || c == '_'))
                return false;
        return true;
    }

    private static string Sanitize(string s)
    {
        var chars = s.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray();
        var r = new string(chars);
        return char.IsDigit(r[0]) ? "_" + r : r;
    }

    private static string SafeParam(string name) =>
        CsKeywords.Contains(name) ? "@" + name : (IsIdentifier(name) ? name : Sanitize(name));
}
