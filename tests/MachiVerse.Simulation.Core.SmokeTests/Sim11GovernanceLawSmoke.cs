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
        Sim11RuntimeGateSmoke.RunAsync().GetAwaiter().GetResult();
    }

    private static void VerifyAstDecodeAndArbitraryCodeReject()
    {
        Require(LawPredicateNodeKindRegistryV1.Decode("FACT_EQUALS") == LawPredicateNodeKindV1.FactEquals,
            "domain.law.ast.decode: registered AST node decode mismatch.");
        RequireReject(
            () => _ = LawPredicateNodeKindRegistryV1.Decode("EXECUTE"),
            "governance.law-ast-node-unregistered",
            "domain.law.arbitrary-code-reject");

        var fact = new LawPredicateNodeV1(
            LawPredicateNodeKindV1.FactEquals,
            [],
            Key: new StableToken("fact.action-kind"),
            TokenValue: new StableToken("action.trade"));
        var step = new LawPredicateNodeV1(
            LawPredicateNodeKindV1.TimeStepRange,
            [],
            FromStep: 10,
            UntilStep: 20);
        new LawPredicateNodeV1(LawPredicateNodeKindV1.And, [fact, step]).Validate();
        RequireReject(
            () => new LawPredicateNodeV1((LawPredicateNodeKindV1)255, []).Validate(),
            "governance.law-ast-node-unregistered",
            "domain.law.ast.decode");
    }

    private static void VerifyApplicability()
    {
        var jurisdiction = Id("00000000000000000000000000012001");
        var otherJurisdiction = Id("00000000000000000000000000012002");
        var rule = Rule(
            "00000000000000000000000000012010",
            jurisdiction,
            priority: 10,
            specificity: 5,
            fromStep: 100,
            untilStep: 200,
            LawEffectKindV1.Permit,
            "legal.permit",
            PredicateFactEquals("fact.action-kind", "action.trade"));

        Require(!rule.IsApplicableTo(Context(otherJurisdiction, 150, "action.trade")) &&
                !rule.IsApplicableTo(Context(jurisdiction, 99, "action.trade")) &&
                rule.IsApplicableTo(Context(jurisdiction, 100, "action.trade")) &&
                rule.IsApplicableTo(Context(jurisdiction, 200, "action.trade")) &&
                !rule.IsApplicableTo(Context(jurisdiction, 201, "action.trade")) &&
                !rule.IsApplicableTo(Context(jurisdiction, 150, "action.move")),
            "domain.law.applicability: jurisdiction/effective Step/predicate boundaries mismatch.");
    }

    private static void VerifyResolutionOrder()
    {
        var jurisdiction = Id("00000000000000000000000000012101");
        var lowerRuleId = Rule(
            "00000000000000000000000000012110", jurisdiction, 5, 4, 0, null,
            LawEffectKindV1.Permit, "legal.permit");
        var higherRuleId = Rule(
            "00000000000000000000000000012111", jurisdiction, 5, 4, 0, null,
            LawEffectKindV1.Permit, "legal.permit");
        var lessSpecific = Rule(
            "00000000000000000000000000012112", jurisdiction, 5, 3, 0, null,
            LawEffectKindV1.Permit, "legal.permit");
        var lowerPriority = Rule(
            "00000000000000000000000000012113", jurisdiction, 6, 99, 0, null,
            LawEffectKindV1.Prohibit, "legal.prohibit");
        var rules = new[] { lowerPriority, higherRuleId, lessSpecific, lowerRuleId };
        var context = Context(jurisdiction, 10, "action.trade");

        var forward = DeterministicLegalRuleResolverV1.Resolve(context, rules);
        var reverse = DeterministicLegalRuleResolverV1.Resolve(context, rules.Reverse());

        Require(forward.Status == LegalResolutionStatusV1.Resolved &&
                forward.Effect == lowerRuleId.Effect &&
                forward.ConsideredRuleIds.SequenceEqual([
                    lowerRuleId.RuleId,
                    higherRuleId.RuleId,
                    lessSpecific.RuleId,
                    lowerPriority.RuleId
                ]) &&
                reverse.Status == forward.Status &&
                reverse.Effect == forward.Effect &&
                reverse.ConsideredRuleIds.SequenceEqual(forward.ConsideredRuleIds),
            "domain.law.resolution-order: priority/specificity/rule-id canonical resolution mismatch.");
    }

    private static void VerifyConflict()
    {
        var jurisdiction = Id("00000000000000000000000000012201");
        var permit = Rule(
            "00000000000000000000000000012210", jurisdiction, 1, 8, 0, null,
            LawEffectKindV1.Permit, "legal.permit");
        var prohibit = Rule(
            "00000000000000000000000000012211", jurisdiction, 1, 8, 0, null,
            LawEffectKindV1.Prohibit, "legal.prohibit");
        var result = DeterministicLegalRuleResolverV1.Resolve(
            Context(jurisdiction, 1, "action.trade"),
            [prohibit, permit]);

        Require(result.Status == LegalResolutionStatusV1.Conflict &&
                result.Effect is null &&
                result.ConsideredRuleIds.SequenceEqual([permit.RuleId, prohibit.RuleId]),
            "domain.law.conflict: conflicting equal-authority terminal effects must produce stable explicit conflict.");
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
        Require(order.TargetRef == target && order.RequiresPhysicalExecution,
            "domain.enforcement.physical-separation: institutional order must not itself move/damage/detain target.");
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
            "domain.border.permission-crossing: legal permission must remain separate from actual physical crossing.");
    }

    private static LawPredicateNodeV1 PredicateFactEquals(string key, string value)
        => new(
            LawPredicateNodeKindV1.FactEquals,
            [],
            Key: new StableToken(key),
            TokenValue: new StableToken(value));

    private static LegalRuleV1 Rule(
        string id,
        OpaqueId128 jurisdiction,
        int priority,
        uint specificity,
        ulong fromStep,
        ulong? untilStep,
        LawEffectKindV1 effectKind,
        string effectToken,
        LawPredicateNodeV1? predicate = null)
        => new(
            Id(id),
            jurisdiction,
            priority,
            specificity,
            fromStep,
            untilStep,
            predicate ?? new LawPredicateNodeV1(
                LawPredicateNodeKindV1.TimeStepRange,
                [],
                FromStep: fromStep,
                UntilStep: untilStep ?? ulong.MaxValue),
            new LawEffectV1(effectKind, new StableToken(effectToken)));

    private static LawEvaluationContextV1 Context(
        OpaqueId128 jurisdiction,
        ulong step,
        string actionKind)
        => new(
            jurisdiction,
            step,
            new Dictionary<StableToken, StableToken>
            {
                [new StableToken("fact.action-kind")] = new StableToken(actionKind),
            },
            new Dictionary<StableToken, long>(),
            new HashSet<StableToken>(),
            new HashSet<StableToken>(),
            new HashSet<StableToken>());

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
