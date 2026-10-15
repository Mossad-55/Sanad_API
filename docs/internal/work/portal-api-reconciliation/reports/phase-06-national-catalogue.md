# PORTAL-06A2 report — All-Egypt catalogue candidate (review only)

Status: review corrections delivered, pending mastermind review — candidate
still not accepted for nationwide use/import (readiness gate fails closed
as designed). No dataset, snapshot, or mapping approved. No importer
implemented; no database access, production change, deployment, or Bruno
occurred. Do not start 06B or present this candidate as approved or
consumer-verified.

## Intake

- Worker base: pushed `main` `814d9739bbd1686e68389f80524ae9ea4f75fea1`
  (All-Egypt scope + this brief). At intake `main == origin/main` with a
  clean tree; no reset performed.
- Reused accepted 06A findings (source pins, model rules, collision and
  gap identification) without repeating the portal audit or re-proposing
  levels-1/2-only with manual Areas.
- Owned outputs: `../reference-data/egypt/candidate.json`,
  `../reference-data/egypt/provenance.md`,
  `../reference-data/egypt/coverage.md`,
  `../reference-data/egypt/Validate-Catalogue.ps1` +
  `validation-fixtures.json`, this report, and PORTAL-06A2 status in
  `../plan.md`. Central handoff/index belong to the mastermind.

## Delivered candidate (structural coverage nationwide, verification honest)

- `candidate.json` (1,385,398 bytes, SHA-256
  `18AB3CB47FBDC34B1DFD77894ADA3B79DC27D22C3B2925B679291EDE45F79C09`):
  **27 governorates, 347 cities, 5712 areas**, plus 18 per-row exclusions,
  1 reinstatement record, and 5 recorded gaps. Keys are stable source IDs
  (`EG..`); no production UUIDs invented; not auto-loaded by anything.
- Source: `open-admin-data` @ `76b47b2` (2026-09-08); re-fetched input
  hashes match the 06A pins exactly (no upstream drift). CC-BY-4.0
  attribution recorded; upstream chain still unproven (limitation kept).
- Cities: 170 urban-district, 164 rural-markaz, 9 new-city, 3
  other-admin-unit; 18 rural rows carry candidate-only `" Markaz"`
  English disambiguation (flagged per row; production untouched).
  Excluded: 14 residual Zemam buckets (incl. three `صحراء…` variants
  caught by English label) and 5 police-department units, with the 10
  shiyakhas beneath them.
- Areas: all 5706 rows carry `verification: "provisional-census"` — real
  source records, explicitly not consumer-verified service zones. Zero
  included cities lack areas; zero duplicate keys; zero orphan parents.
- Known gaps reported, not hidden: Smouha unmappable (seed stays manual),
  New Administrative Capital absent (other new cities present),
  transliteration-quality English, no postal codes, unproven upstream chain.

## Validation (offline, built-in facilities only)

- `Validate-Catalogue.ps1` (default paths): candidate SHA-256 re-hashed,
  counts 27/347/5712/5712-provisional/18-disambiguated, **0 structural
  failures**; 12/12 synthetic fixture checks pass (the original 5 plus
  empty country, wrong types, Arabic-only English, Latin-only Arabic,
  duplicate governorate name, incomplete national set, and known-wrong
  boundary — each rejected for its own reason; valid mini-branch
  accepted). Structural exit 0. No network/API/database/credential/
  mutation involved.
- Structural rules now enforced: exact object/string/boolean types (no
  coercion), nonempty collections, approved 27-governorate set with
  per-governorate city/area coverage manifest, known-parent memberships
  (Armant/Esna → Luxor), snapshot source/revision presence, Arabic-script
  presence in Arabic names and Latin-script presence in English names,
  governorate-level name collisions, and ordinal case-sensitive
  same-parent collision checks (PostgreSQL `==` semantics, not PowerShell
  default case-insensitivity).
- One discarded run: the first hardened-validator invocation read the
  UTF-8 candidate as system ANSI (Windows PowerShell 5.1 `Get-Content`
  default), mojibake-ing Arabic and false-failing ~600 rows. The script
  now forces `-Encoding utf8` on every read; that run's output is not
  evidence. This is precisely why the validator re-hashes its inputs.
- Every city owns ≥1 area; collisions, blank/over-150 names, missing
  verification flags, and national-set deviations all fail closed.

## Readiness gate (separate, fail-closed)

- `Validate-Catalogue.ps1 -Readiness`: structural PASS plus readiness
  FAIL with exit 2 — 4 unresolved branches, 5712 provisional areas, 3
  unaccepted manifest flags, 5 open coverage gaps. Structural success is
  reported as shape validity only, never as release readiness; the 5706→
  5712 provisional rows did not become verified by any flag change.

## 06B proposal (bounded, exact-file — not implemented)

