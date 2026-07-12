using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RobloxCS.Luau;

namespace RobloxCS.Transpiler;

// Lowers one source file (module) into a Luau Chunk. See Transpiler for scope.
internal sealed partial class ModuleEmitter(SemanticModel model, ModuleMap map, ModuleInfo module, Reification reification, List<TranspileDiagnostic> diagnostics)
{
    private readonly Dictionary<string, string> _externalRequires = new(); // localName -> require expr
    private readonly HashSet<string> _thisModuleTypes = new();
    private string _currentType = "";

    // >0 while lowering a try body/catch: `return` becomes a control marker (see RBXCS.try).
    private int _guardDepth;
    private const string ExVar = "__ex";

    // Type params of the method being lowered whose runtime token is available as `__t_<Name>`.
    private HashSet<string> _tokenParams = new();

    // Non-null while lowering an iterator method body: the Luau param `yield return` calls.
    private string? _yieldVar;

    public ModuleResult Emit(List<TypeDeclarationSyntax> types)
    {
        var symbols = types
            .Select(c => (decl: c, sym: model.GetDeclaredSymbol(c)!))
            .Where(x => x.sym is not null)
            .OrderBy(x => InheritanceDepth(x.sym))
            .ToList();

        foreach (var (_, sym) in symbols)
            _thisModuleTypes.Add(sym.Name);

        var enums = model.SyntaxTree.GetRoot().DescendantNodes().OfType<EnumDeclarationSyntax>().ToList();
        foreach (var e in enums)
            _thisModuleTypes.Add(e.Identifier.Text);

        var classStatements = new List<Statement>();
        foreach (var e in enums)
            classStatements.Add(EmitEnum(e));
        foreach (var (decl, sym) in symbols)
        {
            _currentType = sym.Name;
            LowerType(decl, sym, classStatements);
        }

        var chunk = new Chunk();
        chunk.Statements.Add(new RawStatement($"local RBXCS = {map.RuntimeRequire}"));
        foreach (var kv in _externalRequires)
            chunk.Statements.Add(new RawStatement($"local {kv.Key} = {kv.Value}"));
        chunk.Statements.AddRange(classStatements);

        if (module.Kind == ModuleKind.Module)
        {
            // export public types
            var entries = symbols
                .Where(x => x.sym.DeclaredAccessibility == Accessibility.Public)
                .Select(x => new TableEntry(x.sym.Name, new Identifier(x.sym.Name)))
                .ToList();
            entries.AddRange(enums.Select(e => new TableEntry(e.Identifier.Text, new Identifier(e.Identifier.Text))));
            chunk.Statements.Add(new Return(new TableConstructor(entries)));
        }
        else
        {
            // entry script: run the entry type's static Main()
            var entry = symbols.FirstOrDefault(x =>
                x.sym.GetAttributes().Any(a => a.AttributeClass?.Name is "ScriptAttribute" or "LocalScriptAttribute"));
            if (entry.sym is not null && entry.sym.GetMembers("Main").Any())
                chunk.Statements.Add(new ExpressionStatement(
                    new Call(new MemberAccess(new Identifier(entry.sym.Name), "Main"), Array.Empty<Expression>())));
        }

        return new ModuleResult(module.RelativePath, module.Kind, LuauWriter.Write(chunk), module.ModuleName, module.IsActor);
    }
}
