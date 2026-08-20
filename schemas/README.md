# Audit document schemas

`audit-document-1.1.schema.json` is a dependency-free JSON Schema 2020-12 contract for neutral NX audit input. It uses only local `#/$defs/...` references.

Schema 1.1 requires extraction metadata and value evidence for every dimension and hole callout. Complete evidence is further constrained by semantic value kind; incomplete, unsupported, and failed extraction remain valid so the auditor can report uncertainty without inventing engineering facts. The legacy `associationStatus` value `unsupported` is intentionally invalid in 1.1; use `extractionState: "unsupported"` with an association state of `associated`, `partiallyAssociated`, `broken`, or `unknown`.

Port-family, mapping-revision, and candidate-designation fields remain structurally optional. Missing mapping context or multiple candidates must load successfully and become operational unsupported/ambiguous diagnostics in the deterministic comparer.

`AuditDocumentLoader` and `AuditDocumentValidator` remain authoritative for invariants JSON Schema cannot fully express, including identifier uniqueness and evidence-versus-legacy conflict handling. Additive 1.1 fields are init-only Core properties, while the existing positional 1.0 record constructors remain unchanged.
