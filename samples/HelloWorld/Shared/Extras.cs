using RobloxCS;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Roblox;

namespace Demo;

[Shared]
public class ExtrasTest
{
    public int LinqSum()
    {
        var list = new List<int>();
        list.Add(1);
        list.Add(2);
        list.Add(3);
        list.Add(4);
        return list.Where(x => x % 2 == 0).Select(x => x * 10).Sum();
    }

    public int LinqCount()
    {
        var list = new List<int>();
        list.Add(5);
        list.Add(6);
        list.Add(7);
        return list.Where(x => x > 5).Count();
    }

    public int DictOps()
    {
        var d = new Dictionary<string, int>();
        d["a"] = 1;
        d.Add("b", 2);
        var total = d["a"] + d["b"];
        return total + d.Count + (d.ContainsKey("a") ? 100 : 0);
    }

    public async Task<int> One() => 1;
    public async Task<int> Two() => 2;
    public async Task<int> Three() => 3;

    public async Task<int> Combinators()
    {
        var results = await Task.WhenAll(One(), Two(), Three());
        return results[0] + results[1] + results[2];
    }

    public double VectorMath()
    {
        var v = new Vector3(1, 2, 3);
        return v.X + v.Y;
    }
}
