using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Configuration;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains;
using MachiVerse.Simulation.Core.Performance;
using MachiVerse.Simulation.Core.Persistence;
using MachiVerse.Simulation.Core.Runtime;
using MachiVerse.Simulation.Core.WorldState;

internal static class Qa04ProductionDeterminismEvidenceSmoke
{
    [ModuleInitializer]
    internal static void Run()
    {
        var fixture = CreateCanonicalFirstStepFixture();

        VerifyCanonicalAppend(fixture);
        VerifyAccumulatorBoundariesRejected();
        VerifyMissingAuthorityRejected(fixture);
        VerifyOrdinalGapAndDuplicateRejected(fixture);
        VerifyTransitionStepDriftRejected(fixture);
        VerifyConfigAuthorityDriftRejected(fixture);
        VerifyConfigDigestDriftRejected(fixture);
        VerifyOperationCardinalityDriftRejected(fixture);
        VerifyOperationOrderDriftRejected(fixture);
        VerifyDetailDecisionDriftRejected(fixture);
        VerifyOperationPrefixDriftRejected(fixture);
        VerifyMissingCompletionAuthorityRejected();
        VerifyIncompleteRunRejected();
    }

    private static void VerifyCanonicalAppend(StepFixture fixture)
    {
        var producer = new Qa04ProductionDeterminismEvidenceProducerV1();
        producer.Append(
            fixture.InjectionStep,
            fixture.Transition,
            fixture.DetailDecision,
            fixture.ResultingPrefix);

        Require(producer.CommittedTransitionCount == 1,
            "QA-04 determinism producer did not append exactly one canonical transition.");
        Require(producer.TerminalOperationCount == checked((ulong)fixture.Bindings.Count),
            "QA-04 determinism producer terminal operation count drifted after canonical append.");
    }

    private static void VerifyAccumulatorBoundariesRejected()
    {
        var ordinalGap = new Qa04Step2DeterminismAccumulatorV1("mv.qa04-test-ordinal-gap.v1");
        RequireInvalidData(
            () => ordinalGap.Append(2, HashSuite.Hash256([0x11])),
            "qa04.determinism-accumulator.ordinal-gap");
        Require(ordinalGap.Count == 0,
            "QA-04 determinism accumulator mutated after rejecting an ordinal gap.");

        var duplicate = new Qa04Step2DeterminismAccumulatorV1("mv.qa04-test-duplicate.v1");
        duplicate.Append(1, HashSuite.Hash256([0x12]));
        var digestBeforeDuplicate = duplicate.Digest;
        RequireInvalidData(
            () => duplicate.Append(1, HashSuite.Hash256([0x13])),
            "qa04.determinism-accumulator.ordinal-gap");
        Require(duplicate.Count == 1 && duplicate.Digest.AsSpan().SequenceEqual(digestBeforeDuplicate),
            "QA-04 determinism accumulator mutated after rejecting a duplicate ordinal.");

        var malformed = new Qa04Step2DeterminismAccumulatorV1("mv.qa04-test-malformed-item.v1");
        RequireThrows<ArgumentException>(
            () => malformed.Append(1, new byte[31]),
            "QA-04 determinism accumulator must reject a noncanonical item digest length.");
        Require(malformed.Count == 0,
            "QA-04 determinism accumulator mutated after rejecting a noncanonical item digest.");
    }

    private static void VerifyMissingAuthorityRejected(StepFixture fixture)
    {
        var producer = new Qa04ProductionDeterminismEvidenceProducerV1();
        RequireThrows<ArgumentNullException>(
            () => producer.Append(
                fixture.InjectionStep,
                null!,
                fixture.DetailDecision,
                fixture.ResultingPrefix),
            "QA-04 determinism producer must fail closed when transition authority is missing.");
        RequireThrows<ArgumentNullException>(
            () => producer.Append(
                fixture.InjectionStep,
                fixture.Transition,
                null!,
                fixture.ResultingPrefix),
            "QA-04 determinism producer must fail closed when detail decision authority is missing.");
        RequireThrows<ArgumentNullException>(
            () => producer.Append(
                fixture.InjectionStep,
                fixture.Transition,
                fixture.DetailDecision,
                null!),
            "QA-04 determinism producer must fail closed when resulting operation prefix is missing.");
        Require(producer.CommittedTransitionCount == 0 && producer.TerminalOperationCount == 0,
            "QA-04 determinism producer mutated after rejecting missing authority input.");
    }

