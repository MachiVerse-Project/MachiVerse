using MachiVerse.Simulation.Core.Persistence;

internal static class CoreSnapshotSectionWireContractSmoke
{
    internal static void Run()
    {
        CoreSnapshotSectionWireRegistryV1.ValidateStandardRegistry();

        if (CoreSnapshotSectionWireRegistryV1.Entries.Count != 6)
            throw new InvalidOperationException("P4-04 Core snapshot wire registry must contain six entries.");

        Require(
            CoreSnapshotOwnerSectionRegistryV1.WorldStateHeader,
            "core.world-state",
            CoreSnapshotLogicalItemKindV1.WorldStateHeader,
            "CoreWorldStateHeaderSnapshotWireV1");
        Require(
            CoreSnapshotOwnerSectionRegistryV1.SchedulerState,
            "core.scheduler-state",
            CoreSnapshotLogicalItemKindV1.ScheduledOperation,
            "CoreSchedulerStateSnapshotFragmentWireV1");
        Require(
            CoreSnapshotOwnerSectionRegistryV1.OperationState,
            "core.operation-state",
            CoreSnapshotLogicalItemKindV1.DurableOperation,
            "CoreOperationStateSnapshotFragmentWireV1");
        Require(
            CoreSnapshotOwnerSectionRegistryV1.DetailDirectory,
            "core.detail-state",
            CoreSnapshotLogicalItemKindV1.DetailDirectoryItem,
            "CoreDetailDirectorySnapshotFragmentWireV1");
        Require(
            CoreSnapshotOwnerSectionRegistryV1.DomainRegistry,
            "core.domain-registry-state",
            CoreSnapshotLogicalItemKindV1.DomainRuntimeDescriptor,
            "CoreDomainRegistrySnapshotFragmentWireV1");
        Require(
            CoreSnapshotOwnerSectionRegistryV1.ConfigState,
            "config.simulation-core",
            CoreSnapshotLogicalItemKindV1.ConfigField,
            "CoreConfigStateSnapshotFragmentWireV1");

        if (!string.Equals(CoreSnapshotSectionWireRegistryV1.WorldStateHeaderDigestDomain,
                "mv.core-world-state-header.v1", StringComparison.Ordinal) ||
            !string.Equals(CoreSnapshotSectionWireRegistryV1.DomainRegistryDigestDomain,
                "mv.core-domain-registry-state.v1", StringComparison.Ordinal))
            throw new InvalidOperationException("Core snapshot semantic digest domain tags drifted from the P4-04 amendment.");
    }

    private static void Require(
        string sectionId,
        string schemaId,
        CoreSnapshotLogicalItemKindV1 itemKind,
        string fragmentMessageName)
    {
        var contract = CoreSnapshotSectionWireRegistryV1.Get(sectionId);
        if (!string.Equals(contract.SectionSchema.SchemaId.Value, schemaId, StringComparison.Ordinal) ||
            contract.SectionSchema.Version.Major != 1 ||
            contract.SectionSchema.Version.Minor != 0 ||
            contract.LogicalItemKind != itemKind ||
            !string.Equals(contract.FragmentMessageName, fragmentMessageName, StringComparison.Ordinal))
            throw new InvalidOperationException($"Core snapshot wire contract mismatch: {sectionId}");
    }
}
