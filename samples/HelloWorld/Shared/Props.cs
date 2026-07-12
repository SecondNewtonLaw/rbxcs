using RobloxCS;

namespace Demo;

[Shared]
public class Rect
{
    public int W;
    public int H;

    public Rect(int w, int h)
    {
        W = w;
        H = h;
    }

    public int Area => W * H;

    public int Scale
    {
        get { return W; }
        set { W = value; H = value; }
    }
}

[Shared]
public class Grid
{
    private int _a;
    private int _b;

    public int this[int i]
    {
        get { return i == 0 ? _a : _b; }
        set
        {
            if (i == 0)
                _a = value;
            else
                _b = value;
        }
    }
}

[Shared]
public class PropTest
{
    public int AreaOf()
    {
        var r = new Rect(3, 4);
        return r.Area;
    }

    public int ScaleRoundTrip()
    {
        var r = new Rect(3, 4);
        r.Scale = 10;
        return r.Area;
    }

    public int IndexRoundTrip()
    {
        var g = new Grid();
        g[0] = 7;
        g[1] = 9;
        return g[0] + g[1];
    }
}
