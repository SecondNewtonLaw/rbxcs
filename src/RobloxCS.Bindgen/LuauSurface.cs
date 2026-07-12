namespace RobloxCS.Bindgen;

// The C#-visible surface extracted from one Luau module: methods + properties, plus any nested
// modules re-exported through an index (init.luau `return { X = require(...) }`).
public sealed class LuauModule
{
    public string CsName = "";           // C# identifier for the (nested) class
    public bool IsClass;                 // metatable OOP module -> emit a real class, not a static one
    public string? BindingPath;          // [LuauModule] folder-relative path; null for a pure index namespace
    public BindingKind Kind = BindingKind.LocalModule;
    public string? WallyAlias;           // set when Kind == Wally
    public readonly List<LuauMember> Members = new();
    public readonly List<LuauModule> Nested = new();
}

public enum BindingKind { LocalModule, Wally }

public enum MemberKind { Method, Property, Constructor }

public sealed class LuauMember
{
    public MemberKind Kind;
    public bool IsStatic = true;         // false for instance (`:`) methods / instance fields of a class
    public string LuauName = "";
    public string CsName = "";
    public string CsType = "object";     // property type, or method return type (ignored for Constructor)
    public readonly List<LuauParam> Params = new();
}

public sealed class LuauParam
{
    public string Name = "";
    public string CsType = "object";
    public bool IsParams;                // Luau `...` -> C# params object[]
}
