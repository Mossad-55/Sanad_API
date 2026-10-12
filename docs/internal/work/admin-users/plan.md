# SuperAdmin user management — frozen plan (v1, read-only, no implementation)

Owner-ordered full package defined from zero. Nothing here authorizes code,
migration applies, seeds, provider calls, or runtime. Implementation starts
only after explicit owner approval of this plan, phase by phase.

## 0. Goal and non-goals

Backend API for SuperAdmin control over every account in the system, with a
complete audit trail. Non-goals: self-service profile/password flows (already
exist), Caregiver *profile* review (exists under `CaregiverReviewAdmin`),
frontend work (backend only, per project boundaries), and any change to
authentication mechanics (login/OTP/session limits stay exactly as they are).

## 1. Roles (all 9 `AccountType` values — nothing invented)

| # | AccountType | Managed by this package? | Notes |
|---|---|---|---|
| 1 | Family | Yes | Bulk of support work; anonymize precedent exists |
| 2 | MedicalCaregiver | Yes | Distinct from companion (different onboarding) |
| 3 | CompanionCaregiver | Yes | Same tooling, separate type filter |
| 4 | Elderly | Yes | Read-mostly; anonymize with extra care (medical links) |
| 5 | SuperAdmin | Yes, with guards | Create/promote/demote; seeded one untouchable (§4) |
| 6 | ContentAdmin | Yes | CMS-only boundary preserved (see §6) |
| 7 | SupportAdmin | Managed (read/revoke nothing — SuperAdmin-only package per §8.1) | No user-management access of any kind |
| 8 | CareHomeOwner | Yes | Includes facility cross-link in detail view |
| 9 | FinanceAdmin | Yes | Highest blast radius after SuperAdmin; changes need a reason |

## 2. Endpoint catalog (frozen contract — changing it needs a new owner decision)

Base `api/v1/admin/users`, policy `SuperAdminOnly` (new constant, SuperAdmin
account type on Normal access — same shape as `ChatSupportAdmin`).

| # | Verb + route | Purpose | Success | Key failures |
|---|---|---|---|---|
| 1 | `GET /` | List/search/filter/sort/page users | 200 paged list | 400 invalid query |
| 2 | `GET /{id}` | Detail: profile, status, account types, sessions, linked aggregates (§2.1) | 200 | 404 `Identity.User.NotFound` (no existence leak across types) |
| 3 | `POST /{id}/suspend` | Suspend (login blocked, sessions revoked) | 200 | 404 / 409 already-suspended / 403 self / 403 seeded |
| 4 | `POST /{id}/reactivate` | Suspended → Active | 200 | 404 / 409 not-suspended / 403 seeded |
| 5 | `POST /{id}/block` | Block (stronger than suspend; matches domain `Block`) | 200 | 404 / 409 / 403 self / 403 seeded |
| 6 | `POST /{id}/sessions/revoke` | Force-logout: all sessions, or one `{sessionId}` | 204 | 404 unknown user / session |
| 7 | `POST /{id}/password-reset` | Admin-set temporary password + revoke all sessions | 200 (one-time display) | 404 / 403 seeded (seeded resets only via host env) |
| 8 | `PUT /{id}/account-type` | Change account type (e.g. SupportAdmin → ContentAdmin) | 200 | 404 / 409 illegal transition / 403 last-superadmin / 403 seeded |
| 9 | `POST /` | Create admin account (Super/Support/Content/Finance only — never end-user types) | 201 | 400 validation / 409 email taken |
| 10 | `POST /{id}/anonymize` | Scrub PII, keep aggregates (extends family precedent to all types) | 200 | 404 / 409 already-anonymized / 403 seeded |
| 11 | `GET /audit` | Paged admin-action trail with filters | 200 | 400 invalid query |
| 12 | `POST /{id}/soft-delete` + `POST /{id}/restore` | Soft-delete (hidden, reversible) and restore | 200 | 404 / 409 already-(un)deleted / 403 self / 403 seeded |
| 13 | `DELETE /{id}` | Hard delete (permanent, guarded — §3.4) | 204 | 404 / 409 has-dependents / 403 self / 403 seeded / 403 last-superadmin |
| 14 | `GET /export` | CSV export mirroring #1 filters (owner-ordered, §3.5) | 200 CSV | 400 filter too broad / 403 non-SuperAdmin |
| 15 | `GET /sessions` | Cross-admin active-session overview with filters (owner-ordered, §3.6) | 200 | 400 invalid query / 403 non-SuperAdmin |

