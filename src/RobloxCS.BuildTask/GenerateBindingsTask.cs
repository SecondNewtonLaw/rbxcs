using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using RobloxCS.Bindgen;

namespace RobloxCS.BuildTask;

// Generates typed C# bindings for hand-written Luau / Wally packages at build time, into obj/
// (gitignored), so they are never committed. One RobloxCSBinding item per package; metadata:
//   Namespace (C# namespace), Wally (true -> [WallyPackage]), Alias (wally alias), ModuleBase
//   (project-relative mount for [LuauModule] require paths).
public sealed class GenerateBindingsTask : Microsoft.Build.Utilities.Task
{
    [Required] public ITaskItem[] Bindings { get; set; } = [];

    [Required] public string OutputDirectory { get; set; } = "";

    [Output] public ITaskItem[] GeneratedFiles { get; set; } = [];

    public override bool Execute()
    {
        try
        {
            Directory.CreateDirectory(OutputDirectory);
            var generated = new List<ITaskItem>();
            foreach (var b in Bindings)
            {
                var path = b.GetMetadata("FullPath");
                if (!File.Exists(path) && !Directory.Exists(path))
                {
                    Log.LogWarning($"rbxcs bind: path not found: {path}");
                    continue;
                }
                var ns = Meta(b, "Namespace", "RobloxCS.Bindings");
                var wally = Meta(b, "Wally", "").Equals("true", StringComparison.OrdinalIgnoreCase);
                var alias = Meta(b, "Alias", "");
                var moduleBase = Meta(b, "ModuleBase", "");

                var result = Bindgen.Bindgen.Generate(path, ns, moduleBase, wally, alias.Length == 0 ? null : alias);
                foreach (var note in result.Notes)
                    Log.LogMessage(MessageImportance.Normal, $"rbxcs bind: {note}");
                if (string.IsNullOrEmpty(result.Code))
                {
                    Log.LogWarning($"rbxcs bind: no binding produced for {path}");
                    continue;
                }

                var name = alias.Length > 0 ? alias : PackageName(path);
                var outPath = Path.Combine(OutputDirectory, name + ".g.cs");
                File.WriteAllText(outPath, result.Code);
                generated.Add(new TaskItem(outPath));
                Log.LogMessage(MessageImportance.Normal, $"rbxcs bind: {name} -> {outPath}");
            }
            GeneratedFiles = generated.ToArray();
            return !Log.HasLoggedErrors;
        }
        catch (Exception ex)
        {
            Log.LogError($"rbxcs bind failed: {ex.Message}\n{ex.StackTrace}");
            return false;
        }
    }

    private static string Meta(ITaskItem item, string name, string fallback)
    {
        var v = item.GetMetadata(name);
        return string.IsNullOrEmpty(v) ? fallback : v;
    }

    private static string PackageName(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path.TrimEnd('/', '\\'));
        return name is "init" or "" ? new DirectoryInfo(Path.GetDirectoryName(path.TrimEnd('/', '\\'))!).Name : name;
    }
}
