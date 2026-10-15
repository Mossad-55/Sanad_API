# Geography preserve-and-add — executed receipt

Date: 2026-10-15 (Africa/Cairo). Status: Done. Owner explicitly approved retaining
existing data, then the four exact reconciliation links, including Damanhur.
No reset/delete/rename/reparenting. API binary remains deployed `9167e71`.

## Implementation and local evidence

Standalone importer gained optional `--reconciliation sanad-vps-v1` on preview
and apply. Default strict behavior is unchanged. Profile pins the approved target
fingerprint, candidate hash and four old IDs/names/parents. Profile is serialized
into the reviewed plan; apply recomputes the same profile and entire plan. Wrong
target/catalogue, missing/renamed/reparented link rows, collisions or changed
profile are refused. No source dataset edit or API runtime/config change.

Final strict solution build: exit 0, **zero warnings/errors**. Focused isolated
lookup/import tests: **119 passed / 0 failed / 0 skipped** (six new cases).
Full catalogue with a synthetic nine-row legacy fixture and five saved selection
rows: 6081 inserts, original records preserved field-for-field, five selections
still resolve, fresh repeat zero inserts. Tests cover mapping drift/authority
and plan-profile tampering. Exact child integration targets were removed before
tests; fixtures used SQLite memory, not the VPS. Earlier full suite 3358/0/8
remains historical. Standalone Release publish exited 0 without warnings.

Updated tool archive SHA-256:
`860395441851A6C786072608026EC2B13505E58F1F1789AE1527C93CE638946A`.
Candidate hash remains approved `E048767A…8A8E`. Narrow direct review performed;
no delegated worker needed for this scoped tool-only adjustment.

## Exact data operation

Used the running API's effective Caregivers connection privately on the verified
local PostgreSQL 16 cluster/port 5433. No connection value printed or stored in
API settings. Private stage: `/root/sanad-releases/geography-links-v1-20261015/`.

1. Verified archive hash; online actual CLI preview: **6081 inserts, zero
   conflicts, four preserved extras**.
2. Stopped/drained `sanad-api.service`; verified no other client connections to
   the target. Read back expected old 4/3/2 lookups, five selections, seven
   profiles and zero booking Area-ID references before importing.
3. Created private full database backup `database-before.dump`; `pg_restore
   --list` validated its archive catalogue. This is not an executed restore drill.
   Private baseline JSON and previews remain in that protected directory;
   never commit/copy those backups as public artifacts.
4. Fresh maintenance preview: same counts, zero conflicts. Exact plan SHA-256:
   `6A352A0240887AF14AADD86881D445EF8FB198DF03A9DF9CE2798A648BC2AEA8`.
5. Applied approved insert-only transaction: **6081 inserted**.
6. Readback proved **all nine old lookup records field-for-field unchanged**,
   **all five complete selection rows unchanged**, seven profiles retained,
   zero booking Area-ID references and zero orphan Cities/Areas/selections.
7. Fresh repeat preview: **zero inserts, zero conflicts**, four retained extras.
   Repeated apply was not needed. Started the existing API service.

## Final result and public verification

Database totals: **27 governorates / 349 Cities / 5714 Areas**. Candidate totals
are 27/347/5712; two extra legacy Cities and two extra legacy Areas are intentional.
Old bilingual mistakes remain preserved legacy data, not silently certified or
corrected. Five caregivers do not need to reselect areas.

External deployment verifier: exit 0, Production revision `9167e71`, databases
reachable, **9/9 applied migration heads match**, services HTTP 200. Public
governorates **27**; new Cairo branch **41 cities**, sampled city **3 Areas**;
OpenAPI HTTP 200. Service active/running, `NRestarts=0`. First count-check command
incorrectly wrapped PowerShell's returned array; corrected direct assignment
passed without a backend/data change. No tokens transmitted over public HTTP.

No new API configuration, API binary deployment, table/schema/migration change,
CORS/auth change, credential change or provider scenario. Only temporary importer
process environment was used. V1 geography provisioning is complete; scope limits
on modern completeness, caregiver Identity diagnostics and separate Chat runtime/
device/soak tests are not erased by this operation.
