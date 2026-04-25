using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;

namespace NetworkBlaster.OData;

/// <summary>
/// Typed builder for OData <c>$filter</c> expressions.
/// </summary>
/// <remarks>
/// Three composition styles, all interchangeable:
/// <list type="bullet">
/// <item>Static factories: <c>ODataFilter.Eq("Status", "Active")</c></item>
/// <item>Operator overloads: <c>ODataFilter.Eq(...) &amp; ODataFilter.Gt(...)</c></item>
/// <item>LINQ expressions: <c>ODataFilter.For&lt;Customer&gt;(c =&gt; c.Status == "Active" &amp;&amp; c.CreatedOn &gt; date)</c></item>
/// </list>
/// All three produce the same wire output.
/// </remarks>
public abstract record ODataFilter
{
    /// <summary>Renders this filter to its OData <c>$filter</c> expression string.</summary>
    public abstract string Render();

    /// <inheritdoc/>
    public sealed override string ToString() => Render();

    // ---------- comparison factories ----------

    /// <summary><c>field eq value</c>.</summary>
    public static ODataFilter Eq(string field, object? value) => new BinaryFilter(field, "eq", value);
    /// <summary><c>field ne value</c>.</summary>
    public static ODataFilter Ne(string field, object? value) => new BinaryFilter(field, "ne", value);
    /// <summary><c>field gt value</c>.</summary>
    public static ODataFilter Gt(string field, object? value) => new BinaryFilter(field, "gt", value);
    /// <summary><c>field lt value</c>.</summary>
    public static ODataFilter Lt(string field, object? value) => new BinaryFilter(field, "lt", value);
    /// <summary><c>field ge value</c>.</summary>
    public static ODataFilter Ge(string field, object? value) => new BinaryFilter(field, "ge", value);
    /// <summary><c>field le value</c>.</summary>
    public static ODataFilter Le(string field, object? value) => new BinaryFilter(field, "le", value);

    // ---------- string functions ----------

    /// <summary><c>contains(field, 'value')</c>.</summary>
    public static ODataFilter Contains(string field, string value)   => new FunctionFilter("contains",   field, value);
    /// <summary><c>startswith(field, 'value')</c>.</summary>
    public static ODataFilter StartsWith(string field, string value) => new FunctionFilter("startswith", field, value);
    /// <summary><c>endswith(field, 'value')</c>.</summary>
    public static ODataFilter EndsWith(string field, string value)   => new FunctionFilter("endswith",   field, value);

    // ---------- collection helper ----------

