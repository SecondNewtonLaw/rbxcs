namespace RobloxCS.Cli;

// rbxcs CLI. Currently: `add-package` — scaffold [WallyPackage] C# stubs from wally.toml.
internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length == 0)
            return Usage();

        return args[0] switch
        {
            "add-package" => AddPackage.Run(args.Skip(1).ToArray()),
            "bind" => Bind.Run(args.Skip(1).ToArray()),
            "-h" or "--help" or "help" => Usage(),
            _ => Usage($"unknown command '{args[0]}'"),
        };
    }

    private static int Usage(string? error = null)
    {
        if (error is not null)
            Console.Error.WriteLine($"rbxcs: {error}\n");
        Console.WriteLine(
            """
            rbxcs — C#-to-Luau for Roblox

            usage:
              rbxcs add-package [alias] [--output <dir>] [--namespace <ns>] [--packages <dir>] [--force]
                  Generate typed [WallyPackage] bindings from wally.toml + installed Luau source.
                  No alias -> every dependency. Skips existing files unless --force. Stubs if not installed.
                  Defaults: --output Bindings  --namespace Packages  --packages Packages

              rbxcs bind <path> [--module <BasePath>] [--namespace <ns>] [--out <file>] [--wally [alias]]
                  Parse a .luau file or package folder (init.luau) and emit a typed C# binding.
                  Follows the init.luau require chain one level (re-exports -> nested classes).
                  --module: [LuauModule] base path (default Shared/<name>). --wally: emit [WallyPackage].
                  Defaults: --namespace Packages  --out Bindings/<Name>.g.cs
            """);
        return error is null ? 0 : 1;
    }
}
