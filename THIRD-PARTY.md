# Third-party research inventory

Third-party repositories are cloned in the workspace-level sibling directory `../third_party` (for example, `C:\Projects\third_party` when this repository is `C:\Projects\nx-drawing-auditor`). They are not referenced by or copied into the auditor build. A repository-local `third_party/` path is ignored as a safety guard, but is not the script default.

| Local clone | Resolved commit | License posture | Intended use |
| --- | --- | --- | --- |
| `occt` | `7d2efad9c8a9a57ea96c4c8587134b34dd503cd8` | LGPL-2.1 with OCCT exception | Future STEP/AP242 fallback; legal review before distribution |
| `ezdxf` | `8021fe2dcfb3843d921594c151198cc0a28ddd51` | MIT | Future DXF fallback |
| `nist-sfa` | `cef7878f498314ea4e78e6d5a56aa850595c1344` | Confirm repository terms before reuse | STEP/PMI reference oracle and test ideas |
| `autocad-mcp` | `abc2a82e7128358b9e228a7d9442b37019aa3fe5` | MIT | Rule/test architecture reference only |
| `cadrip` | `6d59a510f7e72d0b5956f7d47dc3b730aaacdef0` | GPL-3.0 | Research only; no code incorporation |
| `engvision` | `f3bbec8e85a7870bf96068a06649d97f3f2d8e7c` | No visible license | Research only; no code incorporation |
| `edocr2.git` | `f6f96517a531021ac946f6fc45063bdb77440085` | MIT | Bare clone for future PDF extraction research |
| `nxopen-lib` | `96855b43415ac81dcfdc83f8874ea5622c19d2db` | Repository wrapper is MIT; mirrored Siemens API material remains reference-only | API discovery only; never copy mirror source/assemblies into the product; compile against company-installed NX assemblies |

`edocr2` is stored as a bare Git clone because its history contains Windows alternate-data-stream filenames that cannot be checked out on NTFS. Files remain inspectable with `git show` or can be checked out selectively on a compatible filesystem.

The commit IDs above were verified against the local checkouts on 2026-08-20. Root license evidence was also inspected: `LICENSE_LGPL_21.txt` plus `OCCT_LGPL_EXCEPTION.txt` for OCCT; `LICENSE` for ezdxf, AutoCAD-MCP, cadRip, eDOCr2, and nxopen-lib; and no root license file for NIST SFA or EngVision. The absence of a license file is not permission to reuse source. Repeat both checks whenever a research checkout is refreshed.
