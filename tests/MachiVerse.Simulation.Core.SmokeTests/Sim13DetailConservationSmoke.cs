using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Runtime;

internal static class Sim13DetailConservationSmoke
{
    internal static void Run()
    {
        var identityResident = Ref("resident", 5001);
        var identityVehicle = Ref("vehicle", 5002);
        var obligationContract = Ref("contract", 5101);
        var obligationShipment = Ref("shipment", 5102);
        var provenanceTruth = Ref("truth", 5201);
        var provenanceClaim = Ref("claim", 5202);
        var provenanceDelivery = Ref("delivery", 5203);

        var before = Snapshot(
            [identityVehicle, identityResident],
            [new ConservedQuantityV1(new StableToken("material.iron.g"), 1250), new ConservedQuantityV1(new StableToken("money.currency-a.microunit"), 9000)],
            [obligationShipment, obligationContract],
            [new ConservedQuantityV1(new StableToken("goods.in-flight.count"), 3)],
            [provenanceDelivery, provenanceClaim, provenanceTruth]);
        var demoted = Snapshot(
            [identityResident, identityVehicle],
            [new ConservedQuantityV1(new StableToken("money.currency-a.microunit"), 9000), new ConservedQuantityV1(new StableToken("material.iron.g"), 1250)],
            [obligationContract, obligationShipment],
            [new ConservedQuantityV1(new StableToken("goods.in-flight.count"), 3)],
            [provenanceTruth, provenanceClaim, provenanceDelivery]);
        var promoted = Snapshot(
            demoted.PersistentIdentities.Reverse(),
            demoted.StockTotals.Reverse(),
            demoted.ActiveObligations.Reverse(),
            demoted.InFlightFlowTotals.Reverse(),
            demoted.ProvenanceRefs.Reverse());

        Require(before.CanonicalDigest.SequenceEqual(demoted.CanonicalDigest) &&
                demoted.CanonicalDigest.SequenceEqual(promoted.CanonicalDigest),
            "detail.materialization-repeatability: canonical conservation digest must be independent of input order.");
        Require(DetailConservationInvariantV1.EvaluateBarrier(before, demoted).CanCommit &&
                DetailConservationInvariantV1.EvaluateBarrier(demoted, promoted).CanCommit,
            "detail.identity-preservation: D0->D3->D0 conservation snapshots must remain commit-valid.");

        VerifyFailure(
            before,
            Snapshot(
                [identityResident],
                before.StockTotals,
                before.ActiveObligations,
                before.InFlightFlowTotals,
                before.ProvenanceRefs),
            DetailConservationInvariantV1.IdentityInvariant,
            "detail.identity-preservation");

        VerifyFailure(
            before,
            Snapshot(
                before.PersistentIdentities,
                [new ConservedQuantityV1(new StableToken("material.iron.g"), 1249), new ConservedQuantityV1(new StableToken("money.currency-a.microunit"), 9000)],
                before.ActiveObligations,
                before.InFlightFlowTotals,
                before.ProvenanceRefs),
            DetailConservationInvariantV1.StockInvariant,
            "detail.stock-conservation");

        VerifyFailure(
            before,
            Snapshot(
                before.PersistentIdentities,
                before.StockTotals,
                [obligationContract],
                before.InFlightFlowTotals,
                before.ProvenanceRefs),
            DetailConservationInvariantV1.ObligationInvariant,
            "detail.obligation-preservation");

        VerifyFailure(
            before,
            Snapshot(
                before.PersistentIdentities,
                before.StockTotals,
                before.ActiveObligations,
                [new ConservedQuantityV1(new StableToken("goods.in-flight.count"), 2)],
                before.ProvenanceRefs),
            DetailConservationInvariantV1.FlowInvariant,
            "detail.flow-conservation");

        VerifyFailure(
            before,
            Snapshot(
                before.PersistentIdentities,
                before.StockTotals,
                before.ActiveObligations,
                before.InFlightFlowTotals,
                [provenanceTruth, Ref("belief", 5202), provenanceDelivery]),
            DetailConservationInvariantV1.ProvenanceInvariant,
            "detail.provenance-preservation");
    }

    private static void VerifyFailure(
        DetailConservationSnapshotV1 before,
        DetailConservationSnapshotV1 after,
        StableToken expectedInvariant,
        string testCase)
    {
        var results = DetailConservationInvariantV1.Validate(before, after);
        var failed = results.Where(static result => result.Outcome == InvariantOutcomeV1.Fail).ToArray();
        Require(failed.Length == 1 && failed[0].InvariantId == expectedInvariant,
            $"{testCase}: exactly the changed conservation class must fail.");
        Require(!InvariantBarrierV1.Evaluate(results).CanCommit,
            $"{testCase}: conservation failure must be commit-blocking.");
    }

    private static DetailConservationSnapshotV1 Snapshot(
        IEnumerable<ConservedReferenceV1> identities,
        IEnumerable<ConservedQuantityV1> stock,
        IEnumerable<ConservedReferenceV1> obligations,
        IEnumerable<ConservedQuantityV1> flow,
        IEnumerable<ConservedReferenceV1> provenance)
        => new(identities, stock, obligations, flow, provenance);

    private static ConservedReferenceV1 Ref(string kind, int id)
        => new(new StableToken(kind), OpaqueId128.Parse(id.ToString("x32")));

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
