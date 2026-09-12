using System.Diagnostics.CodeAnalysis;
using MachiVerse.Simulation.Core.Determinism;

namespace MachiVerse.Simulation.Core.WorldState;

public enum DomainPayloadFieldKindV1
{
    Ref,
    RefList,
    Id128,
    Token,
    TokenList,
    Ratio,
    Step,
    UInt8,
    UInt16,
    UInt32,
    UInt64,
    Int32,
    Int64,
    Bool,
    Digest,
    Vec3,
    Quat,
    Length,
    Mass,
    Volume,
    Temperature,
    Pressure,
    Power,
    Energy,
    Money,
    OrderedTokenUInt8Map,
    OrderedTokenUInt32Map,
    OrderedTokenInt32Map,
    OrderedTokenList,
    OrderedNestedList,
    RuleAst,
}

public readonly record struct Vec3Int64V1(long X, long Y, long Z);
public readonly record struct QuaternionQ30V1(int X, int Y, int Z, int W);

public sealed record DomainPayloadFieldRuleV1(
    string Name,
    DomainPayloadFieldKindV1 Kind,
    bool Optional);

public sealed record DomainPayloadSchemaDescriptorV1(
    StableToken PartitionId,
    SchemaRefV1 RecordSchema,
    IReadOnlyList<DomainPayloadFieldRuleV1> Fields);

public interface IDomainRecordReferenceResolverV1
{
    bool Exists(PartitionRecordRefV1 reference);
}

public static class StandardDomainPayloadSchemaRegistry
{
    private static readonly IReadOnlyDictionary<string, DomainPayloadSchemaDescriptorV1> ByPartition = Build();

    public static IReadOnlyList<DomainPayloadSchemaDescriptorV1> Entries { get; } = Array.AsReadOnly(
        ByPartition.Values.OrderBy(static value => value.PartitionId.Value, StringComparer.Ordinal).ToArray());

    static StandardDomainPayloadSchemaRegistry()
    {
        if (Entries.Count != StandardDomainPartitionRegistry.StandardPartitionCount)
            throw new InvalidOperationException($"Standard payload schema count must be 97, got {Entries.Count}.");
        foreach (var identity in StandardDomainPartitionRegistry.Entries)
        {
            if (!ByPartition.TryGetValue(identity.PartitionId.Value, out var descriptor))
                throw new InvalidOperationException($"Missing standard payload descriptor: {identity.PartitionId.Value}.");
            if (descriptor.RecordSchema != identity.RecordSchema)
                throw new InvalidOperationException($"Payload record schema mismatch: {identity.PartitionId.Value}.");
        }
    }

    public static DomainPayloadSchemaDescriptorV1 Get(string partitionId)
        => ByPartition.TryGetValue(partitionId, out var descriptor)
            ? descriptor
            : throw new KeyNotFoundException($"Unknown standard payload partition: {partitionId}");

