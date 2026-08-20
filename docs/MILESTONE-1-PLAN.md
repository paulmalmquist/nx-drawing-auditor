# First Siemens NX Drawing-Auditor Milestone — Revised Plan

Revision of 2026-08-20, incorporating the repo-verified review in `docs/PLAN-REVIEW.md`. The previous draft's architecture and scope are retained; this revision resolves the review's findings before implementation starts.

## Changes from the previous draft

- Association-state vocabulary keeps the existing wire names (`broken`, not `disconnected`); `unknown` is added; `unsupported` moves to the new extraction-state axis but remains accepted on read for 1.0 documents. (Review Finding 1)
- Every 1.1 model addition is a constructor parameter with a default value, so the five existing regressions and 1.0 JSON keep working unmodified. (Finding 2)
- Core multi-targets `net10.0;net8.0` in the portable build now; the NX-hosted framework mismatch is treated as the expected case, gated by the environment manifest, with a self-contained CLI publish as the runtime fallback. (Finding 3)
- Displayed-precision-aware value comparison replaces the single global tolerance as the primary mismatch test. (Finding 4)
- The 1.1 duplicate-feature rule is constrained to same-characteristic annotations; cross-characteristic sharing of a feature downgrades to information severity. (Finding 5)
- The third_party normalization names the concrete edits: script default currently resolves inside the repo root and must move to the true sibling, `third_party/` is added to `.gitignore`, commit IDs go to full length, and the `nxopen-lib-sparse` name changes to `nxopen-lib` in both the script and `THIRD-PARTY.md`. (Finding 6)
- The −12/−16 acceptance uses |difference| = 4 with the sign convention documented from live extraction rather than a hardcoded `+4`. (Finding 7)
- A workstation pre-flight checklist is added, and the milestone is explicitly sequenced as portable phase → manifest gate → live phase. (Finding 8, sequencing)

## Summary

- Preserve the existing architecture: NX extraction → neutral JSON → deterministic rules → local JSON/HTML report.
- Keep the portable .NET 10 build working; its Release build and all five current regressions pass (re-verified 2026-08-20 on a clean SDK) and must stay green throughout.
- Perform the live binding on the company NX workstation against an already-open native drawing, using the supplied journal and real −12/−16 case, only after the environment-manifest gate.
- Treat the milestone as successful only when direct NX evidence identifies the annotation, associated geometry, owning feature, and authoritative −16 value without modifying the NX session.

## Sequencing and gates

- **Phase A (portable, this machine, no NX):** contract 1.1 and schema, association/extraction state model, rule gating and assessments, reporting extraction, expanded test suite, third_party normalization, `net8.0` multi-target of Core, self-contained CLI publish. Everything in Phase A is fully regression-tested before anyone sits at the NX machine.
- **Gate:** the workstation environment manifest. Its NX release and hosted-runtime answer decides the adapter target framework (net8.0 expected; a .NET Framework floor triggers a scoped re-plan of the Core retarget cost before proceeding) and whether the .NET 10 CLI or the self-contained publish is used.
- **Phase B (workstation):** journal review, managed-API recording, dual-mode binding, live extraction, acceptance on the −12/−16 case.

## Implementation Changes

