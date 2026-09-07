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
            case LawPredicateNodeKindV1.SubjectHasStatus:
            case LawPredicateNodeKindV1.RelationExists:
            case LawPredicateNodeKindV1.SpatialWithin:
                RequireLeaf();
                if (Key is null || TokenValue is null)
                    throw new InvalidDataException("governance.law-ast-leaf-payload-invalid");
                if (Minimum is not null || Maximum is not null || FromStep is not null || UntilStep is not null)
                    throw new InvalidDataException("governance.law-ast-leaf-payload-invalid");
                break;
            case LawPredicateNodeKindV1.FactRange:
                RequireLeaf();
                if (Key is null || Minimum is null || Maximum is null || Minimum > Maximum ||
                    TokenValue is not null || FromStep is not null || UntilStep is not null)
                    throw new InvalidDataException("governance.law-ast-range-invalid");
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

    public bool IsApplicableTo(OpaqueId128 jurisdictionRef, ulong step)
    {
        Validate();
        return jurisdictionRef == JurisdictionRef &&
               step >= EffectiveFromStep &&
               (EffectiveUntilStep is null || step <= EffectiveUntilStep.Value);
    }
}

public sealed record ApplicableLegalRuleV1(LegalRuleV1 Rule, int Specificity)
{
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Rule);
        Rule.Validate();
        if (Specificity < 0) throw new InvalidDataException("governance.law-specificity-negative");
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
        OpaqueId128 jurisdictionRef,
        ulong step,
        IEnumerable<ApplicableLegalRuleV1> candidates)
    {
        if (jurisdictionRef.IsZero) throw new InvalidDataException("governance.jurisdiction-id-zero");
        ArgumentNullException.ThrowIfNull(candidates);

        var applicable = candidates
            .Select(candidate =>
            {
                ArgumentNullException.ThrowIfNull(candidate);
                candidate.Validate();
                return candidate;
            })
            .Where(candidate => candidate.Rule.IsApplicableTo(jurisdictionRef, step))
            .OrderBy(static candidate => candidate.Rule.Priority)
            .ThenByDescending(static candidate => candidate.Specificity)
            .ThenBy(static candidate => candidate.Rule.RuleId)
            .ToArray();

        if (applicable.Select(static candidate => candidate.Rule.RuleId).Distinct().Count() != applicable.Length)
            throw new InvalidDataException("governance.law-rule-id-duplicate");
        if (applicable.Length == 0)
            return new LegalResolutionResultV1(
                LegalResolutionStatusV1.NoApplicableRule,
                null,
                Array.Empty<OpaqueId128>());

        var leadingPriority = applicable[0].Rule.Priority;
        var leadingSpecificity = applicable[0].Specificity;
        var leading = applicable
            .TakeWhile(candidate => candidate.Rule.Priority == leadingPriority && candidate.Specificity == leadingSpecificity)
            .ToArray();
        var terminal = leading.Where(static candidate => candidate.Rule.Effect.IsTerminalClassification).ToArray();
        if (HasTerminalConflict(terminal))
            return new LegalResolutionResultV1(
                LegalResolutionStatusV1.Conflict,
                null,
                Array.AsReadOnly(applicable.Select(static candidate => candidate.Rule.RuleId).ToArray()));

        return new LegalResolutionResultV1(
            LegalResolutionStatusV1.Resolved,
            applicable[0].Rule.Effect,
            Array.AsReadOnly(applicable.Select(static candidate => candidate.Rule.RuleId).ToArray()));
    }

    private static bool HasTerminalConflict(IReadOnlyList<ApplicableLegalRuleV1> terminal)
    {
        if (terminal.Count < 2) return false;
        var first = terminal[0].Rule.Effect;
        return terminal.Skip(1).Any(candidate => candidate.Rule.Effect != first);
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