    private static IReadOnlyDictionary<string, DomainPayloadSchemaDescriptorV1> Build()
    {
        var source = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["spatial.world_frame"] = "frame_kind:Token,parent_frame?:Ref,translation:Vec3,rotation:Quat,valid_scope:Ref,transform_revision:UInt64",
            ["spatial.scope_registry"] = "scope_class:Token,geometry_ref:Ref,parent_scope?:Ref,active_from:Step,retired_at?:Step,scope_flags:UInt32",
            ["spatial.terrain_geometry"] = "scope_ref:Ref,root_brick_ref:Ref,geometry_revision:UInt64,surface_classes:TokenList,connectivity_refs:RefList,archive_anchor?:Digest",
            ["spatial.void_geometry"] = "geometry_ref:Ref,connectivity:RefList,entrances:RefList,origin_class:Token,lifecycle:Token,geometry_revision:UInt64",
            ["spatial.containment_topology"] = "subject_ref:Ref,container_scope:Ref,relation_class:Token,basis_geometry_revision:UInt64",
            ["spatial.boundary_topology"] = "scope_a:Ref,scope_b:Ref,interface_geometry_ref:Ref,permeability_classes:TokenList,detail_policy_ref?:Ref,revision:UInt64",
            ["spatial.detail_regions"] = "scope_ref:Ref,level_by_domain:OrderedTokenUInt8Map,lineage_generation:UInt32,last_transition_step:Step,active_guards:TokenList",
            ["spatial.geometry_lineage"] = "subject_ref:Ref,parent_refs:RefList,creation_kind:Token,creation_ref?:Ref,generation:UInt32,source_digest:Digest",

            ["environment.geology"] = "spatial_scope:Ref,material_classes:TokenList,strata_refs:RefList,porosity_ppm:Ratio,stability_ppm:Ratio,permeability_q32:Int64,fault_refs:RefList,resource_refs:RefList",
            ["environment.soil"] = "spatial_scope:Ref,soil_class:Token,depth_mm:Length,moisture_ppm:Ratio,fertility_ppm:Ratio,organic_mass_g:Mass,contaminant_refs:RefList",
            ["environment.resource_deposit"] = "spatial_scope:Ref,resource_kind:Token,remaining_mass_g:Mass,grade_ppm:Ratio,renewal_rate_g_per_step:Int64,accessibility_ppm:Ratio",
            ["environment.groundwater"] = "spatial_scope:Ref,water_volume_ml:Volume,hydraulic_head_mm:Length,quality_ppm:Ratio,temperature_mk:Temperature,neighbor_refs:RefList",
            ["environment.atmosphere"] = "spatial_scope:Ref,pressure_pa:Pressure,temperature_mk:Temperature,humidity_ppm:Ratio,wind_um_s:Vec3,vapor_mass_g:Mass,liquid_mass_g:Mass,gas_ppb:OrderedTokenUInt32Map",
            ["environment.climate"] = "spatial_scope:Ref,regime:Token,temperature_mean_mk:Temperature,precipitation_mean_ml:Int64,wind_mean_um_s:Vec3,sample_count:UInt64,aggregate_generation:UInt32",
            ["environment.weather"] = "spatial_scope:Ref,weather_class:Token,precipitation_ml_per_step:Int64,cloud_ppm:Ratio,visibility_mm:Length,storm_intensity_ppm:Ratio,basis_atmosphere_revision:UInt64",
            ["environment.surface_water"] = "spatial_scope:Ref,water_body_class:Token,volume_ml:Volume,surface_level_mm:Length,flow_um_s:Vec3,temperature_mk:Temperature,quality_ppm:Ratio,downstream_refs:RefList",
            ["environment.ocean"] = "spatial_scope:Ref,water_volume_ml:Volume,surface_level_mm:Length,velocity_um_s:Vec3,temperature_mk:Temperature,salinity_ppm:Ratio,neighbor_refs:RefList",
            ["environment.ecosystem"] = "spatial_scope:Ref,species_or_cohort:Token,population:UInt64,biomass_g:Mass,birth_rate_ppm:Ratio,death_rate_ppm:Ratio,migration_rate_ppm:Ratio,resource_refs:RefList",
            ["environment.contaminant"] = "spatial_scope:Ref,contaminant_kind:Token,stock_mass_g:Mass,concentration_ppb:UInt32,source_refs:RefList,sink_refs:RefList",
            ["environment.hazard"] = "spatial_scope:Ref,hazard_kind:Token,intensity_ppm:Ratio,started_step:Step,expected_end_step?:Step,driver_refs:RefList,affected_scope_refs:RefList",
            ["environment.environment_lineage"] = "subject_ref:Ref,parent_refs:RefList,generation:UInt32,materialization_kind:Token,source_digest:Digest",

            ["physical.presence"] = "subject_ref:Ref,frame_ref:Ref,position:Vec3,orientation:Quat,linear_velocity:Vec3,angular_rate_urad_s:Vec3,shape_ref:Ref,containment_ref?:Ref,presence_mode:Token",
            ["physical.occupancy"] = "presence_ref:Ref,aabb_min:Vec3,aabb_max:Vec3,contact_refs:RefList,occupancy_flags:UInt32,collision_layer:UInt32",
            ["built.structure"] = "spatial_scope:Ref,structure_class:Token,geometry_parts:RefList,material_refs:RefList,integrity_ppm:Ratio,support_refs:RefList,lifecycle:Token",
            ["built.space"] = "structure_ref:Ref,spatial_scope:Ref,space_class:Token,opening_refs:RefList,adjacent_space_refs:RefList,capacity_count:UInt32",
            ["built.opening"] = "structure_ref:Ref,space_refs:RefList,opening_class:Token,mechanism_state:Token,locked:Bool,aperture_ppm:Ratio,geometry_ref:Ref",
            ["physical.container_location"] = "subject_ref:Ref,container_ref:Ref,slot_token?:Token,containment_mode:Token,quantity:Int64,mass_g:Mass",
            ["built.worksite"] = "spatial_scope:Ref,work_kind:Token,target_refs:RefList,progress_ppm:Ratio,required_material_refs:RefList,consumed_material_refs:RefList,worker_refs:RefList,status:Token",
            ["physical.condition"] = "subject_ref:Ref,condition_class:Token,integrity_ppm:Ratio,wear_ppm:Ratio,temperature_mk?:Temperature,damage_refs:RefList,maintenance_due_step?:Step",
            ["physical.combustion"] = "subject_ref:Ref,combustion_state:Token,fuel_mass_g:Mass,temperature_mk:Temperature,heat_output_mw:Power,smoke_mass_g:Mass,ignition_ref?:Ref",
            ["physical.material_handoff"] = "transaction_ref:Id128,material_kind:Token,source_ref:Ref,target_ref:Ref,mass_g:Mass,handoff_state:Token,prepared_step:Step,committed_step?:Step",
            ["physical.lineage"] = "subject_ref:Ref,parent_refs:RefList,material_source_refs:RefList,creation_kind:Token,generation:UInt32,source_digest:Digest",

            ["participation.binding"] = "binding_id:Id128,diver_ref:Id128,resident_ref:Ref,status:Token,effective_from:Step,ended_step?:Step,binding_generation:UInt32,absence_policy_ref?:Ref,causality_refs:RefList",
            ["participation.absence_policy"] = "diver_ref:Id128,policy_generation:UInt32,priority_rules:OrderedNestedList,effective_from:Step,effective_until?:Step",
            ["participation.control_mode"] = "resident_ref:Ref,binding_ref?:Ref,mode:Token,effective_from:Step,input_authority_generation:UInt32",
            ["participation.history"] = "binding_ref:Ref,history_kind:Token,basis_step:Step,previous_history_ref?:Ref,causality_digest:Digest",
            ["participation.detail_requirement"] = "resident_ref:Ref,minimum_detail:UInt8,scope_ref:Ref,reason:Token,effective_from:Step,effective_until?:Step",

            ["resident.identity_lifecycle"] = "resident_id:Id128,lifecycle:Token,birth_step?:Step,death_step?:Step,parent_refs:RefList,lineage_generation:UInt32,profile_token:Token",
            ["resident.body_health"] = "resident_ref:Ref,development_ppm:Ratio,health_capacity_ppm:Ratio,body_region_states:OrderedNestedList,injury_refs:RefList,disease_refs:RefList,recovery_ppm:Ratio",
            ["resident.physiology"] = "resident_ref:Ref,hunger_ppm:Ratio,thirst_ppm:Ratio,fatigue_ppm:Ratio,sleep_pressure_ppm:Ratio,thermal_stress_ppm:Ratio,hygiene_ppm:Ratio",
            ["resident.perception"] = "resident_ref:Ref,attention_target_refs:RefList,perceived_facts:OrderedNestedList,sensory_capacity_ppm:Ratio,basis_step:Step",
            ["resident.knowledge_belief"] = "resident_ref:Ref,subject_ref:Ref,proposition_token:Token,confidence_ppm:Ratio,evidence_refs:RefList,last_updated_step:Step",
            ["resident.memory"] = "resident_ref:Ref,memory_kind:Token,subject_refs:RefList,encoded_step:Step,salience_ppm:Ratio,confidence_ppm:Ratio,decay_state_ppm:Ratio",
            ["resident.psychology"] = "resident_ref:Ref,emotion_vector:OrderedTokenUInt32Map,stress_ppm:Ratio,traits:OrderedTokenUInt32Map,preferences:OrderedTokenInt32Map,values:OrderedTokenInt32Map",
            ["resident.goal_plan"] = "resident_ref:Ref,goal_token:Token,utility:Int64,status:Token,plan_actions:OrderedTokenList,current_action_index:UInt16,planning_generation:UInt32,target_refs:RefList",
            ["resident.skill_aptitude"] = "resident_ref:Ref,skill_token:Token,skill_ppm:Ratio,aptitude_ppm:Ratio,practice_accumulator:UInt64,last_practice_step?:Step",
            ["resident.relationship"] = "subject_resident:Ref,object_resident:Ref,relationship_kind:Token,affinity:Int32,trust_ppm:Ratio,familiarity_ppm:Ratio,status:Token",
            ["resident.family_lineage"] = "resident_ref:Ref,parent_refs:RefList,child_refs:RefList,family_relation_refs:RefList,generation_index:Int32",
            ["resident.behavior_state"] = "resident_ref:Ref,mode:Token,active_goal_ref?:Ref,active_action_token?:Token,action_target_refs:RefList,action_started_step?:Step,control_source:Token",
            ["resident.lineage"] = "resident_ref:Ref,source_aggregate_ref?:Ref,generation:UInt32,materialization_role:Token,creation_ref:Ref,source_digest:Digest",

            ["society.organization"] = "organization_id:Id128,organization_class:Token,lifecycle:Token,purpose_tokens:TokenList,parent_refs:RefList,facility_refs:RefList,founded_step:Step",
            ["society.membership_role"] = "organization_ref:Ref,member_ref:Ref,role_tokens:TokenList,authority_tokens:TokenList,joined_step:Step,ended_step?:Step,status:Token",
            ["society.employment"] = "employer_ref:Ref,worker_ref:Ref,job_token:Token,status:Token,started_step:Step,ended_step?:Step,wage_microunit_per_period:Money,pay_period_steps:UInt64,obligation_refs:RefList",
            ["society.household"] = "member_refs:RefList,shared_account_refs:RefList,residence_refs:RefList,resource_budget_refs:RefList,status:Token",
            ["society.contract_claim"] = "contract_kind:Token,party_refs:RefList,claimant_ref?:Ref,obligor_ref?:Ref,amount?:Money,quantity?:Int64,due_step?:Step,status:Token,terms_digest:Digest",
            ["society.property_right"] = "asset_ref:Ref,holder_ref:Ref,right_kind:Token,share_ppm:Ratio,effective_from:Step,effective_until?:Step,claim_ref?:Ref",
            ["society.currency_money"] = "currency_token:Token,issuer_ref:Ref,supply_microunit:Money,status:Token,policy_refs:RefList,unit_scale:UInt32",
            ["society.finance_account"] = "owner_ref:Ref,institution_ref?:Ref,currency_token:Token,balance_microunit:Money,credit_limit_microunit:Money,status:Token,ledger_head_digest:Digest",
            ["society.market_transaction"] = "market_ref:Ref,instrument_token:Token,order_side?:Token,limit_price?:Money,quantity:Int64,clearing_price?:Money,buyer_ref?:Ref,seller_ref?:Ref,eligible_step:Step,status:Token",
            ["society.business_production"] = "organization_ref:Ref,recipe_token:Token,planned_quantity:Int64,completed_quantity:Int64,input_refs:RefList,output_refs:RefList,work_required:UInt64,energy_required_mj:Energy,status:Token",
            ["society.logistics_obligation"] = "shipper_ref:Ref,consignee_ref:Ref,cargo_refs:RefList,quantity:Int64,origin_ref:Ref,destination_ref:Ref,due_step?:Step,status:Token,carrier_ref?:Ref",
            ["society.education"] = "provider_ref:Ref,learner_ref:Ref,program_token:Token,status:Token,progress_ppm:Ratio,skill_refs:RefList,started_step:Step,ended_step?:Step",
            ["society.culture"] = "subject_ref:Ref,trait_token:Token,affiliation_ppm:Ratio,adoption_step:Step,source_refs:RefList,status:Token",
            ["society.reputation"] = "subject_ref:Ref,audience_scope_ref?:Ref,dimension_token:Token,score:Int32,confidence_ppm:Ratio,evidence_refs:RefList,updated_step:Step",
            ["society.information_claim"] = "claimant_ref:Ref,subject_refs:RefList,claim_token:Token,content_digest:Digest,provenance_refs:RefList,created_step:Step,status:Token",
            ["society.history_lineage"] = "subject_ref:Ref,history_kind:Token,parent_refs:RefList,basis_step:Step,causality_digest:Digest",

            ["governance.polity"] = "related_org_refs:RefList,lifecycle:Token,institution_refs:RefList,jurisdiction_refs:RefList,claim_refs:RefList,control_refs:RefList,recognition_refs:RefList,fiscal_refs:RefList",
            ["governance.institution"] = "polity_ref:Ref,institution_kind:Token,office_refs:RefList,decision_method:Token,selection_rule_ref?:Ref,lifecycle:Token",
            ["governance.law_rule"] = "jurisdiction_ref:Ref,priority:Int32,specificity:UInt32,effective_from:Step,effective_until?:Step,predicate_ast:RuleAst,effect_ast:RuleAst,status:Token",
            ["governance.jurisdiction"] = "polity_ref:Ref,scope_ref:Ref,jurisdiction_kind:Token,subject_classes:TokenList,effective_from:Step,effective_until?:Step",
            ["governance.territorial_claim"] = "claimant_polity_ref:Ref,scope_ref:Ref,claim_kind:Token,strength_ppm:Ratio,effective_from:Step,effective_until?:Step,basis_refs:RefList",
            ["governance.effective_control"] = "controller_ref:Ref,scope_ref:Ref,control_ppm:Ratio,security_capacity_ppm:Ratio,effective_from:Step,basis_refs:RefList",
            ["governance.public_authority"] = "institution_ref:Ref,holder_ref:Ref,authority_tokens:TokenList,scope_refs:RefList,effective_from:Step,effective_until?:Step,status:Token",
            ["governance.tax_fiscal"] = "polity_ref:Ref,tax_kind:Token,tax_base_token:Token,rate_ppm:Ratio,claim_amount?:Money,debtor_ref?:Ref,due_step?:Step,status:Token",
            ["governance.permission_license"] = "subject_ref:Ref,authority_ref:Ref,permission_kind:Token,scope_refs:RefList,effective_from:Step,effective_until?:Step,status:Token,conditions_digest:Digest",
            ["governance.diplomacy"] = "party_refs:RefList,relation_kind:Token,status:Token,effective_from:Step,effective_until?:Step,instrument_refs:RefList,terms_digest:Digest",
            ["governance.security_incident"] = "incident_kind:Token,subject_refs:RefList,scope_ref:Ref,occurred_step:Step,fact_event_refs:RefList,status:Token,severity_ppm:Ratio",
            ["governance.investigation"] = "incident_ref:Ref,authority_ref:Ref,investigator_refs:RefList,evidence_refs:RefList,suspect_refs:RefList,status:Token,opened_step:Step,closed_step?:Step",
            ["governance.judicial_case"] = "case_kind:Token,jurisdiction_ref:Ref,party_refs:RefList,evidence_refs:RefList,charge_or_claim_refs:RefList,status:Token,opened_step:Step,decision_ref?:Ref",
            ["governance.enforcement"] = "authority_ref:Ref,order_kind:Token,subject_refs:RefList,target_refs:RefList,status:Token,issued_step:Step,effective_step?:Step,outcome_event_refs:RefList",
            ["governance.military_authority"] = "polity_ref:Ref,unit_or_org_ref:Ref,command_ref?:Ref,mission_token:Token,objective_refs:RefList,authority_scope_refs:RefList,status:Token,issued_step:Step",
            ["governance.border_control"] = "jurisdiction_ref:Ref,boundary_ref:Ref,checkpoint_refs:RefList,movement_rule_refs:RefList,status:Token,capacity_per_step:UInt32",
            ["governance.lineage"] = "subject_ref:Ref,predecessor_refs:RefList,succession_kind:Token,effective_step:Step,causality_digest:Digest",

            ["infrastructure.network_topology"] = "network_kind:Token,node_refs:RefList,edge_refs:RefList,operator_refs:RefList,scope_refs:RefList,status:Token,topology_revision:UInt64",
            ["infrastructure.transport_service"] = "network_ref:Ref,service_kind:Token,route_refs:RefList,capacity_per_step:UInt64,load:UInt64,schedule_ref?:Ref,availability_ppm:Ratio,status:Token",
            ["infrastructure.water_service"] = "network_ref:Ref,service_scope_ref:Ref,supply_ml_per_step:Volume,demand_ml_per_step:Volume,pressure_head_mm:Length,quality_ppm:Ratio,availability_ppm:Ratio,status:Token",
            ["infrastructure.power_service"] = "network_ref:Ref,service_scope_ref:Ref,generation_mw:Power,demand_mw:Power,delivered_mw:Power,availability_ppm:Ratio,status:Token",
            ["infrastructure.communication_service"] = "network_ref:Ref,service_scope_ref:Ref,capacity_units_per_step:UInt64,queued_units:UInt64,latency_steps:UInt32,availability_ppm:Ratio,status:Token",
            ["infrastructure.dependency"] = "consumer_ref:Ref,provider_ref:Ref,dependency_kind:Token,minimum_service_ppm:Ratio,degradation_curve_ref?:Ref,fallback_refs:RefList,status:Token",
            ["infrastructure.facility_service"] = "facility_ref:Ref,service_kind:Token,capacity_per_step:UInt32,active_load:UInt32,required_resource_refs:RefList,availability_ppm:Ratio,status:Token",
            ["infrastructure.service_queue"] = "service_ref:Ref,requester_ref:Ref,eligible_step:Step,semantic_priority:Int32,requested_units:UInt64,allocated_units:UInt64,status:Token",
            ["information.delivery"] = "content_ref:Ref,sender_ref:Ref,recipient_refs:RefList,channel_ref:Ref,eligible_step:Step,delivered_step?:Step,priority:Int32,status:Token,content_digest:Digest",
            ["information.media_distribution"] = "claim_ref:Ref,publisher_ref:Ref,channel_refs:RefList,audience_scope_refs:RefList,published_step:Step,reach_count:UInt64,status:Token",
            ["information.record_store"] = "record_kind:Token,authority_ref?:Ref,subject_refs:RefList,content_digest:Digest,version:UInt32,created_step:Step,available:Bool,supersedes_ref?:Ref",
            ["information.address_place_index"] = "place_ref:Ref,address_token:Token,scope_ref:Ref,valid_from:Step,valid_until?:Step,aliases:TokenList",
            ["infrastructure.failure_recovery"] = "subject_ref:Ref,failure_kind:Token,severity_ppm:Ratio,started_step:Step,recovery_progress_ppm:Ratio,expected_restore_step?:Step,dependency_refs:RefList,status:Token",
            ["infrastructure.lineage"] = "subject_ref:Ref,predecessor_refs:RefList,change_kind:Token,effective_step:Step,source_digest:Digest",
        };

