using MachiVerse.Simulation.Core.Determinism;

namespace MachiVerse.Simulation.Core.Domains.GovernanceSecurity;

public enum LawPredicateNodeKindV1 : byte
{
    And = 1,
    Or = 2,
    Not = 3,
    FactEquals = 4,
    FactRange = 5,
    SubjectHasStatus = 6,
    RelationExists = 7,
    SpatialWithin = 8,
    TimeStepRange = 9,
}

public static class LawPredicateNodeKindRegistryV1
{
    public static LawPredicateNodeKindV1 Decode(string kind)
        => kind switch
        {
            "AND" => LawPredicateNodeKindV1.And,
            "OR" => LawPredicateNodeKindV1.Or,
            "NOT" => LawPredicateNodeKindV1.Not,
            "FACT_EQUALS" => LawPredicateNodeKindV1.FactEquals,
            "FACT_RANGE" => LawPredicateNodeKindV1.FactRange,
            "SUBJECT_HAS_STATUS" => LawPredicateNodeKindV1.SubjectHasStatus,
            "RELATION_EXISTS" => LawPredicateNodeKindV1.RelationExists,
            "SPATIAL_WITHIN" => LawPredicateNodeKindV1.SpatialWithin,
            "TIME_STEP_RANGE" => LawPredicateNodeKindV1.TimeStepRange,
            _ => throw new InvalidDataException("governance.law-ast-node-unregistered"),
        };
}

public sealed record LawEvaluationContextV1(
    OpaqueId128 JurisdictionRef,
    ulong Step,
    IReadOnlyDictionary<StableToken, StableToken> TokenFacts,
    IReadOnlyDictionary<StableToken, long> NumericFacts,
    IReadOnlySet<StableToken> SubjectStatuses,
    IReadOnlySet<StableToken> Relations,
    IReadOnlySet<StableToken> SpatialScopes)
{
    public void Validate()
    {
        if (JurisdictionRef.IsZero)
            throw new InvalidDataException("governance.jurisdiction-id-zero");
        ArgumentNullException.ThrowIfNull(TokenFacts);
        ArgumentNullException.ThrowIfNull(NumericFacts);
        ArgumentNullException.ThrowIfNull(SubjectStatuses);
        ArgumentNullException.ThrowIfNull(Relations);
        ArgumentNullException.ThrowIfNull(SpatialScopes);
    }
}

