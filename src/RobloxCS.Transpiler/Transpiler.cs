using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RobloxCS.Luau;

namespace RobloxCS.Transpiler;

public enum RbxContext { Server, Client, Shared }

public enum ModuleKind { Module, Script, LocalScript }

public sealed class ModuleResult(string relativePath, ModuleKind kind, string luau, string moduleName, bool isActor)
{
    public string RelativePath { get; } = relativePath; // e.g. "Shared/Demo/Animals"
    public ModuleKind Kind { get; } = kind;
    public string Luau { get; } = luau;
    public string ModuleName { get; } = moduleName; // last path segment; the emitted script's name
    public bool IsActor { get; } = isActor;         // mount the script beneath an Actor instance
}

public sealed class TranspileDiagnostic(string message, string filePath, int line, int column)
{
    public string Message { get; } = message;
    public string FilePath { get; } = filePath;
    public int Line { get; } = line;     // 1-based
    public int Column { get; } = column; // 1-based
}

public sealed class TranspileResult(
    IReadOnlyList<ModuleResult> modules, string rojoProjectJson, string projectName, IReadOnlyList<TranspileDiagnostic> diagnostics)
{
    public IReadOnlyList<ModuleResult> Modules { get; } = modules;
    public string RojoProjectJson { get; } = rojoProjectJson;
    public string ProjectName { get; } = projectName;
    public IReadOnlyList<TranspileDiagnostic> Diagnostics { get; } = diagnostics;
}

// SemanticModel-driven C# -> Luau lowering (decision #2). Output layout comes from ProjectConfig
// (a user ProjectDescriptor, or defaults).
public static class Transpiler
{
    public static TranspileResult Transpile(CSharpCompilation compilation)
    {
        var config = ProjectConfig.Resolve(compilation);
        var map = ModuleMap.Build(compilation, config);
        var reification = Reification.Build(compilation);
        var results = new List<ModuleResult>();
        var diagnostics = new List<TranspileDiagnostic>();

        foreach (var tree in compilation.SyntaxTrees)
        {
            var model = compilation.GetSemanticModel(tree);
            var hasEnum = tree.GetRoot().DescendantNodes().OfType<EnumDeclarationSyntax>().Any();
            var types = tree.GetRoot().DescendantNodes()
                .Where(n => n is ClassDeclarationSyntax or StructDeclarationSyntax)
                .Cast<TypeDeclarationSyntax>()
                .Where(t => model.GetDeclaredSymbol(t) is not { } s || !ProjectConfig.IsNonEmitted(s))
                .ToList();
            if (types.Count == 0 && !hasEnum)
                continue;
            var module = map.ForTree(tree);
            var emitter = new ModuleEmitter(model, map, module, reification, diagnostics);
            results.Add(emitter.Emit(types));
            results.AddRange(emitter.Synthetic); // Parallel.For worker modules

        }

        return new TranspileResult(results, config.RojoProjectJson(), config.ProjectName, diagnostics);
    }
}