        var result = new SortedDictionary<string, DomainPayloadSchemaDescriptorV1>(StringComparer.Ordinal);
        foreach (var (partitionId, fields) in source)
        {
            var identity = StandardDomainPartitionRegistry.Get(partitionId);
            var parsed = fields.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(ParseField)
                .ToArray();
            if (parsed.Select(static field => field.Name).Distinct(StringComparer.Ordinal).Count() != parsed.Length)
                throw new InvalidOperationException($"Duplicate payload field descriptor: {partitionId}.");
            result.Add(partitionId, new DomainPayloadSchemaDescriptorV1(identity.PartitionId, identity.RecordSchema, Array.AsReadOnly(parsed)));
        }
        return result;
    }

    private static DomainPayloadFieldRuleV1 ParseField(string encoded)
    {
        var separator = encoded.IndexOf(':');
        if (separator <= 0 || separator == encoded.Length - 1)
            throw new InvalidOperationException($"Invalid payload field rule: {encoded}.");
        var rawName = encoded[..separator];
        var optional = rawName.EndsWith("?", StringComparison.Ordinal);
        var name = optional ? rawName[..^1] : rawName;
        if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("Payload field name is empty.");
        if (!Enum.TryParse<DomainPayloadFieldKindV1>(encoded[(separator + 1)..], ignoreCase: false, out var kind))
            throw new InvalidOperationException($"Unknown payload field kind: {encoded}.");
        return new DomainPayloadFieldRuleV1(name, kind, optional);
    }
}

