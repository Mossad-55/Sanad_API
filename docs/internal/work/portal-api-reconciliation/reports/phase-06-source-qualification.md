# PORTAL-06A report — geography source qualification and mapping proposal

Status: qualification delivered, pending owner dataset/mapping approval and
mastermind review. No importer implemented; no IDs, references, or data
changed; no API startup, database contact, deployment, or Bruno occurred.
Do not start 06B or present any source as approved.

## Intake

- Worker base: pushed `main` `73cdbdb9c02ee9efd81431d4c334bc3a86a40154`
  (06A brief/intake). At intake `main == origin/main` with a clean tree;
  no reset performed.
- Owned outputs: this report, `../diagnostics/geography-source-qualification.md`
  (reproducible data-only checks), and PORTAL-06A status in `../plan.md`.
  Central handoff/index belong to the mastermind and were not edited.
- Prior facts reused, not redone: `phase-06-source-intake.md` candidate
  shortlist and model ambiguity (markaz guide vs Smouha seed).

## 1. Current model — what blocks blind replacement

- Entities (`Caregivers.Domain/.../Lookups/{Governorate,City,Area}.cs`):
  trim-only name normalization (no case/diacritic folding), 150-char max,
  parent IDs assignable only at creation — **no re-parent endpoint**.
  `UpdateNames` and active flags are the only mutations.
- Handlers (`Caregivers.Application/Lookups/{City,Area,Governorate}Lookups.cs`,
  `LookupsErrors.cs`): parent must exist **and** be active (Area additionally
  requires an active governorate above its city); duplicate = exact `==`
  match on Arabic **OR** English under the same parent → 409
  `NameAlreadyInUse`; public reads require the full active chain;
  deactivation hides children from public reads without touching them.
- Public/Admin list contracts (`docs/admin/lookups-cities-areas.md`,
  `docs/app/public/lookups.md`): parent id required; unknown parent → 200
  `[]`; wrapper `{ value }` IDs.
- Seeder (`TestUserDataSeeder.cs:332-351`): Alexandria → City `سموحة`/
  `Smouha` → Area `سموحة`/`Smouha Area`. A consumer fixture, not a national
  taxonomy; existing IDs/references must be preserved verbatim.
- Consumers (mapping impact): caregiver service-area selection validates
  Area IDs with the full active chain (`Onboarding/CaregiverSelections.cs`);
  discovery filters by Governorate/City/Area IDs (`Discovery/CaregiverDiscovery.cs`);
  pricing insights aggregate by Area ID (`Discovery/GetPricingInsightsQuery.cs`).
  Care Home profiles use location **strings** — unaffected. City/Area rows
  are therefore **selectable service geography**: bulk census units would
  flood onboarding/discovery pickers, and postal/administrative units are
  not automatically valid service zones.

## 2. Candidate qualification (actual files parsed, not README claims)

### A. `open-admin-data/egypt-administrative-divisions` — usable with conditions

- Pin: `master` @ `76b47b22872288c0d8b0c757975980e6986945e8` (2026-09-08).
- Provenance: **self-published, not government-authoritative**. Per-record
  `source` cites only the publisher's own site/API; `metadata` is `{}`.
  `LICENSE` is a 673-byte CC-BY-4.0 summary naming an unrelated holder
  (`jakkrapongt`) and a nonexistent `build/` directory — template artifact.
  1 star / 0 forks / 6 commits; SEO-style sister-site network. The CC-BY-4.0
  grant exists, but the attribution chain for upstream-derived names is
  unproven — redistribution basis for the underlying names is a marked
  uncertainty, not an established right.
- Data parsed (`data/all-governorate.json`, `all-district.json`,
  `all-shiyakha.json`; SHA-256 in diagnostics): publisher counts verified
  **27 / 365 / 5716 exact**; zero duplicate IDs, zero orphan parents; full
  bilingual presence, nothing over 150 chars; coordinates everywhere,
  `zip_codes` empty on all 6108 records. English values are uneven
  transliterations, not official translations.
- Level semantics are census-administrative, not consumer: level 2 =
  police sections / markaz (`قسم`, `مركز`), including a
  `Port Alexandria Police Department` row and a residual `خارج الزمام`
  ("Zemam Out", 0 children) bucket per governorate. Level 3 = census
  shiyakha enumeration units.

### B. `mo7amed-said-223/egypt-cities` — disqualified for import

- Pin: `main` @ `16d3d24d4f3b8edf7ab311d417126aa313d4cc89` (2025-08-01;
  a year old). Personal account; **no license file** → no redistribution
  basis. README/file-name drift (`centers.json` vs actual
  `center_govs.json`).
- Data parsed: 27 governorates and 351 centers fully bilingual, but all
  **5716 level-3 `name_en` values contain Arabic and 0 contain Latin** —
  fails the bilingual requirement at exactly the Area level. All 5716
  level-3 codes are identical to A's shiyakha IDs: same upstream
  derivation, not independent corroboration.

