using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Domains.PhysicalBuilt;
using MachiVerse.Simulation.Core.WorldState;

internal static class Sim08HandoffSmoke
{
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Run()
    {
        VerifyExclusiveLocationAuthority();
        VerifyPreparedThenAtomicCommit();
    }

    private static void VerifyExclusiveLocationAuthority()
    {
        var subject = Ref("physical.container_location", "00000000000000000000000000009101");
        var containerA = Ref("built.space", "00000000000000000000000000009102");
        var containerB = Ref("built.space", "00000000000000000000000000009103");
        var basis = new PhysicalContainerLocationV1(
            subject,
            containerA,
            SlotToken: null,
            new StableToken("contained"),
            Quantity: 1,
            MassGram: 12_000);

        _ = new PhysicalLocationAuthorityV1([basis]);
        RequireInvalidData(
            () => _ = new PhysicalLocationAuthorityV1([
                basis,
                basis with { ContainerRef = containerB },
            ]),
            "physical.item-multiple-location-authority");
    }

    private static void VerifyPreparedThenAtomicCommit()
    {
        var subject = Ref("physical.container_location", "00000000000000000000000000009201");
        var source = Ref("built.space", "00000000000000000000000000009202");
        var target = Ref("built.space", "00000000000000000000000000009203");
        var basis = new PhysicalContainerLocationV1(
            subject,
            source,
            new StableToken("slot-a"),
            new StableToken("contained"),
            Quantity: 1,
            MassGram: 5_000);
        var authority = new PhysicalLocationAuthorityV1([basis]);
        var prepared = new PhysicalMaterialHandoffV1(
            OpaqueId128.Parse("00000000000000000000000000009210"),
            new StableToken("material.item"),
            source,
            target,
            MassGram: 5_000,
            PhysicalMaterialHandoffPhaseV1.Prepared,
            PreparedStep: 200,
            CommittedStep: null);

        Require(authority.Get(subject).ContainerRef == source,
            "Prepared handoff must not create target authority before atomic commit.");

        var commit = authority.CommitTransfer(
            subject,
            prepared,
            target,
            new StableToken("slot-b"),
            new StableToken("contained"),
            committedStep: 201);
        commit.ValidateExclusiveAuthority();

        Require(commit.PreviousLocation.ContainerRef == source &&
                commit.NextLocation.ContainerRef == target &&
                commit.Handoff.Phase == PhysicalMaterialHandoffPhaseV1.Committed &&
                commit.Handoff.CommittedStep == 201,
            "domain.physical.item-transfer-exclusive: atomic handoff result mismatch.");
        Require(authority.Get(subject).ContainerRef == source,
            "Candidate construction must not mutate the authoritative basis projection before Step finalization.");

        RequireInvalidData(
            () => authority.CommitTransfer(
                subject,
                prepared with { SourceRef = target },
                target,
                null,
                new StableToken("contained"),
                committedStep: 201),
            "physical.handoff-source-authority-mismatch");
        RequireInvalidData(
            () => prepared.Commit(199),
            "physical.handoff-committed-before-prepared");
    }

    private static PartitionRecordRefV1 Ref(string partitionId, string recordId)
        => new(partitionId, OpaqueId128.Parse(recordId));

    private static void RequireInvalidData(Action action, string expected)
    {
        try
        {
            action();
        }
        catch (InvalidDataException ex) when (ex.Message == expected)
        {
            return;
        }
        throw new InvalidOperationException($"Expected SIM-08 rejection: {expected}");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
