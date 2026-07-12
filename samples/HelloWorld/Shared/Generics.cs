using RobloxCS;

namespace Demo;

[Shared]
public class Box<T>
{
    public T Value;

    public Box(T value)
    {
        Value = value;
    }

    public T Get()
    {
        return Value;
    }
}

[Shared]
public class Generics
{
    public int BoxInt()
    {
        var b = new Box<int>(42);
        return b.Get();
    }

    public string BoxStr()
    {
        var b = new Box<string>("hi");
        return b.Get();
    }

    public T MakeDefault<T>()
    {
        return default(T);
    }

    public T MakeNew<T>() where T : new()
    {
        return new T();
    }

    public int DefaultInt()
    {
        return MakeDefault<int>();
    }

    public bool DefaultBool()
    {
        return MakeDefault<bool>();
    }

    public string Kind(object o)
    {
        if (o is Box<int>)
            return "box";
        return "other";
    }
}