Input/provenance contract (`candidate.json` + `provenance.md` pins/hashes);
preview-first reconciliation states (insert / exact-match keep /
fail-closed) with per-row audit; full preservation of existing IDs,
links, inactive flags, and manual rows (incl. Smouha); no auto renames,
re-parenting, reactivation, or deletion; transactional idempotent reruns
with counts; isolated initial/repeat/conflict/rollback/active-chain tests
on a disposable target without inherited reset fixtures. Exact tool files
and acceptance to be pinned after catalogue approval; target preview,
backup, and owner import order remain separate authorities.

## Review corrections (R1–R5 responses)

- R1 boundaries: `مركز أرمنت` (EG2704, 8 areas) and `مركز إسنا` (EG2705,
  28 areas) re-parented Qena → Luxor with `reparented` citation records
  (GOPP Luxor plan: 6 markaz incl. Armant/Esna, plus New Luxor/New Tiba
  cities and 1 urban section; fetched 2026-10-15; transfer corroborated
  by 2009 Luxor-governorate creation records). Qena's remaining branch
  checked name-by-name for other Luxor-claimed units: none. Cross-checks:
  Helwan (Cairo) and 6th-October (Giza) presence is consistent with the
  post-2011 abolition; EG-code prefixes are a pre-2009 coding fossil, so
  source vintage is unknown-mixed, not 2026-certified. Luxor now 5 cities
  / 59 areas; Qena 11 / 167. Missing Luxor units (Bayadiya, Qurna,
  Ziniya-as-markaz, Tod, New Luxor) and the other 25 governorates'
  official audits are marked unresolved — the readiness gate fails on them.
- R2 exclusions: all 14 Zemam buckets verified at 0 children each
  (exclusion now evidenced, not label-based); all 4 port units hold
  exactly one customs-facility child each (exclusion evidenced);
  Tiba reinstated with its 6 genuine localities as
  `police-section-aggregate`, flagged `boundaryUnresolved` (Ziniya/New
  Tiba equivalence unverified), return recorded in `reinstatements`.
- R3 validator: rewritten with the rules listed above; 7 new fixtures,
  all retained old cases passing.
- R4 readiness: new `-Readiness` mode plus `boundary-checks.json`
  (approved set, coverage manifest, known parents, audit status,
  unresolved branches, acceptance flags). Structural PASS + readiness
  FAIL (exit 2) is the delivered honest state.
- R5 accuracy: Marina El Alamein corrected to Matrouh; gaps count 5
  (derived from `candidate.gaps`); district-`source`-emptiness claim
  retracted (all 365 empty); bulk wording fixed (1.39 MB derived
  catalogue committed, raw upstream files not); no new legal conclusions;
  target/backup separated below.

## Next input needed

Owner/mastermind approval of: the pinned snapshot, the 18 disambiguated
labels, the 18 exclusions plus Tiba reinstatement, the Armant/Esna
re-parenting, Smouha placement, and the provisional-Area policy.
Catalogue approval is coverage approval only — not source endorsement
beyond the stated basis, and not permission to write any database. The
exact later import target/backup is a separate future write-authorization
gate, not part of catalogue review.

## Mastermind review — acceptance held

Reviewed clean pushed delivery `591881375ddcce088beb85a2fb70acc37722341b`.
Candidate hash/counts match; reran Validate-Catalogue.ps1 offline: exit 0,
0 reported candidate failures and 5/5 synthetic checks. These prove the
delivered validator's current structural checks, not national release readiness.
No API/database/Production action or data import was performed.

Concrete findings:

1. **Wrong governorate boundaries:** `EG2704` Armant and `EG2705` Isna are
   assigned to Qena (`EG27`), carrying 8 + 28 child Areas. The official
   [GOPP Luxor plan](https://gopp.gov.eg/luxor-strategic-plan/) lists Armant
   and Esna in Luxor's administrative centres. This is a substantive error,
   not just a missing modern spelling. Source-count agreement is insufficient
   evidence of current boundaries; the 2026 publication date is not data vintage.
2. **Unjustified exclusions:** Tiba police-station record `EG2903` and six
   Areas were dropped from their label. The official plan also lists New Tiba;
   the exact source-record correspondence needs evidence, not an assumption
   that police-labelled administrative geography has no resident localities.
3. **Validator blind spots reproduced:** actual Test-Candidate function
   definitions accept an empty three-array country and a complete mini-chain
   with Arabic-only englishName values, each with zero failures. Required
   national set/types/language and release-readiness checks are missing.
4. **Documentation drift:** Marina El Alamein is in the JSON under Matrouh,
   not Red Sea as coverage.md says; actual candidate.gaps count is 5, not 6.
   The 1.38 MB derived dataset is committed, though the raw source files are
   not. The publisher's source objects do not establish upstream provenance.

The All-Egypt requirement remains unchanged. No provisional flags, old source
denominators or excluded branches waive it. Run the
[bounded review correction brief](../worker-prompts/06a2-review-corrections.md)
before acceptance or any 06B importer work. Do not ask the owner to approve
known-wrong geography or bundle future Production target/backup authority
with this offline correction task. Dataset/provenance acceptance remains open.
