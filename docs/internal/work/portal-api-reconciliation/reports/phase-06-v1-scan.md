# PORTAL-06 — full V1 candidate scan

## Outcome and owner decision

Intake: clean main/origin `a65d648`. Owner is satisfied with the collected
27-governorate / 347-City / 5712-Area coverage for V1, subject to this scan.
This supersedes exhaustive current-national completeness as the immediate V1
scope gate; it does not certify inaccurate names or authorize a database write.
The historic strict `-Readiness` gate retains its stronger meaning and remains
unsatisfied. No flags or provisional records were relabelled as verified.

**Technical compatibility passes; display review has four definite `?` findings
and twelve underscore-delimiter flags.** Candidate bytes/names/IDs were not
changed by this review. There is no importer or deployed dataset yet.

## Full record scan (not a sample)

Candidate SHA-256:
`18AB3CB47FBDC34B1DFD77894ADA3B79DC27D22C3B2925B679291EDE45F79C09`.

| Check | Result |
| --- | --- |
| Records scanned | 6086 = 27 + 347 + 5712 |
| Nonblank bilingual values / types / required parent keys | Pass |
| Missing parents, duplicate keys, exact same-parent Arabic OR English name collisions | 0 |
| Uncovered included City branches | 0 |
| Normalized same-parent name collisions | 0 |
| Leading/trailing whitespace, control/bidi/replacement characters, placeholder-only labels | 0 |
| Maximum name lengths | Arabic 50; English 67; both below the actual 150-character limit |
| Question-mark source labels | 4 Areas, listed below |
| Underscore delimiter review | 12 additional Areas; not automatically wrong names |
| Offline structural fixtures | 12/12 pass, exit 0 |

Normalized comparison uses FormKC, invariant lowercase and removal of
whitespace/punctuation/combining marks/tatweel within the same parent only.
No aggressive Arabic letter folding, fuzzy identity matching, merges or edits.
Repeated names across different parents are legitimate, not duplicates.

Reproduce: `Validate-Catalogue.ps1` and `Scan-CatalogueNames.ps1` in
`reference-data/egypt/`. Both executed successfully; the latter emits all 16
display-review rows. Exit 0 is technical execution/compatibility, not linguistic
certification. No API/database fixtures are needed for these offline scans.

## Exact name correspondence and public-source search

`Compare-PublisherNames.ps1 -AllowNetwork` independently fetched all three
hash-pinned bilingual source files at revision
`76b47b22872288c0d8b0c757975980e6986945e8`; read-only, in memory, exit 0:

| Level | Exact Arabic matches | Exact English matches | Documented English changes |
| --- | ---: | ---: | ---: |
| Governorates | 27/27 | 27/27 | 0 |
| Cities | 347/347 | 329/347 | 18 rural ` Markaz` suffixes, checked literally |
| Areas | 5712/5712 | 5712/5712 | 0 |

No missing source keys or undocumented name changes. This proves faithful
copying, not that an erroneous source string is correct. Direct historical
CAPMAS-attributed HDX Arabic correspondence was already independently executed;
that unchanged-input evidence is retained in `hdx-reference-evidence.json`.

