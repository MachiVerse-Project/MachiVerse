using MachiVerse.Simulation.Core.Performance;
using MachiVerse.Simulation.Core.Runtime;

internal static class Qa04CanonicalOperationBindingSmoke
{
    internal static void Run()
    {
        Qa04CanonicalOperationBindingV1.ValidateCanonicalContract();
        Require(Qa04CanonicalOperationBindingV1.BoundFamilies.Count == 5,
            "QA-04 canonical Operation binding must expose five authority-complete families.");
        Require(Qa04CanonicalOperationBindingV1.PendingAuthorityFamilies.Count == 1,
            "QA-04 canonical Operation binding must keep only Infrastructure authority-pending.");

        var descriptors = Qa04ReferenceLoadV1.OperationsForStep(1).ToArray();
        var supported = Qa04CanonicalOperationBindingV1.BoundFamilies
            .Select(family => descriptors.First(descriptor => descriptor.FamilyToken == family))
            .ToArray();
        var scheduler = new OperationSchedulerStateV1(nextSchedulableStep: 2, freezeStep: null);

        foreach (var descriptor in supported)
        {
            var sourceDigest = descriptor.PayloadDigest.ToArray();
            var binding = Qa04CanonicalOperationBindingV1.Bind(descriptor, schedulingPolicyGeneration: 1);
            var repeat = Qa04CanonicalOperationBindingV1.Bind(descriptor, schedulingPolicyGeneration: 1);
            var recomputed = Qa04CanonicalOperationBindingV1.ComputeImmutablePayloadDigest(binding.Operation);

            Require(binding.SourceDescriptor == descriptor,
                "QA-04 Operation binding must preserve the source descriptor identity.");
            Require(binding.SourceDescriptor.PayloadDigest.SequenceEqual(sourceDigest),
                "QA-04 Operation binding must not mutate the source descriptor digest in place.");
            Require(binding.BoundDescriptor.OperationId == descriptor.OperationId &&
                    binding.BoundDescriptor.InjectionStep == descriptor.InjectionStep &&
                    binding.BoundDescriptor.FamilyToken == descriptor.FamilyToken &&
                    binding.BoundDescriptor.FamilyOrdinal == descriptor.FamilyOrdinal,
                "QA-04 bound descriptor must preserve source identity fields.");
            Require(binding.BoundDescriptor.PayloadDigest.SequenceEqual(binding.Operation.ImmutablePayloadDigest.ToByteArray()),
                "QA-04 bound descriptor digest must equal StandardOperation immutable payload digest.");
            Require(recomputed.SequenceEqual(binding.Operation.ImmutablePayloadDigest.ToByteArray()),
                "QA-04 StandardOperation digest must rehash from canonical immutable payload + admission.");
            Require(binding.Operation.OperationId.ToByteArray().SequenceEqual(descriptor.OperationId.ToBytes()),
                "QA-04 StandardOperation must preserve deterministic OperationId.");
            Require(binding.Operation.Admission is not null &&
                    binding.Operation.Admission.AdmissionBasisStep == descriptor.InjectionStep &&
                    binding.Operation.Admission.SchedulingPolicyGeneration == 1 &&
                    !binding.Operation.Admission.HasRequestedNotBeforeStep &&
                    !binding.Operation.Admission.HasRequestedDeadlineStep,
                "QA-04 StandardOperation admission must match canonical workload scheduling context.");
            Require(binding.Operation.Candidate is not null &&
                    binding.Operation.Candidate.CandidateStep == descriptor.InjectionStep + 1,
                "QA-04 StandardOperation candidate Step must be injection Step + 1.");
            Require(binding.Operation.OperationPayloadSchemaId == $"operation.{binding.Operation.OperationKind}" &&
                    binding.Operation.OperationPayloadSchemaVersion is not null &&
                    binding.Operation.OperationPayloadSchemaVersion.Major == 1 &&
                    binding.Operation.OperationPayloadSchemaVersion.Minor == 0 &&
                    binding.Operation.OperationPayload.Length > 0,
                "QA-04 StandardOperation payload schema identity must be explicit canonical v1.");
            Require(binding.PrimaryTarget.RecordId.IsZero == false,
                "QA-04 Operation primary target must be an actual non-zero authority ref.");
            Require(binding.ScheduledOperation.OperationId == descriptor.OperationId &&
                    binding.ScheduledOperation.EffectiveStep == descriptor.InjectionStep + 1 &&
                    binding.OrderKey.Phase == 1 &&
                    binding.OrderKey.SemanticPriority == 0 &&
                    binding.OrderKey.IntentId == descriptor.OperationId,
                "QA-04 Operation scheduling identity must match canonical workload ordering rules.");
            Require(binding.Operation.Equals(repeat.Operation) &&
                    binding.OrderKey.ToDatabaseBytes().SequenceEqual(repeat.OrderKey.ToDatabaseBytes()) &&
                    binding.BoundDescriptor.PayloadDigest.SequenceEqual(repeat.BoundDescriptor.PayloadDigest),
                "QA-04 Operation binding must be deterministic across repeated materialization.");

            scheduler.AddDurable(binding.ScheduledOperation);
        }

        Require(scheduler.ForEffectiveStep(2).Count == supported.Length,
            "QA-04 authority-complete Operation bindings must enter the ordinary scheduler without identity loss.");

        var governanceDescriptor = descriptors.First(item => item.FamilyToken.Value == "governance-security");
        var governance = Qa04CanonicalOperationBindingV1.Bind(governanceDescriptor, schedulingPolicyGeneration: 1);
        Require(governance.Operation.OperationKind == "governance.incident.register" &&
                governance.OwnerDomain.Value == "governance_security" &&
                governance.Operation.OperationPayload.Length > 0,
            "QA-04 governance-security family must bind to canonical governance incident registration.");

        RequirePending(descriptors, "infrastructure-service-delivery", "infrastructure-service-pool");
    }

    private static void RequirePending(
        IReadOnlyList<Qa04OperationDescriptorV1> descriptors,
        string family,
        string qualifier)
    {
        var descriptor = descriptors.First(item => item.FamilyToken.Value == family);
        try
        {
            _ = Qa04CanonicalOperationBindingV1.Bind(descriptor, schedulingPolicyGeneration: 1);
            throw new InvalidOperationException($"QA-04 pending Operation family '{family}' must fail closed.");
        }
        catch (InvalidDataException ex) when (
            ex.Message == $"qa04.workload.operation-authority-binding-undefined:{qualifier}")
        {
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
