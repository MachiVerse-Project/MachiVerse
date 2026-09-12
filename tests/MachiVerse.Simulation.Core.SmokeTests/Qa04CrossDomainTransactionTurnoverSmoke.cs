using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Performance;
using MachiVerse.Simulation.Core.Runtime;

internal static class Qa04CrossDomainTransactionTurnoverSmoke
{
    [ModuleInitializer]
    internal static void Run()
    {
        Qa04CrossDomainTransactionTurnoverMaterializerV1.ValidateCanonicalContract();
        var pools = CreatePools();
        var genesis = Qa04CrossDomainTransactionGenesisMaterializerV1.Materialize(pools);
        var active = Qa04CrossDomainTransactionTurnoverMaterializerV1.Initialize(genesis);
        Require(active.Count == 10_000 && active.All(static slot => slot.Generation == 0 && slot.State.IsActive),
            "Turnover authority must initialize exactly 10,000 generation-0 ACTIVE slots.");

        var terminalIds = new HashSet<OpaqueId128>();
        for (ulong basisStep = 300; basisStep <= 3_000; basisStep += 300)
        {
            var beforeBySlot = active.ToDictionary(static slot => slot.SlotOrdinal);
            var turnover = Qa04CrossDomainTransactionTurnoverMaterializerV1.Apply(basisStep, active, pools);
            Require(turnover.BasisStep == basisStep && turnover.ResultingStep == basisStep + 1 &&
                    turnover.CommittedStates.Count == 1_000 && turnover.ActiveSlots.Count == 10_000,
                "Each 300-Step turnover must commit and replace exactly 1,000 states.");
            Require(turnover.ActiveSlots.All(static slot => slot.State.IsActive) &&
                    turnover.ActiveSlots.Select(static slot => slot.State.TransactionId).Distinct().Count() == 10_000,
                "Turnover resulting authority must remain exactly 10,000 unique ACTIVE states.");

            var dueCohort = basisStep / 300 - 1;
            var changed = turnover.ActiveSlots.Where(slot =>
                slot.State.TransactionId != beforeBySlot[slot.SlotOrdinal].State.TransactionId).ToArray();
            Require(changed.Length == 1_000 && changed.All(slot => slot.SlotOrdinal % 10 == dueCohort),
                "Initial turnover must replace exactly the slot%10 due cohort.");
            Require(changed.All(slot =>
                slot.Generation == 1 && slot.State.CreatedStep == basisStep + 1 &&
                slot.State.UpdatedStep == basisStep + 1 && slot.State.TerminalStep is null),
                "Initial replacement lifecycle/generation drifted.");
            Require(turnover.CommittedStates.All(state =>
                state.Lifecycle == TransactionLifecycleV1.Committed && state.TerminalStep == basisStep + 1),
                "Due states must terminalize as COMMITTED in the same resulting Step.");
            Require(turnover.CommittedStates.All(state => terminalIds.Add(state.TransactionId)),
                "A transaction lifetime must terminalize exactly once.");

            foreach (var changedSlot in changed)
            {
                var prior = beforeBySlot[changedSlot.SlotOrdinal];
                Require(changedSlot.State.SubjectIds.SequenceEqual(prior.State.SubjectIds) &&
                        changedSlot.State.RootCausality.Kind == CausalityRefKindV1.Transaction &&
                        changedSlot.State.RootCausality.Id.AsSpan().SequenceEqual(prior.State.TransactionId.ToBytes()) &&
                        changedSlot.State.RootCausality.BasisStep == basisStep,
                    "Replacement must preserve subjects and root causality to the prior transaction id.");
                Require(changedSlot.ParticipantTargetRefs.Count == changedSlot.State.Participants.Count,
                    "Replacement participant target evidence count drifted.");
            }
            active = turnover.ActiveSlots;
        }

        Require(active.All(static slot => slot.Generation == 1),
            "After the first 10 cadence points every slot must have completed exactly one lifetime.");

        var beforeSecondCycle = active.ToDictionary(static slot => slot.SlotOrdinal);
        var secondCycle = Qa04CrossDomainTransactionTurnoverMaterializerV1.Apply(3_300, active, pools);
        var secondChanged = secondCycle.ActiveSlots.Where(slot =>
            slot.State.TransactionId != beforeSecondCycle[slot.SlotOrdinal].State.TransactionId).ToArray();
        Require(secondChanged.Length == 1_000 && secondChanged.All(static slot => slot.SlotOrdinal % 10 == 0 && slot.Generation == 2),
            "At basis Step 3,300 cohort zero must begin its second replacement generation.");
        Require(secondCycle.ActiveSlots.Count(static slot => slot.Generation == 2) == 1_000 &&
                secondCycle.ActiveSlots.Count(static slot => slot.Generation == 1) == 9_000,
            "Second lifetime generation accounting drifted.");

        ExpectReject(
            () => Qa04CrossDomainTransactionTurnoverMaterializerV1.Apply(301, active, pools),
            "Non-cadence turnover basis must fail closed.");
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<OpaqueId128>> CreatePools()
    {
        var names = new[]
        {
            "spatial.scope_registry",
            "environment.hazard",
            "physical.presence",
            "participation.control_mode",
            "resident.identity_lifecycle",
            "society.contract_claim",
            "governance.permission_license",
            "infrastructure.service_queue",
        };
        return names.Select((name, index) => new
            {
                name,
                ids = (IReadOnlyList<OpaqueId128>)Array.AsReadOnly(new[]
                {
                    OpaqueId128.Parse((0x49000 + index * 4 + 1).ToString("x32")),
                    OpaqueId128.Parse((0x49000 + index * 4 + 2).ToString("x32")),
                    OpaqueId128.Parse((0x49000 + index * 4 + 3).ToString("x32")),
                })
            })
            .ToDictionary(static item => item.name, static item => item.ids, StringComparer.Ordinal);
    }

    private static void ExpectReject(Action action, string message)
    {
        try
        {
            action();
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException or ArgumentOutOfRangeException)
        {
            return;
        }
        throw new InvalidOperationException(message);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
