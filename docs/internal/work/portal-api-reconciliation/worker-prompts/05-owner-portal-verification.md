# PORTAL-05 — fixture-backed Care Homes owner API verification

Role: bounded API/fixture test and documentation worker. PORTAL-01 through 04
are accepted; the deployed caregiver-data finding remains independently open
pending operator evidence. Execute only PORTAL-05, not geography or release.

## Intake / pinned baseline

Read AGENTS.md, governance, Mastermind_Handoff, PORTAL-05 and the mandatory
fixture-readiness section of `../plan.md`. Read the existing owner guide,
controllers, real query/command handlers and relevant local test helpers.
Avoid whole-project re-audits or replaying onboarding.

Phase 04 worker delivery was `33af090e2f4d542d5b3247f8cbf90f664ec7445b`.
The following mastermind commit accepts it with bounded SQL/runbook corrections
and adds this brief. Use the launch message's exact final pushed SHA, verify
clean main equals origin/main, record it; do not reset to the older delivery.

Work directly on main, one worker at a time. You are not alone: preserve others'
edits, stage explicit owned paths and stop on overlapping unowned changes.
No branch/worktree, hook bypass, deployment, publish, VPS/provider access, Bruno,
database reset or production fixture change. Commit/push normally after checks.

## Objective / approved routes

The frontend could not exercise owner routes without owner credentials. Admin
403 results were correct, but are not owner-success evidence. Close that bounded
local API coverage gap using synthetic owners and real application behavior.

Verify these **seven GET routes and one settings PUT** through an isolated
in-process HTTP host, retaining production auth/policies, MVC/JSON and dispatch:

| Method | Path (prefix `/api/v1/care-homes`) |
| --- | --- |
| GET | `/facilities/mine` |
| GET | `/inventory/mine` |
| GET | `/booking-requests/mine` |
| GET | `/visits/mine` |
| GET | `/dashboard/mine` |
| GET | `/revenue/mine` |
| GET | `/facilities/mine/visit-settings` |
| PUT | `/facilities/mine/visit-settings` |

Derive actual DTOs/statuses/codes/defaults and date-range rules from source.
Do not assume every empty read is 200, or that every Draft resource is forbidden.
Do not change established business rules to fit a test or frontend expectation.
Use real handlers and isolated module contexts; fake only external ports or
irrelevant dependencies. A fake ISender returning invented success DTOs is not
proof of owner scoping, lifecycle, query or finance behavior.

## Scenario coverage (bounded, meaningful)

- An owned approved facility with inventory: response includes the expected
  synthetic owner/facility IDs and meaningful nested room/bed values; verify
  actual wire IDs/enums, envelope/collection shape and nullable fields.
- A second owner's facility/data: `/mine` must scope to the caller's own data.
  A second valid owner is not automatically forbidden from reading their own
  `/mine`. Assert absence of the other facility's inventory/bookings/visits/
  amounts and no cross-owner mutation, rather than inventing a route facilityId.
  Prove denial on an actually supported foreign-resource path only if needed
  to demonstrate settings mutation isolation; otherwise `/mine` segregation
  plus readback of the untouched foreign fixture is sufficient.
- A valid owner without a facility: assert actual not-found/empty outcome per
  handler. An existing Draft facility: assert actual visibility/gating for the
  selected reads/settings. Do not force an assumed approved-only policy.
- Synthetic SuperAdmin-only and unrelated-role tokens: verify owner policy
  403; anonymous 401. Use a parameterized representative route matrix, not an
  admin 403 as the only result. Keep normal session/auth behavior unchanged.
- Empty versus populated booking/visit/dashboard/revenue views. Seed realistic
  provider-free states via existing lifecycle helpers/domain methods; include
  at least one relevant row and one excluded/foreign control. Assert counts,
  IDs, date filters and monetary totals from the real query, not just HTTP 200
  or a nonempty array. Keep the minimal supported state needed by each view.
- Settings: read current version, successful update with expectedVersion,
  stale-version conflict, fixed-clock hours/windows/closures as relevant and
  safe required/range validation. Read back after failures: target settings/
  version unchanged, foreign facility unchanged. Derive actual error codes;
  no mutation of non-Draft/approved state unless current behavior allows it.

Do not expand this into every Care Homes mutation/detail/media/export route.
List untested nested/detail paths explicitly. Unrelated mobile APIs, geography
provisioning and Chat runtime/device/soak work stay outside PORTAL-05.

## Mandatory readiness BEFORE host startup / requests

