using RobloxCS.Bindgen;

namespace RobloxCS.Cli;

// `rbxcs bind <path>` — parse a Luau module/package and emit a typed C# binding (.g.cs).
internal static class Bind
{
    public static int Run(string[] args)
    {
        if (args.Length == 0 || args[0].StartsWith('-'))
            return Fail("expected a path to a .luau file or a package folder");

        var path = args[0];
        string? module = null, ns = "Packages", output = null, wallyAlias = null;
        var wally = false;
        for (var i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--module" when i + 1 < args.Length: module = args[++i]; break;
                case "--namespace" when i + 1 < args.Length: ns = args[++i]; break;
                case "--out" when i + 1 < args.Length: output = args[++i]; break;
                case "--wally":
                    wally = true;
                    if (i + 1 < args.Length && !args[i + 1].StartsWith('-')) wallyAlias = args[++i];
                    break;
                default:
                    return Fail($"unknown option '{args[i]}'");
            }
        }

        var name = DeriveName(path, wallyAlias);
        module ??= "Shared/" + name;

        var result = Bindgen.Bindgen.Generate(path, ns, module, wally, wallyAlias);
        foreach (var note in result.Notes)
            Console.Error.WriteLine($"rbxcs bind: {note}");
        if (result.Code.Length == 0)
            return Fail("nothing generated");

        output ??= Path.Combine("Bindings", name + ".g.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllText(output, result.Code);
        Console.WriteLine($"wrote {output}");
        if (result.Unknowns.Count > 0)
            Console.WriteLine($"  {result.Unknowns.Count} unmapped type(s) -> object: {string.Join(", ", result.Unknowns)}");
        Console.WriteLine("rbxcs: review the binding, then use it from C#.");
        return 0;
    }

    private static string DeriveName(string path, string? wallyAlias)
    {
        if (!string.IsNullOrEmpty(wallyAlias))
            return wallyAlias!;
        var f = Path.GetFileNameWithoutExtension(path);
        if (!string.IsNullOrEmpty(f) && f != "init")
            return f;
        var dir = Directory.Exists(path) ? path : Path.GetDirectoryName(path) ?? ".";
        return new DirectoryInfo(dir).Name;
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine($"rbxcs bind: {message}");
        return 1;
    }
}
