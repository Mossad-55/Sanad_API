# <Audience> — <phase name> — <specific topic>

Date: YYYY-MM-DD (Africa/Cairo). Phase: <full phase ID>.
Purpose: <alignment / approved contract / implementation delivery / correction>.
Audience: <Admin: admin-portal / care-home-owner; App: family / caregiver / elderly / shared>.
Communication state: <draft / ready for owner forwarding / receipt owner-confirmed>.

## Baseline and authority

- Original intake and issue IDs: <links and identifiers>.
- Canonical plan/approved contract: <links; say not approved if applicable>.
- Source revision and relevant local delta: <SHA/description>.
- Deployment revision: <verified SHA/date/receipt, or unknown/not deployed>.
- Evidence limits: <source inspection, executed tests, frontend-reported only>.
- Frontend repository inspected: <yes with revision / no; do not claim code bugs if no>.

## What is correct and should remain

| Issue | Correct behavior/usage | Evidence |
| --- | --- | --- |
| <ID> | <supported behavior> | <exact guide/source/test> |

## What needs correction or backend work

| Issue | Classification | Current truth and expected frontend action | Backend responsibility / phase | Evidence |
| --- | --- | --- | --- | --- |
| <ID> | <frontend contract mismatch / backend defect / missing read / new feature / docs / data / decision> | <literal behavior; no invented response> | <exact slice or none; planned vs approved> | <link> |

## Implementation connection

| Issue | Contract | Implementation/revision | Verification | Deployment | Remaining action |
| --- | --- | --- | --- | --- | --- |
| <ID> | <approved link / pending> | <link + SHA / not implemented> | <executed command/results or not run> | <receipt / not deployed> | <actor + action> |

Future-work links may point to a plan now; add actual implementation/evidence only
when it exists. A passing test is not a deployment receipt. A new endpoint's final
contract must state method/path, auth/roles, request/response examples, statuses,
errors, paging/filter semantics and compatibility before frontend wiring.

## Decisions and evidence requested

- Owner decisions: <only decisions needed for this topic; recommendation explicit>.
- Frontend evidence: <method/route, timestamp + timezone, selected role/account,
  sanitized payload/response, trace ID and revision; never tokens or credentials>.
- Acceptance: <observable frontend/backend conditions and who verifies them>.
- Configuration/migrations: <none / exact delta / not yet determined; disclose before release>.
- Next action: <one responsible actor and one bounded action>.
