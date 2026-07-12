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
        // `checked` has no cheap Luau overflow detection (numbers are f64); `unchecked` is the default
        // behavior and lowers faithfully, so it is not flagged.
        context.RegisterSyntaxNodeAction(c => Report(c, "checked (integer overflow detection)"), SyntaxKind.CheckedExpression);
        context.RegisterSyntaxNodeAction(c => Report(c, "checked (integer overflow detection)"), SyntaxKind.CheckedStatement);
        context.RegisterSyntaxNodeAction(ReportThread, SyntaxKind.ObjectCreationExpression);
    }

    // Roblox has no shared-memory preemptive threads. Actors are message-passing, isolated VMs — a
    // faithful System.Threading.Thread is impossible; steer to the [Actor] + [Parallel] model.
    private static void ReportThread(SyntaxNodeAnalysisContext context)
    {
        var oc = (ObjectCreationExpressionSyntax)context.Node;
        if (context.SemanticModel.GetTypeInfo(oc).Type?.ToDisplayString() == "System.Threading.Thread")
            Report(context, "System.Threading.Thread (Roblox has no shared-memory threads; use [Actor] + [Parallel] instead)");
    }

    private static void Report(SyntaxNodeAnalysisContext context, string what) =>
        context.ReportDiagnostic(Diagnostic.Create(Unsupported, context.Node.GetLocation(), what));
}
