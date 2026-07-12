using RobloxCS;
using Vendor;

namespace Demo;

// Exercises the rbxcs-generated binding (Pkg.g.cs) so the emitted requires + calls are verified.
[Shared]
public class BindTest
{
    public double Total()
    {
        var s = Pkg.MathX.add(1, 2);
        var c = Pkg.MathX.clamp(s, 0, 10);
        return c + Pkg.MathX.PI + Pkg.MathX.sum(1, 2, 3);
    }

    public string[] Keys()
    {
        return Pkg.Store.keys();
    }
}
