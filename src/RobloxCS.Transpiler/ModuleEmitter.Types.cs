using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RobloxCS.Luau;

namespace RobloxCS.Transpiler;

internal sealed partial class ModuleEmitter
{
    private void LowerType(TypeDeclarationSyntax decl, INamedTypeSymbol sym, List<Statement> output)
    {
        var name = sym.Name;
        var isStruct = sym.TypeKind == TypeKind.Struct;

        var baseType = isStruct ? null : sym.BaseType;
        var baseIsUser = IsUserType(baseType);

        Expression ctorHelper = isStruct
            ? new Call(new MemberAccess(new Identifier("RBXCS"), "struct"),
                new Expression[] { new Literal($"\"{name}\"") })
            : new Call(new MemberAccess(new Identifier("RBXCS"), "class"),
                new Expression[]
                {
                    new Literal($"\"{name}\""),
                    baseIsUser ? new RawExpression(RequireLocalName(baseType!)) : new RawExpression("nil"),
                });
        output.Add(new LocalDeclaration(name, ctorHelper));

        var fieldInits = CollectFieldInits(decl);
        var ctor = decl.Members.OfType<ConstructorDeclarationSyntax>().FirstOrDefault();

        if (ctor is not null || fieldInits.Count > 0 || baseIsUser)
        {
            var body = new Chunk();
            body.Statements.AddRange(fieldInits);

            if (baseIsUser)
            {
                var baseLocal = RequireLocalName(baseType!);
                var baseArgs = new List<Expression> { new Identifier("self") };
                if (ctor?.Initializer is { ThisOrBaseKeyword.RawKind: (int)SyntaxKind.BaseKeyword } init)
                    baseArgs.AddRange(LowerArguments(init.ArgumentList.Arguments));
                body.Statements.Add(new ExpressionStatement(new Call(
                    new MemberAccess(new RawExpression(baseLocal), "__ctor"), baseArgs)));
            }

            if (ctor?.Body is not null)
                foreach (var s in ctor.Body.Statements)
                    body.Statements.Add(LowerStatement(s));

            var pars = ctor?.ParameterList.Parameters.Select(p => LuauId(p.Identifier.Text)).ToList() ?? new List<string>();
            output.Add(new FunctionStatement(new[] { name, "__ctor" }, isMethod: true, pars, body));
        }

        foreach (var method in decl.Members.OfType<MethodDeclarationSyntax>())
        {
            var msym = model.GetDeclaredSymbol(method);
            var isStatic = msym?.IsStatic ?? method.Modifiers.Any(SyntaxKind.StaticKeyword);

            var reifying = msym is not null && reification.NeedsTokens(msym);
            _tokenParams = reifying
                ? new HashSet<string>(msym!.TypeParameters.Select(tp => tp.Name))
                : new HashSet<string>();

            // iterator method (`yield return`) -> returns a lazy enumerable via RBXCS.iterator(yield)
            var isIterator = method.Body?.DescendantNodes().OfType<YieldStatementSyntax>().Any() ?? false;
            _yieldVar = isIterator ? "__yield" : null;

            var body = new Chunk();
            if (method.Body is not null)
                foreach (var s in method.Body.Statements)
                    body.Statements.Add(LowerStatement(s));
            else if (method.ExpressionBody is not null)
                body.Statements.Add(new Return(CopyIfNeeded(method.ExpressionBody.Expression, LowerExpr(method.ExpressionBody.Expression))));

            if (isIterator)
            {
                var wrapped = new Chunk();
                wrapped.Statements.Add(new Return(new Call(
                    new MemberAccess(new Identifier("RBXCS"), "iterator"),
                    new[] { (Expression)new FunctionExpression(new[] { _yieldVar! }, body) })));
                body = wrapped;
                _yieldVar = null;
            }

            // async T M() -> M returns a Promise; its body runs inside RBXCS.async(...)
            if (msym?.IsAsync ?? method.Modifiers.Any(SyntaxKind.AsyncKeyword))
            {
                var wrapped = new Chunk();
                wrapped.Statements.Add(new Return(new Call(
                    new MemberAccess(new Identifier("RBXCS"), "async"),
                    new[] { (Expression)new FunctionExpression(Array.Empty<string>(), body) })));
                body = wrapped;
            }

            var pars = method.ParameterList.Parameters.Select(p => LuauId(p.Identifier.Text)).ToList();
            if (reifying)
                pars.AddRange(msym!.TypeParameters.Select(tp => $"__t_{tp.Name}"));

            output.Add(new FunctionStatement(
                new[] { name, method.Identifier.Text }, isMethod: !isStatic, pars, body));
            _tokenParams = new HashSet<string>();
        }

        foreach (var prop in decl.Members.OfType<PropertyDeclarationSyntax>())
            EmitBodiedProperty(name, prop, output);

        foreach (var indexer in decl.Members.OfType<IndexerDeclarationSyntax>())
            EmitIndexer(name, indexer, output);
    }

