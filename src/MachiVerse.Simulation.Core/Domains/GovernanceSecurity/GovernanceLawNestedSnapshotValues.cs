using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.WorldState;

namespace MachiVerse.Simulation.Core.Domains.GovernanceSecurity;

/// <summary>
/// Exact persistence wrapper for the existing deterministic law predicate runtime AST.
/// No executable code or additional predicate semantics are introduced here.
/// </summary>
public sealed class GovernanceRulePredicateAstNestedValueV1 : ICanonicalDomainNestedValueV1
{
    public const int MaxDepth = 64;

    public GovernanceRulePredicateAstNestedValueV1(LawPredicateNodeV1 node)
    {
        Node = node ?? throw new ArgumentNullException(nameof(node));
        ValidateCanonical();
    }

    public LawPredicateNodeV1 Node { get; }

    public void ValidateCanonical()
        => ValidateNode(Node, 0);

    public LawPredicateNodeV1 ToRuntime() => Node;

    public static GovernanceRulePredicateAstNestedValueV1 FromRuntime(LawPredicateNodeV1 node)
        => new(node);

    internal static int CompareCanonical(
        GovernanceRulePredicateAstNestedValueV1 left,
        GovernanceRulePredicateAstNestedValueV1 right)
        => CompareNode(left.Node, right.Node);

    private static void ValidateNode(LawPredicateNodeV1 node, int depth)
    {
        if (depth > MaxDepth)
            throw new InvalidDataException("governance.law-ast-depth-limit");
        node.Validate();
        foreach (var child in node.Children)
            ValidateNode(child, checked(depth + 1));
    }

    private static int CompareNode(LawPredicateNodeV1 left, LawPredicateNodeV1 right)
    {
        var comparison = left.Kind.CompareTo(right.Kind);
        if (comparison != 0) return comparison;
        comparison = CompareNullableToken(left.Key, right.Key);
        if (comparison != 0) return comparison;
        comparison = CompareNullableToken(left.TokenValue, right.TokenValue);
        if (comparison != 0) return comparison;
        comparison = Nullable.Compare(left.Minimum, right.Minimum);
        if (comparison != 0) return comparison;
        comparison = Nullable.Compare(left.Maximum, right.Maximum);
        if (comparison != 0) return comparison;
        comparison = Nullable.Compare(left.FromStep, right.FromStep);
        if (comparison != 0) return comparison;
        comparison = Nullable.Compare(left.UntilStep, right.UntilStep);
        if (comparison != 0) return comparison;
        comparison = left.Children.Count.CompareTo(right.Children.Count);
        if (comparison != 0) return comparison;
        for (var index = 0; index < left.Children.Count; index++)
        {
            comparison = CompareNode(left.Children[index], right.Children[index]);
            if (comparison != 0) return comparison;
        }
        return 0;
    }

    private static int CompareNullableToken(StableToken? left, StableToken? right)
    {
        if (left is null) return right is null ? 0 : -1;
        if (right is null) return 1;
        return string.CompareOrdinal(left.Value.Value, right.Value.Value);
    }
}

/// <summary>
/// Exact persistence wrapper for the existing deterministic law effect runtime value.
/// </summary>
public sealed class GovernanceRuleEffectAstNestedValueV1 : ICanonicalDomainNestedValueV1
{
    public GovernanceRuleEffectAstNestedValueV1(LawEffectV1 effect)
    {
        Effect = effect ?? throw new ArgumentNullException(nameof(effect));
        ValidateCanonical();
    }

    public LawEffectV1 Effect { get; }

    public void ValidateCanonical() => Effect.Validate();

    public LawEffectV1 ToRuntime() => Effect;

    public static GovernanceRuleEffectAstNestedValueV1 FromRuntime(LawEffectV1 effect)
        => new(effect);

    internal static int CompareCanonical(
        GovernanceRuleEffectAstNestedValueV1 left,
        GovernanceRuleEffectAstNestedValueV1 right)
    {
        var kind = left.Effect.Kind.CompareTo(right.Effect.Kind);
        return kind != 0
            ? kind
            : string.CompareOrdinal(left.Effect.EffectToken.Value, right.Effect.EffectToken.Value);
    }
}
