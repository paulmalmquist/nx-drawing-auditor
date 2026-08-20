# NX Drawing Auditor

Local-first, read-only engineering drawing linting for Siemens NX. The initial implementation separates NX extraction from deterministic drawing/CAD and GD&T rules.

## Current capabilities

- Neutral JSON representation of drawings, sheets, views, annotations, CAD features, associativity, and evidence locations
- Evidence-aware drawing value/designation versus authoritative CAD comparison
- Manual dimension override detection
- Broken dimension associativity detection
- Duplicate non-reference feature definition detection across views
- Flatness-with-datums detection
- JSON and HTML findings reports
- Executable regression cases for the supplied defects
- Strict neutral-contract loading for legacy schema 1.0 and evidence-rich schema 1.1

The portable NX extractor boundary is present, while live extraction remains deliberately gated because this machine does not have NX or the company journal/drawing/profile. Follow `docs/NX-WORKSTATION.md` on the company workstation. The adapter fails closed instead of emitting partial or guessed evidence.

## Build and verify

The portable solution build requires a .NET 10 SDK plus the .NET 8 SDK/targeting pack installed side by side. Core and the default NX adapter target `net8.0`; the CLI and console regression runner target `net10.0`.

```powershell
dotnet build .\Relativity.DrawingAudit.slnx --configuration Release
dotnet run --project .\tests\Relativity.DrawingAudit.Tests\Relativity.DrawingAudit.Tests.csproj --configuration Release
```

Run the sample audit:

```powershell
dotnet run --project .\src\Relativity.DrawingAudit.Cli\Relativity.DrawingAudit.Cli.csproj --configuration Release -- .\examples\four-defect-audit.json .\artifacts
```

The CLI exits `1` when error findings exist, which is expected for the supplied defect fixture. Open `artifacts/audit-report.html` for the human-readable result.

`examples/four-defect-audit.json` remains the canonical schema-1.0 example. `examples/legacy-association-states-1.0.json` protects the legacy `broken` and `unsupported` wire values, and `examples/port-dash-mismatch-1.1.json` demonstrates the categorical `-12` versus `-16` evidence contract.

Publish the portable CLI as a complete self-contained Windows x64 directory with a company-approved package source that contains the required runtime packs:

```powershell
.\scripts\Publish-PortableCli.ps1 -PackageSource 'https://approved-package-source.example/v3/index.json'
```

The source is passed only to that restore invocation and is never persisted in NuGet configuration. The script smoke-tests the published executable against the canonical four-finding example and requires exit `1`.

To clone the optional research repositories on another machine:

```powershell
.\scripts\Clone-ResearchRepos.ps1
```

## NX workstation operator flow

Live NX work is native-session only; a Teamcenter-managed or unconfirmed session stops the milestone. On the NX workstation:

1. Run `scripts/Find-NxOpen.ps1` with the exact installed assembly/template paths and explicit pre-flight confirmations. It writes `%LOCALAPPDATA%\Relativity.DrawingAudit\nx-environment.json` with a blocker list and `gateReady` status.
2. When every blocker is resolved, run `scripts/Build-NxOpenAdapter.ps1`. The enabled build rejects missing, copied, mismatched, or incompatible Siemens assemblies before reference resolution.
3. Run the verified, read-only compiled journal against the already-open drawing and loaded model. NX extraction and deterministic auditing are intentionally separate operations.
4. After a completed extraction has atomically produced `audit-input.json`, run:

```powershell
.\scripts\Invoke-DrawingAudit.ps1 `
  -RunDirectory "$env:LOCALAPPDATA\Relativity.DrawingAudit\runs\<UTC timestamp>" `
  -CliPath 'C:\LocalAuditTools\drawing-audit-win-x64'
```

Exit `0` means the audit completed without error findings; exit `1` means the audit completed and found errors; exit `2` or higher means processing failed. Results remain in the run directory as `audit-result.json` and `audit-report.html`.

The current workstation implementation still reports `NX_API_REVIEW_REQUIRED`: the recorded journal and exact installed-release APIs have not been available for verification. This is an explicit Phase B blocker, not a result to bypass.

Run directories can contain complete extracted CAD evidence. They remain local, are never removed automatically, and must be retained or manually purged according to company policy.

## Safety boundary

The pilot must not open additional parts, update, modify, save, or close NX parts; write to Teamcenter; send drawing evidence to an external service; or claim complete ASME compliance. Standards-backed rules require approved citations and company interpretations recorded in `rules/RULE-CATALOG.md`.