    // User enum -> `local E = { Member = value, ... }`. Members carry their constant int value.
    private Statement EmitEnum(EnumDeclarationSyntax e)
    {
        var entries = new List<TableEntry>();
        long next = 0;
        foreach (var member in e.Members)
        {
            var sym = model.GetDeclaredSymbol(member);
            var value = sym?.ConstantValue is not null ? Convert.ToInt64(sym.ConstantValue) : next;
            entries.Add(new TableEntry(member.Identifier.Text, new Literal(value.ToString())));
            next = value + 1;
        }
        return new LocalDeclaration(e.Identifier.Text, new TableConstructor(entries));
    }

    // Bodied (non-auto) properties -> get_X / set_X accessor methods. Auto-props stay table fields.
    private void EmitBodiedProperty(string typeName, PropertyDeclarationSyntax prop, List<Statement> output)
    {
        var isStatic = prop.Modifiers.Any(SyntaxKind.StaticKeyword);
        var pname = prop.Identifier.Text;

        if (prop.ExpressionBody is not null)
        {
            var body = new Chunk();
            body.Statements.Add(new Return(LowerExpr(prop.ExpressionBody.Expression)));
            output.Add(new FunctionStatement(new[] { typeName, "get_" + pname }, !isStatic, Array.Empty<string>(), body));
            return;
        }
        if (prop.AccessorList is null)
            return;

        foreach (var acc in prop.AccessorList.Accessors)
        {
            if (acc.Body is null && acc.ExpressionBody is null)
                continue; // auto accessor -> field
            var isGet = acc.Keyword.IsKind(SyntaxKind.GetKeyword);
            var body = LowerAccessorBody(acc);
            var pars = isGet ? Array.Empty<string>() : new[] { "value" };
            output.Add(new FunctionStatement(new[] { typeName, (isGet ? "get_" : "set_") + pname }, !isStatic, pars, body));
        }
    }

    private void EmitIndexer(string typeName, IndexerDeclarationSyntax indexer, List<Statement> output)
    {
        var isStatic = indexer.Modifiers.Any(SyntaxKind.StaticKeyword);
        var idxPars = indexer.ParameterList.Parameters.Select(p => LuauId(p.Identifier.Text)).ToList();

        if (indexer.ExpressionBody is not null)
        {
            var body = new Chunk();
            body.Statements.Add(new Return(LowerExpr(indexer.ExpressionBody.Expression)));
            output.Add(new FunctionStatement(new[] { typeName, "get_Item" }, !isStatic, idxPars, body));
            return;
        }
        if (indexer.AccessorList is null)
            return;

        foreach (var acc in indexer.AccessorList.Accessors)
        {
            if (acc.Body is null && acc.ExpressionBody is null)
                continue;
            var isGet = acc.Keyword.IsKind(SyntaxKind.GetKeyword);
            var body = LowerAccessorBody(acc);
            var pars = isGet ? idxPars : idxPars.Concat(new[] { "value" }).ToList();
            output.Add(new FunctionStatement(new[] { typeName, isGet ? "get_Item" : "set_Item" }, !isStatic, pars, body));
        }
    }

    private Chunk LowerAccessorBody(AccessorDeclarationSyntax acc)
    {
        var body = new Chunk();
        if (acc.Body is not null)
            foreach (var s in acc.Body.Statements)
                body.Statements.Add(LowerStatement(s));
        else if (acc.ExpressionBody is not null)
            body.Statements.Add(acc.Keyword.IsKind(SyntaxKind.GetKeyword)
                ? new Return(LowerExpr(acc.ExpressionBody.Expression))
                : new ExpressionStatement(LowerExpr(acc.ExpressionBody.Expression)));
        return body;
    }

    private bool IsBodiedProperty(IPropertySymbol? p)
    {
        if (p is null)
            return false;
        foreach (var r in p.DeclaringSyntaxReferences)
        {
            var node = r.GetSyntax();
            if (node is PropertyDeclarationSyntax { ExpressionBody: not null })
                return true;
            if (node is BasePropertyDeclarationSyntax { AccessorList: { } list }
                && list.Accessors.Any(a => a.Body is not null || a.ExpressionBody is not null))
                return true;
        }
        return false;
    }