public sealed record LawPredicateNodeV1(
    LawPredicateNodeKindV1 Kind,
    IReadOnlyList<LawPredicateNodeV1> Children,
    StableToken? Key = null,
    StableToken? TokenValue = null,
    long? Minimum = null,
    long? Maximum = null,
    ulong? FromStep = null,
    ulong? UntilStep = null)
{
    public void Validate()
    {
        if (!Enum.IsDefined(Kind))
            throw new InvalidDataException("governance.law-ast-node-unregistered");
        ArgumentNullException.ThrowIfNull(Children);
        if (Children.Any(static child => child is null))
            throw new InvalidDataException("governance.law-ast-child-null");
        foreach (var child in Children) child.Validate();

        switch (Kind)
        {
            case LawPredicateNodeKindV1.And:
            case LawPredicateNodeKindV1.Or:
                if (Children.Count < 2) throw new InvalidDataException("governance.law-ast-arity-invalid");
                RequireNoLeafPayload();
                break;
            case LawPredicateNodeKindV1.Not:
                if (Children.Count != 1) throw new InvalidDataException("governance.law-ast-arity-invalid");
                RequireNoLeafPayload();
                break;
            case LawPredicateNodeKindV1.FactEquals:
                RequireLeaf();
                if (Key is null || TokenValue is null || Minimum is not null || Maximum is not null || FromStep is not null || UntilStep is not null)
                    throw new InvalidDataException("governance.law-ast-leaf-payload-invalid");
                break;
            case LawPredicateNodeKindV1.FactRange:
                RequireLeaf();
                if (Key is null || Minimum is null || Maximum is null || Minimum > Maximum ||
                    TokenValue is not null || FromStep is not null || UntilStep is not null)
                    throw new InvalidDataException("governance.law-ast-range-invalid");
                break;
            case LawPredicateNodeKindV1.SubjectHasStatus:
            case LawPredicateNodeKindV1.RelationExists:
            case LawPredicateNodeKindV1.SpatialWithin:
                RequireLeaf();
                if (TokenValue is null || Key is not null || Minimum is not null || Maximum is not null || FromStep is not null || UntilStep is not null)
                    throw new InvalidDataException("governance.law-ast-leaf-payload-invalid");
                break;
            case LawPredicateNodeKindV1.TimeStepRange:
                RequireLeaf();
                if (FromStep is null || UntilStep is null || FromStep > UntilStep ||
                    Key is not null || TokenValue is not null || Minimum is not null || Maximum is not null)
                    throw new InvalidDataException("governance.law-ast-step-range-invalid");
                break;
            default:
                throw new InvalidDataException("governance.law-ast-node-unregistered");
        }
    }

    public bool Evaluate(LawEvaluationContextV1 context)
    {
        Validate();
        ArgumentNullException.ThrowIfNull(context);
        context.Validate();
        return Kind switch
        {
            LawPredicateNodeKindV1.And => Children.All(child => child.Evaluate(context)),
            LawPredicateNodeKindV1.Or => Children.Any(child => child.Evaluate(context)),
            LawPredicateNodeKindV1.Not => !Children[0].Evaluate(context),
            LawPredicateNodeKindV1.FactEquals =>
                context.TokenFacts.TryGetValue(Key!.Value, out var actualToken) && actualToken == TokenValue!.Value,
            LawPredicateNodeKindV1.FactRange =>
                context.NumericFacts.TryGetValue(Key!.Value, out var actualNumber) &&
                actualNumber >= Minimum!.Value && actualNumber <= Maximum!.Value,
            LawPredicateNodeKindV1.SubjectHasStatus => context.SubjectStatuses.Contains(TokenValue!.Value),
            LawPredicateNodeKindV1.RelationExists => context.Relations.Contains(TokenValue!.Value),
            LawPredicateNodeKindV1.SpatialWithin => context.SpatialScopes.Contains(TokenValue!.Value),
            LawPredicateNodeKindV1.TimeStepRange =>
                context.Step >= FromStep!.Value && context.Step <= UntilStep!.Value,
            _ => throw new InvalidDataException("governance.law-ast-node-unregistered"),
        };
    }

    private void RequireLeaf()
    {
        if (Children.Count != 0) throw new InvalidDataException("governance.law-ast-leaf-has-children");
    }

    private void RequireNoLeafPayload()
    {
        if (Key is not null || TokenValue is not null || Minimum is not null || Maximum is not null ||
            FromStep is not null || UntilStep is not null)
            throw new InvalidDataException("governance.law-ast-branch-payload-invalid");
    }
}

public enum LawEffectKindV1 : byte
{
    Classify = 1,
    Permit = 2,
    Prohibit = 3,
    CreateClaim = 4,
    CreateObligation = 5,
    AuthorizeEnforcement = 6,
}

public sealed record LawEffectV1(LawEffectKindV1 Kind, StableToken EffectToken)
{
    public void Validate()
    {
        if (!Enum.IsDefined(Kind))
            throw new InvalidDataException("governance.law-effect-unregistered");
    }

    public bool IsTerminalClassification => Kind is LawEffectKindV1.Classify or LawEffectKindV1.Permit or LawEffectKindV1.Prohibit;
}

public sealed record LegalRuleV1(
    OpaqueId128 RuleId,
    OpaqueId128 JurisdictionRef,
    int Priority,
    uint Specificity,
    ulong EffectiveFromStep,
    ulong? EffectiveUntilStep,
    LawPredicateNodeV1 Predicate,
    LawEffectV1 Effect)
{
    public void Validate()
    {
        if (RuleId.IsZero || JurisdictionRef.IsZero)
            throw new InvalidDataException("governance.law-rule-id-zero");
        if (EffectiveUntilStep is not null && EffectiveUntilStep.Value < EffectiveFromStep)
            throw new InvalidDataException("governance.law-effective-period-invalid");
        ArgumentNullException.ThrowIfNull(Predicate);
        ArgumentNullException.ThrowIfNull(Effect);
        Predicate.Validate();
        Effect.Validate();
    }

    public bool IsApplicableTo(LawEvaluationContextV1 context)
    {
        Validate();
        ArgumentNullException.ThrowIfNull(context);
        context.Validate();
        return context.JurisdictionRef == JurisdictionRef &&
               context.Step >= EffectiveFromStep &&
               (EffectiveUntilStep is null || context.Step <= EffectiveUntilStep.Value) &&
               Predicate.Evaluate(context);
    }
}

