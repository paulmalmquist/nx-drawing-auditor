# Review: First NX Drawing-Auditor Milestone Plan

Reviewed 2026-08-20 against the repository at `C:\Projects\nx-drawing-auditor`. As part of this review the solution was rebuilt from the current sources on a clean .NET 10.0.400 SDK: the Release build completed with zero warnings, all five regression cases passed, and the CLI reproduced the committed four-finding report (exit code 1) from `examples/four-defect-audit.json`.

## Overall verdict

The plan is accurate about the repo and sound in its architecture. Every factual claim it makes about current state checks out: the five rule IDs match `Rules.cs` exactly, the five regressions exist and pass, the NxOpen project is the stub the plan describes, the CLI contract (input JSON → `audit-result.json` + `audit-report.html`, exit 1 on errors) is as stated, and the dual assessment table (override / stale / partially associated / disconnected / failed) is the strongest part — it fixes the real weakness of the current `DRAWING_CAD_VALUE_MISMATCH` rule, which today fires on any displayed-vs-measured delta with no regard for association or override state. The evidence-or-diagnostic stance ("report the milestone incomplete rather than fabricating missing evidence") matches the repo's existing safety boundary.

The findings below are ordered by how likely each is to sink the milestone. The first four are the ones worth resolving before implementation starts.

## Finding 1 — The planned association-state names silently break 1.0 compatibility, and the planned compat test won't catch it

**Severity: high.** The plan specifies association states `associated`, `partiallyAssociated`, `disconnected`, `unknown`. The existing enum in `Models.cs` is `Associated`, `PartiallyAssociated`, `Broken`, `Unsupported`, serialized camel-case via `JsonStringEnumConverter`. Renaming `Broken` → `Disconnected` (and moving `Unsupported` out to the new extraction-state axis) means any 1.0 document containing `"associationStatus": "broken"` or `"unsupported"` throws on deserialization. The trap: the planned "schema 1.0 compatibility" test will presumably use `examples/four-defect-audit.json` — and that fixture only ever uses `"associated"`, so the test passes while real 1.0 documents break. `BROKEN_DIMENSION_ASSOCIATIVITY` also keys on `AssociationStatus.Broken` specifically.

**Recommendation.** Decide the vocabulary explicitly before coding: either keep `broken`/`unsupported` as the wire names and let the plan's prose adopt them, or add the new members additively and accept the old strings as aliases via a tolerant converter. Either way, extend the 1.0 compat fixture so it actually exercises `"broken"` and `"unsupported"` values — that turns the compat promise into an enforced one.

## Finding 2 — "Additive 1.1" needs one stated mechanism, or the preserved regressions won't compile

**Severity: high.** Every model in `Core` is a positional record, and all five regression tests plus the fixture helpers construct them positionally (`new AuditDocument("1.0", identity, sheets, features)`). Adding required parameters for the 1.1 fields (extraction metadata, diagnostics, view transforms, `DimensionEvidence`) breaks every existing construction site and 1.0 JSON deserialization simultaneously — directly contradicting the plan's promise to preserve the five regressions and the 1.0 fixture.

**Recommendation.** Adopt one convention and state it in the plan: every 1.1 addition is a constructor parameter with a default value (or an init-only property). System.Text.Json binds records through the primary constructor and fills missing JSON properties from defaults, so this single rule delivers "additive 1.1" for both compilation and 1.0 parsing at once. Also decide precedence now for the duplication the plan creates: `DrawingAnnotation` already carries `DisplayedValue`/`CadMeasuredValue`, and `DimensionEvidence` carries displayed and CAD values again. State which one the rules read when both are present (cleanest: in 1.1, evidence wins and the legacy fields are treated as derived).

## Finding 3 — Treat the NX target-framework mismatch as the expected case, not a contingency

