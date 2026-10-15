# PORTAL-06A2 — prepare and validate the All-Egypt location catalogue

The owner explicitly chose **"All Egypt"**, not selected launch regions.
Prepare a concrete nationwide bilingual Governorate -> City -> Area catalogue
for review. This is catalogue preparation, not another generic source shortlist,
and not importer implementation or permission to write a database.

## Intake / pinned base

Read AGENTS.md, governance, Mastermind_Handoff, PORTAL-06 in `../plan.md`, and
the final mastermind disposition in `../reports/phase-06-source-qualification.md`.
Reuse the accepted 06A findings; do not repeat the completed portal audit or
re-propose levels 1/2 only with Areas left manual.

Last reviewed revision: `df0b35a1c79e336fd9069bb94a9762a45f9d7432`.
The following mastermind docs commit records All-Egypt scope and adds this
brief. The launch message pins the final pushed SHA. Verify clean main equals
origin/main there and record actual intake; do not reset to the older revision.

Work directly on main, one worker at a time. You are not alone: preserve others'
edits, stage explicit owned paths, and stop on overlapping unowned work. No
branch/worktree, hook bypass, API startup, database access/reset/seed, Production
contact, provider sends, Bruno, publish/deploy, package or schema migration.

## Approved goal and boundaries

- Nationwide coverage, not a partial rollout silently called complete. Prepare
  all governorates and their city/area selections, with Arabic and usable
  English names. Define the coverage denominator and snapshot date explicitly;
  merely having a governorate row or one Area per city is not completeness.
- Preserve runtime endpoints, existing IDs, parent links and active-state rules.
  The catalogue uses its own stable source/canonical keys, **not** invented
  production UUID mappings. Existing Smouha/manual rows require later preview
  reconciliation; they must not be deleted/re-parented automatically.
- The city/area mapping must be usable for the app's location picker, not a
  blind relabeling of police sections/census units. Record the proposed mapping
  and urban/rural/new-city exceptions in plain language with real examples.
  Do not change the application's taxonomy or require new frontend fields.
- No fabricated default Area, "Other", copied city-as-Area placeholder, omitted
  collision pair or silently removed governorate just to pass validation.
- All-Egypt approval is **coverage approval only**. The previously inspected
  third-party source remains unapproved; a dataset/snapshot/mapping still needs
  mastermind acceptance before a bounded 06B importer brief. A later target
  preview, backup and explicit owner import order remain separate.

## Work required

1. Find traceable source material supporting the full hierarchy. Prefer official
   locality records; supplement only with a documented compatible source where
   necessary. Inspect actual reuse/attribution terms and upstream provenance,
   not just a GitHub badge. Do not commit copied material without an established
   redistribution basis; if that cannot be established, report the exact missing
   source/permission rather than guessing. No contact/message to a publisher.
2. Assemble the actual candidate as data, including per-record or traceable
   per-batch source references, immutable source revisions/file SHA-256, source
   date, attribution and manual correction/translation notes. Read downloads
   as data only; never execute downloaded SQL/code. Do not commit irrelevant
   source files or raw bulk copies just because they are public.
3. Resolve the catalogue's names and hierarchy for consumer use, recording
   evidence for every correction/alias. Distinguish cities from markaz/districts
   and Areas from census buckets. Include new-city and rural coverage rather
   than checking Cairo/Alexandria only. Distinguish transliterations from official
   English names. Proposed disambiguating labels belong to the candidate only,
   not automatic renames of existing production records.
4. Validate actual files: unique stable keys, valid parents, bilingual/nonblank
   names <=150 characters, same-parent Arabic OR English collisions under the
   application's exact-match rule, repeated source records/aliases, legitimate
   leaf cases versus missing Area coverage, and consistent country/governorate
   membership. Treat missing/ambiguous data as reported failures, not silently
   skipped rows. Do not introduce fuzzy auto-merge logic.
5. Produce coverage by governorate, expected versus delivered city/area counts
   against cited source lists, unresolved units and actual sample lineage.
   Include Alexandria/Smouha, Cairo/New Cairo, Luxor, rural and new-city cases.
   A regex failing to find a familiar spelling is a candidate alias/coverage
   issue, not proof the locality does not exist nationally.
6. Provide a reproducible, offline-capable validation command for the committed
   candidate using built-in PowerShell/.NET facilities (no package). Hash inputs
   and record output counts/failures. It must not connect to any API/database,
   download executable scripts, mutate source data, read credentials, or import.
   Include small synthetic invalid-input checks for missing parent, duplicate
   bilingual name, missing English and uncovered Area branch.
7. Deliver a bounded exact-file 06B proposal for preview/import once this
   catalogue passes review: input/provenance contract, reconciliation states,
   ID/reference preservation, inactive/manual rows, ambiguity handling,
   transaction/concurrency and repeat-run tests. Do not implement that tool now.

## Owned files only

- New `../reference-data/egypt/candidate.json` — review candidate, not auto-loaded.
- New `../reference-data/egypt/provenance.md` and `coverage.md`.
- New `../reference-data/egypt/Validate-Catalogue.ps1` with optional small
  `validation-fixtures.json` for synthetic validation checks. No new package.
- New `../reports/phase-06-national-catalogue.md`.
- `../plan.md`: only PORTAL-06A2 status/evidence. Mastermind owns central
  handoff/index. Prior 06A evidence stays historical; do not erase limitations.

If a complete or redistributable candidate cannot be assembled, do **not**
commit a misleading complete catalogue: deliver the qualified material that can
legitimately be retained, exact gaps by governorate/level, attempted sources,
and one concrete next input needed. Do not simply repeat the rejected partial
import recommendation or silently reduce All-Egypt scope.

## Gates / output / stop

No database fixture, API host or full .NET suite is needed for catalogue work.
Before data-validation scenarios, prepare/read back the candidate snapshot and
synthetic invalid fixtures, pin inputs and verify configuration is offline.
Run the actual catalogue validator plus its invalid-input checks, validate JSON
and documentation links, and `git diff --check`. Record counts, source pins,
hashes and limitations; no completeness claim from samples alone.

Return exact base/final SHA, owned files, actual nationwide coverage matrix,
validation results, unresolved provenance/data gaps and proposed 06B manifest.
Commit/push completed scoped work normally. Stop for mastermind review before
any importer implementation, database preview/application or deployment.
