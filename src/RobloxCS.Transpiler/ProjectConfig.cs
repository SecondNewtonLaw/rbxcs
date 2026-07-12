using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RobloxCS.Transpiler;

// Resolved output layout for a project (decision #9: owned tree, overridable). Reads a single
// user class deriving from RobloxCS.ProjectDescriptor; unspecified mounts keep the defaults.
public sealed class ProjectConfig
{
    public string ProjectName { get; private set; } = "rbxcs";
    public string SharedMount { get; private set; } = "ReplicatedStorage.rbxcs";
    public string ServerMount { get; private set; } = "ServerScriptService.rbxcs";
    public string ClientMount { get; private set; } = "StarterPlayer.StarterPlayerScripts.rbxcs";
    public string PackagesMount { get; private set; } = "ReplicatedStorage.Packages";
    public string PackagesPath { get; private set; } = "";

    public string MountFor(RbxContext c) => c switch
    {
        RbxContext.Server => ServerMount,
        RbxContext.Client => ClientMount,
        _ => SharedMount,
    };

    public static string FolderFor(RbxContext c) => c switch
    {
        RbxContext.Server => "Server",
        RbxContext.Client => "Client",
        _ => "Shared",
    };

    // Luau require prefix for a mount: "ReplicatedStorage.rbxcs" -> game:GetService("ReplicatedStorage").rbxcs
    public string MountExpr(RbxContext c) => MountExprOf(MountFor(c));

    public string RuntimeRequire => $"require({MountExprOf(SharedMount + ".runtime.RBXCS")})";

    private static string MountExprOf(string dotted)
    {
        var segs = dotted.Split('.');
        var sb = new StringBuilder($"game:GetService(\"{segs[0]}\")");
        for (var i = 1; i < segs.Length; i++)
            sb.Append('.').Append(segs[i]);
        return sb.ToString();
    }

    public static ProjectConfig Resolve(CSharpCompilation compilation)
    {
        var config = new ProjectConfig();
        foreach (var tree in compilation.SyntaxTrees)
        {
            var model = compilation.GetSemanticModel(tree);
            foreach (var cls in tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>())
            {
                if (model.GetDeclaredSymbol(cls) is not { } sym || !DerivesFromDescriptor(sym))
                    continue;
                config.ProjectName = ReadProp(sym, model, "ProjectName") ?? config.ProjectName;
                config.SharedMount = ReadProp(sym, model, "SharedMount") ?? config.SharedMount;
                config.ServerMount = ReadProp(sym, model, "ServerMount") ?? config.ServerMount;
                config.ClientMount = ReadProp(sym, model, "ClientMount") ?? config.ClientMount;
                config.PackagesMount = ReadProp(sym, model, "PackagesMount") ?? config.PackagesMount;
                config.PackagesPath = ReadProp(sym, model, "PackagesPath") ?? config.PackagesPath;
                return config; // first descriptor wins
            }
        }
        return config;
    }

    // Types that are compile-time-only (config or extern bindings) and must not be transpiled.
    public static bool IsNonEmitted(INamedTypeSymbol sym) =>
        DerivesFromDescriptor(sym)
        || sym.GetAttributes().Any(a => a.AttributeClass?.Name is "LuauImportAttribute" or "WallyPackageAttribute" or "LuauModuleAttribute");

    public static bool DerivesFromDescriptor(INamedTypeSymbol sym)
    {
        for (var b = sym.BaseType; b is not null; b = b.BaseType)
            if (b.Name == "ProjectDescriptor" && b.ContainingNamespace?.Name == "RobloxCS")
                return true;
        return false;
    }

    // Reads a string-literal property override on the descriptor subclass; null if not overridden.
    private static string? ReadProp(INamedTypeSymbol sym, SemanticModel model, string name)
    {
        var prop = sym.GetMembers(name).OfType<IPropertySymbol>().FirstOrDefault();
        if (prop is null)
            return null;
        foreach (var r in prop.DeclaringSyntaxReferences)
        {
            if (r.GetSyntax() is not PropertyDeclarationSyntax pd)
                continue;
            var expr = pd.ExpressionBody?.Expression
                ?? (pd.AccessorList?.Accessors
                    .FirstOrDefault(a => a.Keyword.IsKind(SyntaxKind.GetKeyword))?.Body?.Statements
                    .OfType<ReturnStatementSyntax>().FirstOrDefault()?.Expression);
            if (expr is not null && model.GetConstantValue(expr).Value is string s)
                return s;
        }
        return null;
    }

    // Rojo default.project.json mapping each context mount to its output folder.
    public string RojoProjectJson()
    {
        var tree = new Dictionary<string, object> { ["$className"] = "DataModel" };
        AddMount(tree, SharedMount, "Shared");
        AddMount(tree, ServerMount, "Server");
        AddMount(tree, ClientMount, "Client");
        if (!string.IsNullOrEmpty(PackagesPath)) // Wally packages folder alongside transpiled output
            AddMount(tree, PackagesMount, PackagesPath);

        var sb = new StringBuilder();
        sb.Append("{\n  \"name\": \"").Append(ProjectName).Append("\",\n  \"tree\": ");
        WriteJson(sb, tree, 1);
        sb.Append("\n}\n");
        return sb.ToString();
    }

    private static void AddMount(Dictionary<string, object> tree, string dotted, string folder)
    {
        var segs = dotted.Split('.');
        var node = tree;
        for (var i = 0; i < segs.Length; i++)
        {
            if (i == segs.Length - 1)
            {
                node[segs[i]] = new Dictionary<string, object> { ["$path"] = folder };
            }
            else
            {
                if (node.TryGetValue(segs[i], out var existing) && existing is Dictionary<string, object> child)
                    node = child;
                else
                {
                    var created = new Dictionary<string, object>();
                    node[segs[i]] = created;
                    node = created;
                }
            }
        }
    }

    private static void WriteJson(StringBuilder sb, Dictionary<string, object> obj, int indent)
    {
        var pad = new string(' ', indent * 2);
        var padIn = new string(' ', (indent + 1) * 2);
        sb.Append("{\n");
        var first = true;
        foreach (var kv in obj)
        {
            if (!first)
                sb.Append(",\n");
            first = false;
            sb.Append(padIn).Append('"').Append(kv.Key).Append("\": ");
            if (kv.Value is Dictionary<string, object> nested)
                WriteJson(sb, nested, indent + 1);
            else
                sb.Append('"').Append(kv.Value).Append('"');
        }
        sb.Append('\n').Append(pad).Append('}');
    }
}
