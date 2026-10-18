# App (Family/Caregiver/Elderly) — FUR-07 Community report intake — published-post reporting

Date: 2026-10-18 (Africa/Cairo). Phase: FUR-07.
Purpose: backend delivery/deployed notice; client adoption unconfirmed.
Audience: App — shared Family/Caregiver/Elderly clients only. Care Home Owner and
administrative web portal behavior belongs under Admin, not here.
Communication state: backend phase accepted and deployed at `303a131`; client
adoption/forwarding is unconfirmed.

No mobile frontend repository was inspected. No mobile code defect is claimed.
This notice communicates the deployed report-intake contract only.

## Baseline and authority

- Original intake and issue IDs: frontend intake items #7 (missing moderation
  inspection; reports are additional scope) and #8 (mixed statistics/reasons/
  blocking asks) from the A2 requested-endpoints table of the
  [intake audit](../../../../internal/work/portal-api-reconciliation/frontend-report-audit-2026-10-16.md),
  scoped to the app-facing report intake. Owner-approved decisions recorded in
  the contract: posts-only reporting and no hide/restore action for reported
  published posts.
- Canonical plan/approved contract: [FUR-07 community moderation
  contract](../../../../internal/work/portal-api-reconciliation/contracts/fur-07-community-moderation.md)
  (owner-approved 2026-10-17); [release
  receipt](../../../../internal/work/portal-api-reconciliation/reports/fur-07-release.md);
  [worker delivery](../../../../internal/work/portal-api-reconciliation/reports/fur-07-worker-delivery.md);
  review acceptance is recorded in the [Mastermind
  handoff](../../../../internal/Mastermind_Handoff.md).
- Implementation/deployed revision: `303a1319dd6da4752b727e93aec4dec0296b95d4`,
  pushed to `origin/main` and deployed to Production.
- Deployment evidence: [release
  receipt](../../../../internal/work/portal-api-reconciliation/reports/fur-07-release.md)
  — additive Community migration `20261017154400_AddCommunityModeration` applied;
  all 9/9 applied heads matched; host configuration fingerprints unchanged;
  standard verifier and anonymous smoke HTTP 200 passed through an SSH tunnel;
  strict build 0 warnings/errors and 153 focused Community/model tests accepted.
  The PostgreSQL advisory-lock path was not exercised against a live PostgreSQL
  server.
- Evidence limits: no live authenticated report-intake request or Bruno run;
  app clients' adoption of this notice is unknown. Frontend repositories
  inspected: none.

## What is correct and should remain

| Issue | Correct behavior/usage | Evidence |
| --- | --- | --- |
| #7 | An authenticated normal Community user can report a **published** post via `POST /api/v1/community/posts/{postId}/reports` with a `reasonCode` and optional `note`. Accepted reason codes: `Spam`, `HarassmentOrAbuse`, `HarmfulOrUnsafeContent`, `MisleadingOrFalseInformation`, `Other`; `Other` requires a note; notes are optional for the other codes and limited to 1000 characters. Anonymous reports are not accepted, and a user cannot report their own post. | [FUR-07 contract](../../../../internal/work/portal-api-reconciliation/contracts/fur-07-community-moderation.md); [community writes guide](../../../../app/community/writes.md) |
| #7 | One retained report per reporter/post pair: a repeat report returns the existing report without creating another row, and reports are not hard deleted. A report starts `Open` and is later resolved by a moderator as `ActionTaken` or `Dismissed`; resolution does not itself publish/reject content or restrict the author. | [FUR-07 contract](../../../../internal/work/portal-api-reconciliation/contracts/fur-07-community-moderation.md) |
| #7 | A reported published post **remains visible**. FUR-07 adds no hide, restore, delete or unpublish action, and counters shown to moderators are persisted values, never fabricated zeroes. | [FUR-07 contract](../../../../internal/work/portal-api-reconciliation/contracts/fur-07-community-moderation.md) |

| Route | Verb | Policy | Status behavior |
| --- | --- | --- | --- |
| `POST /api/v1/community/posts/{postId}/reports` | POST | Authenticated normal Community user; published posts only; no self-report; no anonymous | 200 retained report (`Open`); repeat request returns the existing report; unknown reason code / self-report / anonymous / unpublished post → standard API error |

## What needs correction or backend work

| Issue | Classification | Current truth and expected frontend action | Backend responsibility / phase | Evidence |
| --- | --- | --- | --- | --- |
| #7 | Client assumption guidance (not a proven app defect) | Do not hide, collapse or remove a post after your user reports it — reported published posts remain visible by design; moderation outcomes appear through normal moderation actions, not instant client-side removal. | None — deployed truth (FUR-07) | [FUR-07 contract](../../../../internal/work/portal-api-reconciliation/contracts/fur-07-community-moderation.md) |
| #7 | Client assumption guidance | Send at most one report per user per post; repeated reports are deduplicated server-side and return the existing report. Offer the five reason codes verbatim and require a note only for `Other`. | None — deployed truth (FUR-07) | [FUR-07 contract](../../../../internal/work/portal-api-reconciliation/contracts/fur-07-community-moderation.md) |

## Implementation connection

| Issue | Contract | Implementation/revision | Verification | Deployment | Remaining action |
| --- | --- | --- | --- | --- | --- |
| #7 | [FUR-07 contract](../../../../internal/work/portal-api-reconciliation/contracts/fur-07-community-moderation.md) | `CommunityPostReport` entity/commands, report intake/dedup/resolution, Community migration `20261017154400_AddCommunityModeration`; app Community Postman collection updated | Strict build 0 warnings/errors; 153 focused Community/model tests; model/persistence readiness tests; Postman JSON/route mapping validated | Deployed `303a131` and verified; PostgreSQL advisory-lock path not exercised live | Implement report reason codes and note rules; do not remove reported posts client-side |

## Decisions and evidence requested

- Owner decisions: none for this topic; no new scope is requested.
- Frontend evidence: confirm the app's report flow offers the five reason codes,
  the `Other`-requires-note rule, and one-report-per-post behavior against the
  deployed OpenAPI; do not assert mobile implementation without an app-side
  check.
- Acceptance: additive authenticated report intake on published posts only;
  no anonymous reports, no self-reports, no hide/restore, no client-visible
  counters.
- Configuration/migrations: one additive Community migration authored and
  applied at the FUR-07 deployment; runtime configuration unchanged.
- Next action: owner forwards this notice at discretion; Mastermind review of
  the FUR-10 integration handoff. No mobile code defect is asserted.