public sealed class StandardDomainPayloadValidatorV1
{
    public void Validate(
        string partitionId,
        IReadOnlyDictionary<string, object?> payload,
        IDomainRecordReferenceResolverV1? references = null)
    {
        ArgumentNullException.ThrowIfNull(payload);
        var descriptor = StandardDomainPayloadSchemaRegistry.Get(partitionId);
        var fieldNames = descriptor.Fields.Select(static field => field.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var key in payload.Keys)
        {
            if (!fieldNames.Contains(key))
                throw new InvalidDataException($"domain.payload.unknown-field:{partitionId}:{key}");
        }

        foreach (var field in descriptor.Fields)
        {
            if (!payload.TryGetValue(field.Name, out var value) || value is null)
            {
                if (!field.Optional)
                    throw new InvalidDataException($"domain.payload.required-field:{partitionId}:{field.Name}");
                continue;
            }
            ValidateField(partitionId, field, value, references);
        }
    }

    private static void ValidateField(
        string partitionId,
        DomainPayloadFieldRuleV1 field,
        object value,
        IDomainRecordReferenceResolverV1? references)
    {
        switch (field.Kind)
        {
            case DomainPayloadFieldKindV1.Ref:
                ValidateReference(partitionId, field.Name, Require<PartitionRecordRefV1>(value, partitionId, field), references);
                break;
            case DomainPayloadFieldKindV1.RefList:
                ValidateReferenceList(partitionId, field.Name, Require<IReadOnlyList<PartitionRecordRefV1>>(value, partitionId, field), references);
                break;
            case DomainPayloadFieldKindV1.Id128:
                if (Require<OpaqueId128>(value, partitionId, field).IsZero) ThrowRange(partitionId, field.Name);
                break;
            case DomainPayloadFieldKindV1.Token:
                ValidateToken(Require<string>(value, partitionId, field), partitionId, field.Name);
                break;
            case DomainPayloadFieldKindV1.TokenList:
                ValidateCanonicalTokenList(Require<IReadOnlyList<string>>(value, partitionId, field), partitionId, field.Name);
                break;
            case DomainPayloadFieldKindV1.OrderedTokenList:
                ValidateSemanticTokenList(Require<IReadOnlyList<string>>(value, partitionId, field), partitionId, field.Name);
                break;
            case DomainPayloadFieldKindV1.Ratio:
                if (Require<uint>(value, partitionId, field) > 1_000_000u) ThrowRange(partitionId, field.Name);
                break;
            case DomainPayloadFieldKindV1.Step:
            case DomainPayloadFieldKindV1.UInt64:
                _ = Require<ulong>(value, partitionId, field);
                break;
            case DomainPayloadFieldKindV1.UInt8:
                _ = Require<byte>(value, partitionId, field);
                break;
            case DomainPayloadFieldKindV1.UInt16:
                _ = Require<ushort>(value, partitionId, field);
                break;
            case DomainPayloadFieldKindV1.UInt32:
                _ = Require<uint>(value, partitionId, field);
                break;
            case DomainPayloadFieldKindV1.Int32:
            case DomainPayloadFieldKindV1.Temperature:
            case DomainPayloadFieldKindV1.Pressure:
                _ = Require<int>(value, partitionId, field);
                break;
            case DomainPayloadFieldKindV1.Int64:
            case DomainPayloadFieldKindV1.Length:
            case DomainPayloadFieldKindV1.Mass:
            case DomainPayloadFieldKindV1.Volume:
            case DomainPayloadFieldKindV1.Power:
            case DomainPayloadFieldKindV1.Energy:
            case DomainPayloadFieldKindV1.Money:
                _ = Require<long>(value, partitionId, field);
                break;
            case DomainPayloadFieldKindV1.Bool:
                _ = Require<bool>(value, partitionId, field);
                break;
            case DomainPayloadFieldKindV1.Digest:
                var digest = Require<byte[]>(value, partitionId, field);
                if (digest.Length != 32) ThrowRange(partitionId, field.Name);
                break;
            case DomainPayloadFieldKindV1.Vec3:
                _ = Require<Vec3Int64V1>(value, partitionId, field);
                break;
            case DomainPayloadFieldKindV1.Quat:
                var quaternion = Require<QuaternionQ30V1>(value, partitionId, field);
                if (quaternion.X == 0 && quaternion.Y == 0 && quaternion.Z == 0 && quaternion.W == 0)
                    ThrowRange(partitionId, field.Name);
                break;
            case DomainPayloadFieldKindV1.OrderedTokenUInt8Map:
                ValidateOrderedMap(Require<IReadOnlyList<KeyValuePair<string, byte>>>(value, partitionId, field), partitionId, field.Name);
                break;
            case DomainPayloadFieldKindV1.OrderedTokenUInt32Map:
                ValidateOrderedMap(Require<IReadOnlyList<KeyValuePair<string, uint>>>(value, partitionId, field), partitionId, field.Name);
                break;
            case DomainPayloadFieldKindV1.OrderedTokenInt32Map:
                ValidateOrderedMap(Require<IReadOnlyList<KeyValuePair<string, int>>>(value, partitionId, field), partitionId, field.Name);
                break;
            case DomainPayloadFieldKindV1.OrderedNestedList:
                ValidateNestedList(Require<IReadOnlyList<ICanonicalDomainNestedValueV1>>(value, partitionId, field), partitionId, field.Name);
                break;
            case DomainPayloadFieldKindV1.RuleAst:
                ValidateNestedValue(Require<ICanonicalDomainNestedValueV1>(value, partitionId, field), partitionId, field.Name);
                break;
            default:
                throw new InvalidOperationException($"Unhandled payload field kind: {field.Kind}.");
        }
    }

