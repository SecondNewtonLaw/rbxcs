using RobloxCS;

namespace Demo;

[Shared]
public class Correctness
{
    // Locals + params named as Luau reserved words must be escaped (end/and/or/local/function...).
    public int Keywords(int end, int and)
    {
        int local = end + and;
        int function = local * 2;
        for (int repeat = 0; repeat < function; repeat++)
            local += repeat;
        return local;
    }

    // C# integer division truncates toward zero; remainder sign follows the dividend.
    public int IntMath()
    {
        int a = 7 / 2;         // 3, not 3.5
        int b = -7 / 2;        // -3 (toward zero), not -4
        int c = 7 % 3;         // 1
        int d = -7 % 3;        // -1 (dividend sign)
        a /= 2;                // 1
        c %= 2;                // 1
        return a + b + c + d;
    }

    // Unsigned integer / and % are exact with native Luau // and %.
    public uint UnsignedMath(uint a, uint b)
    {
        uint q = a / b;
        uint r = a % b;
        q /= 2;
        return q + r;
    }

    // continue is native Luau; increment must still run after it (non-canonical loop desugars safely).
    public int Continues()
    {
        int sum = 0;
        for (int i = 0; i < 10; i++)
        {
            if (i % 2 == 0)
                continue;
            sum += i;
        }
        return sum;
    }

    // Numeric suffixes + underscores; verbatim + escaped strings.
    public double Literals()
    {
        long big = 1_000_000L;
        float f = 2.5f;
        double d = 3.0d;
        int hex = 0xFF;
        return big + f + d + hex;
    }

    public string Strings()
    {
        string path = @"C:\temp\x";   // verbatim: literal backslashes
        string q = "he said \"hi\"\n";
        char ch = 'A';
        return path + q + ch;
    }
}
