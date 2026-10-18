# Admin portal — FUR-07 Community moderation — moderation queue, report lifecycle and author restrictions

Date: 2026-10-18 (Africa/Cairo). Phase: FUR-07.
Purpose: backend delivery/deployed notice; client adoption unconfirmed.
Audience: Admin portal (Admin audience = Admin portal + Care Home Owner portal).
Communication state: backend phase accepted and deployed at `303a131`; client
adoption/forwarding is unconfirmed.

No frontend repository was inspected. No frontend code defect is claimed.

## Baseline and authority

- Original intake and issue IDs: frontend intake items #7 (missing moderation
  inspection) and #8 (requested statistics, persisted reject reasons and timed
  author blocking) from the A2 requested-endpoints table of the
  [intake audit](../../../../internal/work/portal-api-reconciliation/frontend-report-audit-2026-10-16.md).
  Owner-approved decisions recorded in the contract: posts-only reporting,
  community-only timed restrictions (global block/suspend stays SuperAdmin-only),
  required rejection reason codes with an optional note, and no hide/restore
  action for reported published posts.
- Canonical plan/approved contract: [FUR-07 community moderation
  contract](../../../../internal/work/portal-api-reconciliation/contracts/fur-07-community-moderation.md)
  (owner-approved 2026-10-17, including the test-manifest amendment);
  [worker delivery](../../../../internal/work/portal-api-reconciliation/reports/fur-07-worker-delivery.md);
  [release receipt](../../../../internal/work/portal-api-reconciliation/reports/fur-07-release.md);
  review acceptance is recorded in the [Mastermind
  handoff](../../../../internal/Mastermind_Handoff.md).
- Implementation/deployed revision: `303a1319dd6da4752b727e93aec4dec0296b95d4`,
  pushed to `origin/main` and deployed to Production.
- Deployment revision and evidence: [release
  receipt](../../../../internal/work/portal-api-reconciliation/reports/fur-07-release.md)
  — strict build 0 warnings/errors; 153 focused Community/model tests; additive
  Community migration `20261017154400_AddCommunityModeration` authored and
  applied at deployment (other eight contexts zero); all 9/9 applied heads
  matched; nine startup checks present; host configuration fingerprints
  unchanged; standard verifier and anonymous smoke HTTP 200 passed through an
  SSH tunnel. The PostgreSQL advisory-lock path was not exercised against a
  live PostgreSQL server.
- Evidence limits: no live authenticated moderation request or Bruno run;
  deployment evidence is the tunneled standard verifier plus the accepted
  focused tests. Frontend repositories inspected: none.

## What is correct and should remain

| Issue | Correct behavior/usage | Evidence |
| --- | --- | --- |
| #7 | `GET /api/v1/admin/community/posts` is a paged, searchable moderation queue (optional `status`, `reportStatus` and bilingual title/content `search`; default newest first, then stable ID; default page size 25, maximum 100) returning `items`, `page`, `pageSize`, `totalCount` and persisted counters (pending-review/published/rejected posts, open reports, distinct authors with an open report, distinct authors with an active restriction). `GET /api/v1/admin/community/posts/{postId}` returns post detail with retained reports and moderation history. Author fields are ID and Arabic/English display names only — never email, phone or other contact data. | [FUR-07 contract](../../../../internal/work/portal-api-reconciliation/contracts/fur-07-community-moderation.md); [admin moderation guide](../../../../admin/community-moderation.md) |
| #7/#8 | Rejecting a pending-review post now requires a `reasonCode` (`CommunityGuidelines`, `Spam`, `Duplicate`, `UnsafeContent`, `Other` — `Other` requires a note) plus an optional 1000-character note; the moderator ID and UTC decision time are persisted. Publish behavior is unchanged. | [FUR-07 contract](../../../../internal/work/portal-api-reconciliation/contracts/fur-07-community-moderation.md); [admin moderation guide](../../../../admin/community-moderation.md) |
| #7 | Reports start `Open`; a moderator resolves a report as `ActionTaken` or `Dismissed` with moderator ID, UTC time and optional note. Resolution does not itself publish/reject content or restrict an author — those remain explicit moderation actions. One retained report per reporter/post pair; a repeat report returns the existing report; reports are not hard deleted. Reported published posts remain visible — FUR-07 adds no hide, restore, delete or unpublish action. | [FUR-07 contract](../../../../internal/work/portal-api-reconciliation/contracts/fur-07-community-moderation.md) |
| #8 | Community-only timed author restrictions use fixed presets (24 hours, 7 days, 30 days) with a required reason code and optional note; an author has at most one active restriction, endable early by a moderator with a required reason while the original action stays in history. The restriction blocks new posts, comments and replies only, enforced by UTC comparison in the write commands (no background job); existing content, edits/deletes of existing comments/replies, Identity status, login, sessions and all other modules are unchanged. Only the existing Community moderation roles (normal authenticated SuperAdmin or ContentAdmin) can add a restriction; no FUR-07 route grants global Identity suspend/block. | [FUR-07 contract](../../../../internal/work/portal-api-reconciliation/contracts/fur-07-community-moderation.md); [admin moderation guide](../../../../admin/community-moderation.md) |

