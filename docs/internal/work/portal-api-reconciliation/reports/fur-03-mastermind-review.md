# FUR-03 Mastermind review — finance read regression verification

Date: 2026-10-16 (Africa/Cairo). **Status: accepted; phase release completed with deployment N/A.**

The worker reports no reproduced defect, 12/12 PostgreSQL tests, focused existing
tests passed and a strict build with zero warnings/errors. The historical typed-ID
fix `5edd3419781cdc1d3b106b2debb60ea0295f0ff2` is an ancestor of deployed package
`5cd3526b53e8555d3233810b8a024fcb6aa0322e`; the existing fix is in the deployed
binary. No runtime change or FUR-03 deployment is indicated. The report's execution
results are worker-reported; Mastermind has not rerun the tests or touched the
PostgreSQL database.

Static review covered the approved contract, new PostgreSQL fixture test, worker
report, both audience handoffs, the active plan and handoff, and current source
relationships. `git diff --check` is clean. Original review findings and their
resolution status are recorded below.

## Review findings and correction record

**Correction-pass re-review:** R1–R5 are now satisfied by the worker's follow-up:
the dedicated database guard, handler-level/admin-dashboard/Approved-owner test
coverage, reproducible test evidence, and audience handoffs were checked in the
current files. R1–R4 text that follows is the record of the original review
findings, not a claim that they remain open. R5 is resolved by the final wording
correction.

### R5 — Remove contradictory/overstated evidence wording (resolved)

The corrected code and test inventory address R1–R4: the guard pins
`SanadFur03VerifyDb`; Admin revenue invokes the handler; filtered/unfiltered Admin
dashboard and Approved-owner dashboard cases are present; the resource/counter
assertions distinguish facilities. The report and handoffs describe the 12-test
coverage and isolated target consistently. The final worker correction resolved
these two documentation accuracy issues:

- Worker report §5 says a connection string is “shown with redacted password in
  this report,” then says the report contains no connection string. Replace this
  contradiction with an accurate statement that the secret was omitted and not
  persisted in documentation.
- Both audience handoffs label the source revision as “clean `main == origin/main`.”
  `HEAD` equals the baseline `origin/main`, but the phase's tests and documents are
  uncommitted in the workspace. Rephrase to distinguish the clean source revision
  from the dirty/uncommitted delivery workspace; do not imply the workspace is
  clean or deployed.

No test or database run was needed for these wording-only corrections. The report
now states no secret appears in documentation, and both handoffs distinguish the
baseline revision from the uncommitted delivery workspace.

### R1 — Use a FUR-03-specific disposable database (resolved)

The approved contract requires a verified disposable local PostgreSQL target with
a phase-specific database name. The worker used `SanadBrunoTestDb`, and the report
explicitly says that database is shared with other phases. The opt-in connection
key and fail-closed guard are FUR-03-specific, but the database itself is not.
The report says fixture rows were deleted by their generated IDs and post-run
readback found no residue; no cross-phase interference was observed. This does
not meet the approved target boundary for a rerun.

The phase-specific-name requirement was not explicit enough in the original
contract: it required a phase-specific database name but left creation/selection
ambiguous. That contract gap is Mastermind's responsibility. The owner has now
explicitly authorized creation/use of exactly `SanadFur03VerifyDb` on local
PostgreSQL `localhost:5432` (2026-10-16), solely for this correction round. Do
not rerun stateful proof on `SanadBrunoTestDb`. First inspect the authorized
target read-only; if it exists with unknown/non-disposable data or ownership,
stop and report rather than dropping/resetting it. If absent, the named database
may be created and provisioned with the existing committed CareHomes schema and
migrations needed for this proof. Do not alter migration sources or touch any
other database. Update the test guard and evidence to match. Never put a
connection string in docs or reports.

### R2 — Complete the PostgreSQL query coverage (resolved)

The 10 tests include filtered Admin dashboard, unfiltered Admin revenue, owner
Draft dashboard/revenue and owner Approved revenue. They do not include an
unfiltered Admin dashboard or an Approved owner dashboard, although the contract
covers admin filtered and unfiltered dashboard/revenue plus owner dashboard and
revenue for Draft and Approved facilities. Also, the Admin revenue tests call
`CareHomeRevenueProjection.Revenue` directly; they do not execute
`GetAdminCareHomeRevenueHandler.Handle`, so they skip the handler's
`Guid? facilityId` → `CareHomeId?` conversion that is central to the historical
translation fix. The report currently describes those as production-handler
proof, which overstates what ran.

Add PostgreSQL proof for Admin revenue through `GetAdminCareHomeRevenueHandler`
for filtered, unfiltered and unknown-filter cases; add the missing unfiltered
Admin dashboard and Approved owner dashboard paths. Strengthen Admin dashboard
fixtures so filtered and unfiltered results distinguish the facilities in the
dashboard fields being asserted: the current Admin dashboard fixture has only
accepted/paid bookings, no resources or active occupancy, and the reported
assertions distinguish only revenue totals. Include per-facility dashboard
counters/resources that demonstrate the facility predicate's effect, while
preserving minimal fixture scope. Rerun the completed PostgreSQL proof on the
R1-approved target and update counts and claims in the report and handoffs.

### R3 — Make the PostgreSQL execution evidence reproducible (resolved)

The delivery report's PostgreSQL test command uses `dotnet test ...`, so it is
not the exact command required by the contract. Record the exact executable
command and filter, with secrets omitted, and describe how the actual test child
process received only the explicitly selected FUR-03 target while inherited
integration targets were neutralized. Include the target-name/host validation,
test exit code, pass/fail/skip counts, and cleanup readback. Do not disclose the
connection string.

### R4 — Align audience notices with the verified evidence (resolved)

The owner handoff describes Draft and Approved dashboard and revenue reads as
PostgreSQL-verified, but the Approved dashboard path is absent from the current
PostgreSQL test set. The Admin handoff describes dashboard filtering as fully
isolated, while its fixture assertions currently distinguish only revenue and
do not give the dashboard resource/counter queries discriminating data. The
README summarizes both claims as confirmed. After R2, make all three artifacts
match the exact test coverage; identify the shared target accurately until it is
replaced, retain “frontend repository not inspected,” and state deployment N/A
because no runtime API delta was made.

## Acceptance state

No source-level correctness defect was found in the changed test file by static
inspection, and no runtime defect was reproduced in the worker's reported proof.
R1–R5 are accepted based on source/document review and the worker's saved execution
evidence; Mastermind did not rerun tests or access PostgreSQL. No runtime change
was made, so deployment is N/A. FUR-03 is accepted and Done. Owner approved
release; phase commit `858da09` is pushed to `origin/main`. See the
[release receipt](fur-03-release.md). FUR-04 was not included.
