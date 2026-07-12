using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RobloxCS.Luau;

namespace RobloxCS.Transpiler;

internal sealed partial class ModuleEmitter
{
    private Statement LowerStatement(StatementSyntax stmt) => stmt switch
    {
        LocalDeclarationStatementSyntax local => LowerLocal(local),
        ExpressionStatementSyntax expr => LowerExpressionStatement(expr),
        YieldStatementSyntax { RawKind: (int)SyntaxKind.YieldReturnStatement } y =>
            new ExpressionStatement(new Call(new Identifier(_yieldVar!), new[] { LowerExpr(y.Expression!) })),
        YieldStatementSyntax => new Return(null), // yield break
        ReturnStatementSyntax ret => LowerReturn(ret),
        ThrowStatementSyntax th => new ExpressionStatement(new Call(new Identifier("error"),
            new[] { th.Expression is null ? new Identifier(ExVar) : LowerExpr(th.Expression) })),
        TryStatementSyntax t => LowerTry(t),
        IfStatementSyntax i => LowerIf(i),
        WhileStatementSyntax w => new WhileStatement(LowerExpr(w.Condition), LowerBlock(w.Statement)),
        DoStatementSyntax d => new RepeatStatement(LowerBlock(d.Statement), new Unary("not", new Paren(LowerExpr(d.Condition)))),
        ForStatementSyntax f => LowerFor(f),
        ForEachStatementSyntax fe => new GenericFor(new[] { "_", fe.Identifier.Text }, LowerExpr(fe.Expression), LowerBlock(fe.Statement)),
        BreakStatementSyntax => new Break(),
        ContinueStatementSyntax => new Continue(),
        SwitchStatementSyntax s => LowerSwitch(s),
        BlockSyntax b => new DoBlock(LowerBlock(b)),
        _ => new RawStatement($"-- [rbxcs] unsupported statement: {stmt.Kind()}"),
    };

    private Statement LowerLocal(LocalDeclarationStatementSyntax local)
    {
        var v = local.Declaration.Variables[0]; // multi-declarator -> F-later
        var value = v.Initializer is null ? null : CopyIfNeeded(v.Initializer.Value, LowerExpr(v.Initializer.Value));
        return new LocalDeclaration(v.Identifier.Text, value);
    }

    // Normal `return` outside a guarded region; inside try/catch it becomes a control marker.
    private Statement LowerReturn(ReturnStatementSyntax ret)
    {
        var value = ret.Expression is null ? null : CopyIfNeeded(ret.Expression, LowerExpr(ret.Expression));
        if (_guardDepth == 0)
            return new Return(value);

        var values = value is null
            ? new List<TableEntry>()
            : new List<TableEntry> { new(null, value) };
        return new Return(new TableConstructor(new List<TableEntry>
        {
            new("kind", new Literal("\"ret\"")),
            new("values", new TableConstructor(values)),
        }));
    }

    private Statement LowerTry(TryStatementSyntax node)
    {
        _guardDepth++;
        var bodyFn = new FunctionExpression(Array.Empty<string>(), LowerBlock(node.Block));
        _guardDepth--;

        Expression catchArg = node.Catches.Count == 0 ? new RawExpression("nil") : BuildCatchFn(node);
        Expression finArg = node.Finally is null
            ? new RawExpression("nil")
            : new FunctionExpression(Array.Empty<string>(), LowerBlock(node.Finally.Block));

        var tryCall = new Call(new MemberAccess(new Identifier("RBXCS"), "try"),
            new[] { (Expression)bodyFn, catchArg, finArg });

        var chunk = new Chunk();
        chunk.Statements.Add(new LocalDeclaration("__t", tryCall));

        var propagate = new Chunk();
        propagate.Statements.Add(_guardDepth > 0
            ? new Return(new Identifier("__t"))
            : new Return(new RawExpression("table.unpack(__t.values)")));
        chunk.Statements.Add(new IfStatement(
            new[] { new IfBranch(new Binary(new Identifier("__t"), "~=", new RawExpression("nil")), propagate) },
            null));

        return new DoBlock(chunk);
    }

