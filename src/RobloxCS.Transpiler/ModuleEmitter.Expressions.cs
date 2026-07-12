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
                return new FunctionExpression(new[] { sl.Parameter.Identifier.Text }, LambdaBody(sl.Body));
            case ParenthesizedLambdaExpressionSyntax pl:
                return new FunctionExpression(pl.ParameterList.Parameters.Select(p => p.Identifier.Text).ToList(), LambdaBody(pl.Body));
            case AnonymousMethodExpressionSyntax am:
                return new FunctionExpression(am.ParameterList?.Parameters.Select(p => p.Identifier.Text).ToList() ?? new List<string>(), LambdaBody(am.Body));
            case ConditionalAccessExpressionSyntax ca:
                return LowerConditionalAccess(ca);
            case CastExpressionSyntax cast:
                return LowerExpr(cast.Expression); // ponytail: cast erased (no runtime conv yet)
            default:
                return new RawExpression($"nil --[[rbxcs unsupported: {expr.Kind()}]]");
        }
    }

    private static Expression LowerLiteral(LiteralExpressionSyntax lit) => lit.Kind() switch
    {
        SyntaxKind.NullLiteralExpression => new RawExpression("nil"),
        SyntaxKind.TrueLiteralExpression => new Literal("true"),
        SyntaxKind.FalseLiteralExpression => new Literal("false"),
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
                return new Identifier(id.Identifier.Text);
            case IEventSymbol { IsStatic: false }:
                return new MemberAccess(new Identifier("self"), id.Identifier.Text);
            case IPropertySymbol { IsStatic: false } bp when IsBodiedProperty(bp):
                return new MethodCall(new Identifier("self"), "get_" + id.Identifier.Text, Array.Empty<Expression>());
            case IPropertySymbol { IsStatic: true } bps when IsBodiedProperty(bps):
                return new Call(new MemberAccess(new RawExpression(RequireLocalName((INamedTypeSymbol)sym.ContainingType!)), "get_" + id.Identifier.Text), Array.Empty<Expression>());
            case IFieldSymbol { IsStatic: false } or IPropertySymbol { IsStatic: false }:
                return new MemberAccess(new Identifier("self"), id.Identifier.Text);
            case IFieldSymbol { IsStatic: true } or IPropertySymbol { IsStatic: true }:
            {
                var owner = (INamedTypeSymbol)sym.ContainingType!;
                return new MemberAccess(new RawExpression(RequireLocalName(owner)), id.Identifier.Text);
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
            return new MemberAccess(new RawExpression(RequireLocalName(owner)), memberName);

        return new MemberAccess(LowerExpr(ma.Expression), memberName);
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
                    return new Call(new MemberAccess(new RawExpression(RequireLocalName(owner)), methodName), args);
                return new MethodCall(LowerExpr(ma.Expression), methodName, args);
            }
            case IdentifierNameSyntax id:
                return LowerUnqualifiedCall(id.Identifier.Text, sym, isGlobal, args);
            case GenericNameSyntax gen:
                return LowerUnqualifiedCall(gen.Identifier.Text, sym, isGlobal, args);
            default:
                return new Call(LowerExpr(inv.Expression), args);
        }
    }

    // Maps a curated System.* surface (F9) to Luau natives / runtime. Returns null if not a BCL call.
    private Expression? MapBclCall(InvocationExpressionSyntax inv, IMethodSymbol? sym, List<Expression> args)
    {
        var ct = sym?.ContainingType?.OriginalDefinition.ToDisplayString();
        var recv = inv.Expression is MemberAccessExpressionSyntax ma ? LowerExpr(ma.Expression) : new Identifier("self");

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

        // string `+` -> Luau `..`
        if (op == "+" && IsStringConcat(bin))
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
                    parts.Add(new InterpolationPart(null, LowerExpr(hole.Expression)));
                    break;
            }
        }
        return new InterpolatedString(parts);
    }

    private bool IsStringConcat(BinaryExpressionSyntax bin) =>
        model.GetTypeInfo(bin).Type?.SpecialType == SpecialType.System_String
        || model.GetTypeInfo(bin.Left).Type?.SpecialType == SpecialType.System_String
        || model.GetTypeInfo(bin.Right).Type?.SpecialType == SpecialType.System_String;

    // ---- helpers ----

}
