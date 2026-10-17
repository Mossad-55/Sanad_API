# FUR-06 — Eligible caregiver payout selector

## Goal

Add the missing admin read that selects eligible unpaid caregiver bookings for
manual payout recording. The selector is additive to the existing admin payout
list, detail, record, and mark-failed operations.

## API and authorization

- Add a paged read endpoint under the existing
  `/api/v1/admin/caregiver-payouts` route, using the existing
  `PayoutOperationalAdmin` policy.
- Keep it read-only. It must not create/update payout records, call a payment
  provider, or change booking, account, or payout state.
- Return only the selector data needed to identify and record an eligible
  booking: booking ID, caregiver ID and existing safe display name, completion
  time, payout-eligible time, and the existing caregiver fee amount/currency.
  Do not return payout-account or IBAN data.
- Support the existing page/pageSize convention and optional caregiver filter;
  reject or normalize invalid paging consistently with adjacent admin payout
  reads.
- Document the concrete route, query names, response shape, permission, and
  errors to match the implemented behavior.

## Eligibility contract

Reuse the existing command eligibility and policy source. Include a booking
only when it is completed, has both completion and payment timestamps, its
effective payout delay has elapsed, the caregiver has a verified payout
account, and no `Paid` payout exists for that booking. A `Failed` payout does
not permanently exclude an otherwise eligible booking. Missing effective
policy fails closed using the existing payout-policy error behavior.

## Persistence and integrations

- No schema or migration change is approved for this phase.
- No provider call, fixture write, database reset, provider-state change, or
  deployment is part of the worker task.
- Read the existing Caregivers, Families booking, and effective Finance policy
  sources. Keep the endpoint/query read-only and follow existing module/API
  conventions.

## Verification and documentation

- Add focused automated coverage for eligibility inclusions/exclusions,
  failed-payout retry eligibility, missing policy, paging/filtering, and the
  existing admin authorization boundary as applicable to the implementation.
- Update the admin caregiver payout guide and the admin Finance Postman
  collection with the verified route and representative response.
- The worker must report changed files, commands and results, authored versus
  executed checks, migration/provider/state changes (expected none), findings,
  and blockers in a saved delivery report, then stop.

## Boundary with FUR-07

FUR-06 owns caregiver payout selector code, its focused tests, its admin guide,
and its request in the admin Finance collection. It does not own Community
moderation code, Community persistence, Community tests, or the admin Community
collection. FUR-07 remains a separate contract decision; parallel execution is
not authorized by this contract.