public enum LegalResolutionStatusV1 : byte
{
    Resolved = 1,
    NoApplicableRule = 2,
    Conflict = 3,
}

public sealed record LegalResolutionResultV1(
    LegalResolutionStatusV1 Status,
    LawEffectV1? Effect,
    IReadOnlyList<OpaqueId128> ConsideredRuleIds);

public static class DeterministicLegalRuleResolverV1
{
    public static LegalResolutionResultV1 Resolve(
        LawEvaluationContextV1 context,
        IEnumerable<LegalRuleV1> candidates)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(candidates);
        context.Validate();

        var materialized = candidates.Select(rule =>
        {
            ArgumentNullException.ThrowIfNull(rule);
            rule.Validate();
            return rule;
        }).ToArray();
        if (materialized.Select(static rule => rule.RuleId).Distinct().Count() != materialized.Length)
            throw new InvalidDataException("governance.law-rule-id-duplicate");

        var applicable = materialized
            .Where(rule => rule.IsApplicableTo(context))
            .OrderBy(static rule => rule.Priority)
            .ThenByDescending(static rule => rule.Specificity)
            .ThenBy(static rule => rule.RuleId)
            .ToArray();

        if (applicable.Length == 0)
            return new LegalResolutionResultV1(
                LegalResolutionStatusV1.NoApplicableRule,
                null,
                Array.Empty<OpaqueId128>());

        var leader = applicable[0];
        var leading = applicable
            .TakeWhile(rule => rule.Priority == leader.Priority && rule.Specificity == leader.Specificity)
            .ToArray();
        var terminal = leading.Where(static rule => rule.Effect.IsTerminalClassification).ToArray();
        if (HasTerminalConflict(terminal))
            return new LegalResolutionResultV1(
                LegalResolutionStatusV1.Conflict,
                null,
                Array.AsReadOnly(terminal.Select(static rule => rule.RuleId).Order().ToArray()));

        return new LegalResolutionResultV1(
            LegalResolutionStatusV1.Resolved,
            leader.Effect,
            Array.AsReadOnly(applicable.Select(static rule => rule.RuleId).ToArray()));
    }

    private static bool HasTerminalConflict(IReadOnlyList<LegalRuleV1> terminal)
    {
        if (terminal.Count < 2) return false;
        var first = terminal[0].Effect;
        return terminal.Skip(1).Any(rule => rule.Effect != first);
    }
}

public sealed record EnforcementOrderV1(
    OpaqueId128 OrderId,
    OpaqueId128 IssuingAuthorityRef,
    OpaqueId128 TargetRef,
    StableToken ActionKind,
    ulong EffectiveFromStep,
    ulong? EffectiveUntilStep)
{
    public void Validate()
    {
        if (OrderId.IsZero || IssuingAuthorityRef.IsZero || TargetRef.IsZero)
            throw new InvalidDataException("governance.enforcement-id-zero");
        if (EffectiveUntilStep is not null && EffectiveUntilStep.Value < EffectiveFromStep)
            throw new InvalidDataException("governance.enforcement-period-invalid");
    }

    public bool RequiresPhysicalExecution => true;
}

public sealed record BorderPermissionV1(
    OpaqueId128 PermissionId,
    OpaqueId128 HolderRef,
    OpaqueId128 BorderControlRef,
    bool LegalCrossingPermitted,
    ulong EffectiveFromStep,
    ulong? EffectiveUntilStep)
{
    public void Validate()
    {
        if (PermissionId.IsZero || HolderRef.IsZero || BorderControlRef.IsZero)
            throw new InvalidDataException("governance.border-permission-id-zero");
        if (EffectiveUntilStep is not null && EffectiveUntilStep.Value < EffectiveFromStep)
            throw new InvalidDataException("governance.border-permission-period-invalid");
    }

    public bool IsLegallyPermittedAt(ulong step)
    {
        Validate();
        return LegalCrossingPermitted && step >= EffectiveFromStep &&
               (EffectiveUntilStep is null || step <= EffectiveUntilStep.Value);
    }
}
