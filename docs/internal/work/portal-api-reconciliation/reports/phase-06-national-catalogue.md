# PORTAL-06A2 report — All-Egypt catalogue candidate (review only)

Status: candidate delivered for mastermind review; no dataset, snapshot, or
mapping approved. No importer implemented; no database access, production
change, deployment, or Bruno occurred. Do not start 06B or present this
candidate as approved or consumer-verified.

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

- `candidate.json` (1,382,382 bytes, SHA-256
  `2ECB48086A1686DD938E89EF3B99497CBF063533FA3CE420C6BE5862DD5E1BCF`):
  **27 governorates, 346 cities, 5706 areas**, plus 19 per-row exclusions
  and 6 recorded gaps. Keys are stable source IDs (`EG..`); no production
  UUIDs invented; not auto-loaded by anything.
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
  counts 27/346/5706/5706-provisional/18-disambiguated, **0 candidate
  failures**; 5/5 synthetic fixture checks pass (missing parent,
  duplicate bilingual name, missing English, uncovered branch rejected;
  valid mini-branch accepted). Exit 0. No network/API/database/
  credential/mutation involved.
- Every city owns ≥1 area; same-parent Arabic-OR-English collisions,
  blank/over-150 names, and missing verification flags all fail closed.

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

## Next input needed

Owner/mastermind approval of: the pinned snapshot, the 18 disambiguated
labels, the 19 exclusions, Smouha placement, provisional-Area policy, and
the exact later target/backup. Catalogue approval is coverage approval
only — not source endorsement beyond the stated basis, and not permission
to write any database.
