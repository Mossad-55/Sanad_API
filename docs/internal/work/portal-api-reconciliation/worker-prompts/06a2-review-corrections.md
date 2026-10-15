# PORTAL-06A2 — catalogue correctness / validator review corrections

Return to the same catalogue worker. The owner already chose **All Egypt**;
do not ask them to choose that scope again or seek approval of known-wrong
geography. The candidate is not accepted and 06B must not start.

## Intake / authority

Reviewed delivery: `591881375ddcce088beb85a2fb70acc37722341b`.
The following mastermind documentation commit records findings and adds this
brief. Use the launch message's exact final pushed SHA, verify clean main equals
origin/main there, and record intake. Read governance, Mastermind_Handoff,
the original 06A2 brief and the report's mastermind review appendix.

Work directly on main. You are not alone: preserve others' edits and stage
explicit owned paths. No branches/worktrees, hook bypass, API startup, database
access/reset/seed/import, Production contact, provider send, Bruno or deployment.
No importer, production endpoint/domain change, new package or migration.

Own only the existing `reference-data/egypt/` candidate/provenance/coverage/
validator/synthetic fixtures, optionally one small `boundary-checks.json`
and offline validation-check script there, the existing national-catalogue
report, and PORTAL-06 status/evidence in plan.md. Central handoff/index are
mastermind-owned. No broad new source-research phase or unrelated app audit.

## R1 — known wrong governorate assignments (blocking)

Candidate rows:

- `EG2704`, `مركز أرمنت` / Armant, governorateKey `EG27` (Qena), 8 child Areas.
- `EG2705`, `مركز إسنا` / Isna, governorateKey `EG27` (Qena), 28 child Areas.

The official GOPP Luxor plan explicitly lists Armant and Esna among Luxor's
centres, alongside Bayadiya, Qurna, Ziniya and Tod, and lists New Luxor/New Tiba.
Primary evidence: https://gopp.gov.eg/luxor-strategic-plan/ . The candidate's
old source code prefix is not proof of current parent membership.

1. Reconcile these current boundary assignments using official evidence. Change
   candidate-only parents where evidenced; preserve sourceId/history and record
   the old/new mapping with citation/date/reason. No Production ID re-parenting.
2. Check the rest of the Luxor branch and affected Qena neighbours/descendants,
   then run a current-boundary audit across the other governorates against a
   traceable independent reference. Do not patch only two names and declare the
   entire old source current. Explain source publication date versus actual
   underlying data vintage. Unknown vintage is not 2026 geography certification.
3. Add offline regression constraints for known parent memberships and their
   descendant counts/lineage. Do not infer a canonical parent from `EG27...`.
   Update coverage totals/hashes and explicitly separate reference-validated
   coverage from mere agreement with the same source that contained the error.
4. Where a complete current reference cannot be established, mark exact affected
   rows/branches unresolved and make the release-readiness gate fail. The
   fallback is a precise remaining-data handoff, not fabricated national data.

## R2 — exclusions are not justified by names alone (blocking review)

`EG2903`, مركز شرطة طيبة / Tiba police station and its six source Areas were
excluded as non-consumer merely from a label. Official references establish a
New Tiba locality in Luxor, but do not by themselves establish this source
record's exact equivalence or boundaries. Investigate; do not silently restore
or permanently drop it based on an English substring.

Review all 19 exclusions against the underlying localities/children and record
evidence for inclusion, exclusion or unresolved status. The fact that an entity
is a police administrative unit does not establish that no one lives in its
geographic scope. Postal codes are optional here, not grounds to invent Areas.
Smouha/NAC and the remaining current-locality gaps still require sourced
resolution or explicit release blockers; All-Egypt scope is unchanged.

## R3 — validator accepts invalid national/bilingual data (blocking)

Mastermind called the actual Test-Candidate function definitions offline with:

- `governorates: [], cities: [], areas: []` -> zero failures.
- One complete mini-chain with Arabic-only strings in **all** englishName
  fields and provisional-census flags -> zero failures.

The five existing checks pass, but these required failure cases are missing.
Fix using built-in PowerShell/.NET only:

- Validate root/array/object/property/value types and nonempty collections;
  distinguish a structurally valid miniature fixture from an actual national
  candidate. Main national checks must enforce the approved governorate set
  and coverage manifest, not accept one random governorate or an empty country.
- Nonblank is not sufficient for English/Arabic language presence. Reject
  untranslated Arabic-only English values and Latin-only Arabic values; allow
  legitimate bilingual punctuation/digits/diacritics. Do not invent translations
  to make a language-presence check pass; linguistic quality is a separate review.
- Validate governorate-name collisions as well as same-parent City/Area Arabic
  OR English collisions, keys, parent references and required metadata. Match
  ordinal/exact application semantics deliberately; PowerShell @{} is normally
  case-insensitive, so it is not an exact PostgreSQL == comparer by default.
- Use exact type checks, not permissive string coercion of arrays/objects/bools.
- Add small isolated fixtures proving each new rule fails for its own reason,
  including empty/missing collections, wrong types, incomplete national set,
  Arabic-only English, duplicate governorate name and known wrong boundary.
  Retain existing positive/negative cases; do not disable rules just to pass
  the real candidate.

## R4 — separate structural validation from release readiness

Make outputs unambiguous. Structural success means shape/relationships are
valid, **not** that a nationwide catalogue is ready to import.

Add an explicit readiness mode or equivalent separate offline gate. It must
fail closed when required national coverage/current-boundary checks, provenance
acceptance, mapping/exclusion decisions or consumer-locality verification remain
unresolved. Unknown/provisional verification status is not accepted evidence.
The current 5,706 provisional Areas must not become "verified" via a blanket
flag change. Record what evidence supports batch versus individual validation.

Report both results and actual exit codes. A structural PASS plus readiness
FAIL is an honest deliverable, not permission to import; do not rename it to
"production-ready" or ask the owner to approve existing demonstrated errors.

## R5 — documentation accuracy / provenance

- coverage.md says Marina El Alamein-side units are under Red Sea. The actual
  JSON has `EG330701` under `EG3307` (Matrouh). Fix the report, not a correct
  candidate row to fit the prose.
- Actual `candidate.gaps` has **5** objects, not 6 as the delivery/report claims.
  Make reported counts derived from actual structured data and hash the final
  bytes after edits.
- Source fields were independently found empty in all 365 district records;
  retract the repeated claim that they cite the publisher site/API.
- "No bulk dataset committed" is inaccurate: a 1.38 MB derived catalogue was
  committed; only raw upstream bulk files were not. Describe that distinction.
- Record the publisher's stated CC notice/attribution and unresolved upstream
  chain honestly. Do not give a new legal conclusion that factual names remove
  all compilation/redistribution concerns. Keep the candidate review-only and
  do not copy additional unqualified source dumps.
- Exact later import target/backup is not a prerequisite to this offline work
  or catalogue review; it is a separate future write-authorization gate.

## Gates / output / stop

Prepare/read back actual candidate and synthetic fixtures before invoking
offline validation; no API/database fixtures are needed. Capture before/after
regressions, structural and readiness outputs, final file hash/counts, verified
boundary/exclusion evidence, unresolved branches and exact source limitations.
Validate JSON/links and diff-check. No solution build/full .NET suite needed.

Commit/push scoped corrections normally and return exact base/final SHA/report.
If source gaps prevent readiness, state them precisely; do not silently reduce
scope or repeat a broad shortlist. Stop for mastermind review. No 06B importer
implementation or import/deployment order is authorized by this correction.
