# Geography replacement — read-only VPS preflight

Final status: owner selected preserve-and-add, approved all four links, and the
operation is now complete. The destructive replacement below was never run.
See [executed receipt](geography-import-preserve-add.md); earlier pending states
are historical evidence, not current blockers.

Date: 2026-10-15 (Africa/Cairo). Owner approved checking live references and
preparing an exact replacement, with affected records reported before deletion.
Status: read-only check Done; no replacement/import/delete performed.

Verified running release `9167e71`, Production, service active, database reachable.
Effective targets resolved privately from the running process and existing
settings: all nine modules use one local PostgreSQL database. Cluster inventory
confirmed PostgreSQL 16 on loopback port 5433. No inherited/default target was
assumed; the initial 5432 guard refused before querying. Credential values,
database name and personal record identifiers were not output.

All SQL was inside explicit REPEATABLE READ READ ONLY transactions, with 10 s
statement timeout and rollback. Queried catalogue, constraints and aggregate
reference counts only. No service restart, provider call or test fixture.

## Actual impact

- Existing lookups: **4 governorates / 3 Cities / 2 Areas**; hierarchy not orphaned.
- `caregivers.caregiver_area_selections`: **5 rows across 5 caregiver profiles**,
  all pointing to the two existing Areas; zero already-orphaned selections.
- `families.bookings.booking_area_id`: **0 non-null rows**. This does not mean
  there are no bookings, only that none currently stores an Area ID there.
- Total caregiver profiles: **7**. No profile or Identity account was modified.
- Schema inventory found four UUID location-reference columns: Cities' parent,
  Areas' parent, caregiver area selections and family booking area attribution.
  No declared foreign key references to the three lookup tables exist. Thus a
  blind deletion could silently leave invalid saved IDs, not necessarily fail.
  This inventory does not certify arbitrary external caches/clients or JSON/text
  payloads as reference-free.

| Existing Area | Parent | Selections | Finding |
| --- | --- | ---: | --- |
| `01a07c8a-0936-75fa-87e7-4a5b6f0a660b` — مركز مطاي / Damanhur Markaz | Matay → Minya | 4 | Arabic/English disagreement. Do not guess a replacement. |
| `01a07fc1-bd48-79b7-8c6d-2d3bfaeb18d1` — مركز دمنهور / Damanhur Markaz | Damanhur → Beheira | 1 | Old Markaz-shaped Area is not a proven exact consumer-locality mapping. |

## Shortest proposed replacement — not executed

Obtain exact owner approval to clear **only those five caregiver area-selection
rows and the existing 4/3/2 lookup rows**, after a fresh reference check and
verified private backup. Preserve all seven caregiver profiles, Identity users,
bookings and other modules. Load the accepted 27/347/5712 catalogue while writers
are stopped; run count/hierarchy/API verification before restarting writers.
Never DROP the tables or use CASCADE. Roll back failed operations rather than
leave a partially loaded hierarchy.

Consequence requiring explicit acceptance: five caregivers lose their old
service-area choices and must select valid areas again; area-filtered discovery
cannot use those removed choices meanwhile. This is not permission to silently
change profile lifecycle flags or invent replacement selections.

The current standalone CLI is insert-only and does not implement reset. Any
approved replacement must be a narrow operator operation with tested atomicity
and recovery; no extra API/runtime configuration or new external service needed.
Owner may instead choose a specific mapping if the five choices must be retained.

## Superseding owner choice — preserve and add

Owner chose to keep existing locations and all five saved selections, and
approved the preserve-and-add recommendation. The reset proposal above is
therefore **not authorized**. Uploaded the already-published standalone tool
to private staging and ran its actual read-only preview against the effective
Caregivers connection, injected from the running API without exposing values.
No apply command, database write, service restart or permanent config change.

Preview SHA-256:
`99ED0B6F4946D85A368697F062FF25722B1CAA85CCD7FAA7EA03BE992F38FB28`.
Report retained at
`/root/sanad-releases/9167e71-20261015/geography-preview-01/preview.json`.
Result: 5141 proposed inserts, one exact Keep, 944 conflict rows, eight preserved
extras. **Only two root conflicts**: EG18 Behera vs existing Beheira and EG24
Menia vs existing Minya. Other conflicts are dependent children, not 944
independent defects. No partial import was attempted.

Source comparison also identifies Asyut vs Assiut as the same governorate with
both labels differing; the strict literal algorithm would create another entry.
After linking Beheira, source EG1801 قسم دمنهور / Damanhur and existing
دمنهور / Damanhur would require another explicit reconciliation. Do not silently
claim the existing broad city label proves an exact qism boundary equivalence.

Proposed exact operator decisions, pending owner acceptance:

| Source | Existing record to retain unchanged | Purpose |
| --- | --- | --- |
| EG18 البحيرة / Behera | Beheira governorate `01a07c6a-6e2a-70cf-b63a-094d87a95777` | Same governorate, spelling difference |
| EG24 المنيا / Menia | Minya governorate `01a07c7e-e825-7dfb-a7fb-5f7ce01d9845` | Same governorate, spelling difference |
| EG25 أسيوط / Assiut | Asyut governorate `01a07c28-9228-71f4-9717-ade72428ecf2` | Avoid duplicate governorate |
| EG1801 قسم دمنهور / Damanhur | Damanhur City under Beheira `01a07c6b-8664-7e17-b938-0ba33e950f75` | Explicit administrative grouping choice, not inferred geographic certification |

An explicit reviewed alias mechanism would retain those IDs, names, parents,
state and timestamps; candidate bytes remain unchanged, old Areas and choices
remain unchanged. Current CLI has no alias feature; implementing/testing such
a narrow mechanism would precede a fresh reviewed preview and backed-up apply.
Do not hand-edit the preview or bypass source/plan integrity guards.
