using RobloxCS;

namespace Demo;

// enum with a member named as a Luau keyword.
public enum Mode { And, Or, End }

[Shared]
public class Gaps
{
    // #2: field + property named as Luau keywords.
    private int end = 3;
    public int Do { get; set; }

    // #1: multiple declarators in one statement (b was previously dropped).
    public int MultiDecl()
    {
        int a = 1, b = 2, c = a + b;
        return a + b + c; // 6
    }

    // #4: compound assignment to a bodied property.
    private int _n;
    public int Counter
    {
        get => _n;
        set => _n = value;
    }

    public int Compound()
    {
        Counter = 10;
        Counter += 5;   // set_Counter(get_Counter() + 5)
        Counter *= 2;
        return Counter; // 30
    }

    // #2: enum member keyword + field/property keyword access.
    public int Members()
    {
        end += 1;
        Do = (int)Mode.End;
        return end + Do; // 4 + 2 = 6
    }

    // #3: char arithmetic + int/char casts.
    public int CharMath()
    {
        char ch = 'A';
        int code = ch + 1;        // 66
        int diff = 'z' - 'a';     // 25
        char up = (char)(code);   // 'B'
        int back = (int)up;       // 66
        return code + diff + back; // 66 + 25 + 66 = 157
    }
}