    private static T Require<T>(object value, string partitionId, DomainPayloadFieldRuleV1 field)
    {
        if (value is T typed) return typed;
        ThrowType(partitionId, field.Name, field.Kind);
        return default!;
    }

    private static void ValidateReference(
        string partitionId,
        string field,
        PartitionRecordRefV1 reference,
        IDomainRecordReferenceResolverV1? resolver)
    {
        if (reference.RecordId.IsZero) ThrowRange(partitionId, field);
        _ = StandardDomainPartitionRegistry.Get(reference.PartitionId.Value);
        if (resolver is not null && !resolver.Exists(reference))
            throw new InvalidDataException($"domain.payload.reference-validation:{partitionId}:{field}");
    }

    private static void ValidateReferenceList(
        string partitionId,
        string field,
        IReadOnlyList<PartitionRecordRefV1> references,
        IDomainRecordReferenceResolverV1? resolver)
    {
        PartitionRecordRefV1? previous = null;
        foreach (var reference in references)
        {
            ValidateReference(partitionId, field, reference, resolver);
            if (previous is { } before && CompareReference(before, reference) >= 0)
                throw new InvalidDataException($"domain.payload.canonical-list-order:{partitionId}:{field}");
            previous = reference;
        }
    }

    private static int CompareReference(PartitionRecordRefV1 left, PartitionRecordRefV1 right)
    {
        var partition = string.CompareOrdinal(left.PartitionId.Value, right.PartitionId.Value);
        return partition != 0 ? partition : left.RecordId.CompareTo(right.RecordId);
    }

