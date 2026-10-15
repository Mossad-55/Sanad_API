# Provenance — All-Egypt catalogue candidate (PORTAL-06A2)

Review candidate only. Not auto-loaded by any runtime, migration, or seeder.
No production IDs are invented here: keys are stable source IDs.

## Source

- `open-admin-data/egypt-administrative-divisions`, branch `master`,
  pinned revision `76b47b22872288c0d8b0c757975980e6986945e8`
  (committed 2026-09-08), fetched 2026-10-15 into a task temp directory
  (read as data only; nothing executed; raw upstream bulk files not
  committed — the 1,385,398-byte derived `candidate.json` below is
  committed as the review artefact).
- Input files and SHA-256:
  - `data/all-governorate.json` — 18,662 bytes —
    `EA86B0A7C7219D1DC7C79D6FAECF1C66325F78443277CC491D8D39A94CC9CA27`
  - `data/all-district.json` — 315,793 bytes —
    `F265357BDF7248FD162F1CC23B7C8A68F966CA4BC2988FED408AEF078C1C01D8`
  - `data/all-shiyakha.json` — 6,015,612 bytes —
    `A4458361D556ADB1617C836D804FDE399757FA849D0ED7351A5C428E21A51002`
- Hashes match the PORTAL-06A pins exactly: same upstream revision, no drift.
- Candidate output: `candidate.json` — 1,385,398 bytes — SHA-256
  `18AB3CB47FBDC34B1DFD77894ADA3B79DC27D22C3B2925B679291EDE45F79C09`
  (re-hash after Armant/Esna re-parenting and Tiba reinstatement).

## Attribution and redistribution basis

- Upstream compilation is granted under CC-BY-4.0 with attribution:
  "Egypt Administrative Divisions Dataset (CC-BY-4.0),
  https://github.com/open-admin-data/egypt-administrative-divisions".
- The publisher is self-published, not government-authoritative. All 365
  district records carry an empty `source` object, so per-record upstream
  provenance is absent (the earlier claim that records cite the publisher
  site is retracted). The LICENSE file carries an unrelated holder name
  (template artifact) — see the 06A report. Attribution is recorded as
  stated above; whether that settles compilation/redistribution concerns
  for the underlying names is left open, not concluded here. The upstream
  complete transformation chain remains unproven. Subsequent direct reference
  comparison below qualifies the historical Arabic backbone, not the added
  Area English names or current national completeness.
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
- 18 source rows excluded with per-row reasons in `exclusions`: 14
  residual `خارج الزمام`/`Zemam Out` buckets (each verified to hold 0
  child localities) and 4 port police-department units (each holding
  exactly one customs-facility child: Damietta/Said/Alexandria/Suez port
  customs circles — port facilities, not residential service zones).
- Review correction: `مركز أرمنت` (EG2704) and `مركز إسنا` (EG2705) were
  re-parented Qena → Luxor with `reparented` citation records (GOPP Luxor
  plan); `مركز شرطة طيبة` (EG2903) was reinstated under Luxor with its 6
  genuine localities as `police-section-aggregate`, flagged
  `boundaryUnresolved`, with the return recorded in `reinstatements`.
  Source IDs preserved throughout; no production rows touched.
- English names are otherwise source-verbatim transliterations of uneven
  quality (e.g. Menia, Muharam Bik); they are not official translations.
- `mappingKind` per city: `urban-district` (170), `rural-markaz` (164),
  `new-city` (9), `other-admin-unit` (3: Minshat Nasir, Misr Al-Qadima,
  North Coast — kept selectable pending owner confirmation), and
  `police-section-aggregate` (1: Tiba, boundary unresolved).
- Every area row carries `verification: "provisional-census"`: real source
  records, not consumer-verified service zones.

## Direct historical reference qualification — 2026-10-15

The publisher's [source note](https://openadmindata.org/eg/) identifies
[OCHA/HDX COD-AB Egypt](https://data.humdata.org/dataset/cod-ab-egy).
Direct HDX metadata attributes CAPMAS and states CC BY-IGO terms. Downloaded
GeoJSON ZIP SHA-256:
`259987BE0B7C5E367D52EADF3DFEA39802C7750CACE8498833E3212D61B9DBA9`.
Every included candidate key and Arabic name matches that reference exactly.
The 18 City English differences and two City parent differences are the
documented disambiguations and Armant/Esna corrections; Area parents match.
All 5712 included reference Area English values are blank, so this reference
does not validate the publisher's translations/transliterations.

The source dates boundaries to 2017 and explicitly marks ADM3 coverage partial;
its December 2024 review and 2026 resource update are not current-boundary
certification. See [evidence](hdx-reference-evidence.json) and the
[qualification report](../../reports/phase-06-data-qualification.md).
The candidate remains review-only; provenance/readiness acceptance flags are
not automatically satisfied by historical correspondence.
