using Xunit;
using Gen = RobloxCS.Bindgen.Bindgen;

namespace RobloxCS.Tests;

public class BindgenFacts
{
    private static string Bind(string luau, string name, bool wally = false, string? alias = null)
    {
        var dir = Directory.CreateTempSubdirectory("rbxcs-bind").FullName;
        var path = Path.Combine(dir, name + ".luau");
        File.WriteAllText(path, luau);
        try { return Gen.Generate(path, "Test", "", wally, alias).Code; }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Metatable_class_becomes_a_class()
    {
        var code = Bind(
            "local Signal = {}\n" +
            "Signal.__index = Signal\n" +
            "function Signal.new(name: string) return setmetatable({}, Signal) end\n" +
            "function Signal:Fire(...: any) end\n" +
            "return Signal", "Signal");
        Assert.Contains("class Signal", code);
        Assert.Contains("public Signal(", code);
        Assert.Contains("Fire", code);
    }

    [Fact]
    public void Static_module_becomes_a_static_class()
    {
        var code = Bind(
            "local MathX = {}\n" +
            "function MathX.add(a: number, b: number): number return a + b end\n" +
            "return MathX", "MathX");
        Assert.Contains("public static class MathX", code);
        Assert.Contains("add", code);
    }

    [Fact]
    public void Wally_package_gets_the_attribute()
    {
        var code = Bind(
            "local Promise = {}\n" +
            "function Promise.resolve(v: any) return Promise end\n" +
            "return Promise", "Promise", wally: true, alias: "Promise");
        Assert.Contains("WallyPackage", code);
    }
}
