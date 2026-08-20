# NX workstation integration

The deterministic audit engine remains independent of NX. The workstation adapter is a separate, gated `net8.0`/x64 build against the exact managed assemblies installed with the company's NX release. Mirror assemblies and copied Siemens source are not build inputs.

## Phase B status and hard stops

The portable adapter builds on a machine without NX, but live Phase B extraction is intentionally blocked. Do not weaken these gates to make a demonstration run:

- This repository does not contain an NX installation, the recorded five-selection journal, the native defect drawing/model, or an approved company audit profile.
- A Teamcenter-managed session is out of scope. The pilot requires an operator-confirmed native NX session; stop if the session is Teamcenter-managed or cannot be confirmed.
- Stop if the installed template is not `net8.0` and x64. A .NET Framework or pre-.NET 8 floor requires a separate compatibility plan.
- Stop if `NXOpen.dll` and `NXOpen.UF.dll` are missing, have different identities/versions, or do not come from the same installed managed-assembly directory.
- Stop if an NX license is unavailable or site policy prohibits the compiled journal/library.
- Stop if the referenced model is not already loaded. The extractor must not open parts to satisfy the audit.
- The current workstation source deliberately raises `NX_API_REVIEW_REQUIRED` after its safe session/display-part checks. It will not produce `audit-input.json` until the installed journal has verified the exact read-only APIs for modified flags, views, annotations, associations, ownership, and port designation provenance.

An unmet gate is an operational diagnostic, not an engineering finding. Do not infer evidence or mark the milestone complete.

## Prepare the workstation manifest

Before the session, confirm that the recorded journal selects:

- one associative drafting dimension;
- one manually overridden dimension;
- one hole callout;
- one feature-control frame;
- one datum feature symbol.

Keep the journal and the company audit profile outside controlled drawing directories and outside this repository. The profile must contain the company-approved part/revision attribute keys, secondary numeric tolerance, and revision-keyed port-family designation mapping. Do not treat a sample or placeholder mapping as approved engineering data.

Run discovery from the company workstation with explicit operator confirmations. Example paths below are placeholders and must be replaced:

```powershell
.\scripts\Find-NxOpen.ps1 `
  -NXOpenDllPath 'C:\Siemens\NX\managed\NXOpen.dll' `
  -NXOpenUfDllPath 'C:\Siemens\NX\managed\NXOpen.UF.dll' `
  -TemplateProjectPath 'C:\Siemens\NX\templates\ManagedJournal.csproj' `
  -JournalPath 'C:\LocalAuditInputs\recorded-selection-journal.cs' `
  -JournalSelectionsConfirmed `
  -DefectDrawingPath 'C:\LocalAuditInputs\defect-drawing.prt' `
  -ReferencedModelLoaded `
  -AuditProfilePath 'C:\LocalAuditInputs\company-audit-profile.json' `
  -AuditProfileContentsConfirmed `
  -SelfContainedCliPath 'C:\LocalAuditTools\drawing-audit-win-x64' `
  -SessionMode Native `
  -JournalExecutionPolicy Allowed `
  -NxLicenseStatus Available
```

The script writes `%LOCALAPPDATA%\Relativity.DrawingAudit\nx-environment.json` atomically. The manifest records release/build, exact assembly paths and versions, installed template framework/platform and entry-point signatures, Python-stub locations, SDK/runtime inventory, operator confirmations, and a machine-readable blocker list. `gateReady` is true only when every required fact is present and compatible.

Build the workstation adapter only through the manifest-aware wrapper:

```powershell
.\scripts\Build-NxOpenAdapter.ps1
```

The project also requires `EnableNxOpen=true`, exact assembly paths, and the gate properties at MSBuild time. The wrapper supplies them from the manifest. Missing or mismatched values fail before assembly reference resolution; assembly warnings are not suppressed.

Set the manifest path in the environment used to start NX so the in-process adapter can verify the actual loaded assemblies:

```powershell
$env:RELATIVITY_NX_AUDIT_MANIFEST = "$env:LOCALAPPDATA\Relativity.DrawingAudit\nx-environment.json"
```

The compiled `Main`/`GetUnloadOption` entry point is only a candidate until its signature is confirmed against the installed template and journal. It requests immediate unload.

## Read-only and fail-closed boundary

The adapter may read only the active displayed drawing and its already-loaded referenced model. It must never call part-open, update, builder commit, save, close, revise, check-in, or Teamcenter write APIs. Before real extraction is enabled, workstation-verified APIs must capture drawing and model modified flags before and after; any change invalidates the run.

Use managed NX Open APIs first. Add a UF drafting read only for a documented managed-API gap. Unsupported annotation types, ambiguous associations, missing ownership, or unavailable designation provenance produce diagnostics rather than guessed values.

On extraction failure, the journal writes a local `extraction-diagnostic.json` and does not write an audit input. A future verified reader must serialize to a temporary file, validate it, and atomically rename it to `audit-input.json` only when extraction is complete. The journal must never invoke the audit CLI from inside NX.

## Explicit audit step and exit codes

Completed extraction runs live under:

`%LOCALAPPDATA%\Relativity.DrawingAudit\runs\<UTC timestamp>\`

After NX has unloaded the extractor and a completed run contains `audit-input.json`, invoke the deterministic audit as a separate operator action:

```powershell
.\scripts\Invoke-DrawingAudit.ps1 `
  -RunDirectory "$env:LOCALAPPDATA\Relativity.DrawingAudit\runs\<UTC timestamp>" `
  -CliPath 'C:\LocalAuditTools\drawing-audit-win-x64'
```

The wrapper accepts either the intact self-contained publish directory or its `Relativity.DrawingAudit.Cli.exe`. It verifies that `audit-input.json` exists, preserves its SHA-256 hash, writes `audit-result.json` and `audit-report.html` into the same run, and interprets exits as follows:

- `0`: audit completed with no error-severity findings;
- `1`: audit completed successfully and found error-severity findings;
- `2` or greater: extraction/input/CLI/report processing failure.

An `extraction-diagnostic.json` without `audit-input.json` is an incomplete extraction and the wrapper returns a processing failure.

## Local data retention

Each completed run contains a full extracted CAD evidence snapshot plus derived reports. Nothing is uploaded, and no external model or service receives the drawing data. The tools do not delete runs automatically. Keep only the runs required by company policy and purge older run directories manually through the approved workstation process; do not place run directories in the source repository or a shared location by default.

## First acceptance test

The real port defect is categorical: displayed designation `-12` versus authoritative CAD designation `-16`. It is not signed arithmetic, so the report must state that numeric difference is not applicable. Acceptance requires direct NX object/feature evidence, designation provenance, override and association assessments, high confidence, and unchanged part modified-state flags. If any required API or evidence remains ambiguous, retain the diagnostic and report the milestone incomplete.
