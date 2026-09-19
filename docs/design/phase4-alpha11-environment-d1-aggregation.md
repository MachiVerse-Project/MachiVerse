# Alpha 1.1 — Environment D1 canonical aggregation

Status: Decided normative supplement
Tracking: #240
Implementation: Draft PR #265
Parent: `phase4-alpha11-reference-world-decomposition.md`

## 1. Purpose

This document removes the remaining scalar-class ambiguity in the Environment D1 `4 D0 -> 1 D1` rule. It does not change the D0/D1 decomposition or identity rules from the parent document.

Each D1 record consumes exactly four D0 records from the same partition, in the source binding defined by `Qa04EnvironmentReferenceDecompositionV1`. Every D0 source is consumed exactly once across the canonical D1 set.

## 2. Extensive versus intensive values

The canonical rule is semantic, not CLR-type based.

- Extensive stock, quantity, flow-total, population, biomass, volume, mass and sample/count fields use checked SUM.
- Intensive state, level, length, coefficient, concentration, ratio, temperature, pressure and vector fields use round-to-even arithmetic MEAN.
- Tokens use MODE; equal-frequency ties use ASCII ascending.
- Token lists whose benchmark genesis members are invariant across the four sources retain that identical canonical list. Divergent lists are invalid for `perf.reference.v1`.
- RefList fields use canonical set union ordered by `(partition_id ASCII, record_id)`.
- Generation/revision-like fields use `max(source)+1`.
- Begin/basis Step fields use `max(source)`.
- Optional end Step uses NONE when all sources are absent, otherwise the minimum present value.
- The D1 `spatial_scope` is the canonical TileScope selected by the D1 descriptor `RegionalTileIndex`; it is not synthesized from source scopes.

## 3. Field mapping

| Partition | SUM | round-to-even MEAN | MODE / invariant | canonical set union | max+1 / max-step |
|---|---|---|---|---|---|
| geology | — | `porosity_ppm`, `stability_ppm`, `permeability_q32` | `material_classes` invariant | `strata_refs`, `fault_refs`, `resource_refs` | — |
| soil | `organic_mass_g` | `depth_mm`, `moisture_ppm`, `fertility_ppm` | `soil_class` | `contaminant_refs` | — |
| resource_deposit | `remaining_mass_g`, `renewal_rate_g_per_step` | `grade_ppm`, `accessibility_ppm` | `resource_kind` | — | — |
| groundwater | `water_volume_ml` | `hydraulic_head_mm`, `quality_ppm`, `temperature_mk` | — | `neighbor_refs` | — |
| atmosphere | `vapor_mass_g`, `liquid_mass_g` | `pressure_pa`, `temperature_mk`, `humidity_ppm`, `wind_um_s`, each gas ppb entry | gas keys invariant | — | — |
| climate | `sample_count` | `temperature_mean_mk`, `precipitation_mean_ml`, `wind_mean_um_s` | `regime` | — | `aggregate_generation=max+1` |
| weather | `precipitation_ml_per_step` | `cloud_ppm`, `visibility_mm`, `storm_intensity_ppm` | `weather_class` | — | `basis_atmosphere_revision=max+1` |
| surface_water | `volume_ml` | `surface_level_mm`, `flow_um_s`, `temperature_mk`, `quality_ppm` | `water_body_class` | `downstream_refs` | — |
| ocean | `water_volume_ml` | `surface_level_mm`, `velocity_um_s`, `temperature_mk`, `salinity_ppm` | — | `neighbor_refs` | — |
| ecosystem | `population`, `biomass_g` | `birth_rate_ppm`, `death_rate_ppm`, `migration_rate_ppm` | `species_or_cohort` | `resource_refs` | — |
| contaminant | `stock_mass_g` | `concentration_ppb` | `contaminant_kind` | `source_refs`, `sink_refs` | — |
| hazard | — | `intensity_ppm` | `hazard_kind` | `driver_refs`, `affected_scope_refs` | `started_step=max`; `expected_end_step=min-present` |
| environment_lineage | — | — | `materialization_kind=perf.aggregate-d1` | `parent_refs` are the exact four D0 lineage sources | `generation=max(source)+1` |

`renewal_rate_g_per_step` and `precipitation_ml_per_step` are extensive rates because the D1 aggregate represents the total rate over the four source records. `depth_mm`, `surface_level_mm`, `hydraulic_head_mm`, `visibility_mm`, and `permeability_q32` describe aggregate state rather than additive stock and therefore use MEAN.

## 4. Lineage digest

For `environment.environment_lineage` D1 records:

- `subject_ref` is `Qa04EnvironmentLineageAuthorityV1.ResolveD1Subject(binding)`.
- `parent_refs` are `Qa04EnvironmentLineageAuthorityV1.ResolveD1Parents(binding)`.
- `generation = max(source generation) + 1`.
- `materialization_kind = perf.aggregate-d1`.
- `source_digest` is `HashSuite.DomainHash("mv.perf-reference-environment-d1-source.v1", ...)` over the four source payload canonical digests in source RecordId ascending order.

Changing the hash domain or source ordering is a compatibility change to this benchmark profile.

## 5. Acceptance

The production D1 evidence must prove:

1. exactly 250,000 D1 records;
2. exactly 1,000,000 D0 source bindings consumed;
3. every D0 source ordinal appears exactly once;
4. every D1 source remains in the same partition as its D1 target;
5. all required refs resolve through canonical TileScope / Environment authority;
6. all thirteen partition headers are canonical at revision 1, basis Step 0, D1 detail level;
7. Snapshot recovery reproduces the same thirteen semantic partition digests.
