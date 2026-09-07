using MachiVerse.Simulation.Core.Determinism;

namespace MachiVerse.Simulation.Core.Runtime;

public readonly record struct ConservedReferenceV1
{
    public ConservedReferenceV1(StableToken semanticKind, OpaqueId128 referenceId)
    {
        if (referenceId.IsZero) throw new ArgumentException("Conserved reference ZERO is invalid.", nameof(referenceId));
        SemanticKind = semanticKind;
        ReferenceId = referenceId;
    }

    public StableToken SemanticKind { get; }
    public OpaqueId128 ReferenceId { get; }
}

public readonly record struct ConservedQuantityV1
{
    public ConservedQuantityV1(StableToken quantityKind, long quantity)
    {
        QuantityKind = quantityKind;
        Quantity = quantity;
    }

    public StableToken QuantityKind { get; }
    public long Quantity { get; }
}

public sealed class DetailConservationSnapshotV1
{
    public DetailConservationSnapshotV1(
        IEnumerable<ConservedReferenceV1>? persistentIdentities = null,
        IEnumerable<ConservedQuantityV1>? stockTotals = null,
        IEnumerable<ConservedReferenceV1>? activeObligations = null,
        IEnumerable<ConservedQuantityV1>? inFlightFlowTotals = null,
        IEnumerable<ConservedReferenceV1>? provenanceRefs = null)
    {
        PersistentIdentities = CanonicalReferences(persistentIdentities, "detail.conservation.identity-duplicate");
        StockTotals = CanonicalQuantities(stockTotals, "detail.conservation.stock-kind-duplicate");
        ActiveObligations = CanonicalReferences(activeObligations, "detail.conservation.obligation-duplicate");
        InFlightFlowTotals = CanonicalQuantities(inFlightFlowTotals, "detail.conservation.flow-kind-duplicate");
        ProvenanceRefs = CanonicalReferences(provenanceRefs, "detail.conservation.provenance-duplicate");
        CanonicalDigest = ComputeDigest();
    }

    public IReadOnlyList<ConservedReferenceV1> PersistentIdentities { get; }
    public IReadOnlyList<ConservedQuantityV1> StockTotals { get; }
    public IReadOnlyList<ConservedReferenceV1> ActiveObligations { get; }
    public IReadOnlyList<ConservedQuantityV1> InFlightFlowTotals { get; }
    public IReadOnlyList<ConservedReferenceV1> ProvenanceRefs { get; }
    public byte[] CanonicalDigest { get; }

    private static IReadOnlyList<ConservedReferenceV1> CanonicalReferences(
        IEnumerable<ConservedReferenceV1>? values,
        string duplicateDiagnostic)
    {
        var ordered = (values ?? Array.Empty<ConservedReferenceV1>())
            .OrderBy(static value => value.SemanticKind.Value, StringComparer.Ordinal)
            .ThenBy(static value => value.ReferenceId)
            .ToArray();
        if (ordered.Distinct().Count() != ordered.Length)
            throw new InvalidDataException(duplicateDiagnostic);
        return Array.AsReadOnly(ordered);
    }

    private static IReadOnlyList<ConservedQuantityV1> CanonicalQuantities(
        IEnumerable<ConservedQuantityV1>? values,
        string duplicateDiagnostic)
    {
        var ordered = (values ?? Array.Empty<ConservedQuantityV1>())
            .OrderBy(static value => value.QuantityKind.Value, StringComparer.Ordinal)
            .ToArray();
        if (ordered.Select(static value => value.QuantityKind).Distinct().Count() != ordered.Length)
            throw new InvalidDataException(duplicateDiagnostic);
        return Array.AsReadOnly(ordered);
    }

    private byte[] ComputeDigest()
        => HashSuite.DomainHash("mv.state-diagnostic.v1", writer =>
        {
            writer.WriteMapStart(5);
            writer.WriteUnsigned(0); WriteReferences(writer, PersistentIdentities);
            writer.WriteUnsigned(1); WriteQuantities(writer, StockTotals);
            writer.WriteUnsigned(2); WriteReferences(writer, ActiveObligations);
            writer.WriteUnsigned(3); WriteQuantities(writer, InFlightFlowTotals);
            writer.WriteUnsigned(4); WriteReferences(writer, ProvenanceRefs);
        });

    private static void WriteReferences(MvDcborWriter writer, IReadOnlyList<ConservedReferenceV1> values)
    {
        writer.WriteArrayStart((ulong)values.Count);
        foreach (var value in values)
        {
            writer.WriteArrayStart(2);
            writer.WriteAsciiText(value.SemanticKind.Value);
            writer.WriteBytes(value.ReferenceId.ToBytes());
        }
    }

    private static void WriteQuantities(MvDcborWriter writer, IReadOnlyList<ConservedQuantityV1> values)
    {
        writer.WriteArrayStart((ulong)values.Count);
        foreach (var value in values)
        {
            writer.WriteArrayStart(2);
            writer.WriteAsciiText(value.QuantityKind.Value);
            writer.WriteInt64(value.Quantity);
        }
    }
}

public static class DetailConservationInvariantV1
{
    public static readonly StableToken IdentityInvariant = new("detail.identity-preservation");
    public static readonly StableToken StockInvariant = new("detail.stock-conservation");
    public static readonly StableToken ObligationInvariant = new("detail.obligation-preservation");
    public static readonly StableToken FlowInvariant = new("detail.flow-conservation");
    public static readonly StableToken ProvenanceInvariant = new("detail.provenance-preservation");

    private static readonly StableToken IdentityFailure = new("detail.identity-not-preserved");
    private static readonly StableToken StockFailure = new("detail.stock-not-conserved");
    private static readonly StableToken ObligationFailure = new("detail.obligation-not-preserved");
    private static readonly StableToken FlowFailure = new("detail.flow-not-conserved");
    private static readonly StableToken ProvenanceFailure = new("detail.provenance-not-preserved");

    public static IReadOnlyList<InvariantResultV1> Validate(
        DetailConservationSnapshotV1 before,
        DetailConservationSnapshotV1 after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        var results = new[]
        {
            Result(
                IdentityInvariant,
                before.PersistentIdentities.SequenceEqual(after.PersistentIdentities),
                IdentityFailure),
            Result(
                StockInvariant,
                before.StockTotals.SequenceEqual(after.StockTotals),
                StockFailure),
            Result(
                ObligationInvariant,
                before.ActiveObligations.SequenceEqual(after.ActiveObligations),
                ObligationFailure),
            Result(
                FlowInvariant,
                before.InFlightFlowTotals.SequenceEqual(after.InFlightFlowTotals),
                FlowFailure),
            Result(
                ProvenanceInvariant,
                before.ProvenanceRefs.SequenceEqual(after.ProvenanceRefs),
                ProvenanceFailure),
        };

        return Array.AsReadOnly(results);
    }

    public static InvariantBarrierDecisionV1 EvaluateBarrier(
        DetailConservationSnapshotV1 before,
        DetailConservationSnapshotV1 after)
        => InvariantBarrierV1.Evaluate(Validate(before, after));

    private static InvariantResultV1 Result(
        StableToken invariantId,
        bool passed,
        StableToken failureCode)
        => new(
            invariantId,
            InvariantSeverityV1.CommitBlocking,
            passed ? InvariantOutcomeV1.Pass : InvariantOutcomeV1.Fail,
            diagnosticCode: passed ? null : failureCode);
}