List supports: `search` (name/email/phone, case-insensitive), `accountType`, `status`, `sort` (`Newest|Oldest|Name`, §2.2), `page`/`pageSize` (1–100 clamp). Invalid sort/range → 400 `Identity.Admin.InvalidQuery`.

### 2.1 Detail shape (Amendment A, 2026-10-12 — resolves "linked aggregates" gap)

`AdminUserDetail`: id, arabic/english names, email, phone, status, accountTypes[],
createdOnUtc, isVerified, sessions[] (same item shape as
#15: deviceId, deviceName, platform, appVersion, createdOnUtc, expiresOnUtc,
lastSeenUtc), `linked` object: for CareHomeOwner — `{ facilityId, facilityStatus }`
resolved through one small API-layer `ICareHomeOwnerFacilityLookup` port over
CareHomes (mirrors the approved `ApiChatUserLookup` precedent); for all other
types — `{}` (their aggregates stay behind their dedicated endpoints; no
cross-module joins are introduced in v1).

### 2.2 Sort + session-field semantics (Amendments B–C, 2026-10-12)

- `sort=Name` orders by ArabicName ASC, then EnglishName ASC (Arabic-first product).
- There is no request-tracking infrastructure, so `lastSeenUtc` is defined as the
  session's `LastRotatedOnUtc` (last token rotation), documented as such in the
  operator guide. Adding true last-request tracking is explicitly out of scope.
