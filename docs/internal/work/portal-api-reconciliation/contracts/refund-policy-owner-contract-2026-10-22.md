# Refund policy — Owner-approved requirements and source comparison

Audience: Mastermind and implementation/review workers. This is an engineering interpretation, not newly published legal text.

## Authority

- Original: `C:\Users\mosad\Desktop\AHLENA_Refund_Policy_AR.docx` (read, not modified).
- SHA-256: `e6d0be33c8ae48c10b97dad915a638476264a7379e9422cafb08e99a3ec7afc9`.
- The Arabic document is authoritative, as amended by the Owner's approvals below. Existing implementation does not override it.
- Effective date and corrected last-updated date: **2026-10-22**. The original last-updated year 2030 is a confirmed typo. Effective-date handling must not silently reprice historical bookings.
- Products are excluded from implementation scope. Equipment rentals remain included; absence of an existing rental model is not permission to drop them or invent a full commerce platform.
- Source text retains Egyptian consumer-rights protection; implementation must not describe the policy as eliminating those rights.
- Task states and implementation evidence belong only in the [active brief](../worker-prompts/team-reports-refund-policy-2026-10-22.md).

## Binding behavior

| Source clause | Locked requirement |
|---|---|
| 2: customer cancels care | At least 24 hours before visit start: full refund. Less than 24 hours before start: 50%. At/after start, or inability to access the recipient at the appointed time: no refund. Timing is relative to scheduled service, not provider acceptance. |
| 2: reschedule | Free at least 24 hours before start, subject to provider availability. |
| 3: multi-session care | Apply cancellation rules to each session; delivered sessions are non-refundable. Refund unused sessions at their allocated discounted package price, not a standalone undiscounted price. Future sessions cancelled at least 24 hours ahead receive full refunds. |
| 4: provider failure | Provider cancellation, no-show, or lateness exceeding 60 minutes without customer consent: full refund for that session. Customer may choose a replacement at no extra cost. |
| 4: Ahlena cancellation | Full refund for undelivered services, including safety cancellation. Owner amendment: force majeure also refunds undelivered services/unused sessions; delivered services remain payable. Rescheduling or credit requires customer choice. |
| 5: quality complaints | Report within 48 hours of visit. Review may award full/partial refund, credit or replacement. These are reviewed outcomes, not automatic full refunds. Safety concerns are reported immediately. |
| 6: care homes | Apply cancellation terms displayed on the facility page at booking time. If absent, clause 2 applies. Fees paid directly under the facility's own contract remain governed by that contract. Preserve booked terms. |
| 7: products | Explicitly excluded from this implementation scope by Owner. Do not implement product return flows. |
| 8: equipment rentals | Before delivery: full refund. After delivery: used days are payable; unused whole prepaid days are refunded on early return. Deposit returned within seven business days of receipt and inspection, less documented damage beyond normal wear. |
| 9: subscriptions | Applies to family premium and care-home listing subscriptions. Cancel renewal at any time; access continues through the paid period, with no subsequent charge. No ordinary current-period/partial-period or free/discounted introductory-period refund. Erroneous/duplicate charges refunded in full. Ahlena service shutdown refunds the unused portion. |
| 10: refund method | Original payment method by default; app credit only by customer choice. Cash refunds by customer-selected bank transfer, wallet transfer or app credit. Approved refunds processed within seven business days; bank/wallet settlement can take longer. |
| 11: support | Owner amendment: initial response to refund requests within two business days, not a guarantee of final resolution in two days. In-app support is the available channel while official WhatsApp/email are unconfigured. |
| 12: changes | Notify users of material changes before they take effect. Bookings/orders predating a change remain under the policy applicable when made. No retrospective recalculation of settled refunds. |

## Approved configurable support contract