    /// <summary>
    /// Renders <c>field eq v1 or field eq v2 or ...</c>. Empty values list throws.
    /// </summary>
    public static ODataFilter In(string field, params object?[] values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Length == 0) throw new ArgumentException("In() requires at least one value.", nameof(values));
        return new InFilter(field, values);
    }

    // ---------- logical combinators ----------

    /// <summary>Combines two filters with <c>and</c>. Equivalent to the <c>&amp;</c> operator.</summary>
    public static ODataFilter And(ODataFilter left, ODataFilter right) => new LogicalFilter(left, "and", right);
    /// <summary>Combines two filters with <c>or</c>. Equivalent to the <c>|</c> operator.</summary>
    public static ODataFilter Or (ODataFilter left, ODataFilter right) => new LogicalFilter(left, "or",  right);
    /// <summary>Negates a filter (<c>not (...)</c>). Equivalent to the <c>!</c> operator.</summary>
    public static ODataFilter Not(ODataFilter inner)                   => new NotFilter(inner);

    // ---------- escape hatch ----------

    /// <summary>Wrap a hand-authored OData filter expression. No escaping is performed.</summary>
    public static ODataFilter Raw(string expression) => new RawFilter(expression);

    // ---------- LINQ entry-point ----------

    /// <summary>
    /// Translates a typed boolean expression on <typeparamref name="T"/> into an OData filter.
    /// Supported: <c>== != &gt; &lt; &gt;= &lt;= &amp;&amp; || !</c>, <c>string.Contains/StartsWith/EndsWith</c>,
    /// member access on the parameter, closure-captured constants.
    /// </summary>
    public static ODataFilter For<T>(Expression<Func<T, bool>> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return ExpressionTranslator.Translate(predicate.Body, predicate.Parameters[0]);
    }

    // ---------- operator overloads ----------

    /// <summary>Operator alias for <see cref="And(ODataFilter, ODataFilter)"/>.</summary>
    public static ODataFilter operator &(ODataFilter left, ODataFilter right) => And(left, right);
    /// <summary>Operator alias for <see cref="Or(ODataFilter, ODataFilter)"/>.</summary>
    public static ODataFilter operator |(ODataFilter left, ODataFilter right) => Or(left, right);
    /// <summary>Operator alias for <see cref="Not(ODataFilter)"/>.</summary>
    public static ODataFilter operator !(ODataFilter inner) => Not(inner);

    /// <summary>Required by the C# spec when overloading <c>&amp;</c> / <c>|</c>; not meant to be called directly.</summary>
    public static bool operator true(ODataFilter _) => false;
    /// <summary>Required by the C# spec when overloading <c>&amp;</c> / <c>|</c>; not meant to be called directly.</summary>
    public static bool operator false(ODataFilter _) => false;

    // ---------- internal node types ----------

    private sealed record BinaryFilter(string Field, string Op, object? Value) : ODataFilter
    {
        public override string Render() => $"{Field} {Op} {ODataLiteral.From(Value)}";
    }

    private sealed record FunctionFilter(string Function, string Field, string Value) : ODataFilter
    {
        public override string Render() => $"{Function}({Field}, {ODataLiteral.String(Value)})";
    }

    private sealed record InFilter(string Field, object?[] Values) : ODataFilter
    {
        public override string Render()
        {
            var sb = new StringBuilder();
            for (var i = 0; i < Values.Length; i++)
            {
                if (i > 0) sb.Append(" or ");
                sb.Append(Field).Append(" eq ").Append(ODataLiteral.From(Values[i]));
            }
            return Values.Length == 1 ? sb.ToString() : "(" + sb + ")";
        }
    }

    private sealed record LogicalFilter(ODataFilter Left, string Op, ODataFilter Right) : ODataFilter
    {
        public override string Render() => $"({Left.Render()} {Op} {Right.Render()})";
    }

    private sealed record NotFilter(ODataFilter Inner) : ODataFilter
    {
        public override string Render() => $"not ({Inner.Render()})";
    }

    private sealed record RawFilter(string Expression) : ODataFilter
    {
        public override string Render() => Expression;
    }

    // ---------- LINQ → ODataFilter translator ----------

    private static class ExpressionTranslator
    {
        public static ODataFilter Translate(Expression node, ParameterExpression parameter)
        {
            switch (node)
            {
                case BinaryExpression bin when IsLogical(bin.NodeType):
                    var left = Translate(bin.Left, parameter);
                    var right = Translate(bin.Right, parameter);
                    return bin.NodeType == ExpressionType.AndAlso ? And(left, right) : Or(left, right);

                case BinaryExpression bin when IsComparison(bin.NodeType):
                    return TranslateComparison(bin, parameter);

                case UnaryExpression un when un.NodeType == ExpressionType.Not:
                    return Not(Translate(un.Operand, parameter));

                case UnaryExpression un when un.NodeType == ExpressionType.Convert:
                    return Translate(un.Operand, parameter);

                case MethodCallExpression mc:
                    return TranslateMethodCall(mc, parameter);

                case MemberExpression me when me.Type == typeof(bool) && IsParameterAccess(me, parameter):
                    return Eq(GetFieldName(me), true);
            }

            throw new NotSupportedException(
                $"NetworkBlaster.OData: expression '{node}' (NodeType={node.NodeType}) is not supported in filter translation.");
        }

        private static ODataFilter TranslateComparison(BinaryExpression bin, ParameterExpression parameter)
        {
            var (field, value, flipped) = ResolveSides(bin, parameter);
            var op = bin.NodeType switch
            {
                ExpressionType.Equal              => "eq",
                ExpressionType.NotEqual           => "ne",
                ExpressionType.GreaterThan        => flipped ? "lt" : "gt",
                ExpressionType.LessThan           => flipped ? "gt" : "lt",
                ExpressionType.GreaterThanOrEqual => flipped ? "le" : "ge",
                ExpressionType.LessThanOrEqual    => flipped ? "ge" : "le",
                _ => throw new NotSupportedException($"Unsupported comparison: {bin.NodeType}"),
            };
            return new BinaryFilter(field, op, value);
        }

        private static (string field, object? value, bool flipped) ResolveSides(BinaryExpression bin, ParameterExpression parameter)
        {
            if (TryGetField(bin.Left, parameter, out var leftField))
                return (leftField!, EvaluateConstant(bin.Right), false);
            if (TryGetField(bin.Right, parameter, out var rightField))
                return (rightField!, EvaluateConstant(bin.Left), true);
            throw new NotSupportedException(
                $"Comparison must reference a parameter member on at least one side: {bin}");
        }

        private static ODataFilter TranslateMethodCall(MethodCallExpression mc, ParameterExpression parameter)
        {
            // string.Contains / StartsWith / EndsWith on the parameter member
            if (mc.Object is MemberExpression target && IsParameterAccess(target, parameter)
                && mc.Method.DeclaringType == typeof(string)
                && mc.Arguments.Count == 1)
            {
                var field = GetFieldName(target);
                var value = EvaluateConstant(mc.Arguments[0])?.ToString() ?? string.Empty;
                return mc.Method.Name switch
                {
                    nameof(string.Contains)   => Contains  (field, value),
                    nameof(string.StartsWith) => StartsWith(field, value),
                    nameof(string.EndsWith)   => EndsWith  (field, value),
                    _ => throw new NotSupportedException($"string.{mc.Method.Name} is not supported in filter translation."),
                };
            }

            throw new NotSupportedException($"Method call '{mc.Method.DeclaringType?.Name}.{mc.Method.Name}' is not supported.");
        }

        private static bool TryGetField(Expression expr, ParameterExpression parameter, out string? field)
        {
            field = null;
            while (expr is UnaryExpression { NodeType: ExpressionType.Convert } un) expr = un.Operand;
            if (expr is MemberExpression me && IsParameterAccess(me, parameter))
            {
                field = GetFieldName(me);
                return true;
            }
            return false;
        }

        private static bool IsParameterAccess(MemberExpression member, ParameterExpression parameter)
        {
            Expression? expr = member;
            while (expr is MemberExpression me) expr = me.Expression;
            return ReferenceEquals(expr, parameter);
        }

        private static string GetFieldName(MemberExpression member)
        {
            // Support nested members like c.Address.City → "Address/City" per OData v4.
            var parts = new Stack<string>();
            Expression? expr = member;
            while (expr is MemberExpression me)
            {
                parts.Push(me.Member.Name);
                expr = me.Expression;
            }
            return string.Join("/", parts);
        }

        private static object? EvaluateConstant(Expression expr)
        {
            if (expr is ConstantExpression c) return c.Value;
            var lambda = Expression.Lambda(expr).Compile();
            return lambda.DynamicInvoke();
        }

        private static bool IsLogical(ExpressionType t)
            => t == ExpressionType.AndAlso || t == ExpressionType.OrElse;

        private static bool IsComparison(ExpressionType t) => t
            is ExpressionType.Equal
            or ExpressionType.NotEqual
            or ExpressionType.GreaterThan
            or ExpressionType.GreaterThanOrEqual
            or ExpressionType.LessThan
            or ExpressionType.LessThanOrEqual;
    }

    // ---------- typed member-access path helper (used by ODataQuery) ----------

    internal static string Path<T>(Expression<Func<T, object?>> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        var body = selector.Body;
        if (body is UnaryExpression { NodeType: ExpressionType.Convert } un) body = un.Operand;
        if (body is not MemberExpression me)
            throw new ArgumentException($"Expression must be a simple member access, got: {selector}", nameof(selector));

        var parts = new Stack<string>();
        Expression? expr = me;
        while (expr is MemberExpression member)
        {
            parts.Push(member.Member.Name);
            expr = member.Expression;
        }
        return string.Join("/", parts);
    }
}