- There is no verification timestamp on `User` (only status/booleans, and OTP
  timestamps don't cover seeded or bypass paths), so the detail carries
  `isVerified` (true unless `PendingVerification`) instead of a fabricated
  `verifiedOnUtc`. Backfilling timestamps would invent history — forbidden.

## 3. Business rules and full case matrix

### 3.1 Status transitions (domain `UserStatus` already supports all of these)

| From → To | Via | Sessions | Notes |
|---|---|---|---|
| Any → Suspended | #3 | All revoked | Login denied while suspended |
| Suspended → Active | #4 | None restored (fresh logins) | |
| Any → Blocked | #5 | All revoked | Blocked is terminal except via reactivate path (§8 decision 2) |
| PendingVerification → Active | normal verification only | — | Admins cannot force-verify (prevents bypassing OTP) |

### 3.2 Invariants (all enforced in domain/handlers, all tested)

1. **Seeded super-admin is untouchable.** The bootstrap account (matched by the
   seed email configured at first start) rejects suspend/reactivate/block/
   password-reset/account-type-change/anonymize with 403 `Identity.Admin.SeededProtected`.
2. **No self-harm.** An actor cannot suspend, block, demote, or anonymize
   themselves (403 `Identity.Admin.SelfAction`). Self password change stays on
   the existing self-service route.
3. **Last-superadmin guard.** Demoting or blocking the final active SuperAdmin
   fails 409 `Identity.Admin.LastSuperAdmin`. Count is computed at command time.
4. **Promotion/demotion matrix.** Only SuperAdmins change admin types, and only
   within {SuperAdmin, SupportAdmin, ContentAdmin, FinanceAdmin}. End-user
   types (Family, Elderly, both Caregivers, CareHomeOwner) are never assigned
   through #8 — they arise from onboarding only. Demotion SuperAdmin → other
   requires a second active SuperAdmin (rule 3).
5. **Suspend/block revokes everything.** All device sessions die with the
   transition (reuse the existing logout-all semantics, scoped to the user).
6. **Password reset is explicit and audited.** Admin sets a policy-compliant
   temporary password (min 10, same policy as the seeder), all sessions die,
   the plaintext is shown exactly once in the 200 response and never stored or
   logged. Seeded account resets only via host environment, never the API.
7. **Anonymize ≠ delete.** PII (names, email, phone, documents) is scrubbed;
   bookings, payouts, audit rows, and aggregates keep referential integrity
   (extends the existing family-anonymize precedent). Already-anonymized → 409.
8. **Soft delete (reversible).** Sets `DeletedOnUtc` + `DeletedBy` (new nullable
   columns, additive migration with the audit table); `UserStatus` is left
   untouched so restore returns the exact prior state. Soft-deleted users are
   hidden from default lists (`includeDeleted=false` filter opts back in),
   cannot log in, and keep every row intact. Restore clears the markers.
   Double-delete / restore-when-active → 409.
9. **Hard delete (permanent, last resort).** Allowed only when the account has
   zero dependent rows (no bookings, payouts, cases, documents, sessions, or
   family links — checked in the handler). Otherwise 409
   `Identity.Admin.DeleteBlocked` naming what blocks it. The deletion itself
   still writes one audit row (actor + target id snapshot) before the rows go.
   Seeded, self, and last-superadmin guards (§3.2 rules 1–3) apply unchanged,
   plus a required non-empty reason.
10. **Privacy-preserving 404s.** Unknown user id → 404 regardless of type; a
   cross-type existence leak (e.g. probing admin ids) returns the same 404.
11. **Audit everything.** Every mutation (#3–#10, #12, #13) and every export (#14)
   writes one `AdminUserAction` row: actor, target (null for exports — filters +
   row count recorded instead), action, reason (required for suspend/block/demote/
   anonymize/deletes, optional otherwise), UTC timestamp. Reads (#1, #2, #11) write
   nothing. The audit store is append-only — no update/delete endpoints.

### 3.5 Status-change notices + admin session overview (owner-ordered v1)

Suspend, reactivate, block, and password-reset enqueue an email + in-app notice
to the affected user through the existing notification seams (no new
infrastructure). Notices are best-effort: the mutation stands even if enqueue
fails (failure is logged, never silent, never blocking) — locking someone out
must not depend on the mailer. `GET /sessions` pages every admin account's
active sessions (filter by account type/user, same paging/sort conventions as
#1; shows device, platform, last-seen) for compromised-account response;
revoking any of them reuses #6 against that user id. Reads write no audit
rows; the revocations themselves audit normally.

### 3.6 CSV export rules (owner-ordered)

Serves: regulator/compliance extracts, offline support review, escalation
evidence, and spreadsheet analysis — the cases where clicking through a paged
UI is the wrong tool. Columns: id, account type, status, names, email, phone,
created/verified timestamps, deleted marker. **Never**: password hashes, tokens,
OTP material, or document bytes. Guards: SuperAdmin-only; same filters as #1;
hard cap of 10,000 rows (broader matches → 400 asking for narrower filters);
every export audited with actor + filters + row count.

### 3.3 Cases the tests must pin (minimum)

Empty list, search both languages, type/status filters, all sorts, paging
bounds, unknown id 404s, double-suspend/double-block 409s, self-action 403s,
seeded 403s on all guarded routes, last-superadmin 409, session revocation
counts on suspend/reset, one-time password display with immediate login proof, anonymize idempotency,
audit row per mutation with actor/target/action, export audited with filters + row count, notice enqueue asserted per mutation, admin-create email-taken 409, soft-delete/restore round-trip, hard-delete refusals (dependents/self/seeded/last-superadmin), CSV cap refusal,
illegal type transitions 409.

## 4. Seeded super-admin rule (explicit, no exceptions)

Matched by seed email. The API refuses every mutation against it. Rotation or
recovery happens only through host environment + redeploy, never through any
endpoint, including #7. A dedicated test pins all six refusals.

## 5. Audit trail design (new, additive, one migration authored-only)

New `identity.admin_user_actions` table: id (v7), actor id, target id,
action (enum), reason (nullable, ≤1000), created UTC. No update/delete paths.
`GET /audit` filters: actor, target, action, date range; same paging/sort
conventions as #1. Migration authored only, never applied by workers.

## 6. Contradiction check vs the main goal (no conflicts found)

- Backend only ✔ (no UI).
- `ContentAdmin` stays CMS-only ✔ — this package is SuperAdmin-gated; ContentAdmin gains nothing here.
- Family/Elderly supplied flows untouched ✔ (reads only + existing anonymize precedent extended, not altered).
- No new provider/SMTP/Firebase behavior ✔. No ledger/transfer/policy changes ✔.
- Frozen-chat style discipline reused: contract in this file is frozen; changes need a new decision.

## 7. File manifest (exclusive — implementer touches nothing else)

- `Modules/Identity/Application/Users/Admin/` — list/detail/suspend/reactivate/block/revoke/reset/change-type/create/anonymize/audit handlers + validators + errors.
- `Modules/Identity/Domain/Users/` — only additive guards (seeded/self/last-superadmin checks as domain rules where they belong).
- `Modules/Identity/Infrastructure/` — audit entity config + additive migration (authored only) + `IIdentityDbContext.AdminUserActions`.
- `Sanad.API/Controllers/AdminUsersController.cs` + `Requests/AdminUser*Request.cs` (endpoints 1–11).
- `AuthorizationPolicies.cs` (+`SuperAdminOnly`) + DI registration.
- `tests/.../Identity/AdminUserManagementTests.cs` (+ SQLite translation twins for every new query).
- `docs/admin/user-management.md` (full operator guide, new consumer file) + Postman additions (admin folder, endpoints 1–11) + `docs/postman` JSON validation.
- `docs/internal/work/admin-users/{tasks.md,handoff.md,reports/}` tracking.

## 8. Decisions — ALL RESOLVED 2026-10-12 (locked)

1. SuperAdmin-only (owner-ordered) — SupportAdmin gets zero user-management access.
2. Blocked → Active via #4 reactivate (default accepted).
3. Admin-set temporary password (default accepted).
4. Anonymize all types (default accepted).
5. Admin-create strictly admin types (default accepted).
6. Soft + hard delete both in (owner-ordered); CSV export in (owner-ordered); status-change notices + admin session overview in (owner-ordered). Two-person SuperAdmin grants, retention clock, bulk ops, account merge, API keys: out unless separately ordered. Impersonation: rejected.

## 9. Recommended additions (usefulness-ranked, owner picks)

| # | Feature | Verdict | Why |
|---|---|---|---|
| 1 | Temp-password expiry + must-change flag | **Recommended for v1 (awaiting pick)** | A non-expiring admin-set password is a standing backdoor. Expire in 24h + force change on next login; expiry enforced at login, tested. Small, high security value. |
| 2 | Brute-force lockout visibility + admin unlock | **Recommended for v1 (awaiting pick)** | Support's most common identity ticket ("user locked out"). If lockouts exist in Identity, expose state + unlock; if not, add counter-based lockout first. Check during Phase 1 scout. |
| 3 | Per-user session/device detail in #2 | **In v1 (pairs with owner-ordered #15)** | Force-logout without seeing *what* you're killing is blind. Device, platform, last-seen per session; revoke-one stays. Almost free — data already exists. |
| 4 | Super-admin counters dashboard (`GET /stats`) | **Recommended for v1 (awaiting pick)** | Totals by type/status, suspended-this-week, pending-verification aging, sessions-today. One aggregate query set; the first screen every admin opens. |
| 5 | Dry-run preview for anonymize/hard-delete | **Recommended for v1 (awaiting pick)** | `?dryRun=true` returns exactly what would be scrubbed/removed + blockers, writes nothing. Prevents the irreversible oops; reuses the same checks. |
| 6 | Two-person rule for hard delete | **Recommended for v1 (awaiting pick)** | Destructive action needs a second SuperAdmin's confirmation token (pending-deletion state + `POST /{id}/confirm-delete`). Single biggest production-safety upgrade; moderate cost. |
| 7 | Bulk suspend/reactivate (incident response) | **Defer to v2** | Real need (compromised batch), but per-item partial-failure semantics + audit shape deserve their own design pass. Don't bolt onto v1. |
| 8 | CSV export of user lists | **Include in v1 (owner-ordered 2026-10-12)** — see §3.5 for columns, cap, and audit rules |
| 9 | Login-as / impersonation for support | **Reject unless explicitly ordered** | Maximum blast radius for convenience: every action becomes unattributable without heavy guardrails (time-box, reason, dual audit, banner). Recommend never; support via read-only detail + reset instead. |

Defaults if the owner stays silent: include 1–5, add 6, defer 7–8, reject 9.

## 10. Verification (baked in, including live API — the process fix)

Per phase: zero-warning build; focused tests green (InMemory **plus SQLite
translation twins for every new query** — mandatory since the finance/translation
postmortem: InMemory hides SQL translation faults, SQLite proves them); docs +
Postman synced; `git diff --check`; contract-map 0/0. Joint gate once: full
unit + architecture suites. **Live API gate per phase on the disposable target
(port + `SanadBrunoTestDb`): start API, log in as the seeded superadmin
fixture, run the phase's reads (list/detail/audit) expecting real 200s, revoke
the fixture sessions, stop the API.** Mutating live checks (suspend/reset on a
disposable fixture account) only with explicit per-run approval; unrun checks
are recorded limitations, never passes. No full-collection Bruno by default.