    private FunctionExpression BuildCatchFn(TryStatementSyntax node)
    {
        _guardDepth++;
        var branches = new List<IfBranch>();
        Chunk? catchAll = null;

        foreach (var clause in node.Catches)
        {
            var handler = new Chunk();
            if (clause.Declaration is { Identifier.ValueText.Length: > 0 } d)
                handler.Statements.Add(new LocalDeclaration(d.Identifier.Text, new Identifier(ExVar)));
            foreach (var s in clause.Block.Statements)
                handler.Statements.Add(LowerStatement(s));
            if (!EndsInTerminator(clause.Block))
                handler.Statements.Add(new Return(new TableConstructor(new List<TableEntry> { new("kind", new Literal("\"handled\"")) })));

            if (IsCatchAll(clause))
                catchAll = handler;
            else
            {
                var type = (INamedTypeSymbol)model.GetTypeInfo(clause.Declaration!.Type).Type!;
                var cond = new Call(new MemberAccess(new Identifier("RBXCS"), "is"),
                    new Expression[] { new Identifier(ExVar), new RawExpression(RequireLocalName(type)) });
                branches.Add(new IfBranch(cond, handler));
            }
        }
        _guardDepth--;

        var chunk = new Chunk();
        if (branches.Count > 0)
        {
            chunk.Statements.Add(new IfStatement(branches, catchAll));
            chunk.Statements.Add(new Return(new RawExpression("nil"))); // no typed clause matched -> rethrow
        }
        else if (catchAll is not null)
        {
            chunk.Statements.AddRange(catchAll.Statements); // unconditional catch-all (ends in a marker return)
        }
        else
        {
            chunk.Statements.Add(new Return(new RawExpression("nil")));
        }
        return new FunctionExpression(new[] { ExVar }, chunk);
    }

    private bool IsCatchAll(CatchClauseSyntax clause) =>
        clause.Declaration is null
        || model.GetTypeInfo(clause.Declaration.Type).Type?.ToDisplayString() == "System.Exception";

    private static bool EndsInTerminator(BlockSyntax block) =>
        block.Statements.Count > 0 && block.Statements[block.Statements.Count - 1] is ReturnStatementSyntax or ThrowStatementSyntax;

    private Statement LowerExpressionStatement(ExpressionStatementSyntax expr)
    {
        // event += handler / event -= handler  ->  signal:Connect / signal:Disconnect
        if (expr.Expression is AssignmentExpressionSyntax evAsn
            && model.GetSymbolInfo(evAsn.Left).Symbol is IEventSymbol)
        {
            var method = evAsn.IsKind(SyntaxKind.AddAssignmentExpression) ? "Connect" : "Disconnect";
            return new ExpressionStatement(new MethodCall(LowerExpr(evAsn.Left), method, new[] { LowerExpr(evAsn.Right) }));
        }

        switch (expr.Expression)
        {
            case AssignmentExpressionSyntax { RawKind: (int)SyntaxKind.SimpleAssignmentExpression } asn:
                return LowerSimpleAssignment(asn);
            case AssignmentExpressionSyntax asn when IsBitwiseCompound(asn): // a &= b -> a = bit32.band(a, b)
            {
                var l = LowerExpr(asn.Left);
                var isBool = model.GetTypeInfo(asn.Left).Type?.SpecialType == SpecialType.System_Boolean;
                return new Assignment(l, MapBitwise(asn.OperatorToken.Text.TrimEnd('='), l, LowerExpr(asn.Right), isBool)!);
            }
            case AssignmentExpressionSyntax asn: // compound: a += b  (Luau-native)
                return new CompoundAssignment(LowerExpr(asn.Left), CompoundOp(asn), LowerExpr(asn.Right));
            case PostfixUnaryExpressionSyntax p:
                return IncrementOf(p.Operand, p.OperatorToken.Text);
            case PrefixUnaryExpressionSyntax p when p.OperatorToken.Text is "++" or "--":
                return IncrementOf(p.Operand, p.OperatorToken.Text);
            default:
                return new ExpressionStatement(LowerExpr(expr.Expression));
        }
    }

    private Statement LowerSimpleAssignment(AssignmentExpressionSyntax asn)
    {
        var rhs = CopyIfNeeded(asn.Right, LowerExpr(asn.Right));

        if (model.GetSymbolInfo(asn.Left).Symbol is IPropertySymbol { IsIndexer: false } p && IsBodiedProperty(p))
        {
            var setName = "set_" + p.Name;
            if (p.IsStatic)
                return new ExpressionStatement(new Call(
                    new MemberAccess(new RawExpression(RequireLocalName((INamedTypeSymbol)p.ContainingType!)), setName), new[] { rhs }));
            var recv = asn.Left is MemberAccessExpressionSyntax ma ? LowerExpr(ma.Expression) : new Identifier("self");
            return new ExpressionStatement(new MethodCall(recv, setName, new[] { rhs }));
        }

        if (asn.Left is ElementAccessExpressionSyntax ea)
        {
            var recv = LowerExpr(ea.Expression);
            var idx = ea.ArgumentList.Arguments.Select(a => LowerExpr(a.Expression)).ToList();
            if (model.GetSymbolInfo(ea).Symbol is IPropertySymbol { IsIndexer: true })
            {
                idx.Add(rhs);
                return new ExpressionStatement(new MethodCall(recv, "set_Item", idx));
            }
            return new Assignment(new IndexAccess(recv, new Binary(idx[0], "+", new Literal("1"))), rhs);
        }

        return new Assignment(LowerExpr(asn.Left), rhs);
    }