**Severity: high.** The plan phrases it as "if the NX template cannot reference net10.0." That "if" is close to certain: NX-hosted NXOpen automation runs on whatever runtime Siemens ships support for (recent releases the .NET 8 family; older releases .NET Framework), and no shipping NX release hosts .NET 10. So the Core multi-target is the default path, and its cost depends heavily on which floor the workstation lands on. If it's .NET 8: nearly free — the records, collection expressions, and in-box System.Text.Json used by `Core` all work, so `<TargetFrameworks>net10.0;net8.0</TargetFrameworks>` can be added ahead of the workstation visit. If it's .NET Framework 4.8 (older NX): meaningfully expensive — `System.Text.Json` becomes a NuGet dependency, records need an `IsExternalInit` shim, and `TreatWarningsAsErrors` will amplify any package-resolution warnings.

**Recommendation.** Make the environment-manifest capture an explicit gate that happens before any adapter coding, and pre-stage the net8.0 multi-target now since it's cheap insurance. Separately: the "pass audit-input.json to the existing CLI" step assumes a .NET 10 runtime exists on the workstation and that installing one is permitted. Publishing the CLI self-contained (`dotnet publish -r win-x64 --self-contained`) removes that dependency entirely and is worth adding to the milestone as a fallback artifact.

## Finding 4 — A single comparison tolerance will drown the pilot in rounding false positives

**Severity: high for pilot credibility.** The current rule compares the parsed displayed nominal against the CAD measurement with a fixed tolerance (1e-6 in fixtures; the plan makes it a profile setting). On a real drawing this false-positives on nearly every dimension, because displayed text is rounded to the dimension's display precision: a true 45.2378 mm edge shown as "45.24" differs from its own measurement by 0.0022 — a mismatch at any plausible global epsilon, yet completely correct drafting. The −12/−16 case survives because the delta is 4, but the finding will be buried in noise, which is the worst outcome for a first live demonstration.

**Recommendation.** Capture each dimension's displayed decimal places (NX exposes display precision on the dimension) in `DimensionEvidence`, and define the comparison as: round the CAD value to the displayed precision, then compare against the parsed nominal, with the profile tolerance as a secondary guard. The plan's "comparison-tolerance boundary" tests should include exactly this rounding case as a must-not-flag scenario.

## Finding 5 — Duplicate-feature rule semantics are too coarse for live extraction

**Severity: medium.** `DUPLICATE_FEATURE_DEFINITION` flags any two non-reference dimension/hole-callout annotations that associate to the same feature ID. On the synthetic fixture that's correct; on a real extracted drawing it fires whenever a hole's diameter is called out in one view and its position or depth in another — both legitimately associate to the same feature, and neither is a duplicate definition. Since the milestone's live run will extract real association sets, this rule may generate more findings than the mismatch rule it's meant to accompany.

**Recommendation.** For 1.1 documents, constrain duplication to annotations measuring the same characteristic (same parsed value kind, or matching nominal), or downgrade the cross-characteristic case to information severity. Legacy 1.0 behavior stays as is, which the plan already provides for.

## Finding 6 — The third_party "normalization" is a real change, and the plan understates it

**Severity: medium.** The plan says "normalize research checkouts to sibling `../third_party`." Currently `Clone-ResearchRepos.ps1` defaults to `Join-Path (Split-Path $PSScriptRoot -Parent) 'third_party'`, which resolves *inside* the repo root (`C:\Projects\nx-drawing-auditor\third_party`), and `THIRD-PARTY.md` calls this "adjacent," while `.gitignore` does not exclude `third_party/`. So today's layout would put eight research clones — including GPL-3.0 `cadrip` and unlicensed `engvision` — inside the working tree the moment the script runs, and inside the future Git repo the moment `git init` happens. The naming item is similar: both the script and the inventory currently say `nxopen-lib-sparse`, so "use nxopen-lib consistently" means touching both files, not one.

**Recommendation.** Change the script default to the true sibling (`Split-Path` twice), update `THIRD-PARTY.md` wording and the commit column to full 40-char IDs as planned, and add `third_party/` to `.gitignore` anyway as a belt-and-suspenders measure. The plan's licensing postures themselves (no cadRip/EngVision/mirror references in the product build, NIST blocked pending terms) match what `THIRD-PARTY.md` already records — no gaps there.

