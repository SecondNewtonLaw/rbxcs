using RobloxCS;

namespace Demo;

[Shared]
public class Bitwise
{
    public int Masks()
    {
        int a = 0b1100;
        int b = 0b1010;
        int m1 = a & b;
        int m2 = a | b;
        int m3 = a ^ b;
        int m4 = a << 2;
        int m5 = a >> 1;
        int m6 = ~a;
        m1 &= 0b0100;
        m2 |= 1;
        m3 ^= 0b1111;
        m4 <<= 1;
        m5 >>= 1;
        return m1 + m2 + m3 + m4 + m5 + m6;
    }

    public bool Flags(bool x, bool y)
    {
        bool p = x & y;
        bool q = x | y;
        bool r = x ^ y;
        p &= true;
        return p | q | r;
    }
}
