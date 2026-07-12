using RobloxCS;
using System;
using System.Collections.Generic;

namespace Demo;

[Shared]
public class BclTest
{
    public int MathOps()
    {
        return (int)Math.Floor(3.7) + Math.Max(2, 5) + Math.Abs(-4);
    }

    public string StringOps()
    {
        var s = "Hello";
        return s.ToUpper() + ":" + s.Length + ":" + s.Substring(1, 3);
    }

    public int ListOps()
    {
        var list = new List<int>();
        list.Add(10);
        list.Add(20);
        list.Add(30);
        var sum = 0;
        foreach (var x in list)
            sum += x;
        return sum + list.Count + list[0];
    }
}
