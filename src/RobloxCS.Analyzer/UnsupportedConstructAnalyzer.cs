using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace RobloxCS.Analyzer;

// Flags C# that has no faithful Luau lowering, live in the editor (decision: F4 analyzer).
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UnsupportedConstructAnalyzer : DiagnosticAnalyzer
{
    public static readonly DiagnosticDescriptor Unsupported = new(
        id: "RBXCS001",
        title: "Unsupported C# construct",
        messageFormat: "'{0}' has no Luau equivalent and cannot be transpiled by rbxcs",
        category: "rbxcs",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Unsupported);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);

        context.RegisterSyntaxNodeAction(c => Report(c, "goto"), SyntaxKind.GotoStatement);
        context.RegisterSyntaxNodeAction(c => Report(c, "lock"), SyntaxKind.LockStatement);
        context.RegisterSyntaxNodeAction(c => Report(c, "stackalloc"), SyntaxKind.StackAllocArrayCreationExpression);
        context.RegisterSyntaxNodeAction(c => Report(c, "fixed"), SyntaxKind.FixedStatement);
        context.RegisterSyntaxNodeAction(c => Report(c, "unsafe"), SyntaxKind.UnsafeStatement);
        context.RegisterSyntaxNodeAction(c => Report(c, "pointer type"), SyntaxKind.PointerType);
    }

    private static void Report(SyntaxNodeAnalysisContext context, string what) =>
        context.ReportDiagnostic(Diagnostic.Create(Unsupported, context.Node.GetLocation(), what));
}
