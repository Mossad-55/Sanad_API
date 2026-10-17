# FUR-05 — Owner settlements contract

**Status:** Owner-approved 2026-10-17.
**Scope:** Read-only owner settlement ledger over existing Care Home payout truth.

## Goal

Give a Care Home Owner a facility-scoped view of currently eligible unpaid
balances and already recorded transfers. The response reuses the existing
Admin payout calculation and must not treat customer collections as owner
income or move financial calculation into the frontend.

## Route and authorization

- `GET /api/v1/care-homes/payouts/mine`
- Require `CareHomeOwnerAccess`.
- Resolve the facility from the authenticated owner, following the existing
  owner finance read pattern. Accept no facility ID or other caller-controlled
  scope parameter.
- If the authenticated owner has no facility, return the existing safe
  `404 CareHomes.Bookings.NotFound` result used by owner finance reads.
- Return `200` with an empty array when the owner has a facility but no
  settlement rows.

## Response and row membership

- Return the existing `IReadOnlyList<CareHomePayoutLedgerItem>` response shape:
  booking/facility IDs, stay dates, remaining customer amount, gross, fee rate
  and amount, net payable, nullable payout ID, facility debt, currency,
  nullable transfer reference and nullable recorded timestamp.
- Include every already-recorded payout, including one with subsequent debt.
  A non-null `payoutId` identifies a recorded transfer.
- Include an unpaid row only when it currently satisfies the existing completed
  stay, paid/refunded payment, completed checkout, refund-state and positive
  remaining-amount eligibility checks and its calculated `netPayable` is
  positive. A null `payoutId` identifies an eligible unpaid balance.
- Exclude non-completed/unpaid stays, pending refunds, fully refunded or
  otherwise ineligible rows that have no recorded payout. Do not return
  zero-payable non-payout rows.
- Preserve existing ledger ordering by checkout time descending. No new
  pagination or filters are introduced in this phase.
- Calculate payable, fee and debt values using the existing application
  calculations and persisted payout/debt snapshots. When an eligible unpaid
  balance needs a Finance rule but none is effective, preserve the existing
  fail-closed `CareHomes.Payout.FinanceConfigurationMissing` behavior.

## Exclusions and invariants

- Read only: no payout/reversal command, transfer/provider call, file access,
  write, audit mutation, or ledger/debt creation.
- Do not change payout eligibility, Finance fee/refund/debt calculations,
  recorded settlement state, currency convention, or Admin authorization and
  routes.
- Do not expose evidence, reason, or recording actor fields that are not part
  of the existing ledger read model.
- No migration, configuration, provider, or database-target change.

## Acceptance evidence

- Focused tests prove owner-to-facility scoping and foreign-facility exclusion;
  included eligible unpaid and recorded payout rows; debt visibility after a
  recorded payout; exclusion of ineligible unpaid rows; safe no-facility and
  empty results; and no state mutation.
- Authorization/route metadata proves owner policy and exact route, with no
  caller-supplied facility filter.
- Update the Care Homes finance guide and Care Homes Finance Postman collection with the
  actual route, response and authentication requirements. Parse the modified
  Postman JSON.
- Report exact changed files, commands/results, tests, limitations and whether
  any database, migration, provider or runtime was touched. No such stateful
  target is authorized by this contract.
