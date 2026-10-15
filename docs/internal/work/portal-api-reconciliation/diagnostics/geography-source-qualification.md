# PORTAL-06A diagnostics — reproducible data-only source checks

All checks are read-only parses of downloaded data files inspected as data.
No downloaded SQL/code/script was executed; no dataset is committed; no API,
database, or network write beyond fetching public files occurred. Temporary
artifacts live only in the worker's task-specific temp directory (`geo06a/`)
and are reported, not committed.

## Pins and file integrity (fetched 2026-10-15)

| # | Source | Pinned revision | File (branch path) | Bytes | SHA-256 |
| --- | --- | --- | --- | --- | --- |
| A | `open-admin-data/egypt-administrative-divisions`, `master` | `76b47b22872288c0d8b0c757975980e6986945e8` (2026-09-08) | `data/all-governorate.json` | 18662 | `EA86B0A7C7219D1DC7C79D6FAECF1C66325F78443277CC491D8D39A94CC9CA27` |
| A | same | same | `data/schema.json` | 1632 | `DF2316AE59F6013CB9126AF0A4C1669E71F6F434C5A962E73DF62385531FA765` |
| A | same | same | `data/all-flat.csv` | 54230 | `01E7FD0848B73E768DA0314E599E3053524D3EF9BE00D3D70E2079153875784C` |
| A | same | same | `data/all-district.json` | 315793 | `F265357BDF7248FD162F1CC23B7C8A68F966CA4BC2988FED408AEF078C1C01D8` |
| A | same | same | `data/all-shiyakha.json` | 6015612 | `A4458361D556ADB1617C836D804FDE399757FA849D0ED7351A5C428E21A51002` |
| A | same | same | `LICENSE` | 673 | `C3479E413D67B5E12C11D3917349E3F1CB418E167302B907722F189881CBC306` |
| B | `mo7amed-said-223/egypt-cities`, `main` | `16d3d24d4f3b8edf7ab311d417126aa313d4cc89` (2025-08-01) | `governorates.json` | 2032 | `FDFB495BD5370D67D90C42CED7D4F4DEBD8106A0682724AC426325525FED5BB3` |
| B | same | same | `center_govs.json` | 38811 | `20C653AF1D26E158834D890AEDAD2A4EE559134A328BE5641AF6DF1374332992` |
| B | same | same | `city_centers.json` | 807535 | `86E974E0E061066A7793DB23DC6AAE949BA80F13CD91D04A1540456838B6DC5A` |

(Revision SHAs via public commit API; file hashes re-verified locally after
fetch. Hashes correspond to branch HEAD at fetch time — re-pin before 06B.)

Publisher claims vs parsed reality:

- A claims 27 governorates / 365 districts / 5716 shiyakhas — parsed
  **27 / 365 / 5716 exact**. Referential integrity clean: no duplicate
  source IDs at any level, no orphan parents.
- B claims 27 / 351 / 5716 — parsed **27 / 351 / 5716 exact**; internal
  references consistent (0 orphan center/governorate links; 0 duplicate
  ids/codes). All 5716 B level-3 `code` values are identical to A
  shiyakha IDs → shared upstream derivation, not independent corroboration.
- B level-3 `name_en`: **5716/5716 contain Arabic, 0/5716 contain Latin**
  (English column untranslated). B levels 1–2 are bilingual
  (27/27 and 351/351 Latin-bearing).
- A bilingual presence is complete at all levels (0 missing ar/en, 0 names
  over the 150-character domain limit), but English values are
  transliterations of uneven quality (`Menia` for Minya, `Muharam Bik`,
  `A L Labban`, `Kesm than Al Raml`, `Wadi Al-NatrUn`, `15 Mayu`).
- A `zip_codes` is empty on all 6108 records (advertised "where available"
  = nowhere). Coordinates present on all records. Per-record `source`
  cites only the publisher's own site/API; `metadata` is `{}` — no
  independent upstream citation per record.
- A `LICENSE` (673 bytes) is a CC-BY-4.0 summary naming copyright holder
  `jakkrapongt` and a `build/` directory that does not exist in the repo —
  template artifact. B has **no license file at all**.
- A `all-flat.csv` starts with a `#`-comment line before the header, so
  naive CSV parsers misread the header row (data wart, not a blocker).

## Same-parent English collisions in A level 2 (18 groups — import-blocking)

Under the app's exact-match rule (Arabic OR English equal under the same
parent → 409 `NameAlreadyInUse`), each pair below cannot both import:

