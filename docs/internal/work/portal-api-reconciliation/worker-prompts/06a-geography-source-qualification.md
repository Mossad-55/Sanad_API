# PORTAL-06A — geography source qualification and mapping proposal ONLY

Role: bounded read-only repository/source scout and planner. The owner said
"let's go" after core PORTAL-07 acceptance, authorizing Phase 06 preparation.
No national dataset, hierarchy reinterpretation, importer or target write has
yet been approved. Produce the evidence needed for that decision; stop there.

## Intake / pinned baseline

Read AGENTS.md, governance, Mastermind_Handoff and PORTAL-06 in `../plan.md`.
Read `../reports/phase-06-source-intake.md` for the already-inspected facts and
candidate shortlist. Do not restart the completed portal audit.

Last accepted core revision: `08904196947bcaff60dec96705227e7f5a0b1eed`.
The following mastermind documentation commit adds this brief/intake and sets
Phase 06 to source qualification. The launch message pins that final pushed
SHA: verify clean main equals origin/main and record actual base.

Work directly on main, one worker at a time. You are not alone: preserve others'
edits, stage explicit owned paths and stop on overlapping work. No branches/
worktrees, hook bypass, production access, API scenario requests, external
messages, credential reads, data writes, migration/reset, provider sends,
publish/deploy or Bruno. Commit/push only completed owned docs normally.

## Decision to enable (not make on the owner's behalf)

Find a defensible, pinned, bilingual Egyptian location source and propose how
its levels map to the existing immutable-parent Governorate/City/Area model.
The approved eventual importer must be previewable/idempotent, preserve IDs
and references, and reject ambiguous mappings. Do not implement it yet.

The current guide calls Cities "markaz", but actual city fixtures include
Smouha. Administrative districts/markaz and consumer-facing cities or
neighborhoods are not automatically interchangeable. Trace enough consumers
(caregiver areas/onboarding/search/pricing where relevant) to explain mapping
impact, without widening this into a new product audit. Care Home profile
locations currently use strings; do not invent a required lookup-ID migration.

## Required evidence

1. Inspect exact current entities, name normalization/uniqueness checks,
   parent immutability/active-chain behavior, FK/reference use and seeder
   examples. State which parent/name collisions prevent blind replacement.
   No live inventory: current production IDs/links require a later authorized
   read-only preview, not a guess from development seeds.
2. Evaluate at most three serious source candidates from the intake shortlist
   or a demonstrably better official source. Prefer official/traceable source
   records. A GitHub dataset is not government-authoritative just because it
   says comprehensive, recently updated, or CC-licensed. Inspect upstream
   provenance, actual data vintage, license/attribution and redistribution
   conditions (including upstream material), language coverage and update
   mechanism. Mark unresolved rights/provenance explicitly; do not invent them.
3. Inspect actual data files, not README counts alone. Record source URL,
   immutable revision/version and file SHA-256 where obtainable; actual per-
   level/per-parent counts, missing parents/names, duplicate source IDs,
   bilingual collisions, invalid lengths, empty-child branches and outliers.
   Read/parse files as data only; never execute downloaded SQL/code/scripts.
   Do not commit copied datasets without established redistribution basis.
4. Check representative Alexandria/Cairo urban districts, a rural governorate,
   Luxor and new-city coverage against traceable official records. Explain
   administrative versus postal/consumer locality differences. Sample checks
   are not proof of nationwide completeness/currentness. English names may be
   transliterations; do not call them official translations without evidence.
5. Propose a clear level mapping with concrete examples for urban/rural/new-city
   cases and explain what the frontend picker labels would mean. Offer a
   primary recommendation plus an alternative if the source does not fit
   City/Area semantics. Do not insert synthetic placeholder areas just to make
   every picker branch nonempty; distinguish a legitimate leaf from a data gap.
6. Draft the bounded import design/handoff for subsequent approval:
   - input snapshot/provenance/attribution manifest and validation report;
   - read-only preview with exact target/environment/backup preconditions;
   - explicit source-to-existing-ID mappings/aliases where justified;
   - exact-match reconciliation under the correct parent, no fuzzy auto-merge;
   - fail-closed ambiguous Arabic/English/duplicate/parent collisions;
   - preserve existing IDs, links, inactive flags and manual additions;
   - no automatic renames, re-parenting, reactivation or deletion;
   - transaction/concurrency/repeat-run guarantees, counts and audit artifact;
   - isolated seeded initial/repeat/conflict/rollback/active-chain tests before
     API requests, with no inherited PostgreSQL reset fixture;
   - exact owner decision needed for dataset, mapping/coverage and later target
     import. Dataset approval is not permission to write Production.
7. Produce a proposed exact implementation manifest for 06B based on source
   inspection. Prefer an explicit operator import/preview tool, not startup
   seeding, a public endpoint or thousands of rows hidden in a schema migration.
   Mention any essential new persistent source-ID mapping separately; do not
   silently approve a migration or change existing APIs/enum/ID formats.

## Owned outputs / gates

Only these paths may be created/updated:

- New `../reports/phase-06-source-qualification.md` with source comparison,
  actual-vs-claimed coverage, concrete hierarchy examples, proposed design/
  implementation manifest and **one concise owner decision request**.
- Optional new `../diagnostics/geography-source-qualification.md` for detailed
  reproducible data-only checks if the report would otherwise become unwieldy.
- `../plan.md`: only PORTAL-06A status/evidence, explicitly pending dataset/
  mapping approval. Central handoff/index belong to the mastermind.

No production/test code, downloaded full dataset, package, appsettings, migration,
Postman or current lookup mutation. Temporary data inspection must use a fresh
task-specific temp directory; do not clean broad/shared directories. Report
temporary artifacts honestly; do not expose private target data or credentials.

This is a documentation/data-source assessment, so no API startup, solution
build/full test suite or database fixture is needed. Validate report links and
source-derived facts, record the parsing/check commands and limitations, and
run `git diff --check`. No statistical/completeness claim from unrun checks.

Return exact base/final SHA, files, inspected source pins/counts, provenance/
license uncertainties, recommended mapping and bounded 06B plan. Do not start
06B, import even local production-like data, or present the source as approved.
