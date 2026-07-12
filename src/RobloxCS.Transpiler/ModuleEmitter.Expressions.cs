using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RobloxCS.Luau;

namespace RobloxCS.Transpiler;

internal sealed partial class ModuleEmitter
{
    private Expression LowerExpr(ExpressionSyntax expr)
    {
        switch (expr)
        {
            case LiteralExpressionSyntax { RawKind: (int)SyntaxKind.DefaultLiteralExpression }:
                return new RawExpression($"RBXCS.default({RenderToken(model.GetTypeInfo(expr).Type)})");
            case LiteralExpressionSyntax lit:
                return LowerLiteral(lit);
            case ParenthesizedExpressionSyntax paren:
                return LowerExpr(paren.Expression);
            case CheckedExpressionSyntax chk: // checked(x)/unchecked(x): overflow semantics erased (Luau f64)
                return LowerExpr(chk.Expression);
            case ThisExpressionSyntax:
                return new Identifier("self");
            case IdentifierNameSyntax id:
                return LowerIdentifier(id);
            case MemberAccessExpressionSyntax ma:
                return LowerMemberAccess(ma);
            case ElementAccessExpressionSyntax ea:
            {
                var recv = LowerExpr(ea.Expression);
                var idx = ea.ArgumentList.Arguments.Select(a => LowerExpr(a.Expression)).ToList();
                return model.GetSymbolInfo(ea).Symbol is IPropertySymbol { IsIndexer: true }
                    ? new MethodCall(recv, "get_Item", idx)
                    : new IndexAccess(recv, new Binary(idx[0], "+", new Literal("1"))); // C# 0-based array -> Luau 1-based
            }
            case InvocationExpressionSyntax inv:
                return LowerInvocation(inv);
            case ObjectCreationExpressionSyntax obj:
                return LowerObjectCreation(obj);
            case BinaryExpressionSyntax { RawKind: (int)SyntaxKind.IsExpression } isb:
                return new Call(new MemberAccess(new Identifier("RBXCS"), "is"),
                    new[] { LowerExpr(isb.Left), TypeToken(model.GetTypeInfo(isb.Right).Type) });
            case BinaryExpressionSyntax { RawKind: (int)SyntaxKind.CoalesceExpression } co:
            {
                var cl = LowerExpr(co.Left);
                return new IfExpression(new Binary(cl, "~=", new RawExpression("nil")), cl, LowerExpr(co.Right));
            }
            case BinaryExpressionSyntax bin:
                return LowerBinary(bin);
            case ConditionalExpressionSyntax cond:
                return new IfExpression(LowerExpr(cond.Condition), LowerExpr(cond.WhenTrue), LowerExpr(cond.WhenFalse));
            case ThrowExpressionSyntax th:
                return new Call(new Identifier("error"), new[] { LowerExpr(th.Expression) });
            case DefaultExpressionSyntax def:
                return new RawExpression($"RBXCS.default({RenderToken(model.GetTypeInfo(def.Type).Type)})");
            case TypeOfExpressionSyntax tof:
                return TypeToken(model.GetTypeInfo(tof.Type).Type);
            case IsPatternExpressionSyntax { Pattern: TypePatternSyntax tp } isp:
                return new Call(new MemberAccess(new Identifier("RBXCS"), "is"),
                    new[] { LowerExpr(isp.Expression), TypeToken(model.GetTypeInfo(tp.Type).Type) });
            case IsPatternExpressionSyntax { Pattern: DeclarationPatternSyntax dpat } ispd:
                return new Call(new MemberAccess(new Identifier("RBXCS"), "is"),
                    new[] { LowerExpr(ispd.Expression), TypeToken(model.GetTypeInfo(dpat.Type).Type) });
            case IsPatternExpressionSyntax { Pattern: ConstantPatternSyntax { Expression: LiteralExpressionSyntax { RawKind: (int)SyntaxKind.NullLiteralExpression } } } ispn:
                return new Binary(LowerExpr(ispn.Expression), "==", new RawExpression("nil")); // x is null
            case PrefixUnaryExpressionSyntax pre:
                return LowerPrefixUnary(pre);
            case PostfixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.SuppressNullableWarningExpression } sup:
                return LowerExpr(sup.Operand); // x! (null-forgiving) is a no-op at runtime
            case InterpolatedStringExpressionSyntax interp:
                return LowerInterpolated(interp);
            case AwaitExpressionSyntax aw:
                return new Call(new MemberAccess(new Identifier("RBXCS"), "await"), new[] { LowerExpr(aw.Expression) });
            case SimpleLambdaExpressionSyntax sl:
                return new FunctionExpression(new[] { LuauId(sl.Parameter.Identifier.Text) }, LambdaBody(sl.Body));
            case ParenthesizedLambdaExpressionSyntax pl:
                return new FunctionExpression(pl.ParameterList.Parameters.Select(p => LuauId(p.Identifier.Text)).ToList(), LambdaBody(pl.Body));
            case AnonymousMethodExpressionSyntax am:
                return new FunctionExpression(am.ParameterList?.Parameters.Select(p => LuauId(p.Identifier.Text)).ToList() ?? new List<string>(), LambdaBody(am.Body));
            case ConditionalAccessExpressionSyntax ca:
                return LowerConditionalAccess(ca);
            case CastExpressionSyntax cast:
                return LowerCast(cast);
            default:
                return new RawExpression($"nil --[[rbxcs unsupported: {expr.Kind()}]]");
        }
    }

    private static Expression LowerLiteral(LiteralExpressionSyntax lit) => lit.Kind() switch
    {
        SyntaxKind.NullLiteralExpression => new RawExpression("nil"),
        SyntaxKind.TrueLiteralExpression => new Literal("true"),
        SyntaxKind.FalseLiteralExpression => new Literal("false"),
        SyntaxKind.NumericLiteralExpression => new Literal(NormalizeNumber(lit.Token.Text)),
        SyntaxKind.StringLiteralExpression => new Literal(LuauString(lit.Token.Value as string ?? "")),
        SyntaxKind.CharacterLiteralExpression => new Literal(LuauString(lit.Token.Value?.ToString() ?? "")),
        _ => new Literal(lit.Token.Text),
    };

    private Expression LowerIdentifier(IdentifierNameSyntax id)
    {
        var sym = model.GetSymbolInfo(id).Symbol;

        // Roblox.Globals members (game / workspace / print) accessed unqualified -> bare global.
        if (sym is { IsStatic: true } && sym.ContainingType?.ToDisplayString() == "Roblox.Globals")
            return new RawExpression(id.Identifier.Text);

        switch (sym)
        {
            case IParameterSymbol or ILocalSymbol:
                return new Identifier(LuauId(id.Identifier.Text));
            case IEventSymbol { IsStatic: false }:
                return new MemberAccess(new Identifier("self"), UserId(sym, id.Identifier.Text));
            case IPropertySymbol { IsStatic: false } bp when IsBodiedProperty(bp):
                return new MethodCall(new Identifier("self"), "get_" + id.Identifier.Text, Array.Empty<Expression>());
            case IPropertySymbol { IsStatic: true } bps when IsBodiedProperty(bps):
                return new Call(new MemberAccess(new RawExpression(RequireLocalName((INamedTypeSymbol)sym.ContainingType!)), "get_" + id.Identifier.Text), Array.Empty<Expression>());
            case IFieldSymbol { IsStatic: false } or IPropertySymbol { IsStatic: false }:
                return new MemberAccess(new Identifier("self"), UserId(sym, id.Identifier.Text));
            case IFieldSymbol { IsStatic: true } or IPropertySymbol { IsStatic: true }:
            {
                var owner = (INamedTypeSymbol)sym.ContainingType!;
                return new MemberAccess(new RawExpression(RequireLocalName(owner)), UserId(sym, id.Identifier.Text));
            }
            case INamedTypeSymbol t:
                return new RawExpression(RequireLocalName(t));
            default:
                return new Identifier(id.Identifier.Text);
        }
    }

    private Expression LowerMemberAccess(MemberAccessExpressionSyntax ma)
    {
        var sym = model.GetSymbolInfo(ma).Symbol;
        var memberName = ma.Name.Identifier.Text;

        if (sym?.ContainingType?.ToDisplayString() == "Roblox.Globals")
            return new RawExpression(memberName); // game / workspace as bare globals

        var ct = sym?.ContainingType?.OriginalDefinition.ToDisplayString();
        if (sym?.ContainingType?.SpecialType == SpecialType.System_String && memberName == "Length")
            return new Unary("#", LowerExpr(ma.Expression));
        if (memberName == "Count" && ct is "System.Collections.Generic.List<T>"
                or "System.Collections.Generic.Dictionary<TKey, TValue>" or "System.Collections.Generic.HashSet<T>")
            return new MethodCall(LowerExpr(ma.Expression), "Count", Array.Empty<Expression>());
        if (ct == "System.Nullable<T>")
            return memberName switch
            {
                "HasValue" => new Binary(LowerExpr(ma.Expression), "~=", new RawExpression("nil")),
                "Value" => LowerExpr(ma.Expression),
                _ => new MemberAccess(LowerExpr(ma.Expression), memberName),
            };

        if (ImportModule(sym?.ContainingType) is { } importModule)
        {
            var member = LuauMemberName(sym);
            return sym!.IsStatic
                ? new MemberAccess(new Identifier(RequireImport((INamedTypeSymbol)sym.ContainingType!, importModule)), member)
                : new MemberAccess(LowerExpr(ma.Expression), member);
        }

        if (sym is IPropertySymbol bp && IsBodiedProperty(bp))
            return sym.IsStatic
                ? new Call(new MemberAccess(new RawExpression(RequireLocalName((INamedTypeSymbol)sym.ContainingType!)), "get_" + memberName), Array.Empty<Expression>())
                : new MethodCall(LowerExpr(ma.Expression), "get_" + memberName, Array.Empty<Expression>());

        MaybeWarnExternal(sym, ma);

        if (sym is { IsStatic: true } && sym.ContainingType is INamedTypeSymbol owner)
            return new MemberAccess(new RawExpression(RequireLocalName(owner)), UserId(sym, memberName));

        return new MemberAccess(LowerExpr(ma.Expression), UserId(sym, memberName));
    }

    private Expression LowerInvocation(InvocationExpressionSyntax inv)
    {
        var sym = model.GetSymbolInfo(inv).Symbol as IMethodSymbol;
        var args = LowerArguments(inv.ArgumentList.Arguments);
        if (sym is not null && reification.NeedsTokens(sym))
            args.AddRange(sym.TypeArguments.Select(TypeToken));

        // Logging (print / warn / Console.Write*) -> prepend the C# source location for traceability.
        if (LoggingLuauFn(sym) is { } logFn)
        {
            var located = new List<Expression> { new Literal($"\"{SourceLocation(inv)}\"") };
            located.AddRange(args);
            return new Call(new Identifier(logFn), located);
        }

        var isGlobal = sym?.ContainingType?.ToDisplayString() == "Roblox.Globals";

        // event.Invoke(args) -> signal:Fire(args)
        if (inv.Expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Invoke" } evMa
            && model.GetSymbolInfo(evMa.Expression).Symbol is IEventSymbol)
            return new MethodCall(LowerExpr(evMa.Expression), "Fire", args);

        if (sym is { MethodKind: MethodKind.DelegateInvoke })
            return new Call(LowerExpr(inv.Expression), args); // delegate/Func/Action invocation

        // [LuauImport] binding: call the bound Luau module member
        if (ImportModule(sym?.ContainingType) is { } importModule)
        {
            var member = LuauMemberName(sym);
            if (sym!.IsStatic)
                return new Call(new MemberAccess(new Identifier(RequireImport(sym.ContainingType!, importModule)), member), args);
            var recv = inv.Expression is MemberAccessExpressionSyntax rma ? LowerExpr(rma.Expression) : new Identifier("self");
            return new MethodCall(recv, member, args);
        }

        if (MapParallel(inv, sym, args) is { } par)
            return par;

        var bcl = MapBclCall(inv, sym, args);
        if (bcl is not null)
            return bcl;

        MaybeWarnExternal(sym, inv);

        switch (inv.Expression)
        {
            case MemberAccessExpressionSyntax ma:
            {
                var methodName = ma.Name.Identifier.Text;
                if (isGlobal)
                    return new Call(new Identifier(methodName), args);
                if (sym is { IsStatic: true } && sym.ContainingType is INamedTypeSymbol owner)
                    return new Call(new MemberAccess(new RawExpression(RequireLocalName(owner)), UserId(sym, methodName)), args);
                return new MethodCall(LowerExpr(ma.Expression), UserId(sym, methodName), args);
            }
            case IdentifierNameSyntax id:
                return LowerUnqualifiedCall(id.Identifier.Text, sym, isGlobal, args);
            case GenericNameSyntax gen:
                return LowerUnqualifiedCall(gen.Identifier.Text, sym, isGlobal, args);
            default:
                return new Call(LowerExpr(inv.Expression), args);
        }
    }

    // Parallel.For fan-out. Lifts the body into a synthetic Actor-hosted worker Script and emits a
    // clone-pool driver, IFF the body is self-contained: captures only SharedTables (they cross a VM
    // by reference) and pulls in no other module. Otherwise falls back to sequential RBXCS.pfor with
    // a diagnostic — a runtime closure cannot cross an Actor VM, so anything else can't parallelize.
    private Expression LowerParallelFor(InvocationExpressionSyntax inv, List<Expression> args)
    {
        var sequential = new Call(new RawExpression("RBXCS.pfor"), args);
        if (inv.ArgumentList.Arguments.Count < 3
            || inv.ArgumentList.Arguments[2].Expression is not AnonymousFunctionExpressionSyntax lambda)
            return sequential;

        var lambdaParams = lambda switch
        {
            SimpleLambdaExpressionSyntax s => new[] { s.Parameter },
            ParenthesizedLambdaExpressionSyntax p => p.ParameterList.Parameters.ToArray(),
            _ => Array.Empty<ParameterSyntax>(),
        };
        if (lambdaParams.Length != 1)
            return sequential;
        var paramSyms = new HashSet<ISymbol>(
            lambdaParams.Select(p => model.GetDeclaredSymbol(p)).OfType<ISymbol>(), SymbolEqualityComparer.Default);

        var flow = lambda.Body is ExpressionSyntax be ? model.AnalyzeDataFlow(be)
            : lambda.Body is StatementSyntax st ? model.AnalyzeDataFlow(st) : null;
        if (flow is not { Succeeded: true })
            return WarnSequential(inv, sequential, "body could not be analyzed");

        var caps = flow.DataFlowsIn.Where(s => !paramSyms.Contains(s)).ToList();
        if (caps.Any(s => CapType(s)?.ToDisplayString() != "Roblox.SharedTable"))
            return WarnSequential(inv, sequential,
                "body captures non-SharedTable state (pass shared state via a SharedTable to parallelize)");

        // Lower the body; if it pulls in another module, it is not self-contained -> sequential.
        var snapshot = new Dictionary<string, string>(_externalRequires);
        var ivar = LuauId(lambdaParams[0].Identifier.Text);
        var bodyChunk = new Chunk();
        if (lambda.Body is BlockSyntax blk)
            foreach (var s in blk.Statements)
                bodyChunk.Statements.Add(LowerStatement(s));
        else if (lambda.Body is ExpressionSyntax ex)
            bodyChunk.Statements.Add(LowerExpressionAsStatement(ex));
        if (_externalRequires.Count != snapshot.Count)
        {
            _externalRequires.Clear();
            foreach (var kv in snapshot)
                _externalRequires[kv.Key] = kv.Value;
            return WarnSequential(inv, sequential, "body references another module");
        }

        CheckParallelSafety(lambda.Body); // this body runs in the parallel phase now

        // Synthetic worker: an [Actor] Script binding a parallel "run" handler over a slice.
        var workerName = $"{module.ModuleName}__pfor{++_pforCounter}";
        var capNames = caps.Select(c => LuauId(c.Name)).ToList();
        var handlerParams = new List<string> { "__lo", "__hi", "__done" };
        handlerParams.AddRange(capNames);
        var handlerBody = new Chunk();
        handlerBody.Statements.Add(new NumericFor(ivar, new RawExpression("__lo"),
            new Binary(new RawExpression("__hi"), "-", new Literal("1")), null, bodyChunk));
        handlerBody.Statements.Add(new ExpressionStatement(new Call(new RawExpression("SharedTable.increment"),
            new Expression[] { new RawExpression("__done"), new Literal(LuauString("n")), new Literal("1") })));

        var workerChunk = new Chunk();
        workerChunk.Statements.Add(new RawStatement($"local RBXCS = {map.RuntimeRequire}"));
        workerChunk.Statements.Add(new RawStatement("local __actor = script:GetActor()"));
        workerChunk.Statements.Add(new ExpressionStatement(new MethodCall(new RawExpression("__actor"),
            "BindToMessageParallel",
            new Expression[] { new Literal(LuauString("run")), new FunctionExpression(handlerParams, handlerBody) })));

        var workerRel = module.RelativePath.Substring(0, module.RelativePath.Length - module.ModuleName.Length) + workerName;
        _synthetic.Add(new ModuleResult(workerRel, ModuleKind.Script, LuauWriter.Write(workerChunk), workerName, isActor: true));

        // Driver: RBXCS.parallelFor(<template Actor>, from, to, { caps })
        var templatePath = module.RequireTargetExpr.Substring(0, module.RequireTargetExpr.Length - module.ModuleName.Length) + workerName;
        var capTable = new TableConstructor(capNames.Select(n => new TableEntry(null, (Expression)new Identifier(n))).ToList());
        return new Call(new RawExpression("RBXCS.parallelFor"),
            new Expression[] { new RawExpression(templatePath), args[0], args[1], capTable });
    }

    // Checks a body that will run in the parallel phase (a [Parallel] method or a Parallel.For body):
    // any Roblox member tagged Unsafe is illegal there, and a write to a ReadSafe member is illegal
    // (readable but not writable). Emits a diagnostic per violation. Data: RobloxThreadSafety.g.cs.
    private void CheckParallelSafety(SyntaxNode body)
    {
        foreach (var ma in body.DescendantNodesAndSelf().OfType<MemberAccessExpressionSyntax>())
        {
            var sym = model.GetSymbolInfo(ma).Symbol;
            if (sym is not (IPropertySymbol or IMethodSymbol or IEventSymbol))
                continue;
            var ct = sym.ContainingType;
            if (ct?.ContainingNamespace?.ToDisplayString() != "Roblox")
                continue;
            var key = ct.Name + "." + sym.Name;
            if (RobloxThreadSafety.Unsafe.Contains(key))
                ParallelWarn(ma, $"'{key}' is Unsafe in the parallel phase and will error at runtime; move it out of the parallel body");
            else if (ma.Parent is AssignmentExpressionSyntax asn && asn.Left == ma && RobloxThreadSafety.ReadOnly.Contains(key))
                ParallelWarn(ma, $"'{key}' is read-only in the parallel phase; do the assignment outside the parallel body");
        }
    }

    private void ParallelWarn(SyntaxNode at, string msg)
    {
        var loc = at.GetLocation().GetLineSpan();
        diagnostics.Add(new TranspileDiagnostic(
            $"Parallel-safety: {msg}.", loc.Path, loc.StartLinePosition.Line + 1, loc.StartLinePosition.Character + 1));
    }

    private ITypeSymbol? CapType(ISymbol s) => s switch
    {
        ILocalSymbol l => l.Type,
        IParameterSymbol p => p.Type,
        IFieldSymbol f => f.Type,
        _ => null,
    };

    private Expression WarnSequential(InvocationExpressionSyntax inv, Expression sequential, string why)
    {
        var loc = inv.GetLocation().GetLineSpan();
        diagnostics.Add(new TranspileDiagnostic(
            $"Parallel.For running sequentially: {why}.", loc.Path, loc.StartLinePosition.Line + 1, loc.StartLinePosition.Character + 1));
        return sequential;
    }

    // Parallel primitives: RobloxCS.Parallel.* -> task.*; SharedTable extension ops -> SharedTable.*.
    private Expression? MapParallel(InvocationExpressionSyntax inv, IMethodSymbol? sym, List<Expression> args)
    {
        var ct = sym?.ContainingType?.ToDisplayString();
        if (ct == "RobloxCS.ParallelLuau")
            return sym!.Name switch
            {
                "Desynchronize" => new Call(new RawExpression("task.desynchronize"), args),
                "Synchronize" => new Call(new RawExpression("task.synchronize"), args),
                "For" => LowerParallelFor(inv, args),
                _ => null,
            };

        if (ct == "Roblox.SharedTableOps" && sym is { IsExtensionMethod: true })
        {
            var recv = inv.Expression is MemberAccessExpressionSyntax ma ? LowerExpr(ma.Expression) : new Identifier("self");
            Expression[] WithRecv() => new[] { recv }.Concat(args).ToArray();
            return sym.Name switch
            {
                "Get" => new IndexAccess(recv, args[0]),                                  // st[key]
                "Set" => new Call(new RawExpression("RBXCS.stset"), WithRecv()),          // st[key] = value
                "Increment" => new Call(new RawExpression("SharedTable.increment"), WithRecv()),
                "Update" => new Call(new RawExpression("SharedTable.update"), WithRecv()),
                "Size" => new Call(new RawExpression("SharedTable.size"), new[] { recv }),
                "Clear" => new Call(new RawExpression("SharedTable.clear"), new[] { recv }),
                _ => null,
            };
        }
        return null;
    }

    // Maps a curated System.* surface (F9) to Luau natives / runtime. Returns null if not a BCL call.
    private Expression? MapBclCall(InvocationExpressionSyntax inv, IMethodSymbol? sym, List<Expression> args)
    {
        var ct = sym?.ContainingType?.OriginalDefinition.ToDisplayString();
        var recv = inv.Expression is MemberAccessExpressionSyntax ma ? LowerExpr(ma.Expression) : new Identifier("self");

        // Object/primitive methods with no user override (no source refs) -> Luau natives / runtime.
        // A user-overridden member has source refs and falls through to a normal recv:Member() call.
        if (sym is { DeclaringSyntaxReferences.Length: 0 } obj)
        {
            if (obj is { Name: "ToString", Parameters.Length: 0 })
                return new Call(new RawExpression("tostring"), new[] { recv });
            if (obj is { Name: "ToString", Parameters.Length: 1 }
                && obj.Parameters[0].Type.SpecialType == SpecialType.System_String)
                return new Call(new RawExpression("RBXCS.tostringf"), new[] { recv, args[0] }); // ToString(format)
            if (obj is { Name: "Equals", Parameters.Length: 1 })
                return new Paren(new Binary(recv, "==", args[0])); // parens: may sit under `not`/arithmetic
            if (obj is { Name: "GetHashCode", Parameters.Length: 0 })
                return new Call(new RawExpression("RBXCS.hashCode"), new[] { recv });
        }

        if (ct == "System.Math")
        {
            var fn = sym!.Name switch
            {
                "Ceiling" => "ceil",
                "Floor" => "floor",
                "Sqrt" => "sqrt",
                "Abs" => "abs",
                "Max" => "max",
                "Min" => "min",
                "Round" => "round",
                "Sin" => "sin",
                "Cos" => "cos",
                "Tan" => "tan",
                _ => sym.Name.ToLowerInvariant(),
            };
            return new Call(new RawExpression($"math.{fn}"), args);
        }

        if (ct == "System.Threading.Tasks.Task")
            switch (sym!.Name)
            {
                case "WhenAll":
                    return new Call(new RawExpression("RBXCS.whenAll"), new[] { ArrayTable(args) });
                case "WhenAny":
                    return new Call(new RawExpression("RBXCS.whenAny"), new[] { ArrayTable(args) });
                case "FromResult":
                    return new Call(new RawExpression("RBXCS.resolved"), args);
                case "Delay":
                    return new Call(new RawExpression("RBXCS.delay"), new Expression[] { new Binary(args[0], "/", new Literal("1000")) });
            }

        if (sym is { IsExtensionMethod: true } && sym.ContainingType?.ToDisplayString() == "System.Linq.Enumerable")
        {
            var fn = sym.Name switch
            {
                "Where" => "where",
                "Select" => "select",
                "ToList" or "ToArray" => "toList",
                "Count" => "count",
                "Sum" => "sum",
                "First" or "FirstOrDefault" => "first",
                "Any" => "any",
                "All" => "all",
                _ => null,
            };
            if (fn is not null)
            {
                var linqArgs = new List<Expression> { recv };
                linqArgs.AddRange(args);
                return new Call(new RawExpression($"RBXCS.Linq.{fn}"), linqArgs);
            }
        }

        if (sym?.ContainingType?.SpecialType == SpecialType.System_String)
            switch (sym!.Name)
            {
                case "ToUpper": return new Call(new RawExpression("string.upper"), new[] { recv });
                case "ToLower": return new Call(new RawExpression("string.lower"), new[] { recv });
                case "Substring":
                    var start = new Binary(args[0], "+", new Literal("1"));
                    return args.Count == 1
                        ? new Call(new RawExpression("string.sub"), new Expression[] { recv, start })
                        : new Call(new RawExpression("string.sub"),
                            new Expression[] { recv, start, new Binary(args[0], "+", args[1]) });
            }

        return null;
    }

    private static Expression ArrayTable(IEnumerable<Expression> items) =>
        new TableConstructor(items.Select(i => new TableEntry(null, i)).ToList());

    private Expression LowerUnqualifiedCall(string methodName, IMethodSymbol? sym, bool isGlobal, IReadOnlyList<Expression> args)
    {
        if (isGlobal)
            return new Call(new Identifier(methodName), args);
        if (sym is { IsStatic: true })
            return new Call(new MemberAccess(new Identifier(_currentType), methodName), args);
        return new MethodCall(new Identifier("self"), methodName, args); // implicit this
    }

    private Expression LowerObjectCreation(ObjectCreationExpressionSyntax obj)
    {
        var type = model.GetTypeInfo(obj).Type;
        var args = obj.ArgumentList is null ? new List<Expression>() : LowerArguments(obj.ArgumentList.Arguments);

        if (type?.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.List<T>")
            return new Call(new MemberAccess(new RawExpression("RBXCS.List"), "new"), Array.Empty<Expression>());
        if (type?.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.Dictionary<TKey, TValue>")
            return new Call(new MemberAccess(new RawExpression("RBXCS.Dictionary"), "new"), Array.Empty<Expression>());
        if (type?.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.HashSet<T>")
            return new Call(new MemberAccess(new RawExpression("RBXCS.HashSet"), "new"), Array.Empty<Expression>());

        // Roblox class (Part, ...) -> Instance.new("Part"); Roblox DataType (Vector3, ...) -> Vector3.new(...)
        if (type is INamedTypeSymbol rbx && IsRobloxNative(rbx))
            return DerivesFromInstance(rbx)
                ? new Call(new MemberAccess(new Identifier("Instance"), "new"), new Expression[] { new Literal($"\"{rbx.Name}\"") })
                : new Call(new MemberAccess(new RawExpression(rbx.Name), "new"), args);

        // [LuauImport] type -> `require(module).new(...)`
        if (type is INamedTypeSymbol imp && ImportModule(imp) is { } impMod)
            return new Call(new MemberAccess(new Identifier(RequireImport(imp, impMod)), "new"), args);

        MaybeWarnExternalType(type as INamedTypeSymbol, obj);

        // new T() on a type parameter -> construct via its runtime token
        if (type is ITypeParameterSymbol tp && _tokenParams.Contains(tp.Name))
        {
            var newArgs = new List<Expression> { new RawExpression($"__t_{tp.Name}") };
            newArgs.AddRange(args);
            return new Call(new MemberAccess(new Identifier("RBXCS"), "new"), newArgs);
        }

        var local = type is INamedTypeSymbol n ? RequireLocalName(n) : "nil";
        return new Call(new MemberAccess(new RawExpression(local), "new"), args);
    }

    // Runtime type token for a concrete type or an in-scope type parameter.
    private Expression TypeToken(ITypeSymbol? t)
    {
        if (t is ITypeParameterSymbol tp)
            return new RawExpression(_tokenParams.Contains(tp.Name) ? $"__t_{tp.Name}" : "nil");
        return new RawExpression(RenderToken(t));
    }

    private string RenderToken(ITypeSymbol? t)
    {
        if (t is ITypeParameterSymbol tp)
            return _tokenParams.Contains(tp.Name) ? $"__t_{tp.Name}" : "nil";
        switch (t?.SpecialType)
        {
            case SpecialType.System_Boolean:
                return "\"boolean\"";
            case SpecialType.System_Byte or SpecialType.System_SByte or SpecialType.System_Int16
                or SpecialType.System_UInt16 or SpecialType.System_Int32 or SpecialType.System_UInt32
                or SpecialType.System_Int64 or SpecialType.System_UInt64 or SpecialType.System_Single
                or SpecialType.System_Double or SpecialType.System_Decimal:
                return "\"number\"";
        }
        if (t is INamedTypeSymbol { TypeKind: TypeKind.Struct or TypeKind.Class } n && (n.DeclaringSyntaxReferences.Length > 0 || map.TryGetType(n, out _)))
            return RequireLocalName(n);
        return "nil";
    }

    private Expression LowerBinary(BinaryExpressionSyntax bin)
    {
        var left = LowerExpr(bin.Left);
        var right = LowerExpr(bin.Right);
        var op = bin.OperatorToken.Text;
        var lt = model.GetTypeInfo(bin.Left).Type;
        var rt = model.GetTypeInfo(bin.Right).Type;

        var concat = op == "+" && IsStringConcat(bin);
        // C# char promotes to its int code point in arithmetic/bitwise (but stays text in `+` concat).
        // ponytail: string.byte is the first UTF-8 byte, so only ASCII chars are exact.
        if (!concat && op is "+" or "-" or "*" or "/" or "%" or "&" or "|" or "^" or "<<" or ">>")
        {
            if (lt?.SpecialType == SpecialType.System_Char) left = ByteOf(left);
            if (rt?.SpecialType == SpecialType.System_Char) right = ByteOf(right);
        }

        // Luau has no bitwise operators: &/|/^/<</>> map to bit32.* (on bool, &/|/^ are logical).
        if (MapBitwise(op, left, right, IsBoolOperand(bin)) is { } bw)
            return bw;

        // Integer / and %: unsigned operands use native // and % (exact, both >= 0). Signed operands
        // need helpers — Luau // floors toward -inf but C# truncates toward zero, and Luau % takes the
        // divisor's sign while C# takes the dividend's.
        if (MapIntDivMod(op, left, right, lt, rt) is { } dm)
            return dm;

        if (concat)
            op = "..";
        op = op switch
        {
            "!=" => "~=",
            "&&" => "and",
            "||" => "or",
            _ => op,
        };
        return new Binary(left, op, right);
    }

    private static Expression ByteOf(Expression charExpr) =>
        new Call(new RawExpression("string.byte"), new[] { charExpr });

    // Casts are otherwise erased, except char <-> integer, which changes representation (char is a
    // 1-char string; its integer form is the code point). ponytail: numeric conv (double->int etc.)
    // still relies on Luau's own coercion.
    private Expression LowerCast(CastExpressionSyntax cast)
    {
        var inner = LowerExpr(cast.Expression);
        var to = model.GetTypeInfo(cast.Type).Type?.SpecialType;
        var from = model.GetTypeInfo(cast.Expression).Type?.SpecialType;
        if (from == SpecialType.System_Char && to is not SpecialType.System_Char and not null && to != SpecialType.System_String)
            return ByteOf(inner); // (int)ch -> code point
        if (to == SpecialType.System_Char && from != SpecialType.System_Char)
            return new Call(new RawExpression("string.char"), new[] { inner }); // (char)n -> 1-char string
        return inner;
    }

    // &/|/^/<</>> -> Luau. Integer operands use bit32 (32-bit unsigned semantics — differs from C#
    // signed int on overflow); bool operands use logical and/or, ^ as `~=`. Returns null if not bitwise.
    private Expression? MapBitwise(string op, Expression left, Expression right, bool isBool) => op switch
    {
        "&" => isBool ? new Binary(left, "and", right) : Bit("band", left, right),
        "|" => isBool ? new Binary(left, "or", right) : Bit("bor", left, right),
        "^" => isBool ? new Binary(left, "~=", right) : Bit("bxor", left, right),
        "<<" => Bit("lshift", left, right),
        ">>" => Bit("rshift", left, right),
        _ => null,
    };

    private static Expression Bit(string fn, Expression a, Expression b) =>
        new Call(new RawExpression($"bit32.{fn}"), new[] { a, b });

    private bool IsBoolOperand(BinaryExpressionSyntax bin) =>
        model.GetTypeInfo(bin.Left).Type?.SpecialType == SpecialType.System_Boolean
        || model.GetTypeInfo(bin.Right).Type?.SpecialType == SpecialType.System_Boolean;

    private static bool IsIntegral(ITypeSymbol? t) => t?.SpecialType is
        SpecialType.System_Byte or SpecialType.System_SByte or SpecialType.System_Int16
        or SpecialType.System_UInt16 or SpecialType.System_Int32 or SpecialType.System_UInt32
        or SpecialType.System_Int64 or SpecialType.System_UInt64 or SpecialType.System_Char; // char promotes to int

    private static bool IsUnsigned(ITypeSymbol? t) => t?.SpecialType is
        SpecialType.System_Byte or SpecialType.System_UInt16 or SpecialType.System_UInt32
        or SpecialType.System_UInt64;

    // Integer / and % -> Luau. Unsigned operands map to native // and % (operands >= 0 so exact);
    // signed operands use RBXCS.idiv/imod (truncate toward zero, dividend-signed remainder). null if
    // not an integer /,%.
    private Expression? MapIntDivMod(string op, Expression left, Expression right, ITypeSymbol? lt, ITypeSymbol? rt)
    {
        if ((op != "/" && op != "%") || !IsIntegral(lt) || !IsIntegral(rt))
            return null;
        if (IsUnsigned(lt) && IsUnsigned(rt))
            return new Binary(left, op == "/" ? "//" : "%", right);
        return new Call(new RawExpression(op == "/" ? "RBXCS.idiv" : "RBXCS.imod"), new[] { left, right });
    }

    // Lambda / anonymous-method body: expression body -> `return expr`; block -> lowered statements.
    private Chunk LambdaBody(CSharpSyntaxNode body)
    {
        var chunk = new Chunk();
        if (body is ExpressionSyntax e)
            chunk.Statements.Add(new Return(LowerExpr(e)));
        else if (body is BlockSyntax b)
            foreach (var s in b.Statements)
                chunk.Statements.Add(LowerStatement(s));
        return chunk;
    }

    // x?.member / x?.M() -> `if x ~= nil then <x.member> else nil` (ponytail: x evaluated twice)
    private Expression LowerConditionalAccess(ConditionalAccessExpressionSyntax ca)
    {
        var receiver = LowerExpr(ca.Expression);
        var access = ca.WhenNotNull switch
        {
            MemberBindingExpressionSyntax mb => (Expression)new MemberAccess(receiver, mb.Name.Identifier.Text),
            InvocationExpressionSyntax { Expression: MemberBindingExpressionSyntax mbi } inv =>
                new MethodCall(receiver, mbi.Name.Identifier.Text, inv.ArgumentList.Arguments.Select(a => LowerExpr(a.Expression)).ToList()),
            _ => receiver,
        };
        return new IfExpression(new Binary(receiver, "~=", new RawExpression("nil")), access, new RawExpression("nil"));
    }

    private Expression LowerPrefixUnary(PrefixUnaryExpressionSyntax pre)
    {
        var operand = LowerExpr(pre.Operand);
        return pre.OperatorToken.Text switch
        {
            "!" => new Unary("not", operand),
            "-" => new Unary("-", operand),
            "+" => operand,
            "~" => new Call(new RawExpression("bit32.bnot"), new[] { operand }), // bitwise complement
            _ => new RawExpression($"nil --[[rbxcs unsupported unary: {pre.OperatorToken.Text}]]"),
        };
    }

    private Expression LowerInterpolated(InterpolatedStringExpressionSyntax interp)
    {
        var parts = new List<InterpolationPart>();
        foreach (var content in interp.Contents)
        {
            switch (content)
            {
                case InterpolatedStringTextSyntax text:
                    parts.Add(new InterpolationPart(text.TextToken.ValueText, null));
                    break;
                case InterpolationSyntax hole:
                    parts.Add(new InterpolationPart(null, LowerHole(hole)));
                    break;
            }
        }
        return new InterpolatedString(parts);
    }

    // `{expr,align:format}` — apply the C# format via RBXCS.tostringf, then pad to the alignment
    // width (negative = left-justify). Both clauses are optional; bare `{expr}` is just the expr.
    private Expression LowerHole(InterpolationSyntax hole)
    {
        var e = LowerExpr(hole.Expression);
        if (hole.FormatClause is { } fmt)
            e = new Call(new RawExpression("RBXCS.tostringf"),
                new[] { e, new Literal(LuauString(fmt.FormatStringToken.ValueText)) });
        if (hole.AlignmentClause is { } al && model.GetConstantValue(al.Value).Value is { } w)
            e = new Call(new RawExpression("RBXCS.align"), new[] { e, new Literal(Convert.ToInt64(w).ToString()) });
        return e;
    }

    private bool IsStringConcat(BinaryExpressionSyntax bin) =>
        model.GetTypeInfo(bin).Type?.SpecialType == SpecialType.System_String
        || model.GetTypeInfo(bin.Left).Type?.SpecialType == SpecialType.System_String
        || model.GetTypeInfo(bin.Right).Type?.SpecialType == SpecialType.System_String;

    // ---- helpers ----

}