### C. CAPMAS 2017 census catalogue — disqualified as a direct source

- Official (`metadata.capmas.gov.eg`), but a survey-microdata catalogue
  (variables like `GOV_CODE`, marital status, gender) — no downloadable
  bilingual place-name hierarchy and no gazetteer redistribution terms.
  The shared `EGxx` coding family is plausibly census-derived (unproven).

## 3. Coverage spot-checks (parsed, samples in diagnostics)

- Alexandria (19 districts): consumer-known sections present (Sidi Gabir,
  Raml, Muharam Bik, Mandara-side Muntazah) alongside police-department
  and North Coast rows. Cairo (42): Nasr City, Maadi, Misr al-Gadida,
  Zamalik, New Cairo-1/2/3, Shorouk, Badr. Luxor (4): urban `قسم` +
  rural `مركز` both named "Luxor", Tiba police station, Zemam Out.
  Rural Behera (19): Damanhur/Kafr Al-Dawwar urban+rural pairs plus real
  markaz units (Rashid, Idku, Wadi Al-NatrUn).
- Smouha is absent from **both** datasets at every level — the existing
  seed row cannot be reconciled from either source and must be preserved
  manually with an explicit placement decision.
- New cities are partial: El-Shorouk/Badr/Obour/New Cairo shiyakhas exist
  (filed under Cairo districts — an admin-vs-consumer label mismatch);
  the **New Administrative Capital is absent** from both; New Mansoura
  not found. Neither dataset is current for new-city coverage.
- Import-blocking collisions: **18 same-parent English pairs** (urban
  `قسم` vs rural `مركز` sharing one English name, e.g. Damanhur ×2,
  Luxor ×2) that the exact-match uniqueness rule rejects with 409 on the
  second insert. Arabic never collides (prefixes differ).

## 4. Recommended mapping (primary + alternative)

**Primary: A pinned to `76b47b2`, levels 1–2 only, filtered.**

- Governorate ← A level 1 (27; reconcile on Arabic names, verify English).
- City ← A level 2 **filtered to consumer-meaningful units**: urban
  `قسم` districts (Cairo/Alexandria sections users recognize) and rural
  `مراكز` (matches the guide's "markaz" language). This resolves the
  guide-vs-seed tension: rural City = markaz, urban City = district.
- **Exclude**: all `خارج الزمام` buckets, police-department units, and
  the 18 English-collision pairs pending per-pair owner resolution
  (options: keep urban *or* rural with approved disambiguated English —
  renaming needs explicit approval — or keep both out).
- Area ← **no bulk import**: 5716 census shiyakhas are not service areas,
  Smouha is unmappable, NAC is missing, and MS English is absent. Areas
  stay admin-curated via existing endpoints (caregiver service zones).
- Frontend picker labels: Governorate → City (district/markaz, Arabic +
  English shown) → Area (service zone). New-city rows display under their
  administering governorate until the owner approves consumer relabeling.

**Alternative if level-2 filtering is rejected:** manual admin seeding of
a small owner-approved City/Area set through the existing Admin lookup
endpoints — no importer, no external data, full control of names.

## 5. Bounded 06B implementation manifest (for approval, not started)

- Operator-run preview/import tool (own file/command): never startup
  seeding, no public endpoint, no rows hidden in a schema migration.
- Input snapshot manifest: pinned revision, file SHA-256 table, license/
  attribution note with the §2 uncertainties attached.
- Read-only preview first: exact target/environment/backup preconditions,
  per-row decisions (insert / exact-match keep / fail-closed collision),
  counts and an audit artifact — owner reviews before any write.
- Reconciliation: exact match on Arabic **and** English under the correct
  parent; any ambiguity (either-language collision, missing parent,
  inactive chain, >150 chars, empty-child bucket) fails closed for manual
  disposition. Explicit source-to-existing-ID alias map (incl. Smouha)
  where justified.
- Invariants: preserve all existing IDs, links, inactive flags, and manual
  additions; no automatic renames, re-parenting, reactivation, or deletion.
- Execution: single transaction per batch, idempotent reruns (same input
  → zero changes on repeat), concurrency-safe, full counts report.
- Tests before any API request: isolated initial/repeat/conflict/rollback/
  active-chain cases on a disposable target with no inherited PostgreSQL
  reset fixture.
- New persistent source-ID mapping, if needed, is proposed as its own
  explicit migration — never silent, never bundled with data.
- Owner decisions required: dataset pin, per-pair collision resolutions +
  exclusions, Smouha placement, Area-manual policy, exact target/backup.
  Dataset approval is **not** permission to write Production.

## Owner decision request (one)

Approve: (a) source A pinned to `76b47b2` for levels 1–2 filtered import;
(b) the per-pair resolution for the 18 English-collision pairs plus
Zemam/police exclusions; (c) Smouha City/Area placement; (d) Areas stay
manually curated; (e) the exact later import target and backup. No 06B
work and no target write until all five are explicitly approved.