Additional search found actual spelling variants in primary CAPMAS sources:
[2017 census governorate labels](https://metadata.capmas.gov.eg/en/index.php/catalog/621/variable/V2),
[2016 marriage/divorce governorate labels](https://metadata.capmas.gov.eg/en/index.php/catalog/279/variable/F5/V243?name=GOV_CODE),
and [2021 income survey labels](https://metadata.capmas.gov.eg/en/index.php/catalog/747/variable/F34/V5875?name=Gov).
These support treating Menia/Minia, Behera/Beheira and Suhag/Sohag as spelling
variants, not inventing a wrong-location defect from transliteration alone.
No population inference is made from category case counts.

An additional independent Arabic lexical check used the public
[CAPMAS 2022 disability-survey SECTION_CODE codebook](https://metadata.capmas.gov.eg/ar/index.php/catalog/1922/variable/F5/V6683?name=SECTION_CODE).
Parsed 6400 six-digit category codes/names from HTML table cells; compared Area
names after the conservative normalization above (without lowercasing Arabic):
3774/5712 have a name match, of which 3169 have exactly one reference name hit;
1938 have no normalized full-label match. This is supplemental evidence only:
no code/parent/identity equivalence is inferred from a lexical hit, and a non-hit
is not evidence of a fake place (spelling, qualifiers and vintage differ).
The newer codes are not replacements for the existing source keys.

The codebook supports the core names الإسماعيلية شرق, بوابة القنطرةشرق الجمرك,
and ابوتندب, but not the lost `?` characters, exact legacy identity or every
qualifier. CAPMAS is a name reference, not an independently complete consumer
location picker. Neither this source nor HDX certifies all added English names.

## Four definite display-review findings

| Stable source key | Current Arabic label | Recommended bounded review |
| --- | --- | --- |
| EG190806 | الاسماعيليةشرق?رقم?سابقا? | Core name الإسماعيلية شرق is corroborated; do not guess the missing former number. Prefer a sourced clean display label, retain raw provenance. |
| EG190809 | السلامالجديدة?تشملالبحيراتالمرة? | Review spacing/parenthetical qualifier; do not invent or certify the locality's geographic extent. |
| EG190810 | بوابةالقنطرة?شرقالجمرك? | Core wording is corroborated; clean punctuation/spacing with raw source retained. |
| EG310205 | منجمأبوتندب?قبليوبحري? | Core name is corroborated; preserve raw qualifier until an evidenced display-only cleanup is approved. |

These are existing source defects, not introduced during export. The 12
underscore flags mostly separate compound names, aliases or scope descriptions;
they may be reformatted for readability, but must not be blindly split into
multiple locations. Arabic source labels also sometimes omit word spacing.
The full machine flag list is reproducible from the offline name scan.

## Real application compatibility, isolated fixtures

New `EgyptCatalogueCompatibilityTests` has one scenario for every governorate,
27 total. Each reads the actual candidate from the repository, verifies snapshot
counts, creates an explicitly empty unique InMemory Caregivers context, and:

1. Runs real command validators and the real create handlers for every included
   Governorate, City and Area in parent-first order. No direct bulk insertion
   bypasses handler duplicate, active-parent or domain rules.
2. Reads back the active governorate before City calls and each persisted City
   before Area calls. No HTTP host, startup seeding or providers are involved.
3. Reads every name through real public/admin query handlers, verifies exact
   bilingual labels, counts and actual generated parent IDs. Fixture IDs exist
   only in isolated memory; source keys are never used as production UUIDs.
4. Verifies duplicate-governorate rejection, ancestor deactivation hiding Areas,
   row preservation and reactivation visibility in every branch.

The selected suite also includes existing Governorate/City/Area domain and
handler regressions. EF InMemory proves application compatibility only; it is
not PostgreSQL constraint/concurrency/translation proof, HTTP testing, import
idempotency or deployed-data reconciliation. No production settings were read.

Strict solution build: exit 0, zero warnings/errors. Focused suite: 90 passed,
0 failed, 0 skipped (27 new branch scenarios + 63 existing). Final assertion
revision rebuilt cleanly (35.49 s); final focused rerun passed 90/90 (10 s).
An initial shell environment-blanking preflight rejected its own configuration
and stopped before any test process was started. The executed suite instead used
an explicit ProcessStartInfo child: every inherited IntegrationDatabase key
removed, nine module connections dummy, migrations/seeders off, exact class
filter. No reset-based fixture, PostgreSQL connection or external send ran.

## V1 recommendation / next action

The collected data is a technically viable V1 baseline with the disclosed
historical/translation limitations and admin maintenance. Do not label it an
exhaustive current gazetteer or advertise absent Smouha/NAC picker coverage.
Do not turn unknown current boundaries into verified ones to satisfy a flag.
Before importer implementation, approve a small source-preserving display-label
cleanup for the four `?` rows (and optionally the 12 delimiter rows), or explicitly
hold those ambiguous rows from initial activation. No such mutation or exclusion
was silently applied by this scan. Existing location IDs and manual rows must be
preserved during later preview/reconciliation; admin rename cannot re-parent.

Scan is complete with concrete findings, not another national research gate.
Importer design/test and exact live target/backup/write authority remain separate.
No production app code, candidate data, migration, CORS, provider, database,
Bruno, publish or deployment change was made.