| Route | Verb | Policy | Status behavior |
| --- | --- | --- | --- |
| `GET /api/v1/admin/community/posts` | GET | Community moderation (SuperAdmin/ContentAdmin) | 200 paged queue with counters; invalid paging → 400 standard validation |
| `GET /api/v1/admin/community/posts/{postId}` | GET | Community moderation | 200 detail with retained reports and moderation history; author ID/display names only |
| `POST /api/v1/admin/community/posts/{postId}/reject` | POST | Community moderation | 200 rejected with persisted reason code/note/moderator/time; missing reason code → 400; only pending-review posts |
| `POST /api/v1/admin/community/reports/{reportId}/resolve` | POST | Community moderation | 200 resolved `ActionTaken`/`Dismissed` with optional note |
| `POST /api/v1/admin/community/authors/{authorId}/restrictions` | POST | Community moderation | 200/201 retained restriction with UTC expiry |
| `POST /api/v1/admin/community/restrictions/{restrictionId}/revoke` | POST | Community moderation | 200 revocation recorded; original restriction preserved in history |

## What needs correction or backend work

| Issue | Classification | Current truth and expected frontend action | Backend responsibility / phase | Evidence |
| --- | --- | --- | --- | --- |
| #7 | Frontend assumption guidance (not a proven client defect) | Do not build a hide/restore/delete control for reported published posts — reported posts remain visible by owner decision; moderation acts through report resolution and explicit publish/reject/restriction actions. | None — deployed truth (FUR-07) | [FUR-07 contract](../../../../internal/work/portal-api-reconciliation/contracts/fur-07-community-moderation.md) |
| #8 | Frontend assumption guidance | Keep global user suspend/block under SuperAdmin-only user management; Community restriction routes never invoke Identity status changes and must not be presented as a global ban. Display counters as persisted values, never fabricated zeroes. | None — deployed truth (FUR-07) | [FUR-07 contract](../../../../internal/work/portal-api-reconciliation/contracts/fur-07-community-moderation.md) |

## Implementation connection

| Issue | Contract | Implementation/revision | Verification | Deployment | Remaining action |
| --- | --- | --- | --- | --- | --- |
| #7/#8 | [FUR-07 contract](../../../../internal/work/portal-api-reconciliation/contracts/fur-07-community-moderation.md) | Community queue/detail queries, report intake/lifecycle commands, restriction commands, required reject reasons, Community migration `20261017154400_AddCommunityModeration`; `docs/app/community/writes.md` and Admin/App Community Postman collections updated | Strict build 0 warnings/errors; 153 focused Community/model tests; model/persistence readiness tests; Postman JSON/route mapping validated | Deployed `303a131` and verified; Community migration applied; PostgreSQL advisory-lock path not exercised live | Consume queue/detail/report/restriction routes; render persisted counters; never expose author contact data |

## Decisions and evidence requested

- Owner decisions: none for this topic; no new scope is requested.
- Frontend evidence: confirm the admin portal renders the moderation queue,
  filters, counters, reject reason codes and restriction presets against the
  deployed OpenAPI, and keeps author contact data absent from screens.
- Acceptance: additive moderation reads/actions plus the required reject-reason
  change; existing publish behavior, policies and global Identity suspension
  unchanged.
- Configuration/migrations: one additive Community migration authored and
  applied at the FUR-07 deployment; runtime configuration unchanged.
- Next action: owner forwards this notice at discretion; Mastermind review of
  the FUR-10 integration handoff. No frontend code defect is asserted.
