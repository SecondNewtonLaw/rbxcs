using System.Text;

namespace RobloxCS.Luau;

// ponytail: recursive string writer. Fine until output size/perf matters; swap for a
// pooled StringBuilder + span formatting only if profiling says so.
public sealed class LuauWriter
{
    private readonly StringBuilder _sb = new();
    private int _indent;

    public static string Write(Chunk chunk)
    {
        var w = new LuauWriter();
        w.WriteChunk(chunk);
        return w._sb.ToString();
    }

    private void WriteChunk(Chunk chunk)
    {
        foreach (var stmt in chunk.Statements)
            WriteStatement(stmt);
    }

    private void WriteStatement(Statement stmt)
    {
        switch (stmt)
        {
            case LocalDeclaration d:
                Line(d.Value is null
                    ? $"local {d.Name}"
                    : $"local {d.Name} = {Expr(d.Value)}");
                break;
            case ExpressionStatement e:
                Line(Expr(e.Expression));
                break;
            case Return r:
                Line(r.Value is null ? "return" : $"return {Expr(r.Value)}");
                break;
            case Assignment a:
                Line($"{Expr(a.Target)} = {Expr(a.Value)}");
                break;
            case CompoundAssignment ca:
                Line($"{Expr(ca.Target)} {ca.Op}= {Expr(ca.Value)}");
                break;
            case RawStatement raw:
                Line(raw.Text);
                break;
            case FunctionStatement fs:
                WriteFunctionStatement(fs);
                break;
            case IfStatement ifs:
                WriteIf(ifs);
                break;
            case WhileStatement w:
                Line($"while {Expr(w.Condition)} do");
                Indented(w.Body);
                Line("end");
                break;
            case RepeatStatement rep:
                Line("repeat");
                Indented(rep.Body);
                Line($"until {Expr(rep.Until)}");
                break;
            case NumericFor nf:
                var step = nf.Step is null ? "" : $", {Expr(nf.Step)}";
                Line($"for {nf.Variable} = {Expr(nf.Start)}, {Expr(nf.Stop)}{step} do");
                Indented(nf.Body);
                Line("end");
                break;
            case GenericFor gf:
                Line($"for {string.Join(", ", gf.Variables)} in {Expr(gf.Iter)} do");
                Indented(gf.Body);
                Line("end");
                break;
            case Break:
                Line("break");
                break;
            case Continue:
                Line("continue");
                break;
            case DoBlock db:
                Line("do");
                Indented(db.Body);
                Line("end");
                break;
            default:
                throw new NotSupportedException($"statement {stmt.GetType().Name}");
        }
    }

    private void WriteFunctionStatement(FunctionStatement fs)
    {
        var kw = fs.Local ? "local function" : "function";
        string name;
        if (fs.IsMethod)
        {
            // last segment binds with ':'; the rest with '.'
            var head = string.Join(".", fs.NameChain.Take(fs.NameChain.Count - 1));
            name = $"{head}:{fs.NameChain[fs.NameChain.Count - 1]}";
        }
        else
        {
            name = string.Join(".", fs.NameChain);
        }
        Line($"{kw} {name}({string.Join(", ", fs.Parameters)})");
        _indent++;
        WriteChunk(fs.Body);
        _indent--;
        Line("end");
    }

    private void WriteIf(IfStatement ifs)
    {
        for (var i = 0; i < ifs.Branches.Count; i++)
        {
            var b = ifs.Branches[i];
            Line($"{(i == 0 ? "if" : "elseif")} {Expr(b.Condition)} then");
            Indented(b.Body);
        }
        if (ifs.ElseBody is not null)
        {
            Line("else");
            Indented(ifs.ElseBody);
        }
        Line("end");
    }

    private void Indented(Chunk body)
    {
        _indent++;
        WriteChunk(body);
        _indent--;
    }

    private string Expr(Expression expr) => expr switch
    {
        Literal l => l.Raw,
        Identifier i => i.Name,
        RawExpression r => r.Text,
        MemberAccess m => $"{Expr(m.Target)}.{m.Member}",
        IndexAccess ix => $"{Expr(ix.Target)}[{Expr(ix.Index)}]",
        Call c => $"{Expr(c.Callee)}({string.Join(", ", c.Arguments.Select(Expr))})",
        MethodCall mc => $"{Expr(mc.Receiver)}:{mc.Method}({string.Join(", ", mc.Arguments.Select(Expr))})",
        Binary b => $"{Expr(b.Left)} {b.Op} {Expr(b.Right)}",
        Unary u => u.Op == "not" ? $"not {Expr(u.Operand)}" : $"{u.Op}{Expr(u.Operand)}",
        Paren p => $"({Expr(p.Inner)})",
        IfExpression ie => $"(if {Expr(ie.Condition)} then {Expr(ie.Then)} else {Expr(ie.Else)})",
        InterpolatedString s => WriteInterpolated(s),
        FunctionExpression fe => WriteFunctionExpression(fe),
        TableConstructor t => WriteTable(t),
        _ => throw new NotSupportedException($"expression {expr.GetType().Name}")
    };

    private string WriteInterpolated(InterpolatedString s)
    {
        var sb = new StringBuilder("`");
        foreach (var p in s.Parts)
        {
            if (p.Expression is not null)
                sb.Append('{').Append(Expr(p.Expression)).Append('}');
            else
                // escape Luau interpolation metacharacters in literal text
                sb.Append((p.Text ?? "").Replace("`", "\\`").Replace("{", "\\{").Replace("}", "\\}"));
        }
        return sb.Append('`').ToString();
    }

    private string WriteFunctionExpression(FunctionExpression fe)
    {
        // Inline function values render on one logical block; reuse the statement writer via a temp.
        var inner = new LuauWriter { _indent = _indent + 1 };
        inner.WriteChunk(fe.Body);
        var body = inner._sb.ToString();
        var pad = new string('\t', _indent);
        return $"function({string.Join(", ", fe.Parameters)})\n{body}{pad}end";
    }

    private string WriteTable(TableConstructor t)
    {
        if (t.Entries.Count == 0)
            return "{}";
        var parts = t.Entries.Select(e => e.Key is null ? Expr(e.Value) : $"{e.Key} = {Expr(e.Value)}");
        return "{ " + string.Join(", ", parts) + " }";
    }

    private void Line(string text) => _sb.Append(new string('\t', _indent)).Append(text).Append('\n');
}