- Normalize research checkouts to the true sibling directory `C:\Projects\third_party`: change `Clone-ResearchRepos.ps1`'s default destination (it currently resolves inside the repo root), update `THIRD-PARTY.md`'s "adjacent" wording, record full 40-character commit IDs, verify checked-out license files, add `third_party/` to `.gitignore`, and rename `nxopen-lib-sparse` to `nxopen-lib` consistently in both the script and the inventory. Never reference cadRip, EngVision, or mirror assemblies from the product build; block NIST source reuse pending terms review.
- On the NX workstation, as the first act and as the Phase B gate, capture an environment manifest containing NX release/build, `NXOpen.dll` and `NXOpen.UF.dll` paths and versions, the .NET runtime the installed NX templates require, installed .NET SDK/runtime availability, Python-stub availability, licensing, journal-execution policy (unsigned journals permitted or not), and native-session confirmation.
- Review the recorded selections for the associative dimension, overridden dimension, hole callout, FCF, and datum symbol. Record the exact managed API types and properties exposed by that release; use UF drafting reads only for a documented managed-API gap.
- Make `src/Relativity.DrawingAudit.NxOpen` dual-mode:
  - The default portable build retains a no-NX stub.
  - Workstation properties `NxOpenRoot` and `NxTargetFramework` enable the real extractor, reference only installed Siemens assemblies with `Private=false`, and fail fast when the paths/runtime do not match the installed template.
  - Multi-target the shared Core project to `net10.0;net8.0` in Phase A (its records, collection expressions, and in-box System.Text.Json all compile on net8.0). CLI and tests stay on .NET 10. A .NET Framework-only NX release found at the gate is a scoped decision point, not an in-flight improvisation.
  - Scope a targeted `NoWarn` for assembly-resolution warnings (e.g., MSB3277-class noise from `Private=false` Siemens references) to the NxOpen project only; `TreatWarningsAsErrors` stays global otherwise.
- Publish the CLI self-contained (`dotnet publish -r win-x64 --self-contained`) as a Phase A artifact so the workstation needs no .NET 10 runtime install.
- Implement a compiled, in-process NX entry point using the installed template's journal signature. It reads only the active displayed drawing and referenced loaded model; it must not open, update, save, close, revise, or check in parts.
- Enumerate sheets, drafting views, and drafting dimensions. For the known dimension subtype, collect raw displayed text, parsed nominal, displayed decimal places and units, NX automatic/computed value, reference state, all association slots, view membership/transform, NX tags, owning parts, and direct owning-feature relationships.
- Add one independent CAD-measurement handler for the actual geometry type found in the defect case. Use direct geometry/feature APIs only; unsupported dimension or geometry types produce diagnostics instead of inferred values.

## Public Contract and Rule Behavior

- Add an additive `1.1` neutral contract and JSON Schema at `schemas/audit-document-1.1.schema.json`, while continuing to accept the existing `1.0` fixture. **Additivity mechanism:** every new model member is a primary-constructor parameter with a default value (or init-only property), so all existing positional construction sites compile unchanged and System.Text.Json fills absent 1.0 properties from defaults.
- Extend `AuditDocument` with extraction metadata and diagnostics; extend views with NX evidence ID, orientation, transform, and referenced-part identity.
- Add `DimensionEvidence` containing displayed text/value, displayed decimal places, NX automatic value, CAD value, units, signed difference (`displayed − CAD`), measurement method, manual-override state, association state, associated geometry, owning feature, and confidence. **Precedence:** for 1.1 documents rules read `DimensionEvidence`; the legacy annotation fields (`displayedValue`, `cadMeasuredValue`) remain for 1.0 documents and are treated as derived in 1.1.
- **Comparison semantics:** the primary mismatch test rounds the CAD value to the dimension's displayed decimal places and compares against the parsed nominal; the profile comparison tolerance is a secondary guard only. A correctly rounded display of a full-precision measurement must never flag.
- Define composite evidence IDs from part number, revision, object type, and NX tag. NX tags are session-scoped, so these IDs are stable within a run only; make no cross-run identity claims. Never substitute names or geometric similarity when a direct association/owner cannot be established.
- Separate association state (`associated`, `partiallyAssociated`, `broken`, `unknown`) from extraction state (`complete`, `incomplete`, `unsupported`, `failed`). The wire names keep the existing enum's `broken` (the previous draft's "disconnected"); `unsupported` is no longer emitted as an association state in 1.1 but remains accepted on read for 1.0 documents and is normalized to extraction state at load. Add an explicit dimension assessment so the report distinguishes:

| Evidence | Assessment and behavior |
| --- | --- |
| Explicit override with complete CAD evidence | Manual override; run both override and mismatch rules as applicable |
| Fully associated, not overridden, automatic value inconsistent with CAD | Associative but stale; emit mismatch with the evidence conflict explained |
| Some required associations missing | Partially associated; emit associativity finding and suppress unsupported mismatch conclusions |
| No valid association (`broken`) | Emit associativity finding and suppress mismatch |
| API exception or unavailable required evidence | Extraction failure/unsupported diagnostic; no engineering conclusion |