    private Statement IncrementOf(ExpressionSyntax target, string op) =>
        new CompoundAssignment(LowerExpr(target), op == "++" ? "+" : "-", new Literal("1"));

    private static bool IsBitwiseCompound(AssignmentExpressionSyntax asn) =>
        asn.OperatorToken.Text is "&=" or "|=" or "^=" or "<<=" or ">>=";

    private string CompoundOp(AssignmentExpressionSyntax asn)
    {
        var op = asn.OperatorToken.Text.TrimEnd('=');
        if (op == "+" && model.GetTypeInfo(asn.Left).Type?.SpecialType == SpecialType.System_String)
            return "..";
        return op;
    }

    // C# body statement -> Luau Chunk (unwraps a block; wraps a single statement).
    private Chunk LowerBlock(StatementSyntax body)
    {
        var chunk = new Chunk();
        if (body is BlockSyntax b)
            foreach (var s in b.Statements)
                chunk.Statements.Add(LowerStatement(s));
        else
            chunk.Statements.Add(LowerStatement(body));
        return chunk;
    }

    private IfStatement LowerIf(IfStatementSyntax node)
    {
        var branches = new List<IfBranch> { BuildBranch(node.Condition, node.Statement) };
        Chunk? elseBody = null;
        var elseClause = node.Else;
        while (elseClause is not null)
        {
            if (elseClause.Statement is IfStatementSyntax nested)
            {
                branches.Add(BuildBranch(nested.Condition, nested.Statement));
                elseClause = nested.Else;
            }
            else
            {
                elseBody = LowerBlock(elseClause.Statement);
                elseClause = null;
            }
        }
        return new IfStatement(branches, elseBody);
    }

    // Builds an if-branch, handling `x is T v` pattern binding: emits RBXCS.is(x,T) as the condition
    // and prepends `local v = x` to the branch body.
    private IfBranch BuildBranch(ExpressionSyntax condition, StatementSyntax stmt)
    {
        var body = LowerBlock(stmt);
        if (condition is IsPatternExpressionSyntax { Pattern: DeclarationPatternSyntax { Designation: SingleVariableDesignationSyntax v } dp } isp)
        {
            var cond = new Call(new MemberAccess(new Identifier("RBXCS"), "is"),
                new[] { LowerExpr(isp.Expression), TypeToken(model.GetTypeInfo(dp.Type).Type) });
            body.Statements.Insert(0, new LocalDeclaration(v.Identifier.Text, LowerExpr(isp.Expression)));
            return new IfBranch(cond, body);
        }
        return new IfBranch(LowerExpr(condition), body);
    }

    // Canonical `for (int i = a; i </<= b; i++/i+=k)` -> numeric for (continue-safe).
    // Anything else desugars to a do{ init; while cond do body; incr end } block.
    private Statement LowerFor(ForStatementSyntax f)
    {
        if (TryCanonicalFor(f, out var numeric))
            return numeric!;

        // do
        //   <init>
        //   local __first = true
        //   while true do
        //     if not __first then <incr> end   -- runs every iteration incl. after `continue`
        //     __first = false
        //     if not (<cond>) then break end
        //     <body>
        //   end
        // end
        var outer = new Chunk();
        if (f.Declaration is not null)
            foreach (var v in f.Declaration.Variables)
                outer.Statements.Add(new LocalDeclaration(v.Identifier.Text,
                    v.Initializer is null ? null : LowerExpr(v.Initializer.Value)));
        foreach (var init in f.Initializers)
            outer.Statements.Add(new ExpressionStatement(LowerExpr(init)));

        outer.Statements.Add(new LocalDeclaration("__first", new Literal("true")));

        var loopBody = new Chunk();
        var incr = new Chunk();
        foreach (var inc in f.Incrementors)
            incr.Statements.Add(LowerIncrementor(inc));
        if (incr.Statements.Count > 0)
            loopBody.Statements.Add(new IfStatement(
                new[] { new IfBranch(new Unary("not", new Identifier("__first")), incr) }, null));
        loopBody.Statements.Add(new Assignment(new Identifier("__first"), new Literal("false")));
        if (f.Condition is not null)
        {
            var brk = new Chunk();
            brk.Statements.Add(new Break());
            loopBody.Statements.Add(new IfStatement(
                new[] { new IfBranch(new Unary("not", new Paren(LowerExpr(f.Condition))), brk) }, null));
        }
        foreach (var s in (f.Statement is BlockSyntax b ? b.Statements.Cast<StatementSyntax>() : new[] { f.Statement }))
            loopBody.Statements.Add(LowerStatement(s));

        outer.Statements.Add(new WhileStatement(new Literal("true"), loopBody));
        return new DoBlock(outer);
    }

