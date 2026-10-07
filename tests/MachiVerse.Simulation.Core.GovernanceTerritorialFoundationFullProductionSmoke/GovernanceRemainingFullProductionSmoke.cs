using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Domains.GovernanceSecurity;
using MachiVerse.Simulation.Core.Performance;

internal static class GovernanceRemainingFullProductionSmoke
{
    [ModuleInitializer]
    internal static void Run()
    {
        Console.WriteLine("Validating approved Governance remaining authority (259,000 records)...");
        Qa04GovernanceRemainingCanonicalAuthorityV1.ValidateCanonicalContract();
        var materialization = Qa04GovernanceRemainingCanonicalAuthorityV1.MaterializeCanonical();

        Require(materialization.MaterializedRecordCount == Qa04GovernanceRemainingCanonicalAuthorityV1.CanonicalCount,
            "Governance remaining authority must materialize exactly 259,000 records.");
        Require(materialization.LawRules.ItemCount == 30_000 &&
                materialization.TaxFiscal.ItemCount == 50_000 &&
                materialization.Diplomacy.ItemCount == 10_000 &&
                materialization.SecurityIncidents.ItemCount == 45_000 &&
                materialization.Investigations.ItemCount == 30_000 &&
                materialization.JudicialCases.ItemCount == 25_000 &&
                materialization.Enforcements.ItemCount == 30_000 &&
                materialization.MilitaryAuthorities.ItemCount == 10_000 &&
                materialization.BorderControls.ItemCount == 10_000 &&
                materialization.Lineages.ItemCount == 19_000,
            "Governance remaining partition counts drifted.");

        Require(materialization.LawRules.RecordsCanonical.All(static record =>
                record.Payload.PredicateAst is GovernanceRulePredicateAstNestedValueV1 &&
                record.Payload.EffectAst is GovernanceRuleEffectAstNestedValueV1 &&
                record.Payload.Specificity == 1 && record.Payload.EffectiveFrom == 0 &&
                record.Payload.EffectiveUntil is null && record.Payload.Status.Value == "active"),
            "Governance LawRule nested AST/genesis semantics drifted.");
        Require(materialization.TaxFiscal.RecordsCanonical.All(static record =>
                record.Payload.RatePpm is >= 100_000 and <= 140_000 &&
                record.Payload.RatePpm % 10_000 == 0 &&
                record.Payload.ClaimAmount is null && record.Payload.DebtorRef is null &&
                record.Payload.DueStep is null && record.Payload.Status.Value == "active"),
            "Governance TaxFiscal benchmark semantics drifted.");
        Require(materialization.Diplomacy.RecordsCanonical.All(static record =>
                record.Payload.PartyRefs.Count == 2 && record.Payload.PartyRefs[0] != record.Payload.PartyRefs[1] &&
                record.Payload.InstrumentRefs.Count == 1 && record.Payload.TermsDigest.Length == 32),
            "Governance Diplomacy benchmark semantics drifted.");
        Require(materialization.SecurityIncidents.RecordsCanonical.All(static record =>
                record.Payload.SubjectRefs.Count == 1 && record.Payload.FactEventRefs.Count == 0 &&
                record.Payload.OccurredStep == 0 && record.Payload.SeverityPpm is >= 100_000 and <= 900_000),
            "Governance SecurityIncident benchmark semantics drifted.");
        Require(materialization.Investigations.RecordsCanonical.All(static record =>
                record.Payload.InvestigatorRefs.Count == 1 && record.Payload.EvidenceRefs.Count == 0 &&
                record.Payload.SuspectRefs.Count == 1 && record.Payload.OpenedStep == 0 && record.Payload.ClosedStep is null),
            "Governance Investigation benchmark semantics drifted.");
        Require(materialization.JudicialCases.RecordsCanonical.All(static record =>
                record.Payload.PartyRefs.Count == 2 && record.Payload.PartyRefs[0] != record.Payload.PartyRefs[1] &&
                record.Payload.EvidenceRefs.Count == 0 && record.Payload.ChargeOrClaimRefs.Count == 1 &&
                record.Payload.DecisionRef is null),
            "Governance JudicialCase benchmark semantics drifted.");
        Require(materialization.Enforcements.RecordsCanonical.All(static record =>
                record.Payload.SubjectRefs.Count == 1 && record.Payload.TargetRefs.Count == 1 &&
                record.Payload.SubjectRefs[0] != record.Payload.TargetRefs[0] && record.Payload.OutcomeEventRefs.Count == 0),
            "Governance Enforcement benchmark semantics drifted.");
        Require(materialization.MilitaryAuthorities.RecordsCanonical.All(static record =>
                record.Payload.CommandRef is not null && record.Payload.ObjectiveRefs.Count == 1 &&
                record.Payload.AuthorityScopeRefs.Count == 1 && record.Payload.IssuedStep == 0),
            "Governance MilitaryAuthority benchmark semantics drifted.");
        Require(materialization.BorderControls.RecordsCanonical.All(static record =>
                record.Payload.CheckpointRefs.Count == 1 && record.Payload.MovementRuleRefs.Count == 1 &&
                record.Payload.CapacityPerStep is >= 1 and <= 1_000),
            "Governance BorderControl benchmark semantics drifted.");
        Require(materialization.Lineages.RecordsCanonical.All(static record =>
                record.Payload.PredecessorRefs.Count == 0 && record.Payload.SuccessionKind.Value == "perf.genesis" &&
                record.Payload.EffectiveStep == 0 && record.Payload.CausalityDigest.Length == 32),
            "Governance Lineage benchmark semantics drifted.");

        var recovered = Qa04GovernanceRemainingSnapshotRecoveryEvidenceV1.Verify(materialization);
        Require(recovered.TotalCount == Qa04GovernanceRemainingCanonicalAuthorityV1.CanonicalCount,
            "Governance remaining Snapshot/recovery must semantically rehash all 259,000 records.");

        Console.WriteLine($"governance-remaining-full-production-pass records={materialization.MaterializedRecordCount} recovered={recovered.TotalCount}");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