    private List<Statement> CollectFieldInits(TypeDeclarationSyntax decl)
    {
        var inits = new List<Statement>();
        foreach (var field in decl.Members.OfType<FieldDeclarationSyntax>())
        {
            if (field.Modifiers.Any(SyntaxKind.StaticKeyword))
                continue;
            var fieldType = model.GetTypeInfo(field.Declaration.Type).Type;
            foreach (var v in field.Declaration.Variables)
                if (v.Initializer is not null)
                    inits.Add(new Assignment(
                        new MemberAccess(new Identifier("self"), v.Identifier.Text),
                        CopyIfNeeded(v.Initializer.Value, LowerExpr(v.Initializer.Value))));
                else if (ZeroDefault(fieldType) is { } zero)
                    inits.Add(new Assignment(new MemberAccess(new Identifier("self"), v.Identifier.Text), zero)); // C# value-type fields zero-init
        }
        foreach (var prop in decl.Members.OfType<PropertyDeclarationSyntax>())
        {
            if (prop.Modifiers.Any(SyntaxKind.StaticKeyword) || IsBodiedProperty(model.GetDeclaredSymbol(prop)))
                continue;
            if (prop.Initializer is not null)
                inits.Add(new Assignment(
                    new MemberAccess(new Identifier("self"), prop.Identifier.Text),
                    CopyIfNeeded(prop.Initializer.Value, LowerExpr(prop.Initializer.Value))));
            else if (ZeroDefault(model.GetTypeInfo(prop.Type).Type) is { } zero)
                inits.Add(new Assignment(new MemberAccess(new Identifier("self"), prop.Identifier.Text), zero));
        }
        // events -> a signal object per event field
        foreach (var ev in decl.Members.OfType<EventFieldDeclarationSyntax>())
            if (!ev.Modifiers.Any(SyntaxKind.StaticKeyword))
                foreach (var v in ev.Declaration.Variables)
                    inits.Add(new Assignment(
                        new MemberAccess(new Identifier("self"), v.Identifier.Text),
                        new Call(new MemberAccess(new Identifier("RBXCS"), "signal"), Array.Empty<Expression>())));
        return inits;
    }

    // C# zero-init for a value-type field: number->0, bool->false. Reference/nullable -> nil (skip).
    private static Expression? ZeroDefault(ITypeSymbol? t) => t?.SpecialType switch
    {
        SpecialType.System_Boolean => new Literal("false"),
        SpecialType.System_Byte or SpecialType.System_SByte or SpecialType.System_Int16
            or SpecialType.System_UInt16 or SpecialType.System_Int32 or SpecialType.System_UInt32
            or SpecialType.System_Int64 or SpecialType.System_UInt64 or SpecialType.System_Single
            or SpecialType.System_Double or SpecialType.System_Decimal => new Literal("0"),
        _ => null,
    };

    // Struct value semantics (decision #4): reading an existing struct *place* (variable, field,
    // element, this) that then flows into a new binding/arg/return must copy, so the two don't alias.
    // Fresh values (new S(), method results, another copy) are already unique -> no copy.
    // User-declared structs only; primitives and Roblox native value types have no source decl -> exempt.
    private Expression CopyIfNeeded(ExpressionSyntax source, Expression lowered)
    {
        if (IsUserStruct(model.GetTypeInfo(source).Type) && IsPlace(source))
            return new Call(new MemberAccess(new Identifier("RBXCS"), "copy"), new[] { lowered });
        return lowered;
    }

    private static bool IsUserStruct(ITypeSymbol? t) =>
        t is INamedTypeSymbol { TypeKind: TypeKind.Struct, SpecialType: SpecialType.None } n
        && n.DeclaringSyntaxReferences.Length > 0;

    private static bool IsPlace(ExpressionSyntax e) =>
        e is IdentifierNameSyntax or MemberAccessExpressionSyntax or ElementAccessExpressionSyntax or ThisExpressionSyntax;

    private List<Expression> LowerArguments(IEnumerable<ArgumentSyntax> args) =>
        args.Select(a => a.RefKindKeyword.IsKind(SyntaxKind.None)
            ? CopyIfNeeded(a.Expression, LowerExpr(a.Expression)) // by-value struct -> copy
            : LowerExpr(a.Expression)) // ref/in/out -> alias, no copy
            .ToList();

    // ---- statements ----

}
