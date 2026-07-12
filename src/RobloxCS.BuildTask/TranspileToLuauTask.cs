using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using RobloxCS.Transpiler;

namespace RobloxCS.BuildTask;

// Builds a real CSharpCompilation from the project's sources + resolved references (decision #2),
// runs the transpiler, and emits the rbxcs-owned Rojo tree (decision #9).
public sealed class TranspileToLuauTask : Microsoft.Build.Utilities.Task
{
    [Required] public ITaskItem[] SourceFiles { get; set; } = [];

    [Required] public ITaskItem[] ReferencePaths { get; set; } = [];

    [Required] public string OutputDirectory { get; set; } = "";

    [Required] public string RuntimeDirectory { get; set; } = "";

    // Hand-written .luau files in the project, copied verbatim into the tree (mirroring their path).
    public ITaskItem[] LuauContentFiles { get; set; } = [];

    public string ProjectDirectory { get; set; } = "";

    [Output] public ITaskItem[] GeneratedFiles { get; set; } = [];

    public override bool Execute()
    {
        try
        {
            return Run();
        }
        catch (Exception ex)
        {
            Log.LogError($"rbxcs: transpile failed: {ex.Message}\n{ex.StackTrace}");
            return false;
        }
    }

    private bool Run()
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        var trees = new List<SyntaxTree>();
        foreach (var item in SourceFiles)
        {
            var path = item.GetMetadata("FullPath");
            if (File.Exists(path))
                trees.Add(CSharpSyntaxTree.ParseText(File.ReadAllText(path), parseOptions, path));
        }

        var refs = ReferencePaths
            .Select(r => r.GetMetadata("FullPath"))
            .Where(File.Exists)
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .ToList();

        var compilation = CSharpCompilation.Create(
            "rbxcs.compiled",
            trees,
            refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var result = Transpiler.Transpiler.Transpile(compilation);

        Directory.CreateDirectory(OutputDirectory);
        var generated = new List<ITaskItem>();

        foreach (var m in result.Modules)
        {
            var suffix = m.Kind switch
            {
                ModuleKind.Script => ".server.luau",
                ModuleKind.LocalScript => ".client.luau",
                _ => ".luau",
            };
            string outPath;
            if (m.IsActor)
            {
                // Rojo: a dir with init.meta.json {"className":"Actor"} becomes an Actor instance; the
                // script inside runs in its own Luau VM (the only place task.desynchronize is legal).
                var actorDir = Path.Combine(OutputDirectory, m.RelativePath.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(actorDir);
                File.WriteAllText(Path.Combine(actorDir, "init.meta.json"), "{\n  \"className\": \"Actor\"\n}\n");
                outPath = Path.Combine(actorDir, m.ModuleName + suffix);
            }
            else
            {
                outPath = Path.Combine(OutputDirectory, m.RelativePath.Replace('/', Path.DirectorySeparatorChar) + suffix);
                Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
            }
            File.WriteAllText(outPath, m.Luau);
            generated.Add(new TaskItem(outPath));
            Log.LogMessage(MessageImportance.Normal, $"rbxcs: {m.RelativePath} [{m.Kind}{(m.IsActor ? " Actor" : "")}]");
        }

        foreach (var d in result.Diagnostics)
            Log.LogWarning("rbxcs", "RBXCS100", "", d.FilePath, d.Line, d.Column, d.Line, d.Column, d.Message);

        CopyRuntime();
        CopyLuauContent(generated);
        File.WriteAllText(Path.Combine(OutputDirectory, "default.project.json"), result.RojoProjectJson);

        GeneratedFiles = generated.ToArray();
        Log.LogMessage(MessageImportance.High,
            $"rbxcs: emitted {generated.Count} module(s) + runtime to {OutputDirectory}");
        return !Log.HasLoggedErrors;
    }

    // Runtime lands under Shared/runtime -> ReplicatedStorage.rbxcs.runtime.
    private void CopyRuntime()
    {
        if (!Directory.Exists(RuntimeDirectory))
        {
            Log.LogWarning($"rbxcs: runtime dir not found: {RuntimeDirectory}");
            return;
        }
        var dest = Path.Combine(OutputDirectory, "Shared", "runtime");
        Directory.CreateDirectory(dest);
        foreach (var file in Directory.GetFiles(RuntimeDirectory, "*.luau"))
            File.Copy(file, Path.Combine(dest, Path.GetFileName(file)), overwrite: true);
    }

    // Hand-written .luau files -> copied verbatim into the tree at their project-relative path, so
    // they mount alongside transpiled modules (and can require them / be required by them).
    private void CopyLuauContent(List<ITaskItem> generated)
    {
        if (LuauContentFiles.Length == 0 || string.IsNullOrEmpty(ProjectDirectory))
            return;
        var outRoot = Path.GetFullPath(OutputDirectory);
        foreach (var item in LuauContentFiles)
        {
            var src = item.GetMetadata("FullPath");
            if (!File.Exists(src) || Path.GetFullPath(src).StartsWith(outRoot, StringComparison.OrdinalIgnoreCase))
                continue; // skip emitted output
            var rel = MakeRelative(ProjectDirectory, src);
            var dest = Path.Combine(OutputDirectory, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(src, dest, overwrite: true);
            generated.Add(new TaskItem(dest));
            Log.LogMessage(MessageImportance.Normal, $"rbxcs: copied luau {rel}");
        }
    }

    private static string MakeRelative(string root, string full)
    {
        var rootSlash = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var f = Path.GetFullPath(full);
        return f.StartsWith(rootSlash, StringComparison.OrdinalIgnoreCase) ? f.Substring(rootSlash.Length) : Path.GetFileName(f);
    }
}
