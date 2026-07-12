using RobloxCS;
using Roblox;

namespace Demo;

// Uses the bindgen-generated Signal class (Signal.g.cs) — a metatable-OOP Wally package modeled as a
// real C# class: `new Signal(...)` -> require(...).new(...), instance methods -> `sig:Method(...)`.
[Shared]
public class WallyTest
{
    public Signal MakeSignal()
    {
        var sig = new Signal("evt");
        sig.Connect(v => { });
        sig.Fire(1, 2);
        sig.Destroy();
        return Signal.wrap(default!);
    }
}
