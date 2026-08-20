# Initial rule catalog

Every production rule requires an engineering-approved standard citation and company interpretation before it can be represented as a compliance conclusion. Until then, findings are drafting-review advisories.

| Rule ID | Deterministic behavior | Approval state |
| --- | --- | --- |
| `DRAWING_CAD_VALUE_MISMATCH` | In 1.1, compares complete, direct, high-confidence semantic evidence. Port dash sizes are canonical designations (`-12` versus `-16`) with no numeric difference. Numeric values use explicit signed/magnitude semantics and displayed resolution. Incomplete or conflicting evidence suppresses the conclusion. | Implemented advisory; standard, edition, citation, owner, and approval date pending |
| `MANUAL_DIMENSION_OVERRIDE` | Reports a directly established manual override even when authoritative CAD-value extraction is unavailable; evaluates mismatch separately when CAD evidence is complete. | Implemented advisory; company interpretation and owner approval pending |
| `BROKEN_DIMENSION_ASSOCIATIVITY` | Reports directly established partial or broken association; unsupported/failed extraction remains an operational diagnostic. | Implemented advisory; company interpretation and owner approval pending |
| `DUPLICATE_FEATURE_DEFINITION` | In 1.1, groups non-reference definitions by direct feature, semantic characteristic, and target subgeometry, never nominal value. Different known characteristics on one feature are informational. Missing identity suppresses the conclusion. Schema 1.0 retains feature-only grouping. | Implemented advisory; standard citation, exceptions, and owner approval pending |
| `FLATNESS_REFERENCES_DATUM` | Preserves the tested schema-1.0 parsed-frame behavior. Schema 1.1 emits an operational diagnostic and suppresses the conclusion until verified FCF parse state, confidence, and provenance are added. | Legacy advisory implemented; NX evidence binding, ASME Y14.5-2009 paragraph, and company interpretation pending |

The executable catalog in `RuleCatalog.cs` supplies every finding with standard/edition, paragraph citation, interpretation, applicability, permitted exceptions, severity, engineering owner, approval date/status, evidence requirements, and incomplete-evidence behavior. Unapproved fields remain explicitly `Pending`; the repository does not reproduce standards text or describe these advisories as compliance conclusions.
