using Basic.Reference.Assemblies;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Transp = RobloxCS.Transpiler.Transpiler;
using RobloxCS.Transpiler;

namespace RobloxCS.Tests;

// Compiles a C# snippet against the real Defs reference + the BCL, runs the transpiler, and returns
// the emitted Luau / diagnostics — the whole pipeline, no mocks.
internal static class Harness
{
    private static readonly MetadataReference[] Refs = Net90.References.All
        .Append(MetadataReference.CreateFromFile(typeof(global::RobloxCS.ServerAttribute).Assembly.Location))
        .ToArray();

    private static TranspileResult Run(string body)
    {
        var src = "using RobloxCS; using Roblox; using System; using static Roblox.Globals;\nnamespace T;\n" + body;
        var tree = CSharpSyntaxTree.ParseText(src, new CSharpParseOptions(LanguageVersion.Latest));
        var comp = CSharpCompilation.Create("t", new[] { tree }, Refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        return Transp.Transpile(comp);
    }

    // Luau of the first emitted module (the type under test).
    public static string Emit(string body) => Run(body).Modules[0].Luau;

    public static TranspileResult Full(string body) => Run(body);
}
