# PORTAL-06 source intake — 2026-10-15

Status: preparation authorized by the owner's "let's go". Dataset and hierarchy
mapping **not approved**. No importer or target-data changes authorized yet.

## Current implementation facts

- Core portal reconciliation is accepted at `0890419`; geography is separate.
- Model: Governorate -> City -> Area. City/Area names require Arabic/English,
  max 150 characters, with parent-scoped application uniqueness checks. Parent
  IDs are immutable via existing endpoints. Active parent chain controls public
  visibility; no automatic child reactivation/deletion is approved.
- `docs/admin/lookups-cities-areas.md` describes Cities as markaz, while
  `TestUserDataSeeder.cs` creates Smouha under Alexandria as a City and a
  Smouha Area beneath it. Those fixtures are not a national taxonomy or a live
  ID reconciliation manifest. Preserve existing IDs/references; do not guess
  how an eventual administrative dataset should remap them.
- Care Home profile draft uses location strings, not these lookup IDs.
- No production lookup inventory or new API call was made for this intake.

## Preliminary sources (not qualified or approved)

These primary publisher pages were inspected on 2026-10-15. Counts below are
**publisher claims**, not parsed/validated counts or completeness proof.

1. [CAPMAS census catalogue](https://metadata.capmas.gov.eg/ar/index.php/catalog/Census2017?view=v)
   is an official starting point. Finding a current, usable bilingual hierarchy
   and its redistribution basis remains open; census vintage must not be
   confused with current geography.
2. [Open Admin Data Egypt](https://github.com/open-admin-data/egypt-administrative-divisions)
   advertises 27 governorates, 365 districts, 5,716 shiyakhas, Arabic/English,
   JSON/CSV and CC-BY-4.0; README labels an update of 2026-09-08. Actual upstream
   provenance, license applicability, data vintage/coverage and file integrity
   are unverified. A recent README date is not proof of current boundaries.
3. [mo7amed-said-223/egypt-cities](https://github.com/mo7amed-said-223/egypt-cities)
   advertises 27 governorates, 351 centers and 5,716 cities/villages/local areas.
   Its inspected landing page does not establish an upstream source or clear
   redistribution permission. Its second-level definition/count differs from
   the preceding candidate; neither is automatically the Sanad City model.

The [Tech-Labs governorates/cities repository](https://github.com/Tech-Labs/egypt-governorates-and-cities-db)
was also identified; two levels alone do not establish a usable third Area
level. Do not mix unrelated datasets by display-name similarity without proof.

## Next action / boundary

Issue [PORTAL-06A source qualification](../worker-prompts/06a-geography-source-qualification.md).
The external worker will inspect actual pinned files and provenance, propose
mapping/coverage and a bounded import design, then stop for owner approval.
No nationwide coverage, English translation authority or licensing suitability
is claimed by this shortlist. No build/test/API/import/deploy action occurred.
