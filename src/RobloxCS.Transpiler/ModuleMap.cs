using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RobloxCS.Transpiler;

public sealed class ModuleInfo(string relativePath, string requireTargetExpr, RbxContext context, ModuleKind kind, string moduleName, bool isActor)
{
    public string RelativePath { get; } = relativePath;         // "Server/Demo/Animals"
    public string RequireTargetExpr { get; } = requireTargetExpr; // Luau: game:GetService(...).rbxcs.Demo.Animals
    public RbxContext Context { get; } = context;
    public ModuleKind Kind { get; } = kind;
    public string ModuleName { get; } = moduleName;
    public bool IsActor { get; } = isActor; // [Actor]: runner Script mounts beneath an Actor instance
}

// Resolves every type to the module it lives in and the Luau require path that reaches it,
// under the rbxcs-owned tree (decision #9). Mounts:
//   Shared -> ReplicatedStorage.rbxcs   Server -> ServerScriptService.rbxcs
//   Client -> StarterPlayer.StarterPlayerScripts.rbxcs   Runtime -> ReplicatedStorage.rbxcs.runtime
public sealed class ModuleMap
{
    private readonly Dictionary<SyntaxTree, ModuleInfo> _byTree = new();
    private readonly Dictionary<ISymbol, ModuleInfo> _byType = new(SymbolEqualityComparer.Default);

    private ProjectConfig _config = new();

    public string RuntimeRequire => _config.RuntimeRequire;
    public string PackagesMount => _config.PackagesMount;

    // Resolves a project-relative luau path ("Shared/sub/Mod", extension optional) to a dotted
    // DataModel path, mapping the leading context folder through the current mount config.
    public string ContentModulePath(string relative)
    {
        var segs = relative.Replace('\\', '/').Split('/');
        var mount = segs[0] switch
        {
            "Server" => _config.ServerMount,
            "Client" => _config.ClientMount,
            "Packages" => _config.PackagesMount,
            _ => _config.SharedMount,
        };
        var rest = segs.Skip(1)
            .Select(s => s.EndsWith(".luau") ? s.Substring(0, s.Length - ".luau".Length) : s);
        var tail = string.Join(".", rest);
        return tail.Length == 0 ? mount : mount + "." + tail;
    }

    public static ModuleMap Build(CSharpCompilation compilation, ProjectConfig config)
    {
        var map = new ModuleMap { _config = config };
        foreach (var tree in compilation.SyntaxTrees)
        {
            var decls = tree.GetRoot().DescendantNodes()
                .Where(n => n is ClassDeclarationSyntax or StructDeclarationSyntax or EnumDeclarationSyntax)
                .Cast<BaseTypeDeclarationSyntax>()
                .ToList();
            if (decls.Count == 0)
                continue;

            var model = compilation.GetSemanticModel(tree);
            var symbols = decls
                .Select(c => model.GetDeclaredSymbol(c))
                .OfType<INamedTypeSymbol>()
                .Where(s => !ProjectConfig.IsNonEmitted(s))
                .ToList();
            if (symbols.Count == 0)
                continue;

            var context = ContextOf(symbols);
            var kind = KindOf(symbols);
            var moduleName = Path.GetFileNameWithoutExtension(tree.FilePath);
            if (string.IsNullOrEmpty(moduleName))
                moduleName = symbols[0].Name;

            var nsSegments = NamespaceSegments(symbols[0]);
            var folder = ProjectConfig.FolderFor(context);
            var relParts = new List<string> { folder };
            relParts.AddRange(nsSegments);
            relParts.Add(moduleName);
            var relativePath = string.Join("/", relParts);

            var target = config.MountExpr(context);
            foreach (var seg in nsSegments)
                target += "." + seg;
            target += "." + moduleName;

            var isActor = symbols.Any(s => s.GetAttributes().Any(a => a.AttributeClass?.Name == "ActorAttribute"));
            var info = new ModuleInfo(relativePath, target, context, kind, moduleName, isActor);
            map._byTree[tree] = info;
            foreach (var s in symbols)
                map._byType[s] = info;
        }
        return map;
    }

    public ModuleInfo ForTree(SyntaxTree tree) => _byTree[tree];

    public bool TryGetType(INamedTypeSymbol symbol, out ModuleInfo info) => _byType.TryGetValue(symbol, out info!);

    // `require(<module>).<TypeName>` — reaches a user type from another module.
    public string TypeRequire(INamedTypeSymbol symbol) =>
        _byType.TryGetValue(symbol, out var info)
            ? $"require({info.RequireTargetExpr}).{symbol.Name}"
            : symbol.Name;

    private static IReadOnlyList<string> NamespaceSegments(INamedTypeSymbol symbol)
    {
        var ns = symbol.ContainingNamespace;
        var segs = new List<string>();
        while (ns is { IsGlobalNamespace: false })
        {
            segs.Insert(0, ns.Name);
            ns = ns.ContainingNamespace;
        }
        return segs;
    }

    private static RbxContext ContextOf(IEnumerable<INamedTypeSymbol> symbols)
    {
        foreach (var s in symbols)
            foreach (var a in s.GetAttributes())
                switch (a.AttributeClass?.Name)
                {
                    case "ServerAttribute": return RbxContext.Server;
                    case "ClientAttribute": return RbxContext.Client;
                    case "SharedAttribute": return RbxContext.Shared;
                }
        return RbxContext.Shared;
    }

    private static ModuleKind KindOf(IEnumerable<INamedTypeSymbol> symbols)
    {
        foreach (var s in symbols)
            foreach (var a in s.GetAttributes())
                switch (a.AttributeClass?.Name)
                {
                    case "ScriptAttribute": return ModuleKind.Script;
                    case "LocalScriptAttribute": return ModuleKind.LocalScript;
                }
        return ModuleKind.Module;
    }
}
