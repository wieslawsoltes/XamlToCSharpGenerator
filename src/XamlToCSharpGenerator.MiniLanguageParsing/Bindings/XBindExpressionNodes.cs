using System.Collections.Immutable;

namespace XamlToCSharpGenerator.MiniLanguageParsing.Bindings;

public enum XBindLiteralKind
{
    Null = 0,
    Boolean = 1,
    Number = 2,
    String = 3
}

public abstract record XBindExpressionNode;

public sealed record XBindIdentifierExpression(string Identifier) : XBindExpressionNode;

public sealed record XBindTypeReferenceExpression(string TypeToken) : XBindExpressionNode;

public sealed record XBindLiteralExpression(XBindLiteralKind Kind, string RawValue) : XBindExpressionNode;

public sealed record XBindCastExpression(string TypeToken, XBindExpressionNode? Operand) : XBindExpressionNode;

public sealed record XBindMemberAccessExpression(
    XBindExpressionNode Target,
    string MemberName,
    bool IsConditional) : XBindExpressionNode;

public sealed record XBindAttachedPropertyAccessExpression(
    XBindExpressionNode Target,
    string OwnerTypeToken,
    string PropertyName,
    bool IsConditional) : XBindExpressionNode;

public sealed record XBindIndexerExpression(
    XBindExpressionNode Target,
    ImmutableArray<XBindExpressionNode> Arguments) : XBindExpressionNode;

public sealed record XBindInvocationExpression(
    XBindExpressionNode Target,
    ImmutableArray<XBindExpressionNode> Arguments) : XBindExpressionNode;
