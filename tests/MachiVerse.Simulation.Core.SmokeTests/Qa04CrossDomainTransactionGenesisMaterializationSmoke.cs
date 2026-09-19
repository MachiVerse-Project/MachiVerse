using System.Runtime.CompilerServices;
using MachiVerse.Simulation.Core.Determinism;
using MachiVerse.Simulation.Core.Performance;
using MachiVerse.Simulation.Core.Runtime;

internal static class Qa04CrossDomainTransactionGenesisMaterializationSmoke
{
    [ModuleInitializer]
    internal static void Run()
    {
        Qa04CrossDomainTransactionGenesisMaterializerV1.ValidateCanonicalContract();
        var pools = CreatePools();
        var material = Qa04CrossDomainTransactionGenesisMaterializerV1.Materialize(pools);

        Require(material.Count == 10_000 && material.All(static binding => binding.State.IsActive),
            "QA-04 initial CrossDomainTransaction authority must contain exactly 10,000 ACTIVE states.");
        Require(material.Select(static binding => binding.AuthoritativeTransactionId).Distinct().Count() == 10_000,
            "QA-04 initial CrossDomainTransaction authoritative ids must be unique.");
        Require(material.Select(static binding => binding.DescriptorTransactionId).Distinct().Count() == 10_000,
            "QA-04 initial CrossDomainTransaction descriptor ids must remain one-to-one evidence.");
        Require(material.All(static binding =>
                binding.State.CreatedStep == 0 && binding.State.UpdatedStep == 0 && binding.State.TerminalStep is null &&
                binding.State.CanonicalDigest().Length == 32),
            "QA-04 initial CrossDomainTransaction persistent state genesis lifecycle/digest drifted.");

        var counts = material.GroupBy(static binding => binding.TransactionKind.Value, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.Count(), StringComparer.Ordinal);
        var expected = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["transaction.market-sale-delivery"] = 3_500,
            ["transaction.employment-work"] = 2_000,
            ["transaction.food-consumption"] = 1_000,
            ["transaction.information-transmission"] = 1_000,
            ["transaction.medical-service"] = 500,
            ["transaction.construction"] = 500,
            ["transaction.mining-excavation"] = 300,
            ["transaction.crime-justice"] = 300,
            ["transaction.border-crossing"] = 300,
            ["transaction.infrastructure-outage-cascade"] = 200,
            ["transaction.natural-disaster-cascade"] = 200,
            ["transaction.demolition"] = 34,
            ["transaction.birth"] = 34,
            ["transaction.death"] = 33,
            ["transaction.disease-transmission"] = 33,
            ["transaction.public-record"] = 33,
            ["transaction.military-operation"] = 33,
        };
        Require(counts.Count == CrossDomainTransactionKindRegistryV1.StandardKindCount &&
                expected.All(pair => counts.TryGetValue(pair.Key, out var actual) && actual == pair.Value),
            "QA-04 initial CrossDomainTransaction kind distribution drifted.");

        foreach (var binding in material)
        {
            Require(binding.ParticipantTargetRefs.Count == binding.State.Participants.Count,
                "Every persistent participant must retain one actual target binding evidence Ref.");
            for (var index = 0; index < binding.State.Participants.Count; index++)
            {
                var participant = binding.State.Participants[index];
                var target = binding.ParticipantTargetRefs[index];
                Require(target.PartitionId == participant.PartitionId &&
                        pools[target.PartitionId.Value].Contains(target.RecordId),
                    "Persistent participant target evidence must resolve into the supplied actual canonical pool.");
            }
        }

        var incomplete = new Dictionary<string, IReadOnlyList<OpaqueId128>>(pools, StringComparer.Ordinal);
        incomplete.Remove("environment.hazard");
        ExpectReject(
            () => Qa04CrossDomainTransactionGenesisMaterializerV1.MaterializeSlot(0, incomplete),
            "Missing actual participant target pool must fail closed.");
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
                    OpaqueId128.Parse((0x48000 + index * 4 + 1).ToString("x32")),
                    OpaqueId128.Parse((0x48000 + index * 4 + 2).ToString("x32")),
                    OpaqueId128.Parse((0x48000 + index * 4 + 3).ToString("x32")),
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
