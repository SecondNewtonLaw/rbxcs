using RobloxCS;

namespace Demo;

[Shared]
public struct Point {
    public int X;
    public int Y;

    public Point(int x, int y) {
        X = x;
        Y = y;
    }

    public int Sum() {
        return X + Y;
    }
}

[Shared]
public class StructTest {
    public int AliasCheck() {
        var a = new Point(1, 2);
        var b = a;
        b.X = 99;
        return a.X;
    }

    public int PassCheck(Point p) {
        p.X = 99;
        return p.X;
    }

    public int CallerAfterPass() {
        var a = new Point(5, 6);
        PassCheck(a);
        return a.X;
    }
}