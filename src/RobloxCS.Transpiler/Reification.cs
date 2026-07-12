using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RobloxCS.Transpiler;

// Selective reification (decision #5): generics erase by default. A generic method that inspects a
// type parameter at runtime -- default(T), typeof(T), new T(), `x is T` -- gets hidden type-token
// params threaded in, passed at each call site. This pass finds those methods across the compilation.
public sealed class Reification
{
    private readonly HashSet<IMethodSymbol> _needsTokens = new(SymbolEqualityComparer.Default);

    public bool NeedsTokens(IMethodSymbol method) => _needsTokens.Contains(method.OriginalDefinition);

    public static Reification Build(CSharpCompilation compilation)
    {
        var r = new Reification();
        foreach (var tree in compilation.SyntaxTrees)
        {
            var model = compilation.GetSemanticModel(tree);
            foreach (var method in tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>())
            {
                if (method.TypeParameterList is null || model.GetDeclaredSymbol(method) is not { } sym)
                    continue;
                if (UsesTypeParamReifyingly(method, model, sym))
                    r._needsTokens.Add(sym.OriginalDefinition);
            }
        }
        return r;
    }

    private static bool UsesTypeParamReifyingly(MethodDeclarationSyntax method, SemanticModel model, IMethodSymbol sym)
    {
        foreach (var node in method.DescendantNodes())
        {
            ITypeSymbol? probed = node switch
            {
                DefaultExpressionSyntax d => model.GetTypeInfo(d.Type).Type,
                TypeOfExpressionSyntax t => model.GetTypeInfo(t.Type).Type,
                ObjectCreationExpressionSyntax o => model.GetTypeInfo(o).Type,
                LiteralExpressionSyntax { RawKind: (int)SyntaxKind.DefaultLiteralExpression } lit => model.GetTypeInfo(lit).Type,
                BinaryExpressionSyntax { RawKind: (int)SyntaxKind.IsExpression } b => model.GetTypeInfo(b.Right).Type,
                _ => null,
            };
            if (IsOwnTypeParam(probed, sym))
                return true;

            if (node is IsPatternExpressionSyntax { Pattern: DeclarationPatternSyntax dp }
                && IsOwnTypeParam(model.GetTypeInfo(dp.Type).Type, sym))
                return true;
            if (node is IsPatternExpressionSyntax { Pattern: TypePatternSyntax tp }
                && IsOwnTypeParam(model.GetTypeInfo(tp.Type).Type, sym))
                return true;
        }
        return false;
    }

    private static bool IsOwnTypeParam(ITypeSymbol? t, IMethodSymbol method) =>
        t is ITypeParameterSymbol tp && SymbolEqualityComparer.Default.Equals(tp.DeclaringMethod?.OriginalDefinition, method.OriginalDefinition);
}