    private static void VerifyOrdinalGapAndDuplicateRejected(StepFixture fixture)
    {
        var gapProducer = new Qa04ProductionDeterminismEvidenceProducerV1();
        RequireInvalidData(
            () => gapProducer.Append(
                injectionStep: 1,
                fixture.Transition,
                fixture.DetailDecision,
                fixture.ResultingPrefix),
            "qa04.determinism-evidence.transition-ordinal-drift");

        var duplicateProducer = new Qa04ProductionDeterminismEvidenceProducerV1();
        duplicateProducer.Append(
            fixture.InjectionStep,
            fixture.Transition,
            fixture.DetailDecision,
            fixture.ResultingPrefix);
        RequireInvalidData(
            () => duplicateProducer.Append(
                fixture.InjectionStep,
                fixture.Transition,
                fixture.DetailDecision,
                fixture.ResultingPrefix),
            "qa04.determinism-evidence.transition-ordinal-drift");
    }

    private static void VerifyTransitionStepDriftRejected(StepFixture fixture)
    {
        var transition = CreateTransition(
            fixture.Config,
            fixture.DetailDecision,
            fixture.Bindings,
            fixture.Outcomes,
            effectiveStep: 2,
            resultingStep: 3);
        var producer = new Qa04ProductionDeterminismEvidenceProducerV1();
        RequireInvalidData(
            () => producer.Append(
                fixture.InjectionStep,
                transition,
                fixture.DetailDecision,
                fixture.ResultingPrefix),
            "qa04.determinism-evidence.transition-step-drift");
    }

    private static void VerifyConfigAuthorityDriftRejected(StepFixture fixture)
    {
        var transition = CreateTransition(
            fixture.Config,
            fixture.DetailDecision,
            fixture.Bindings,
            fixture.Outcomes,
            activeConfigGeneration: checked(fixture.Config.Generation + 1UL));
        var producer = new Qa04ProductionDeterminismEvidenceProducerV1();
        RequireInvalidData(
            () => producer.Append(
                fixture.InjectionStep,
                transition,
                fixture.DetailDecision,
                fixture.ResultingPrefix),
            "qa04.determinism-evidence.config-authority-drift");
    }

    private static void VerifyConfigDigestDriftRejected(StepFixture fixture)
    {
        var driftedDigest = fixture.Config.Digest.ToArray();
        driftedDigest[0] ^= 0x01;
        var transition = CreateTransition(
            fixture.Config,
            fixture.DetailDecision,
            fixture.Bindings,
            fixture.Outcomes,
            activeConfigDigest: driftedDigest);
        var producer = new Qa04ProductionDeterminismEvidenceProducerV1();
        RequireInvalidData(
            () => producer.Append(
                fixture.InjectionStep,
                transition,
                fixture.DetailDecision,
                fixture.ResultingPrefix),
            "qa04.determinism-evidence.config-authority-drift");
    }

    private static void VerifyOperationCardinalityDriftRejected(StepFixture fixture)
    {
        Require(fixture.Bindings.Count > 1,
            "QA-04 canonical first Step must contain multiple operations for cardinality coverage.");
        var bindings = fixture.Bindings.Take(fixture.Bindings.Count - 1).ToArray();
        var outcomes = fixture.Outcomes.Take(fixture.Outcomes.Count - 1).ToArray();
        var transition = CreateTransition(
            fixture.Config,
            fixture.DetailDecision,
            bindings,
            outcomes);
        var producer = new Qa04ProductionDeterminismEvidenceProducerV1();
        RequireInvalidData(
            () => producer.Append(
                fixture.InjectionStep,
                transition,
                fixture.DetailDecision,
                fixture.ResultingPrefix),
            "qa04.determinism-evidence.operation-cardinality-drift");
    }

    private static void VerifyOperationOrderDriftRejected(StepFixture fixture)
    {
        var bindings = fixture.Bindings.Reverse().ToArray();
        var outcomes = bindings
            .Select(static binding => SuccessfulTerminal(binding.SourceDescriptor.OperationId))
            .ToArray();
        var transition = CreateTransition(
            fixture.Config,
            fixture.DetailDecision,
            bindings,
            outcomes);
        var producer = new Qa04ProductionDeterminismEvidenceProducerV1();
        RequireInvalidData(
            () => producer.Append(
                fixture.InjectionStep,
                transition,
                fixture.DetailDecision,
                fixture.ResultingPrefix),
            "qa04.determinism-evidence.operation-order-drift");
    }