Sharkia/Faqus; Giza/Giza + Giza/Imbaba; Kalyoubia/Banha + Kalyoubia/Qalyub;
Fayoum/Fayyum; Behera/Damanhur + Behera/Kafr Al-Dawwar; Aswan/Aswan;
Qena/Qina; Dakahlia/Mit Ghamr; Beni Suef/Bani Swayf; Suhag/Girga +
Suhag/Suhag; Menoufia/Shibin al-Kum; Kafr El-Shikh/Disuq +
Kafr El-Shikh/Kafr Al-Shaykh; Luxor/Luxor. Pattern: urban `قسم` vs rural
`مركز` sharing one English name (Arabic differs, so 0 Arabic collisions).

## Representative level-2 samples (A, parsed)

Alexandria (19): Muntazah, Al Raml, Sidi Gabir, Bab Sharqi, Muharam Bik,
Al Attarin, Al Manshiyya, Karmuz, A L Labban, Al Gumruk, Mina Al-Basal,
Al Dikhila, Al Amreia, Burg al-Arab, Port Alexandria Police Department,
Burg Al-Arab City, Kesm than Al Raml, North Coast, Zemam Out.
Cairo (42): police-section districts incl. Nasr City, Maadi, Misr
al-Gadida, Zamalik, New Cairo-1/2/3, Shroq (El Shorouk), Badr, Zemam Out.
Luxor (4): `قسم الأقصر`/`مركز الأقصر` (both "Luxor"), Tiba police station,
Zemam Out. Suez (6): five sections + Port Suez Police Department (no
Zemam row). Behera (19, rural): Damanhur/Kafr Al-Dawwar urban+rural pairs
plus Abu-l-Matamir, Badr, Rashid, Wadi Al-NatrUn, Zemam Out, etc.

## Shiyakha spot checks (A level 3, parsed)

- Sidi Gaber district (EG0203) holds 6 shiyakhas (Abu Al-Nawatir,
  Al-Riyada, Sidi Gaber, Ezbet al-Nozha, Ezbet Saad, Mostafa Kamel wa
  Bolokli) — **Smouha is absent** from the district and from all 5716
  rows (both datasets).
- New cities are partial: El-Shorouk 1/2, Badr 1/2, El Obour, Marina El
  Alamein and Madinet El Alamein present; the **New Administrative
  Capital is absent** (no `عاصمة` record in either dataset); New Mansoura
  not found.
- `EG0140` (Shroq) and `EG0141` (Badr) sit under Cairo (EG01) — new cities
  administered as Cairo districts, a consumer-label mismatch if shown raw.

## Current-model facts (source: `src/`, `docs/`, seeder)

- `Governorate`/`City`/`Area` (`Caregivers.Domain/.../Lookups/`): trim-only
  normalization (no case/diacritic folding), 150-char max, parent IDs set
  only at creation (no re-parent API), `UpdateNames` + active flags only.
- `CityLookups`/`AreaLookups` handlers: parent must exist and be active
  (Area additionally requires an active governorate above the city);
  duplicate = exact `==` on Arabic OR English under the same parent →
  409; public reads require the full active chain; deactivation hides
  children from public reads without touching them.
- Seeder (`TestUserDataSeeder.cs:332-351`): Alexandria → City `سموحة`/
  `Smouha` → Area `سموحة`/`Smouha Area` (Smouha as a *city* is a consumer
  fixture, not an administrative unit).
- Guide (`docs/admin/lookups-cities-areas.md`): Cities described as
  "markaz"; parent immutability, chain-visibility, and uniqueness rules as
  above.
- Consumers: caregiver service-area selection validates Area IDs with the
  full active chain (`CaregiverSelections.cs`); discovery filters by
  Governorate/City/Area IDs (`CaregiverDiscovery.cs`); pricing insights
  aggregate by Area ID (`GetPricingInsightsQuery.cs`). Care Home profiles
  use location **strings**, unaffected.
- Mapping impact: City/Area rows are selectable service geography. Bulk
  census units (police sections, `Zemam Out` buckets, 5716 shiyakhas)
  would flood consumer pickers and break the onboarding/discovery UX.

## Commands run (data-only; nothing executed from downloads)

- `Invoke-WebRequest` fetches + `Get-FileHash SHA256` (log above).
- Python stdlib `json`/`csv`/`collections`/`re` parses and the counts,
  collision, integrity, bilingual-coverage, and spot-check listings above.
- No `dotnet` build/test, no API startup, no database contact, no Bruno.