- Require complete, direct, high-confidence evidence before `DRAWING_CAD_VALUE_MISMATCH` or duplicate-feature conclusions. Preserve all five rule IDs and legacy behavior for schema 1.0.
- For 1.1 documents, `DUPLICATE_FEATURE_DEFINITION` applies only to non-reference annotations defining the same characteristic of a feature (matching measurement kind/nominal); two annotations measuring different characteristics of one feature (e.g., a hole's diameter in one view and its depth in another) downgrade to an information-severity advisory instead of an error.
- Add machine-readable rule metadata with approval status. Leave unapproved citations, interpretations, owners, and dates explicitly pending and label findings as drafting-review advisories rather than ASME compliance conclusions.
- Move HTML rendering into testable reporting code. Show drawing/revision, sheet/view, annotation and geometry tags, owning feature/part, displayed/automatic/CAD values, difference, override/association assessment, confidence, rule ID, advisory status, and diagnostics.
- Write each run under `%LOCALAPPDATA%\Relativity.DrawingAudit\runs\<UTC timestamp>` and pass `audit-input.json` to the existing CLI to produce `audit-result.json` and `audit-report.html`. Each run persists a full extracted CAD snapshot; document a simple retention practice (keep the most recent runs, purge the rest manually) so snapshots don't accumulate on the shared workstation.

## Test and Acceptance Plan

- Preserve the five current regressions untouched (guaranteed compilable by the additive-defaults convention) and add:
  - One integrated same-feature scenario with wrong left value, correct right value, duplicate non-reference definitions, and an allowed reference occurrence.
  - Positive and negative manual-override cases.
  - Fully associated stale, partially associated, broken, unsupported, and failed-extraction cases.
  - Evidence-conflict, missing-value, parsing-failure, and comparison-tolerance boundaries — including the display-rounding must-not-flag case (full-precision CAD value whose correctly rounded display matches the nominal).
  - Positive and negative flatness cases.
  - A named placeholder fixture for the undescribed second colleague example.
  - Schema 1.0 compatibility using an extended canonical 1.0 fixture that exercises `"broken"` and `"unsupported"` association strings (the current example only uses `"associated"` and would not catch a vocabulary regression), schema 1.1 validation, diagnostic serialization, and required HTML evidence fields.
- On the workstation, record part modified-state flags before and after extraction and require them to remain unchanged.
- For the real case, require JSON and HTML to contain the actual drawing/revision, sheet/view, annotation tag, associated geometry tags, owning feature, displayed −12 text, CAD-derived 16-magnitude value, |difference| = 4 with the sign convention documented from what live extraction actually returns, override/association classification, confidence, and `DRAWING_CAD_VALUE_MISMATCH`.
- If any required association, ownership, override, or measurement API remains ambiguous, emit an unsupported diagnostic and report the milestone incomplete rather than fabricating the missing evidence.

## Workstation pre-flight checklist

Confirm before the Phase B session is scheduled: the recorded journal's location and readability (it is not in the repo); the defect drawing is accessible, native (not Teamcenter-managed), and openable with the referenced model loaded; an NX license seat is available for the session; site policy permits executing unsigned/compiled journals; and either a .NET 10 runtime is present or the self-contained CLI publish is on hand.

## Assumptions and Defaults

- The live phase runs on the company NX workstation with the drawing already open in native NX; both the recorded journal and defect drawing/model will be available there (verified by the pre-flight checklist, not assumed on the day).
- A local profile will provide the authoritative native NX attribute keys for part number and revision, standard profile, and the secondary comparison tolerance. Missing identity attributes fail acceptance rather than falling back to guessed filenames or title-block text.
- Hole-callout, FCF, and datum-symbol journal APIs are reviewed in this milestone, but production extraction for them is deferred unless required by the −12/−16 case.
- All CAD data, extracted JSON, reports, and logs remain local. No external model, service, or LLM evaluates rules or receives engineering data.
- No Git repository initialization, standards-text copying, Teamcenter integration, or NX visual overlay is included in this milestone (`.gitignore` gains `third_party/` now regardless, so a future `git init` is safe).