    private static void VerifyDetailDecisionDriftRejected(StepFixture fixture)
    {
        var detail = fixture.DetailDecision with
        {
            BasisStep = 2,
            ResultingStep = 3,
        };
        var producer = new Qa04ProductionDeterminismEvidenceProducerV1();
        RequireInvalidData(
            () => producer.Append(
                fixture.InjectionStep,
                fixture.Transition,
                detail,
                fixture.ResultingPrefix),
            "qa04.determinism-evidence.detail-decision-step-drift");
    }

    private static void VerifyOperationPrefixDriftRejected(StepFixture fixture)
    {
        var driftedPrefix = fixture.ResultingPrefix with
        {
            TerminalSemanticDigest = HashSuite.Hash256([0x7f]),
        };
        var producer = new Qa04ProductionDeterminismEvidenceProducerV1();
        RequireInvalidData(
            () => producer.Append(
                fixture.InjectionStep,
                fixture.Transition,
                fixture.DetailDecision,
                driftedPrefix),
            "qa04.determinism-evidence.operation-prefix-drift");
    }

    private static void VerifyMissingCompletionAuthorityRejected()
    {
        var producer = new Qa04ProductionDeterminismEvidenceProducerV1();
        var state = CreateMinimalWorldState();
        RequireThrows<ArgumentNullException>(
            () => producer.Complete(null!, Qa04OperationClosedPrefixV1.Empty()),
            "QA-04 determinism producer must fail closed when final state authority is missing.");
        RequireThrows<ArgumentNullException>(
            () => producer.Complete(state, null!),
            "QA-04 determinism producer must fail closed when final operation prefix authority is missing.");
    }

    private static void VerifyIncompleteRunRejected()
    {
        var producer = new Qa04ProductionDeterminismEvidenceProducerV1();
        var state = CreateMinimalWorldState();
        RequireInvalidData(
            () => producer.Complete(state, Qa04OperationClosedPrefixV1.Empty()),
            "qa04.determinism-evidence.append-count-mismatch");
    }

    private static StepFixture CreateCanonicalFirstStepFixture()
    {
        const ulong injectionStep = 0;
        const ulong effectiveStep = 1;
        const ulong resultingStep = 2;

        Qa04ReferenceLoadV1.ValidateCanonicalContract();
        var config = Qa04ReferenceConfigAuthorityV1.CreateCanonical();
        var bindings = Qa04ReferenceLoadV1.OperationsForStep(injectionStep)
            .Select(descriptor => Qa04CanonicalOperationBindingV1.Bind(descriptor, config.Generation))
            .OrderBy(static binding => binding.OrderKey)
            .ThenBy(static binding => binding.SourceDescriptor.OperationId)
            .ToArray();
        var outcomes = bindings
            .Select(static binding => SuccessfulTerminal(binding.SourceDescriptor.OperationId))
            .ToArray();

        var detailDirectory = new DetailDirectoryV1(Array.Empty<DetailRegionStateV1>());
        var detailPlan = DetailTransitionPlannerV1.Plan(
            detailDirectory,
            Array.Empty<DetailTransitionCandidateV1>(),
            effectiveStep,
            DetailTransitionPolicyV1.FromConfig(config));
        var detailDecision = Qa04DetailDecisionAuthorityBuilderV1.Create(
            Qa04ReferenceLoadV1.WorldId,
            new HistoryAnchor(1, HashSuite.Hash256([0x31])),
            effectiveStep,
            detailPlan);

        var transition = CreateTransition(
            config,
            detailDecision,
            bindings,
            outcomes,
            effectiveStep,
            resultingStep);

        var terminalBatchDigest = Qa04TerminalSemanticAuthorityV1.ComputeBatchDigest(bindings, outcomes);
        var terminalStepDigest = Qa04TerminalSemanticAuthorityV1.ComputeStepItemDigest(
            effectiveStep,
            checked((ulong)bindings.Length),
            terminalBatchDigest);
        var terminalAccumulator = new Qa04Step2DeterminismAccumulatorV1("mv.qa04-operation-terminal.v1");
        terminalAccumulator.Append(1, terminalStepDigest);
        var resultingPrefix = Qa04OperationClosedPrefixV1.Empty().Advance(
            injectionStep,
            checked((ulong)bindings.Length),
            terminalAccumulator.Digest,
            resultingStep);

        return new StepFixture(
            injectionStep,
            config,
            Array.AsReadOnly(bindings),
            Array.AsReadOnly(outcomes),
            detailDecision,
            transition,
            resultingPrefix);
    }

