# PORTAL-04 operator runbook — caregiver / Identity diagnostic

Scope: read-only diagnosis of the reported caregiver rows with null Identity
enrichment. No production repair, deletion, quarantine, migration, seed, or
deployment is authorized by PORTAL-04. If you cannot run these steps, return
this file unexecuted: the worker delivers local-test evidence only and the
deployed finding stays explicitly unverified.

## 1. What the operator is being asked

The admin caregivers list (`GET /api/v1/admin/caregivers`) shows one row per
caregiver row with names/phone LEFT JOINed from `identity.users` (on the
**Caregivers connection's database**). A row with null names/phone therefore
means exactly one of:

- (a) no `identity.users` row with that `user_id` is visible **from the
  Caregivers-target database** (genuinely absent there, or the Identity module
  points at a different physical database);
- (b) the user row exists but the join missed (wrong id value);
- (c) the display is read from a stale copy/fixture.

The diagnostic distinguishes (a) from (b)/(c) and, for present users, checks
whether the expected caregiver account type exists
(`identity.user_accounts`: `2` for Medical caregivers, `3` for Companion).
It does **not** decide repair: any delete/recreate/quarantine/indicator needs
a separate exact mastermind/owner decision.

## 2. Prerequisites (operator verifies, worker must not)

- An authorized **read-only** database role on the deployment. The VPS is
  Production; the database is not disposable.
- The exact module targets: which physical database each of
  `IdentityDatabase`, `CaregiversDatabase`, `FamiliesDatabase` points at.
  Compare the labels **privately** (host/database names from deployment
  configuration). Never paste connection strings, passwords, or tokens into
  reports or chat; the Section 0 fingerprint query exposes only database and
  schema names.
- Full caregiver/user ids. The frontend report used abbreviated ids; **never
  expand a truncated id by guesswork**. Obtain full ids via the authorized
  admin list (`GET /api/v1/admin/caregivers?page=1&pageSize=10`) or exact local
  records.

## 3. Target check (no secrets in output)

Effective wiring (from current source, `*DbContext` DI + `*DbContextFactory`):

- Key names: `ConnectionStrings:CaregiversDatabase`, fallback
  `ConnectionStrings:IdentityDatabase` (same fallback pattern for Families and
  the other modules). A blank module key means that module silently shares the
  Identity target.
- The connection string is captured **eagerly** at startup
  (`UseNpgsql(connectionString)` inside `AddCaregiversInfrastructure`), so the
  final `IConfiguration` snapshot alone is not proof of the already-captured
  DI options (proven in PORTAL-03: a machine-inherited `localhost:5432`
  target survived a dummy `IConfiguration` override).
- Operator step: for each module, record privately the effective
  host/port/database label (from the deployment environment as injected into
  the running host, not from a guessed file), then run the Section 0
  fingerprint query on each reachable target and compare. Report back only:
  `same-database: yes/no per module pair` plus the schema-presence result.
  If a target is unreachable with the read-only role, record which one and
  stop its sections.

## 4. Execution (manual, one section at a time)

1. Connect manually (psql or equivalent) with the read-only role. The script
   never connects by itself and reads no environment variable.
2. On **each** target you will inspect, run:
   `BEGIN READ ONLY; SET LOCAL statement_timeout = '5s';` then Section 0,
   then `ROLLBACK;`
   Record which of the listed tables exist per target. A missing table
   invalidates only its own section — skip it and record
   `not inspectable on <target>`.
3. On the **Caregivers target**: Sections 1–4.
   - Section 1 reconciles list/count: `matched + missing_user` must equal both
     the list total and the count total under identical filters. Any other
     relationship is a defect in the two production statements — report it
     verbatim.
   - Section 2 lists missing-user rows (bounded; default cap inside the file).
     For the reported incident, note whether the count of such rows is six
     and their statuses/types — but do not treat the count alone as proof of
     orphaned production rows.
   - Section 3 classifies present users. Record the per-class counts
     (`matched`, `present_user_missing_expected_account`) and the 3b/3c
     listings (actual account codes, duplicate `user_id` groups).
   - Section 4 runs once per selected full `:caregiverId`: row presence,
     certificate status counts, child-selection counts, rating count, payout
     account/payout/review ids+statuses. No content, banking values, or
     payloads.
4. If the Identity module uses a **different** physical database, Sections 2–3
   run against an `identity` schema that is empty or absent here — that is the
   target-mismatch hypothesis made visible, not a data defect. In that case
   run Section 5 **independently on the Identity target** for each supplied
   full user id (existence, status, account types, document existence) and
   reconcile the id sets privately.
5. On the **Families target**: Section 6 per selected caregiver id (booking
   ids+statuses, report counts). These references cannot be inspected from a
   split Caregivers database — hand them off explicitly instead of reporting
   zero counts from the wrong target.
6. Keep every run inside `BEGIN READ ONLY ... ROLLBACK` with the short
   statement timeout. No `INSERT/UPDATE/DELETE/DDL`, no temporary-table
   creation on Production, no `SELECT` of name/phone/email columns, no
   unbounded export.

## 5. Return format (sanitized)

Reply with, and only with:

- Target sameness: `Caregivers==Identity: yes/no`,
  `Caregivers==Families: yes/no` (+ which schema-presence rows were missing).
- Section 1: `list_total / matched / missing_user / count_total` per filter
  used (unfiltered + the reported filter, if any).
- Section 2: number of missing-user rows; per row: full `caregiver_id`,
  full `user_id`, `caregiver_type`, `caregiver_status`, `updated_on_utc`.
- Section 3: classification counts; 3b rows (ids + actual account codes);
  3c duplicate groups (full `user_id` + row count + first/last caregiver ids).
- Section 4 per caregiver: the six count/id/status blocks.
- Sections 5–6: per-id existence/status/accounts and booking/report blocks.
- Anything you could not run, and the exact preflight row that blocked it.

## 6. What happens next (not the operator's decision)

The worker reconciles returned ids against the report, classifies the cause
(stale fixture data vs. target mismatch vs. reproduced write-path defect),
and returns a **candidate** handoff: recommended repair/quarantine/indicator
wording for mastermind/owner approval. No data action is applied from this
diagnostic.
