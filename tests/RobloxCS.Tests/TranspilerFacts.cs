using Xunit;

namespace RobloxCS.Tests;

public class TranspilerFacts
{
    [Fact]
    public void Class_lowers_to_metatable()
    {
        Assert.Contains("RBXCS.class(\"Animal\"", Harness.Emit(
            "[Shared] public class Animal { public string Name = \"\"; }"));
    }

    [Fact]
    public void Signed_int_division_uses_idiv()
    {
        Assert.Contains("RBXCS.idiv", Harness.Emit(
            "[Shared] public class C { public int F(int a, int b) => a / b; }"));
    }

    [Fact]
    public void Unsigned_int_division_is_native_floor()
    {
        var luau = Harness.Emit("[Shared] public class C { public uint F(uint a, uint b) => a / b; }");
        Assert.Contains("//", luau);
        Assert.DoesNotContain("RBXCS.idiv", luau);
    }

    [Fact]
    public void Char_increment_stays_a_char()
    {
        Assert.Contains("string.char(string.byte", Harness.Emit(
            "[Shared] public class C { public int F() { char c = 'A'; c++; return (int)c; } }"));
    }

    [Fact]
    public void Parameterless_ToString_maps_to_tostring()
    {
        Assert.Contains("tostring(", Harness.Emit(
            "[Shared] public class C { public string F(int n) => n.ToString(); }"));
    }

    [Fact]
    public void ToString_with_format_maps_to_tostringf()
    {
        Assert.Contains("RBXCS.tostringf", Harness.Emit(
            "[Shared] public class C { public string F(int n) => n.ToString(\"X\"); }"));
    }

    [Fact]
    public void Interpolation_format_clause_maps_to_tostringf()
    {
        Assert.Contains("RBXCS.tostringf", Harness.Emit(
            "[Shared] public class C { public string F(double p) => $\"{p:F2}\"; }"));
    }

    [Fact]
    public void Bitwise_and_maps_to_bit32()
    {
        Assert.Contains("bit32.band", Harness.Emit(
            "[Shared] public class C { public int F(int a, int b) => a & b; }"));
    }

    [Fact]
    public void Parallel_method_body_is_wrapped()
    {
        Assert.Contains("RBXCS.parallel(", Harness.Emit(
            "[Shared] public class C { [Parallel] public void M() { var x = 1 + 1; } }"));
    }

    [Fact]
    public void ParallelFor_with_sharedtable_capture_fans_out()
    {
        var r = Harness.Full(
            "[Shared] public class C { public void M() { var t = new SharedTable(); ParallelLuau.For(0, 4, i => t.Increment(\"n\", 1)); } }");
        Assert.Contains("RBXCS.parallelFor", r.Modules[0].Luau);
        Assert.Contains(r.Modules, m => m.IsActor); // synthetic worker actor emitted
    }

    [Fact]
    public void ParallelFor_with_nonshared_capture_falls_back_and_warns()
    {
        var r = Harness.Full(
            "[Shared] public class C { public int M() { int acc = 0; ParallelLuau.For(0, 4, i => acc += i); return acc; } }");
        Assert.Contains("RBXCS.pfor", r.Modules[0].Luau);
        Assert.Contains(r.Diagnostics, d => d.Message.Contains("sequentially"));
    }

    [Fact]
    public void Parallel_body_touching_unsafe_member_is_flagged()
    {
        var r = Harness.Full(
            "[Shared] public class C { [Parallel] public void M(Part p) { p.Destroy(); } }");
        Assert.Contains(r.Diagnostics, d => d.Message.Contains("Unsafe"));
    }
}
