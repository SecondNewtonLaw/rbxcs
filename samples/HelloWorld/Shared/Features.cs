using RobloxCS;
using System;

namespace Demo;

public enum Color
{
    Red,
    Green = 5,
    Blue,
}

public interface IShape
{
    int Area();
}

[Shared]
public class Square : IShape
{
    private int _s;

    public Square(int s)
    {
        _s = s;
    }

    public int Area()
    {
        return _s * _s;
    }
}

[Shared]
public class FeatureTest
{
    public int ColorValue()
    {
        return (int)Color.Blue;
    }

    public int InterfaceDispatch()
    {
        IShape shape = new Square(4);
        return shape.Area();
    }

    public int LambdaAndDelegate()
    {
        Func<int, int> f = x => x + 1;
        return f(10);
    }

    public int NullCoalesce(string? maybe)
    {
        var s = maybe ?? "def";
        return s.Length;
    }

    public int NullConditional(Square? sq)
    {
        return sq?.Area() ?? -1;
    }
}