- Reuse `GET/PUT /api/v1/admin/help-center/support-contact` and public `GET /api/v1/support/contact`.
- SuperAdmin and SupportAdmin must be able to manage these contact fields. Preserve existing ContentAdmin contact access unless an explicit subsequent decision changes it; do not broaden SupportAdmin access to FAQ/CMS writes.
- `supportPhone` is the configured support/WhatsApp contact; `supportEmail` is the support email. No additional duplicate settings store.
- No row, or both fields unset: **200** with `{ "supportPhone": null, "supportEmail": null }`, not 404, 204 or a zero-byte body. Preserve existing response metadata where applicable.
- Each value may independently be null. Validate supplied values; a malformed populated email/phone still returns a meaningful validation error. GET must not create a row.
- PUT is replacement semantics: null clears the corresponding value; document omission/null behavior explicitly. No fake contacts, secrets, SMS or email sending.
- Clients hide unset contact options and use in-app support. Policy publication replaces placeholders with the in-app channel until official contacts are configured. Changing settings must not silently rewrite signed/published legal history.
- General partner enquiries keep the earlier non-binding 'usually within two business days' expectation. Refund requests use the firm initial-response commitment.

## Confirmed source comparison (planning baseline `4a066b3`)

| Finding | Evidence | Consequence |
|---|---|---|
| **Conflict: caregiver family cancellation** | `src/Modules/Families/Domain/Sanad.Modules.Families.Domain/Bookings/BookingCancellationPolicy.cs`: full captured refund while less than 60 minutes since `ConfirmedOnUtc`, otherwise none; before acceptance full refund; only full/no-refund entitlement. | Replace for new-policy bookings with scheduled-start thresholds and partial amounts. Review before-acceptance timing too. Preserve old-policy dispatch for older bookings. |
| **Conflict: care-home fallback** | `src/Modules/CareHomes/Domain/Sanad.Modules.CareHomes.Domain/Bookings/CareHomeBooking.cs`, `CancelByFamily`: total amount before check-in, half after check-in. | Not the approved fallback; requires policy/terms snapshot and zero-refund cancellation state. `BeginRefund` currently refuses amounts <= 0. |
| Partial alignment: provider cancellation | Caregiver policy grants full captured refund for assigned caregiver cancellation; care-home `CancelByFacility` grants total amount. | Preserve correct amounts; audit no-show, delay consent, Ahlena and safety paths separately. |
| Partial alignment: family subscription cancellation | `SubscriptionCommands.cs` cancel-renewal handler invokes `FamilySubscription.CancelRenewal`; domain has paid-period end and renewal state. | Verify no later charge and retained access; this is not proof of duplicate-charge or shutdown refund support. |
| Support empty-state mismatch | `SupportContactCommands.cs` admin read returns `SupportContactNotFound`; update requires both fields. Public `PublicSupportContactQuery.cs` already returns a two-null object. `AdminHelpCenterController.cs` uses `CmsContent`. | Change admin empty state, nullable persistence/validation as needed, and narrowly scoped contact authorization. Preserve the public behavior already implemented. |
| New capability assessment required | Source name/symbol searches found no `Rental`, `EquipmentRental`, or `ListingSubscription` implementation. | Absence is not yet proven across aliases. Worker must inventory modules, controllers and financial entities; if absent, propose bounded architecture before writing a new domain. |
| Historical policy gap to assess | Caregiver cancellation policy stamps `CurrentPolicyVersion = 1` on cancellation decisions; its comment protects already-ended bookings. | A decision-time stamp alone does not establish booking-time policy selection for uncancelled legacy bookings. Audit persistence and migration strategy. |

## Final decision lock — all 30 recommendations approved by Owner

The Owner approved the complete numbered recommendation list before worker execution. This section supersedes earlier tentative recommendations in the brief/workflow. Do not re-ask these questions. Approval locks behavior; dispatch and release remain separately controlled.

