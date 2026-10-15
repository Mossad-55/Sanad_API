# Coverage — All-Egypt catalogue candidate (PORTAL-06A2)

Denominator: the pinned source's own hierarchy (27 governorates, 365
districts, 5716 shiyakhas at revision `76b47b2`), not an independent
official census. Snapshot date 2026-09-08; candidate assembled 2026-10-15.
The candidate represents all 27 governorates, not proven exhaustive national
locality coverage; every Area is `provisional-census` (see gaps). Direct
[HDX reference evidence](hdx-reference-evidence.json) confirms the historical
Arabic backbone but explicitly identifies partial ADM3 coverage and 2017 vintage.

Delivered: 27 governorates, 347 cities (170 urban-district, 164
rural-markaz, 9 new-city, 3 other-admin-unit, 1 police-section-aggregate;
18 candidate-disambiguated), 5712 areas. Excluded: 18 districts (14
residual Zemam buckets with 0 children each, 4 port police-department
units each holding a single customs-facility child) and the 4 shiyakhas
beneath excluded districts. Zero included cities lack areas; zero
duplicate keys; zero orphan parents.

Owner approved 16 Area display-label cleanups for V1; see
[before/after provenance](label-corrections.json). Counts and all parent links
remain unchanged. This is not additional consumer/boundary verification.

## Per-governorate matrix (delivered / source)

| Gov | Cities | Areas | Note |
| --- | --- | --- | --- |
| Cairo (EG01) | 41 / 42 | 324 / 324 | Zemam bucket out; New Cairo 1–3, Shorouk, Badr kept |
| Alexandria (EG02) | 17 / 19 | 140 / 141 | Zemam + port police unit out; **Smouha missing from source** |
| Port Said (EG03) | 11 / 12 | 21 / 22 | Port police unit out |
| Suez (EG04) | 5 / 6 | 17 / 18 | Port police unit out |
| Damietta (EG11) | 8 / 9 | 100 / 101 | Damietta port police unit out |
| Dakahlia (EG12) | 21 / 21 | 513 / 513 | Mansoura urban/rural kept distinct |
| Sharkia (EG13) | 21 / 22 | 531 / 531 | Zemam bucket out (0 children) |
| Kalyoubia (EG14) | 14 / 15 | 216 / 216 | Banha/Qalyub pairs disambiguated |
| Kafr El-Shikh (EG15) | 12 / 12 | 226 / 226 | Disuq + capital pairs disambiguated |
| Gharbia (EG16) | 12 / 12 | 350 / 350 | Full |
| Menoufia (EG17) | 12 / 12 | 330 / 330 | Shibin al-Kum pair disambiguated |
| Behera (EG18) | 18 / 19 | 534 / 534 | Damanhur/Kafr Al-Dawwar pairs disambiguated |
| Ismailia (EG19) | 8 / 8 | 56 / 56 | Full |
| Giza (EG21) | 21 / 22 | 239 / 239 | Giza/Imbaba pairs disambiguated |
| Beni Suef (EG22) | 9 / 10 | 240 / 240 | Pair disambiguated |
| Fayoum (EG23) | 8 / 9 | 174 / 174 | Fayyum pair disambiguated; Fayoum New kept |
| Menia (EG24) | 12 / 13 | 378 / 378 | New Minya kept |
| Assiut (EG25) | 14 / 15 | 261 / 261 | Assiut New kept |
| Suhag (EG26) | 19 / 20 | 290 / 290 | Girga/Suhag pairs disambiguated; Akhmim/Suhag New kept |
| Qena (EG27) | 11 / 14 | 167 / 203 | Armant+Esna re-parented to Luxor on GOPP evidence; Qina pair disambiguated; Qena New kept |
| Aswan (EG28) | 9 / 10 | 122 / 122 | Aswan pair disambiguated; Aswan/Toshka New kept |
| Luxor (EG29) | 5 / 4 | 59 / 23 | Armant+Esna re-parented in; Tiba aggregate reinstated (equivalence unresolved); Bayadiya/Qurna/Ziniya-markaz/Tod/New-Luxor absent from source; **needs official completion** |
| Red Sea (EG31) | 8 / 8 | 41 / 41 | Full |
| New Valley (EG32) | 4 / 4 | 75 / 75 | Full |
| Matrouh (EG33) | 8 / 8 | 113 / 113 | Full, incl. Madinet El Alamein and Marina El Alamein |
| North Sinai (EG34) | 11 / 11 | 96 / 96 | Full |
| South Sinai (EG35) | 8 / 8 | 99 / 99 | Full |

## Required sample lineage

- Alexandria/Smouha: 17 districts delivered (Sidi Gabir, Raml, Montazah…);
  Smouha exists in neither inspected dataset — seed row stays manual.
- Cairo/New Cairo: 41 districts incl. Nasr City, Maadi, New Cairo-1/2/3,
  Shorouk, Badr; New Administrative Capital absent everywhere.
- Luxor: urban `قسم الأقصر` ("Luxor") + rural `مركز الأقصر`
  ("Luxor Markaz"), Armant and Esna corrected into Luxor, and Tiba aggregate
  reinstated with its six localities. Current centre/child equivalence unresolved.
- Rural: Behera/Dakahlia/Sharkia markaz rows kept verbatim (guide's
  "markaz" language); urban/rural English pairs disambiguated per rule.
- New cities: Shorouk, Badr, Obour, New Cairo, New Minya/Qena/Sohag/
  Aswan/Assiut/Fayoum/Akhmim, Burg Al-Arab, Toshka present; filed under
  administering governorates (e.g. Shorouk/Badr under Cairo).

## Honest gaps (not silently skipped)

1. Smouha (seed City + Area) unmappable from source — manual preview
   reconciliation required; never auto-delete/re-parent.
2. New Administrative Capital absent — picker cannot claim it.
3. All 5712 area rows unverified census units; consumer service-zone
   fitness unproven (validator counts them, coverage does not vouch).
4. English names are transliterations; 18 disambiguated labels are
   candidate-only proposals.
5. Postal codes absent from source (optional, not a release blocker).

The five entries above correspond to `candidate.gaps`; they are not the
entire readiness ledger. Historical Arabic backbone attribution/correspondence
is now evidenced; publisher Area English derivation and current consumer
hierarchy remain unqualified. Smouha/NAC existence is supported by official
references in the [qualification report](../../reports/phase-06-data-qualification.md),
but their required selectable hierarchy/legacy-ID mapping is still unresolved.
A missing familiar spelling (e.g. Menia/Minya) alone is not proof of absence.
