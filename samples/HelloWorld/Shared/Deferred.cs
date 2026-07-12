using RobloxCS;
using System.Collections.Generic;
using System.Linq;

namespace Demo;

public delegate void Notify(int value);

[Shared]
public class DeferredTest
{
    public int DictForeach()
    {
        var d = new Dictionary<string, int>();
        d["a"] = 10;
        d["b"] = 20;
        var sum = 0;
        foreach (var kv in d)
            sum += kv.Value;
        return sum;
    }

    public int HashSetOps()
    {
        var set = new HashSet<int>();
        set.Add(1);
        set.Add(1);
        set.Add(2);
        return set.Count + (set.Contains(2) ? 100 : 0);
    }

    public int NullableOps(int? x)
    {
        if (x.HasValue)
            return x.Value;
        return -1;
    }

    public string IsPattern(object o)
    {
        if (o is Square s)
            return "sq:" + s.Area();
        return "no";
    }

    public int LinqForeach()
    {
        var list = new List<int>();
        list.Add(1);
        list.Add(2);
        list.Add(3);
        list.Add(4);
        var sum = 0;
        foreach (var x in list.Where(v => v > 2))
            sum += x;
        return sum;
    }

    public int CrossEnum()
    {
        return (int)Color.Green;
    }

    // iterator method (yield return) -> lazy IEnumerable, consumable by LINQ
    public IEnumerable<int> Squares(int n)
    {
        for (int i = 0; i < n; i++)
            yield return i * i;
    }

    public int IteratorLinq()
    {
        return Squares(4).Where(x => x > 0).Sum();
    }

    // non-canonical for (multi-declarator) with `continue` — increment must still run
    public int SkipEvens(int n)
    {
        var sum = 0;
        for (int i = 0, unused = 0; i < n; i++)
        {
            if (i % 2 == 0)
                continue;
            sum += i;
        }
        return sum;
    }
}

[Shared]
public class Publisher
{
    public event Notify OnPublish;
    public int Total;

    public void Subscribe()
    {
        OnPublish += v => { Total += v; };
    }

    public void Publish(int v)
    {
        OnPublish.Invoke(v);
    }
}
