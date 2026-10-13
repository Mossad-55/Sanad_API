# Chat Plus — Phase 2 evidence: pricing insights (code)

Base: `41f9c91`. Branch: `builder/chat-plus-production`.
Status: code + focused tests complete and committed. Guide + Postman were
authored separately (statically checked only) and need the Phase 6
verification pass before this phase is marked Done. Live PostgreSQL
aggregate equality stays a Phase 7 gate.

## Changed manifest (Phase 2, code)

- Families domain: `Booking.BookingAreaId/BookingServiceId` (nullable,
  immutable, both-together-or-neither with empty-GUID rejection).
- Families application: `CreateBookingCheckoutCommand` (+optional pair),
  checkout validation (shape → owning `Bookings.AttributionInvalid` 400;
  active + caregiver-selected via new `ICaregiverCatalogLookup` seam,
  optional so pre-existing handler constructions compile unchanged).
- Families infrastructure: `BookingConfiguration` (+columns, pair index),
  migration `20261013005844_AddBookingAreaServiceAttribution`
  authored-only.
- API: `CaregiversIntegration/CaregiverCatalogLookup.cs` (active catalogue
  + caregiver's own selections, single collapse to false), checkout
  request fields + passthrough, `CaregiverPricingInsightsController`
  (`GET /api/v1/caregiver/pricing-insights`, `CaregiverAccess`).
- Caregivers application: `Discovery/GetPricingInsightsQuery.cs` — one
  clock instant per query, `[t-90d, t]` inclusive on `CompletedOnUtc`,
  single bounded SQL pass (Completed + paid + in-window + pair + EGP +
  positive base fee), Hourly-only rating by booked hours with the
  overnight wrap (zero-length excluded), EightHourShift-only base fees,
  banker's rounding on hourly rates, N=5 distinct bookings per pool with
  independent nulls, `updatedOnUtc = t`, `InvalidFilter` for
  missing/malformed/empty/unknown/inactive ids.
- Tests: `ChatPlusPricingTests` (9 facts).

## Verification (executed)

- New: 9/9 pass (4-vs-5 independent nulls, inclusive/exclusive window
  edges + future exclusion, legacy/foreign-pair non-contribution,
  overnight rating, half-even rounding, unpaid/shift exclusions,
  filter codes, domain shape rules).
- Regression in-turn: Families 682 (+3 PG-only skips), Caregivers 768 —
  all pass. One domain defect found and fixed in-turn (private ctor
  dropped the new fields; locking test added).
- Migration generated via `dotnet ef` (additive nullable columns +
  index); model alignment implied by clean generation.

## Skipped / blocked (Phase 7)

- Direct-PostgreSQL aggregate equality, provider translation of the
  owned-snapshot/nullable-GUID predicates: need exact-target
  authorization; InMemory proves the calculation only.
- Guide + Postman verification against implementation (Phase 6).