    private static void ValidateCanonicalTokenList(IReadOnlyList<string> values, string partitionId, string field)
    {
        string? previous = null;
        foreach (var value in values)
        {
            ValidateToken(value, partitionId, field);
            if (previous is not null && string.CompareOrdinal(previous, value) >= 0)
                throw new InvalidDataException($"domain.payload.canonical-list-order:{partitionId}:{field}");
            previous = value;
        }
    }

    private static void ValidateSemanticTokenList(IReadOnlyList<string> values, string partitionId, string field)
    {
        foreach (var value in values)
            ValidateToken(value, partitionId, field);
    }

    private static void ValidateOrderedMap<T>(
        IReadOnlyList<KeyValuePair<string, T>> values,
        string partitionId,
        string field)
    {
        string? previous = null;
        foreach (var item in values)
        {
            ValidateToken(item.Key, partitionId, field);
            if (previous is not null && string.CompareOrdinal(previous, item.Key) >= 0)
                throw new InvalidDataException($"domain.payload.canonical-list-order:{partitionId}:{field}");
            previous = item.Key;
        }
    }

    private static void ValidateNestedList(
        IReadOnlyList<ICanonicalDomainNestedValueV1> values,
        string partitionId,
        string field)
    {
        foreach (var value in values)
        {
            if (value is null) ThrowType(partitionId, field, DomainPayloadFieldKindV1.OrderedNestedList);
            ValidateNestedValue(value, partitionId, field);
        }
    }

    private static void ValidateNestedValue(ICanonicalDomainNestedValueV1 value, string partitionId, string field)
    {
        try
        {
            value.ValidateCanonical();
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or OverflowException)
        {
            throw new InvalidDataException($"domain.payload.nested-invalid:{partitionId}:{field}", ex);
        }
    }

    private static void ValidateToken(string value, string partitionId, string field)
    {
        try
        {
            _ = new StableToken(value);
        }
        catch (ArgumentException)
        {
            ThrowRange(partitionId, field);
        }
    }

    [DoesNotReturn]
    private static void ThrowType(string partitionId, string field, DomainPayloadFieldKindV1 kind)
        => throw new InvalidDataException($"domain.payload.scalar-type:{partitionId}:{field}:{kind}");

    [DoesNotReturn]
    private static void ThrowRange(string partitionId, string field)
        => throw new InvalidDataException($"domain.payload.scalar-range:{partitionId}:{field}");
}

public interface ICanonicalDomainNestedValueV1
{
    void ValidateCanonical();
}