1. Inspect existing configuration and fixture builders first, including
   `CareHomeFacilityTests`, `CareHomeInventoryTests`,
   `CareHomeBookingLifecycleTests`, `CareHomeVisitTests`,
   `CareHomeFinanceReadModelTests` and API `PortalApiMetadataHost`. Reuse valid
   builders/states; record actual helper reuse instead of fabricating a new
   general seeding framework or replaying all onboarding routes.
2. Prepare safe host configuration before startup: every module connection/
   concrete context isolated, migrations and fixture seeders disabled, no
   production hosted jobs, ephemeral keys, temporary per-run storage and fake
   email/SMS/payment/push providers. Respect the Phase 03 finding that final
   IConfiguration alone does not replace eagerly captured concrete contexts.
   No inherited PostgreSQL target or secret reads. Prefer no database network.
3. Seed only the minimum synthetic actor roles/identities, owners/facilities,
   required profile/location strings, inventory, booking/visit/revenue states
   and settings required by selected scenarios. Location lookups are not a
   reason to seed a nationwide hierarchy; facility profile uses location text.
   Separate fixed domain time from JWT validity so tokens remain valid.
4. Read back all prerequisites and relationships before starting the host:
   actor IDs/roles, owner-to-facility links, Draft/approved lifecycle, inventory
   foreign keys, booking/visit/payment status and expected amounts, settings
   version and date windows. Record a concise synthetic readiness manifest
   with seed/reuse counts. No passwords/tokens/PII in docs.
5. Only then start host and send scenarios. Verify state before reuse after
   mutations; rebuild only the consumed fixture, not the entire application.
   Seed creation prerequisites first; creation-under-test itself can occur
   later if selected. No startup seeding/migrations as fixture preparation.

Use isolated InMemory where supported. If exact query semantics need a
relational provider, use compatible in-memory SQLite with honest limitations.
PostgreSQL only on an already-authorized, exact verified disposable target,
minimal writes and no reset. No inherited EnsureDeleted fixtures. Record a
provider-specific unrun boundary instead of silently mocking away the query.

## Owned manifest

- New `tests/Sanad.UnitTests/API/CareHomeOwnerPortalTests.cs` and optionally
  `CareHomeOwnerPortalFixture.cs` / `CareHomeOwnerPortalHost.cs` in that same
  API directory (private bounded harness, not a public test-data API).
- `tests/Sanad.UnitTests/API/PortalApiMetadataHost.cs` only if a small optional
  isolation/reuse seam is essential. Preserve its existing metadata scenarios
  and rerun those isolation tests if altered; prefer extending locally.
- Existing Care Homes tests listed above only for narrowly shared helper reuse
  needed by this task; name exact selected files at intake. No wholesale helper
  refactor or modification of unrelated test expectations.
- `docs/postman/care-homes/Sanad.CareHomes.Owner.postman_collection.json`:
  selected owner request examples/descriptions only, with synthetic placeholders
  and verified states/versions. Do not auto-send the collection or reformat it.
- Relevant sections only of `docs/care-homes/owner-onboarding.md`,
  `inventory-and-availability.md`, `booking-lifecycle.md`, `visits.md`,
  `finance-reporting.md` as actual endpoint evidence requires.
- New `../reports/phase-05-owner-portal.md` and only PORTAL-05 status/evidence in
  `../plan.md`. Central handoff/index are mastermind-owned.

Production controllers/handlers/domain/configuration are read-only in this
verification task. If a concrete defect appears, capture the failing request,
state, response and exact source cause; stop the dependent scenario, continue
independent checks and return a bounded correction proposal. Do not silently
change policy/wire shapes or broaden scope. No new package/migration/route,
reference-data import, integrity repair, provider setting or CORS change.

## Acceptance / gates / handoff

Return an endpoint-by-state matrix: actual HTTP status/code, meaningful payload
assertions, ownership/date/version behavior, handler/provider exercised,
fixture setup/readback and limitations. Clearly distinguish real handler/HTTP
evidence, fake external-port evidence and any unrun PostgreSQL boundary. A
passing empty list or an attribute-only authorization assertion is insufficient.

After final test code: strict solution build exit 0 / zero warnings/errors;
focused owner-portal tests and relevant reused Care Homes tests with
`--no-build --no-restore`, zero failures/skips unless explicitly justified;
parse touched Postman JSON, Postman-only mapping zero missing/orphan, diff-check.
Run affected metadata-host/isolation tests if its shared inputs change. Reuse
architecture if boundaries remain unchanged. Full integrated suite is PORTAL-07.
Do not run a whole collection/full reset-based suite or deploy anything.

Commit/push normally. Return exact base/final SHA, selected manifest, readiness
manifest, commands/results/warnings/skips, actual endpoint matrix, new findings
and remaining handoffs. Stop for mastermind review; do not start PORTAL-06/07.
