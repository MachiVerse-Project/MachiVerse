using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.GovernanceSecurity;

internal static class Sim11GovernanceLawSmoke
{
    internal static void Run()
    {
        VerifyAstDecodeAndArbitraryCodeReject();
        VerifyApplicability();
        VerifyResolutionOrder();
        VerifyConflict();
        VerifyEnforcementPhysicalSeparation();
        VerifyBorderPermissionCrossingSeparation();
    }

    private static void VerifyAstDecodeAndArbitraryCodeReject()
    {
        var fact = new LawPredicateNodeV1(
            LawPredicateNodeKindV1.FactEquals,
            [],
            new StableToken("fact.action-kind"),
            new StableToken("action.trade"));
        var step = new LawPredicateNodeV1(
            LawPredicateNodeKindV1.TimeStepRange,
            [],
            FromStep: 10,
            UntilStep: 20);
        var root = new LawPredicateNodeV1(LawPredicateNodeKindV1.And, [fact, step]);
        root.Validate();

        RequireReject(
            () => new LawPredicateNodeV1((LawPredicateNodeKindV1)255, []).Validate(),
            "governance.law-ast-node-unregistered",
            "domain.law.ast.decode");
        RequireReject(
            () => new LawPredicateNodeV1((LawPredicateNodeKindV1)254, []).Validate(),
            "governance.law-ast-node-unregistered",
            "domain.law.arbitrary-code-reject");
    }

    private static void VerifyApplicability()
    {
        var jurisdiction = Id("00000000000000000000000000012001");
        var otherJurisdiction = Id("00000000000000000000000000012002");
        var rule = Rule(
            "00000000000000000000000000012010",
            jurisdiction,
            priority: 10,
            fromStep: 100,
            untilStep: 200,
            LawEffectKindV1.Permit,
            "legal.permit");

        Require(!rule.IsApplicableTo(otherJurisdiction, 150) &&
                !rule.IsApplicableTo(jurisdiction, 99) &&
                rule.IsApplicableTo(jurisdiction, 100) &&
                rule.IsApplicableTo(jurisdiction, 200) &&
                !rule.IsApplicableTo(jurisdiction, 201),
            "domain.law.applicability: jurisdiction/effective Step boundaries mismatch.");
    }

    private static void VerifyResolutionOrder()
    {
        var jurisdiction = Id("00000000000000000000000000012101");
        var lowerRuleId = Rule(
            "00000000000000000000000000012110", jurisdiction, 5, 0, null,
            LawEffectKindV1.Permit, "legal.permit");
        var higherRuleId = Rule(
            "00000000000000000000000000012111", jurisdiction, 5, 0, null,
            LawEffectKindV1.Permit, "legal.permit");
        var lessSpecific = Rule(
            "00000000000000000000000000012112", jurisdiction, 5, 0, null,
            LawEffectKindV1.Permit, "legal.permit");
        var lowerPriority = Rule(
            "00000000000000000000000000012113", jurisdiction, 6, 0, null,
            LawEffectKindV1.Prohibit, "legal.prohibit");

        var forward = DeterministicLegalRuleResolverV1.Resolve(
            jurisdiction,
            10,
            [
                new ApplicableLegalRuleV1(lowerPriority, 99),
                new ApplicableLegalRuleV1(higherRuleId, 4),
                new ApplicableLegalRuleV1(lessSpecific, 3),
                new ApplicableLegalRuleV1(lowerRuleId, 4),
            ]);
        var reverse = DeterministicLegalRuleResolverV1.Resolve(
            jurisdiction,
            10,
            new[]
            {
                new ApplicableLegalRuleV1(lowerPriority, 99),
                new ApplicableLegalRuleV1(higherRuleId, 4),
                new ApplicableLegalRuleV1(lessSpecific, 3),
                new ApplicableLegalRuleV1(lowerRuleId, 4),
            }.Reverse());

        Require(forward.Status == LegalResolutionStatusV1.Resolved &&
                forward.Effect == lowerRuleId.Effect &&
                forward.ConsideredRuleIds.SequenceEqual([
                    lowerRuleId.RuleId,
                    higherRuleId.RuleId,
                    lessSpecific.RuleId,
                    lowerPriority.RuleId
                ]) &&
                forward == reverse,
            "domain.law.resolution-order: priority/specificity/rule-id canonical resolution mismatch.");
    }

    private static void VerifyConflict()
    {
        var jurisdiction = Id("00000000000000000000000000012201");
        var permit = Rule(
            "00000000000000000000000000012210", jurisdiction, 1, 0, null,
            LawEffectKindV1.Permit, "legal.permit");
        var prohibit = Rule(
            "00000000000000000000000000012211", jurisdiction, 1, 0, null,
            LawEffectKindV1.Prohibit, "legal.prohibit");
        var result = DeterministicLegalRuleResolverV1.Resolve(
            jurisdiction,
            1,
            [new ApplicableLegalRuleV1(prohibit, 8), new ApplicableLegalRuleV1(permit, 8)]);

        Require(result.Status == LegalResolutionStatusV1.Conflict && result.Effect is null,
            "domain.law.conflict: conflicting leading terminal effects must produce explicit conflict.");
    }

    private static void VerifyEnforcementPhysicalSeparation()
    {
        var target = Id("00000000000000000000000000012301");
        var order = new EnforcementOrderV1(
            Id("00000000000000000000000000012310"),
            Id("00000000000000000000000000012311"),
            target,
            new StableToken("enforcement.detain"),
            20,
            30);
        order.Validate();
        Require(order.TargetRef == target,
            "domain.enforcement.physical-separation: institutional order must remain an order record and not replace physical target identity/state.");
    }

    private static void VerifyBorderPermissionCrossingSeparation()
    {
        var holder = Id("00000000000000000000000000012401");
        var permission = new BorderPermissionV1(
            Id("00000000000000000000000000012410"),
            holder,
            Id("00000000000000000000000000012411"),
            LegalCrossingPermitted: true,
            EffectiveFromStep: 50,
            EffectiveUntilStep: 60);

        Require(!permission.IsLegallyPermittedAt(49) &&
                permission.IsLegallyPermittedAt(50) &&
                permission.IsLegallyPermittedAt(60) &&
                !permission.IsLegallyPermittedAt(61) &&
                permission.HolderRef == holder,
            "domain.border.permission-crossing: legal permission must be Step-bounded institutional state, separate from actual physical crossing.");
    }

    private static LegalRuleV1 Rule(
        string id,
        OpaqueId128 jurisdiction,
        int priority,
        ulong fromStep,
        ulong? untilStep,
        LawEffectKindV1 effectKind,
        string effectToken)
        => new(
            Id(id),
            jurisdiction,
            priority,
            fromStep,
            untilStep,
            new LawPredicateNodeV1(
                LawPredicateNodeKindV1.TimeStepRange,
                [],
                FromStep: fromStep,
                UntilStep: untilStep ?? ulong.MaxValue),
            new LawEffectV1(effectKind, new StableToken(effectToken)));

    private static OpaqueId128 Id(string value) => OpaqueId128.Parse(value);

    private static void RequireReject(Action action, string expected, string acceptance)
    {
        try
        {
            action();
        }
        catch (InvalidDataException ex) when (ex.Message == expected)
        {
            return;
        }
        throw new InvalidOperationException($"{acceptance}: expected SIM-11 rejection {expected}");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