    private bool TryCanonicalFor(ForStatementSyntax f, out Statement? numeric)
    {
        numeric = null;
        if (f.Declaration is not { Variables.Count: 1 } decl || decl.Variables[0].Initializer is null)
            return false;
        if (f.Incrementors.Count != 1)
            return false;
        if (f.Condition is not BinaryExpressionSyntax cond || cond.Left is not IdentifierNameSyntax condVar)
            return false;

        var name = decl.Variables[0].Identifier.Text;
        if (condVar.Identifier.Text != name)
            return false;

        Expression? step = null;
        var inc = f.Incrementors[0];
        if (inc is PostfixUnaryExpressionSyntax { OperatorToken.Text: "++" } post && IsVar(post.Operand, name))
        {
            // step stays null (implicit +1)
        }
        else if (inc is PrefixUnaryExpressionSyntax { OperatorToken.Text: "++" } pref && IsVar(pref.Operand, name))
        {
            // step stays null (implicit +1)
        }
        else if (inc is AssignmentExpressionSyntax { RawKind: (int)SyntaxKind.AddAssignmentExpression } add && IsVar(add.Left, name))
        {
            step = LowerExpr(add.Right);
        }
        else
        {
            return false;
        }

        var opText = cond.OperatorToken.Text;
        if (opText is not ("<" or "<="))
            return false;

        var start = LowerExpr(decl.Variables[0].Initializer!.Value);
        var bound = LowerExpr(cond.Right);
        var stop = opText == "<" ? new Binary(bound, "-", new Literal("1")) : bound;
        numeric = new NumericFor(name, start, stop, step, LowerBlock(f.Statement));
        return true;
    }

    private static bool IsVar(ExpressionSyntax e, string name) =>
        e is IdentifierNameSyntax id && id.Identifier.Text == name;

    private Statement LowerIncrementor(ExpressionSyntax inc) => inc switch
    {
        PostfixUnaryExpressionSyntax p => IncrementOf(p.Operand, p.OperatorToken.Text),
        PrefixUnaryExpressionSyntax p when p.OperatorToken.Text is "++" or "--" => IncrementOf(p.Operand, p.OperatorToken.Text),
        AssignmentExpressionSyntax { RawKind: (int)SyntaxKind.SimpleAssignmentExpression } a => new Assignment(LowerExpr(a.Left), LowerExpr(a.Right)),
        AssignmentExpressionSyntax a => new CompoundAssignment(LowerExpr(a.Left), CompoundOp(a), LowerExpr(a.Right)),
        _ => new ExpressionStatement(LowerExpr(inc)),
    };

    // switch -> if/elseif chain on `gov == caseValue`. C# has no fallthrough, so each section
    // is one branch; trailing `break` per section is dropped. ponytail: side-effecting governing
    // expr is re-rendered per case.
    private Statement LowerSwitch(SwitchStatementSyntax s)
    {
        var gov = LowerExpr(s.Expression);
        var branches = new List<IfBranch>();
        Chunk? elseBody = null;

        foreach (var section in s.Sections)
        {
            var body = new Chunk();
            foreach (var st in section.Statements)
                if (st is not BreakStatementSyntax)
                    body.Statements.Add(LowerStatement(st));

            if (section.Labels.Any(l => l is DefaultSwitchLabelSyntax))
            {
                elseBody = body;
                continue;
            }

            Expression? combined = null;
            foreach (var label in section.Labels.OfType<CaseSwitchLabelSyntax>())
            {
                var test = new Binary(gov, "==", LowerExpr(label.Value));
                combined = combined is null ? test : new Binary(combined, "or", test);
            }
            if (combined is not null)
                branches.Add(new IfBranch(combined, body));
        }
        return new IfStatement(branches, elseBody);
    }

    // ---- expressions ----

}
