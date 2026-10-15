# Provenance — All-Egypt catalogue candidate (PORTAL-06A2)

Review candidate only. Not auto-loaded by any runtime, migration, or seeder.
No production IDs are invented here: keys are stable source IDs.

## Source

- `open-admin-data/egypt-administrative-divisions`, branch `master`,
  pinned revision `76b47b22872288c0d8b0c757975980e6986945e8`
  (committed 2026-09-08), fetched 2026-10-15 into a task temp directory
  (read as data only; nothing executed; raw bulk files not committed).
- Input files and SHA-256:
  - `data/all-governorate.json` — 18,662 bytes —
    `EA86B0A7C7219D1DC7C79D6FAECF1C66325F78443277CC491D8D39A94CC9CA27`
  - `data/all-district.json` — 315,793 bytes —
    `F265357BDF7248FD162F1CC23B7C8A68F966CA4BC2988FED408AEF078C1C01D8`
  - `data/all-shiyakha.json` — 6,015,612 bytes —
    `A4458361D556ADB1617C836D804FDE399757FA849D0ED7351A5C428E21A51002`
- Hashes match the PORTAL-06A pins exactly: same upstream revision, no drift.
- Candidate output: `candidate.json` — 1,382,382 bytes — SHA-256
  `2ECB48086A1686DD938E89EF3B99497CBF063533FA3CE420C6BE5862DD5E1BCF`.

## Attribution and redistribution basis

- Upstream compilation is granted under CC-BY-4.0 with attribution:
  "Egypt Administrative Divisions Dataset (CC-BY-4.0),
  https://github.com/open-admin-data/egypt-administrative-divisions".
- The publisher is self-published, not government-authoritative;
  per-record `source` cites only its own site/API and the LICENSE file
  carries an unrelated holder name (template artifact) — see the 06A
  report. Place names as facts are reproduced with attribution; the
  upstream derivation chain remains unproven and is recorded as a
  limitation, not a right that is assumed beyond the stated CC grant.
- Cross-check: `mo7amed-said-223/egypt-cities` @ `16d3d24` shares all
  5716 level-3 codes identically (same upstream derivation, not
  independent corroboration); its level-3 English column is entirely
  Arabic and it carries no license file, so it was not used.

## Manual corrections and translation notes (all inside the candidate only)

- 18 rural-markaz rows received a candidate-only `" Markaz"` English
  suffix (e.g. `مركز دمنهور` → "Damanhur Markaz") because each shares its
  source English name with the urban district under the same governorate;
  without this, the application's exact-match rule rejects the second
  insert with 409. Each such row carries `disambiguated: true` plus a
  note. Production rows are unchanged by these labels.
- 19 source rows excluded with per-row reasons in `exclusions`: 14
  residual `خارج الزمام`/`Zemam Out` buckets (0 consumer localities) and
  5 police-department units. 10 shiyakhas under excluded districts are
  likewise dropped (counted, listed by exclusion).
- English names are otherwise source-verbatim transliterations of uneven
  quality (e.g. Menia, Muharam Bik); they are not official translations.
- `mappingKind` per city: `urban-district` (170), `rural-markaz` (164),
  `new-city` (9), `other-admin-unit` (3: Minshat Nasir, Misr Al-Qadima,
  North Coast — kept selectable pending owner confirmation).
- Every area row carries `verification: "provisional-census"`: real source
  records, not consumer-verified service zones.