## Finding 7 — The −12/−16 acceptance hardcodes a sign convention the extractor may not produce

**Severity: medium.** Acceptance requires "displayed −12, CAD-derived −16, difference +4." Within the plan that arithmetic is consistent, and it matches the synthetic fixture, which stores literal negatives. But NX dimension computed values and geometry measurements are typically positive magnitudes with direction carried elsewhere; if live extraction yields 12 and 16, the signed difference is −4 and strict acceptance fails on a formality while the engineering conclusion is correct.

**Recommendation.** Define the convention in the contract (e.g., compare magnitudes, record direction/sign as part of parsed-text evidence) or phrase acceptance as |difference| = 4 with the sign convention documented from whatever the real extraction shows. Decide it before the workstation session so the acceptance check isn't renegotiated live.

## Finding 8 — External dependencies of the live phase aren't in the repo

**Severity: medium.** The "supplied journal" with the five recorded selections (associative dim, overridden dim, hole callout, FCF, datum symbol per `docs/NX-WORKSTATION.md`) exists nowhere in the repository, and neither does anything identifying the defect drawing. The milestone's central activity — "review the recorded selections... record the exact managed API types" — therefore depends entirely on materials that can't be verified in advance.

**Recommendation.** Before the workstation day, confirm where the journal lives and bring a pre-flight checklist: journal file present, defect drawing accessible and openable natively (not Teamcenter-managed, per the manifest step), NX license seat available, permission to run unsigned journals (`UGII_JOURNAL_...` / author-signing settings can block execution on locked-down installs), and either a .NET 10 runtime or the self-contained CLI from Finding 3.

## Smaller notes

**NX tags are session-scoped.** Composite evidence IDs built from part/revision/type/NX tag are stable within a run but not across NX sessions; tags are transient handles. That's fine for this milestone's per-run evidence — just avoid wording that promises cross-run identity. Journal identifiers or feature names are the future answer if runs must be diffed.

**`TreatWarningsAsErrors` is global.** With `Private=false` references to installed Siemens assemblies, any assembly-resolution warning (version mismatch chatter like MSB3277) fails the workstation build. Consider scoping a targeted `NoWarn` in the NxOpen project only, keeping the strict default everywhere else.

**No JSON round-trip test exists today.** All five regressions build documents in code; `examples/four-defect-audit.json` is exercised only when someone runs the CLI by hand. The plan's serialization and schema tests fix a real gap — make the existing example the canonical 1.0 fixture (extended per Finding 1) so it's enforced continuously.

**HTML relocation is justified.** Rendering currently lives as a local function inside `Cli/Program.cs`, untestable and missing sheet/view context; the planned move into testable reporting code with the fuller evidence columns is straightforwardly right.

**Run directory under `%LOCALAPPDATA%`.** Consistent with the local-only stance, but each run persists a full extracted CAD snapshot; add a one-line retention/cleanup note so runs don't accumulate indefinitely on the shared workstation.

**Extraction-state separation resolves an existing conflation.** Today `Unsupported` sits inside `AssociationStatus`, mixing "we couldn't read it" with "it isn't attached." The plan's two-axis model (association state vs extraction state) is the correct fix; Finding 1 only concerns how the wire names migrate.

## Suggested sequencing adjustment

The plan's items are individually right but the ordering should make the environment manifest the first workstation act and an explicit gate: the manifest's NX release and runtime answer determines the Core multi-target cost (Finding 3), the journal review scope, and whether the .NET 10 CLI can run at all. Everything portable — contract 1.1 with the additive-defaults convention, schema file, assessment states, rule gating, reporting extraction, the expanded test suite, tolerance semantics — can be built and fully regression-tested before anyone sits at the NX machine, which is also the order that keeps the five existing regressions green the whole way.