| # | Locked decision |
|---|---|
| 1 | Africa/Cairo timezone; Sunday–Thursday business days, excluding official Egyptian public holidays. Persist UTC timestamps. |
| 2 | Initial-response clock starts at refund-request submission; seven-business-day refund-processing clock starts at approval. |
| 3 | Full refunds include all collected service amounts, platform fees and tax; no customer deduction of gateway fees. |
| 4 | Calculate in currency minor units (piastres for EGP), using consistent rounding; cumulative refunds cannot exceed captured funds less previous refunds. |
| 5 | Allocate discounted package price to sessions at purchase; allocations sum exactly to the paid package total. |
| 6 | Subscription shutdown refund uses unused duration of the current paid period and amount actually paid, capped by remaining refundable value. |
| 7 | No automatic retrospective change: preserve existing bookings' previous rules. Apply the new policy to bookings created after actual activation, separately recording the approved document date. |
| 8 | Rescheduling retains the original policy version. Separately purchased extensions snapshot the policy/terms applicable at purchase. |
| 9 | Care-home custom terms must be structured, displayed before booking and snapshotted. Prose alone does not drive automatic money calculations; use approved fallback until structured terms exist. |
| 10 | Facility changes apply only to future bookings, never existing booked terms. |
| 11 | Ordinary policy-based cancellations are automatic. Quality complaints, disputed no-shows/delay, damage and exceptional compensation require review. |
| 12 | SupportAdmin receives/investigates; SuperAdmin approves reviewed financial awards and exceptional adjustments. Both can manage support contacts. |
| 13 | Accept complaints submitted after 48 hours and flag them for late review; no automatic compensation promise. Safety reports remain available. |
| 14 | Record timestamps, reporting party, evidence and staff dispute decision for no-show, lateness, access and consent. Allegations alone cannot trigger irreversible transfers. |
| 15 | Explicitly chosen app credit is a recorded non-withdrawable balance usable for eligible Ahlena purchases. Implement a real financial capability, not only a refund label. |
| 16 | Cash/bank/wallet refund completion needs the customer's selected destination, transfer reference and completion evidence. Approval alone is not completion. |
| 17 | Ambiguous provider timeout outcomes are reconciled before retry to prevent duplicate refunds. |
| 18 | Staff coordinates equivalent replacement care with customer agreement and no extra charge; no automatic reassignment. |
| 19 | Rental days are 24-hour periods anchored to actual delivery. Started periods count as used; refund remaining whole prepaid periods. Display before purchase. |
| 20 | Deposit return deadline starts on physical return; inspection must finish within that seven-business-day window and cannot postpone it indefinitely. |
| 21 | SuperAdmin approves documented damage deductions; customer sees amount/reason and can dispute. |
| 22 | If rental capability is absent, implement a bounded equipment/rate, booking/payment, delivery, return, inspection, deposit and refund lifecycle. No product-sales or broader inventory platform. |
| 23 | Admin gateway card is read-only configuration/status; credentials remain in server configuration. No editable secrets or live-payment test button. |
| 24 | Restore archived library content to Draft, then require normal validation/publication. |
| 25 | FAQ removal uses deactivation, preserving references/history; no permanent-delete route. |
| 26 | Draft-facility finance returns 200 with empty/zero results when no transactions exist; retain ownership checks and show real transactions accurately if present. |
| 27 | Preserve orphan caregiver profiles and expose an admin-readable integrity flag; investigate before exact-record cleanup, never automatically delete. |
| 28 | Add compatible field-level library validation details while retaining the stable error code. |
| 29 | Financial/lifecycle tests use disposable local database and simulated gateway first. Shared-host fixtures require a separate exact approved write list. |
| 30 | Content/admin approve and publish legal text/assets; backend verifies reads/pages, not invented legal copy or replacement assets. |
| 31 | **Subscription proration floor: zero is acceptable.** An exact time-proration may compute a 0-piastre award (e.g., one second remaining); no minimum-award rule. Record the award as computed; never round up. |
| 32 | **Two new authorized photo routes approved** (as designed by S0 lane D4): (a) caregiver avatar download for eligible app users — never exposes raw private storage keys or identity/medical media; (b) community author avatar for non-anonymous posts/comments/replies with the existing anonymity enforcement unchanged. Both reuse the existing authorized-read pattern; no public key-to-URL mapping. |
| 33 | **Bounded Rentals module + Finance money-core placement approved.** New `Rentals` module strictly for clause 8/#19-22 (equipment/rate/booking/delivery/return/inspection/deposit/refund — no product sales, no general inventory); shared refund money core (policy versions, ledger, credit, complaint review, business-day calendar) lives in the Finance module per S0 design. Rentals gets its own migration slot. |
| 34 | **Avatar parity everywhere (extends #32): raw storage keys never on the wire.** One rule on every surface: the authorized download *path* where an approved route exists, `null` otherwise. Chat conversation participants: personal avatars become `null` (no authorized cross-user identity-avatar route exists; the care-home owner's facility cover path stays — it is already authorized). Family member profiles: avatar becomes `null`. Identity self-profile: `GET /api/v1/auth/avatar` serves the caller's own avatar, so the profile wires that *path*, never the key. A general cross-user identity-avatar route (which would restore chat/family avatar images) is a **separate future Owner decision** — flagged, not assumed. |
| 35 | **RefundLedger `NetRecorded` counts only money-movement entries.** Requested and TransferInitiated entries are informational (no money has moved) and are excluded from the net that enforces "never exceed captured funds less previous refunds" (#4); approves, confirmed transfers, posted credit, failures and ambiguous timeouts count. Rejects/reconciliations already carry zero amounts. |
| 36 | **Refund business-day calendar includes official Egyptian public holidays (extends #1's clock rule).** In addition to the Sun–Thu weekend: fixed-date holidays (Coptic Christmas Jan 7, Jan 25, Sinai Liberation Apr 25, Labour May 1, Jul 23, Armed Forces Oct 6); Coptic feasts via the Julian computus (Coptic Easter + Sham El-Nessim the following Monday); Islamic feasts read from published lunar tables (`UmmAlQuraCalendar`): Eid al-Fitr (Shawwal 1–3), Arafat Day + Eid al-Adha (Dhul-Hijjah 9–12), Hijri New Year, Mawlid. **Moon-sighting caveat documented:** government announcements may shift a computed lunar date by about a day; the announced date always wins via an announced-holiday override seam filled at startup. |
| 37 | **Finance money columns standardized to `numeric(18,2)`**, matching the Families money-column convention (piastre-exact EGP). Applied to the refund ledger amount, the credit balance, their migration and snapshot **before** the migration is ever applied (migration is files-only so far). |

### Owner clarification #38 — retained care-home cancellation money

Money retained under the approved care-home cancellation policy for an undelivered stay belongs exclusively to the platform. The facility receives no share of that retained cancellation money. This includes both a partial-refund cancellation and a zero-refund cancellation where no stay was delivered. For example, on an EGP 1,000 payment with an EGP 500 refund, the retained EGP 500 is platform-only and facility entitlement is EGP 0; with no refund, the retained EGP 1,000 is platform-only and facility entitlement is EGP 0. These examples describe allocation, not a new tax calculation.

This clarification resolves the W3-F2 retained-money allocation question. Apply the booking-time policy/version rule in #7; it does not authorize retrospective changes or change payouts for delivered stays. Acceptance must verify platform accounting and exclusion from facility payables, including never-checked-in cancellation cases. Activation and release retain their separate authorization requirements.

## Remaining technical design work (not reopened product questions)

S0 must map the locked behavior to source, define exact file manifests, formulas/boundary examples and schemas, and identify real constraints. No currently identified Owner decision blocks S0. Escalate only a newly discovered material ambiguity or conflict; do not silently change approved behavior.

- Identify legacy rules and booking-time policy/version persistence without changing historical entitlements.
- Map structured facility terms, extensions, package allocation, holiday-calendar source and monetary rounding implementation to the locked decisions.
- Inventory credit, transfers, listing subscriptions, rentals, complaint and replacement workflows; define bounded architecture for missing capabilities.
- Design policy publication/version notifications without silently repurposing Privacy or Terms or assuming a new public document type is already approved.

## Required financial checks in implementation stages

- Exactly 24 hours, immediately below/above; exactly start; no-show threshold exactly 60 and greater than 60; timezone and monetary boundaries.
- Mixed completed/future/late-cancelled sessions and discounted allocation; sums never exceed captured funds less prior refunds.
- Repeated/concurrent cancellation and provider callbacks cannot duplicate refunds or credit; ambiguous provider outcomes must be reconciled, not retried blindly.
- Ledger, payout and tax effects remain consistent; test customer refund after provider payout and partial refund followed by another award.
- Snapshot/version behavior for pre-policy, post-policy and rescheduled/extended bookings; previously completed refunds remain unchanged.
- Subscription cancellation, duplicate charge and shutdown must have distinct reasons and outcomes.
- Real relational transaction checks on a verified disposable target for race-sensitive financial changes; no external charge/refund during ordinary automated tests.
