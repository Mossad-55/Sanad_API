# V1 location label cleanup — local, awaiting final approval

Owner approved the [exact proposal](phase-06-label-cleanup-proposal.md) on
2026-10-15. Base remains `f932c46d826f758837398242ea370b6521c75e0e` plus the
existing local proposal/handoff. No commit/push/import/publish/deploy performed.

## Applied scope and preserved data

- Exactly 16 Area display rows changed (16 Arabic labels, 8 English labels).
- All original Arabic/English values and parents preserved in
  [label-corrections.json](../reference-data/egypt/label-corrections.json).
- No keys, parents, source IDs, verification flags, row order, snapshot metadata,
  City/Governorate records or unrelated fields changed. Counts remain 27/347/5712.
- Original incomplete former-number fragment for EG190806 is preserved in
  provenance; no number invented. Compound records remain single records.
- Names inventory updated. City-only inventory unchanged because no City changed.
- Current candidate: 1,385,428 bytes, SHA-256
  `E048767A00F5DE28B32E8F4E1E2B2BCBC4FA52C19BEA0C0D14D085DDB5AB8A8E`.

`Test-LabelCleanup.ps1` compares the entire candidate to the pinned Git baseline
plus ONLY the approved pairs, also checking the approval table and original
values. Executed successfully, exit 0; all 6070 other records and every non-name
property are unchanged. This is a semantic whole-document proof, not a count-only
check. `Compare-PublisherNames.ps1` accepts only exact source-before/candidate-after
pairs for the 16 corrections and still verifies all remaining original labels
plus the existing 18 literal Markaz suffixes.

## Validation

- Whole-document approved-change proof: PASS, exit 0.
- Structural validation: PASS, 12/12 synthetic fixtures, no candidate failures.
- Full text/normalized-collision scan: PASS, all 6086 records; 0 failures,
  0 normalized same-parent collisions, 0 question-mark rows, 0 underscore rows.
- Pinned bilingual publisher comparison: PASS, exit 0. All 16 approved pairs
  checked against original source names; 5696 unmodified Area labels still
  source-exact in both languages. Governorates/Cities unchanged (18 suffixes).
  Initial sandbox request was blocked; the authorized read-only network retry
  succeeded. No downloaded code ran or credentials/provider services were used.

Isolated lookup compatibility rerun: **90 passed, 0 failed, 0 skipped**, exit 0,
8 seconds. The first sandboxed attempt aborted after a 90-second testhost IPC
connection timeout and produced no test result; it is not counted as evidence.
An authorized local-process retry using the same exact child isolation/filter
succeeded. Both attempts removed inherited integration targets, used dummy
module connections and disabled startup migrations/seeders; the selected tests
only prepare/read unique InMemory contexts. No real database/reset/provider ran.

Existing compiled implementation/tests are unchanged, so the
valid strict zero-warning/error build is reusable; do not build the solution
merely because JSON/script/document inputs changed. Existing tests read the
edited JSON at runtime. No full .NET suite or PostgreSQL run is claimed.

## Boundaries

This is display cleanup, not geographic or linguistic certification. The
stronger complete/current national-readiness flags remain false and provisional
Areas remain provisional. Owner accepted the collected coverage for V1; no
new national-source investigation is required to repeat that scope choice.
Importer work still needs its own brief, isolated repeat/rollback/conflict
tests and preview preserving existing/manual IDs. Publishing will not load this
JSON into a database. Exact target/backup/write authority remains separate.

Stop after presenting final local validation. Wait for explicit approval to
commit/push; publishing and deployment also require their own owner order.