    private static WorldStateV1 CreateMinimalWorldState()
    {
        var partitions = StandardDomainPartitionRegistry.Entries.Select(entry => new PartitionStateRefV1(
            new PartitionStateHeaderV1(
                entry,
                revision: 1,
                basisStep: 0,
                detailLevel: DetailLevelV1.D0Entity,
                itemCount: 0,
                canonicalDigest: HashSuite.Hash256(System.Text.Encoding.ASCII.GetBytes(entry.PartitionId.Value)))));

        return new WorldStateV1(
            new WorldStateHeaderV1(
                Qa04ReferenceLoadV1.WorldId,
                step: 0,
                worldSeedDigest: new byte[32],
                configGeneration: 1,
                masterGeneration: 1,
                rateGeneration: 1),
            new OrderedPartitionDirectoryV1(partitions),
            WorldStateV1.EmptySubstate("core.scheduler-state"),
            WorldStateV1.EmptySubstate("core.operation-state"),
            WorldStateV1.EmptySubstate("core.detail-state"),
            WorldStateV1.EmptySubstate("core.domain-registry-state"),
            HashSuite.Hash256([0x44]));
    }

    private static Qa04TransitionCommittedAuthorityV1 CreateTransition(
        EffectiveCoreConfig config,
        Qa04DetailDecisionAuthorityV1 detailDecision,
        IReadOnlyList<Qa04CanonicalOperationBindingResultV1> bindings,
        IReadOnlyList<TerminalOperationCommit> outcomes,
        ulong effectiveStep = 1,
        ulong resultingStep = 2,
        ulong? activeConfigGeneration = null,
        byte[]? activeConfigDigest = null)
        => Qa04TransitionCommittedAuthorityV1.Create(
            Qa04ReferenceLoadV1.WorldId,
            historySequence: checked(detailDecision.History.Sequence + 1UL),
            previousHistoryRecordDigest: detailDecision.History.RecordDigest,
            effectiveStep,
            resultingStep,
            activeConfigGeneration ?? config.Generation,
            activeConfigDigest: activeConfigDigest ?? config.Digest,
            appliedOperationIds: bindings.Select(static binding => binding.SourceDescriptor.OperationId).ToArray(),
            operationOutcomes: outcomes,
            previousStateContinuityToken: HashSuite.Hash256([0x41]),
            stateDiagnosticHash: HashSuite.Hash256([0x42]),
            partitionDigests:
            [
                new Qa04TransitionPartitionDigestV1(
                    "resident.identity",
                    HashSuite.Hash256([0x43])),
            ]);

    private static TerminalOperationCommit SuccessfulTerminal(OpaqueId128 operationId)
        => new(
            operationId,
            (int)CoreOperationResultStatusV1.Success,
            "operation.succeeded");

    private static void RequireInvalidData(Action action, string expectedCode)
    {
        try
        {
            action();
        }
        catch (InvalidDataException ex) when (string.Equals(ex.Message, expectedCode, StringComparison.Ordinal))
        {
            return;
        }
        throw new InvalidOperationException($"Expected InvalidDataException '{expectedCode}'.");
    }

    private static void RequireThrows<T>(Action action, string message)
        where T : Exception
    {
        try
        {
            action();
        }
        catch (T)
        {
            return;
        }
        throw new InvalidOperationException(message);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed record StepFixture(
        ulong InjectionStep,
        EffectiveCoreConfig Config,
        IReadOnlyList<Qa04CanonicalOperationBindingResultV1> Bindings,
        IReadOnlyList<TerminalOperationCommit> Outcomes,
        Qa04DetailDecisionAuthorityV1 DetailDecision,
        Qa04TransitionCommittedAuthorityV1 Transition,
        Qa04OperationClosedPrefixV1 ResultingPrefix);
}
