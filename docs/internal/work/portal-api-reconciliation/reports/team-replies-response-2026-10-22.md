# Team-facing response — frontend (Q9–Q15, WEB-01–16) and mobile (MOB-01–15)

**Date:** 2026-10-22
**Scope:** one answer for every supplied frontend question (Q9–Q15), frontend request (WEB-01–WEB-16) and mobile request (MOB-01–MOB-15), based on the accepted contract reconciliation of the team replies follow-up brief.
**Source baseline:** `6e9bba7f5ff90ffb7c7c742ee431aa2c03a1c664`.
**Release state:** the October 22 frontend/mobile follow-up batch was released (revision `82b7af65d7b4f4ae1c01a45fa3ad5dfe68864a1c`) — a receipt exists for that release. This document states the **current-code** contract observed in the source at the baseline above.

## How to read this

Each item carries a **classification** and an **evidence label**.

**Classifications**

- **Delivered / integration** — the backend contract exists and is described here exactly; the remaining work is on the client side (adoption or integration).
- **Documentation** — the behavior already exists; use the corrected statement here.
- **Approved gap (implementation in progress)** — a real missing capability that has been approved and is being implemented now. No date is promised.
- **Deferred decision** — deliberately not built, because a product or content decision is still open. The reason is stated; nothing is guessed.
- **Diagnostic evidence missing** — a client-side symptom was reported without the evidence needed to act. The exact evidence that would move it is listed; this is not an assignment of blame.

**Evidence labels**

- **source verified** — the statement matches the source read at the baseline above.
- **team claim (not re-verified)** — a claim that came from the supplied report and has no independent source backing in this slice.
- **not re-verified** — an existing release/deployment claim that this slice did not re-run.

## Important limitations (read before using any of this)

- These are **current-code contracts** at the baseline above. They describe what the code does and what the consumer guides say; they are **not** a live or deployment proof.
- **No authenticated or live behavior was re-exercised in this slice.** The presence of a route, an unmodified test, or an earlier release receipt does not, on its own, prove a live end-to-end behavior. Any item that needs a live check says so.
- The October 21 frontend report and the mobile report predate the October 22 release. Neither is production evidence and neither establishes a current regression.
- No secrets, environment values or internal targets appear here.

---

# Part A — Frontend questions (Q9–Q15)

## Q9 — Family-home sections: kinds, service targets, access
**Classification:** Delivered / integration. **Evidence:** source verified; access boundary proven by policy (and the E05 anonymous-401 release smoke; not re-verified here).

**Current contract**
- Route: `GET /api/v1/family/home/sections`; policy `FamilyAccess` (Normal JWT, `account_type = Family`). Anonymous → `401`; non-Family account → `403`; a caller who owns no family and is not a member of one (or a deleted family) → `404 Families.Family.NotFound`.
- Kinds are a closed registry: `banners`, `quick-links`, `featured-services` (enum names `Banners=1`, `QuickLinks=2`, `FeaturedServices=3`). Responses always use the kebab string key in `kind`.
- Targets are an allowlist only: `targetType: "services"` (whole catalog, no id) or `targetType: "service"` (one non-empty `targetServiceId`). There are no arbitrary URLs or free-form routes.
- A `featured-services` item carries the **live** catalog fields (`serviceNameArabic`, `serviceNameEnglish`, `serviceIconKey`, `serviceCaregiverType` — `1` medical, `2` companion) and never a price or availability.
- Ids in this surface are plain `Guid` strings (no `{ "value": … }` wrapper).

**What the team should do:** render the three kinds and the two target types as defined; treat a `featured-services` entry as a live catalog reference (renaming/re-iconing a service updates the app without republishing).
**Deferred:** any extra anonymous destination, or automatic time-based expiry of a section, is a **deferred decision** — it is not built and was not requested as a contract change.

## Q10 — Chat conversation scope and `bookingId`
**Classification:** Delivered / integration. **Evidence:** source verified.

**Current contract**
- `GET /api/v1/chat/conversations` list items carry the persisted `scope` and `bookingId` of the same row.
- `scope` is the repository-wide **numeric** enum: `Direct = 0`, `Booking = 1` — not a string.
- `bookingId` is `null` for a direct conversation and the booking id for a booking-scoped conversation.
- Full detail: `docs/app/chat/conversations.md`.

**What the team should do:** read `scope` as an integer and treat `bookingId` as nullable.
**Deferred:** a stay/room-type (or any booking) summary on the conversation list stays **excluded** — deferred decision.

## Q11 — Missed check-ins: profile-local day, cohort, no reminders
**Classification:** Delivered / integration. **Evidence:** source verified; anonymous-401 smoke from the E06 release (not re-verified here).

**Current contract** — `GET /api/v1/admin/elderly/check-ins/missed`
- Policy `ElderlyMedicationOperationalRead` (Normal `SuperAdmin`/`SupportAdmin`). No Admin write route.
- Required `startDate` and `endDate` (inclusive, at most 31 calendar days, not reversed); optional `elderlyId`; `page` (default 1), `pageSize` (default 20, max 100).
- Response: `{ evaluationOnUtc, items, page, pageSize, totalCount }`. Items contain only `elderlyId`, `familyId`, `localDate`, `dueOnUtc`.
- **`localDate` is the elderly profile's own local calendar date**, derived from that elderly's stored IANA `TimeZoneId` — not the server's UTC date and not a fixed Cairo date. `dueOnUtc` is that local day's next-midnight deadline converted to UTC.
- Eligible cohort: a non-deleted family plus an `Active` identity status. An expected day is reported missing only once its local deadline has passed and no check-in row records an answer by `evaluationOnUtc`.
- Error outcomes: `400 Families.AdminCheckIn.InvalidDateRange` (reversed/over-31-day range, or an end date of `9999-12-31`); `409 Families.AdminCheckIn.IndeterminateEvaluation`; `503 Families.AdminCheckIn.EvaluationUnavailable`. Missing dates return the standard model-validation `400`.
- Full detail: `docs/admin/elderly-check-ins.md`.

**What the team should do:** display `localDate` as the resident's local calendar day; do not re-derive it from UTC.
**Deferred:** medication reminders and incident resolution are **out** — deferred decision. No reminder producer exists.

## Q12 — Multi-account legal / help audience resolution
**Classification:** Delivered / integration — the contract exists at the baseline; the input report's "planned" wording is stale. **Evidence:** source verified and documented.

**Current contract**
- `audience` is an **optional** query parameter on `GET /api/v1/legal/privacy-policy`, `GET /api/v1/legal/terms`, and `GET /api/v1/help-center`.
- Value space is `LegalAudience`: `1` Family, `2` MedicalCaregiver, `3` CompanionCaregiver, `4` Elderly, `5` CareHomeOwner. This is a **separate value space** from `AccountType`: Care Home Owner is `5` here, not `8`. The enum name is also accepted (case-insensitive).
- Omitted + exactly one owned audience → that audience is selected automatically.
- Omitted + more than one owned audience → `400 Cms.Content.AudienceRequired`.
- Malformed or undefined value (non-numeric, out of range, or a comma list) → `400 Cms.Content.InvalidAudience`, checked first and for every account class, including admin tokens.
- Well-formed but unowned value, or a caller with no app audience (admin account types) → `403 Cms.Content.UnsupportedAudience`.
- Selection is claim-order independent; duplicate `account_type` claims are collapsed; Care Home Owner maps `AccountType 8 → LegalAudience 5` **by name**.

**Mechanism note (important for any future refactor):** the two `400`s are **controller-constructed** through `BadRequestWithCode` (`ApiControllerBase.cs:169-170` and `:204-205`) and are **not** rows in `ResultProblemDetailsMapper`. Only `Cms.Content.UnsupportedAudience → 403` is a mapper row (`ResultProblemDetailsMapper.cs:192`). Stating this prevents a future mapper refactor from silently changing the two 400 statuses.

**What the team should do:** adoption — send `audience` whenever the token owns more than one audience, and handle the two 400 codes and the 403 explicitly. No backend gap remains on this item.

## Q13 — Five Admin screens: field and role reconciliation
**Classification:** Delivered / integration for the four reads below; the **fifth screen is unresolved**. **Evidence:** source verified (ratings/visits also carry F5/F6/F7 release receipts, not re-verified here).

Four of the five screens map to real backend reads. Each route below is restricted to Normal `SuperAdmin` or `SupportAdmin` unless stated.

- **Support inbox** — `GET /api/v1/admin/support-tickets` and `/{id}`. Sender fields: a single `displayName` (English preferred, Arabic fallback, `null` when missing/anonymized) and a single first-registered `accountType` (numeric). Email and phone are **never** returned.
- **Ratings moderation** — caregiver `GET /api/v1/admin/caregivers/ratings` and care-home `GET /api/v1/admin/care-homes/ratings`. Item `moderationState` is a lowercase string (`visible`/`hidden`); the filter is `visible|hidden|all`; `familyId` is exposed; reviewer PII is withheld. The care-home read adds the unresolved-flag summary.
- **Admin visits** — `GET /api/v1/admin/care-homes/visits` and `/{id}`. `facilityName` and `familyName` are **single strings** (English-preferred with an Arabic fallback), **not** AR/EN pairs. Visitor name/phone, visitor count, the requesting family user id and the pending-decision expiry are never returned. (If the frontend expected AR/EN pairs, treat that as a display expectation, not a backend defect.)
- **Families register** — `GET /api/v1/admin/families` and `/{id}`. Read-only; no medical, contact or assessment data.

**Unresolved:** the request names a **fifth** screen. Only four map to a source read. **Action:** the owner/Mastermind must name the fifth screen; the backend field lists here cover the four identified reads.

## Q14 — Growth / user statistics
**Classification:** Delivered / integration for registration growth and live landing counts; **deferred decision** for any undefined "services growth" series. **Evidence:** source verified and documented (E01 receipt not re-verified here).

**Current contract**
- `GET /api/v1/admin/users/statistics` — SuperAdmin-only aggregate read. Returns `{ totalUsers, usersByStatus, usersByAccountType, newRegistrationsByMonth }`.
- `newRegistrationsByMonth` is the 12 latest Cairo calendar months (including the current partial month), as `yyyy-MM` buckets with counts; zero months included.
- Live landing counters come from `GET /api/v1/landing/stats` (`familiesCount`, `medicalCaregiversCount`, `companionCaregiversCount`, `approvedFacilitiesCount`, and the derived `totalCaregiversCount`).

**No services/metric time-series exists and none is approved.** **Action:** the owner must define the metric before any additional series.

## Q15 — Registration fields, weekday integers, reactivation / Draft-finance baseline
**Classification:** Documentation (fields + weekday convention); reactivation and Draft-finance are previously released. **Evidence:** source verified and documented (A6/A7 receipts not re-verified here).

**Corrected statements**
- `POST /api/v1/auth/register` accepts `arabicFullName`, `englishFullName`, `email`, `phoneNumber`, `password`, `accountType` (numeric enum). Public registration accepts `1` Family, `2` MedicalCaregiver, `3` CompanionCaregiver, `8` CareHomeOwner; Elderly (`4`) and all admin account types are rejected by validation with `400 Api.Validation.Failed`.
- Weekday integers are `System.DayOfWeek`: **`0` = Sunday … `6` = Saturday**. This is the convention in the caregiver schedule and the care-home visit settings.
- Reactivation of an account that does not meet activation readiness returns `409 Identity.Admin.ActivationNotReady`.
- Full detail: `docs/auth/registration-and-verification.md`, `docs/app/caregivers/schedule-and-availability.md`.

**No change** is made to reactivation or Draft-finance behavior in this slice; those reopen only on **fresh evidence** (see WEB-12).

---

# Part B — Frontend requests (WEB-01–WEB-16)

## WEB-01 — Adoption coordination for `LegalAudience = 5`
**Classification:** Delivered / integration. **Evidence:** source verified and documented.

Same contract as Q12. The `audience` query parameter is implemented at the baseline; Care Home Owner maps `AccountType 8 → LegalAudience 5` by name.

**What the team should do:** send `audience=5` (or the enum name) for an owner token, and handle `400 AudienceRequired`, `400 InvalidAudience` and `403 UnsupportedAudience`. The report's "planned, not deployed" wording is stale. The frontend decides which build ships the parameter.

## WEB-02 — Confirm the existing Landing shape
**Classification:** Documentation. **Evidence:** source verified and documented.

`GET /api/v1/landing/sections` returns published, visible sections in `displayOrder` order. Every section carries bilingual `arabicTitle`/`englishTitle`, optional `arabicBody`/`englishBody`, optional `imagePath`, `displayOrder`, the two resolved-ref arrays (`testimonials`/`faqs`) and the type-specific `contentJson`. `contentJson` is returned **raw**, exactly as stored. Blank nullable fields serialize as `null`. Testimonials resolve to real caregiver/family ratings; FAQ refs resolve live from active Help Center entries. Full detail: `docs/app/public/landing.md`.

**No change:** raw-vs-typed is a settled implementation choice.

## WEB-03 — Bilingual nested-text contract delta + concrete AR/EN examples
**Classification:** Documentation (nested text needs no server change); **deferred decision** on any authored copy. **Evidence:** source verified and documented.

**Current contract:** `contentJson` is opaque to the server — it is length-only validated (max 8000 characters) and stored/returned verbatim. Nested `{ar,en}` text objects already transport unchanged; the server interprets `contentJson` only for the `testimonials` and `faq` ref lists. **No server change is required for nested text.**

**What the team should do:** supply the concrete AR/EN examples and any client-side delta. **No copy is generated or translated by the API.** (A documentation note on the Landing page about verbatim nested text is tracked separately.)

## WEB-04 — Seed script `scripts/seed-landing.mjs`
**Classification:** Diagnostic evidence missing. **Evidence:** none in this repository.
**What would move it:** the frontend supplies the script's path and source, or confirms it lives outside this workspace. No such script or Landing seed repository exists here.

## WEB-05 — Dry-run payload per section for all 11 stored types
**Classification:** Documentation. **Evidence:** source verified and documented.

**All 11 stored type keys** (plus the virtual `stats`, which is never listed in the sections read): `hero`, `partner-logos`, `about`, `features`, `activity-cards`, `steps`, `dashboard-promo`, `testimonials`, `faq`, `contact-form`, `footer`.

`contentJson` meaning per type: every type accepts free-form JSON stored/returned unchanged, except `testimonials` and `faq` (a `{"refs":[…]}` object resolved live), and `contact-form` (field configuration whose submissions use the shared support flow). No fixtures are authored here. Full table: `docs/app/public/landing.md`.

## WEB-06 — Remaining hardcoded navbar/SEO/footer AR/EN + original SVGs
**Classification:** Diagnostic evidence missing / deferred decision. **Evidence:** none available (no live SVG render was available as evidence).
**What would move it:** the frontend supplies the current revision, the authored AR/EN strings, and the original SVG assets. There is no backend contract for these; they are frontend assets.

## WEB-07 — `/choose-type` product detail
**Classification:** Deferred decision. **Evidence:** no such route in source.
**Why deferred:** the product detail (choices, mappings, auth, screenshot) is unresolved. No backend route is invented.

## WEB-08 — Public legal audiences / owners / texts (T9-B)
**Classification:** Deferred decision. **Evidence:** no source decision.
**Why deferred:** T9-B (public legal content) was deliberately **not** delivered, pending the owner's answer on **which `LegalAudience` member the public pages use** (or a new "Public" member, which implies a domain change plus a migration) and **who authors the AR/EN content**. No audience was guessed. The authenticated legal read (Q12/WEB-01) is unaffected and already shipped.

## WEB-09 — Real partner testimonials
**Classification:** Deferred decision (optional, product-owned). **Evidence:** source verified.

Testimonials resolve from real caregiver/family ratings; there is no partner domain in source. **What the team should do:** only if the owner requests partner testimonials, supply the consent, source and role. No supplier domain is invented.

## WEB-10 — Contact-form reply promise + hardcoded company/city source
**Classification:** Documentation + **approved gap (implementation in progress)**. **Evidence:** source verified and documented.

**Released contract:** `POST /api/v1/partner-contact` is anonymous and its destination is the admin support inbox. The current request carries `name`, optional `company`, `email`, optional `subject`, `message`, plus a required `Idempotency-Key` header. There is **no `phone`, `companyType` or `city`** in the released request, and reply handling/reply-time promise is not modelled.

**Approved gap — in progress now:** optional `phone`, `companyType` and `city` are approved and are being implemented; `company` stays separate from `companyType`. **No date is promised.** Until that ships, the released contract above is authoritative.

**Deferred decision:** the contact **reply-time promise** remains a product decision (frequency/an owner-facing promise), not backend authority.

## WEB-11 — Acknowledge ISR accepted
**Classification:** Documentation (no action). **Evidence:** source verified and documented.

The client uses an ~60-second ISR window. The endpoint itself is per-request and has **no revalidation webhook**. Acknowledged; no backend change.

## WEB-12 — Reactivation / Draft-finance only on fresh regression
**Classification:** Diagnostic evidence missing. **Evidence:** none supplied.
**What would move it:** current, reproducible evidence — the method/path, request and response, status code, a trace id and the revision — from the current build. If that arrives, the item reopens; it is not forced here.

## WEB-13 — Supplier count omitted
**Classification:** Delivered / integration (deferred by design). **Evidence:** source verified and documented.

`GET /api/v1/landing/stats` has no supplier field; there is nothing to display. **What the team should do:** omit the supplier card/count rather than show `0`. Making it visible is a product/content decision.

## WEB-14 — `next/image` config + relevant build failures
**Classification:** Diagnostic evidence missing. **Evidence:** none available (frontend-only; no backend contract).
**What would move it:** the frontend supplies its build configuration and **only** the relevant failures. No backend action.

## WEB-15 — Owner-portal field mismatches + role issues
**Classification:** Delivered / integration (the fields exist) + documentation. **Evidence:** source verified (F2/F3 receipts not re-verified here).

**Actual owner-portal fields on the owner bookings row:** resident snapshot `elderlyArabicName`/`elderlyAge`; responsible contact `responsibleContactName`/`responsibleContactPhone`/`responsibleContactRelationship`; `careNeedsNotes`; and `decisionHoldUntilUtc`, returned **only** while the booking is `PaidAwaitingDecision` and `null` otherwise. The facility profile read returns `openRevisionId` (the current editable revision, else `null`). The owner routes are policy-scoped (`CareHomeOwnerAccess`); non-owner roles — including `SuperAdmin` — are rejected on the owner surfaces.

**What the team should do:** send a fresh mismatch report (page, exact payload, expected vs actual, revision) for anything that still disagrees. None has been supplied, so nothing is changed here.

## WEB-16 — Admin UI field→UI mapping per screen
**Classification:** Diagnostic evidence missing (frontend-owned). **Evidence:** source verified.

The backend DTOs exist (see Q13 for the per-screen field lists). The field→control/label/required mapping is a frontend deliverable. **What would move it:** the frontend returns its mapping for the identified screens.

---

# Part C — Mobile requests (MOB-01–MOB-15)

## MOB-01 — Payment-failure diagnostics
**Classification:** Diagnostic evidence missing. **Evidence:** source verified for the public status fields; the on-file mobile codes (`JRFP`/`JRFL`) are a team claim.
**Current contract:** `GET /api/v1/platform/status` exposes `availablePaymentMethods`; the payment-intent gate can still reject with `400 Platform.PaymentMethodUnavailable`, `503 Platform.Maintenance`, or `503 Platform.ControlStateUnavailable`.
**What would move it:** the mobile build/revision, a UTC timestamp, the redacted request and response, the flow/plan id and the selected method.

## MOB-02 — Newer evidence only
**Classification:** Diagnostic evidence missing (optional). **Evidence:** team claim.
The on-file codes `JRFP`/`JRFL`/`JRHN`/`JRHD` are sufficient. **No replay is required.**

## MOB-03 — Visit-failure diagnostics
**Classification:** Delivered / integration (root cause explained) + diagnostic evidence missing (mobile capture). **Evidence:** source verified.

A resident care-home visit create returns `404 CareHomes.Visits.NotFound` when the elderly has no eligible accepted / paid / not-checked-out stay at that facility on the visit date. **What the team should do:** use the eligible-residents read (MOB-06) and supply both the canonical code and the human-readable error.

## MOB-04 — Identifier-handling rule
**Classification:** Documentation (no reply needed). **Evidence:** mobile report claim.
Redacted/synthetic stable correlations are the right channel; no private channel is provisioned. **No action.**

## MOB-05 — Booking facts the app can see
**Classification:** Documentation (fields exist) + diagnostic evidence missing. **Evidence:** source verified.

The care-home booking response/list row carries status, `paymentStatus`, start/end dates, check-in/check-out facts, and `holdUntilUtc`/`decisionHoldUntilUtc`. **What would move it:** mobile supplies screenshots; there is no DB access in this slice.

## MOB-06 — Eligibility selection
**Classification:** Delivered / integration. **Evidence:** source verified.

`GET /api/v1/family/care-home-visits/eligible-residents` (policy `FamilyAccess`) is the correct source. It returns the same resident set the create path accepts, in a deterministic order, one booking per resident. **What the team should do:** switch to this read — it also explains the `404` in MOB-03.

## MOB-07 — Write hygiene / no blind retries
**Classification:** Diagnostic evidence missing. **Evidence:** none (client behavior).
**What would move it:** mobile describes its retry/backoff behavior and confirms it makes no blind writes.

## MOB-08 — Required-vs-optional fields; "missing" vs "empty"
**Classification:** Documentation (null semantics) + **deferred decision** on authored copy. **Evidence:** source verified.

The caregiver booking-detail additions are nullable/additive: `visitHistory` and `familyReview` come from real records; `upcomingVisits` is documented below. `null` means "nothing was recorded" — it is **not** an empty array. **Deferred:** the "missing" vs "empty" display copy is authored content, a mobile/product decision.

## MOB-09 — "Upcoming" scope semantics
**Classification:** Deferred decision (awaiting mobile confirmation) / approved behavior. **Evidence:** source verified.

The approved behavior is **the same booking only**. `upcomingVisits` is always `null`; no other or future booking is mapped as a visit and no synthetic schedule is produced. **What the team should do:** confirm the "same booking" meaning.

## MOB-10 — Author anonymity = null
**Classification:** Documentation (no reply needed). **Evidence:** source verified.

**Corrected statement:** Community posts, comments and replies each carry `displayName` (English preferred, Arabic fallback, `null` when the identity row is missing) and `avatarUrl` (**always `null` today** — Community has no publicly servable avatar). Posts carry **no `authorId` field at all**: an anonymous post surfaces no author identity (its `displayName` and `avatarUrl` are `null`). Comments and replies do carry a nullable `authorId`; an anonymous comment or reply — written by the anonymous post's author inside that post's own thread — has `authorId`, `displayName` and `avatarUrl` **all `null`**. The API never substitutes a placeholder name. Full detail: `docs/app/community/README.md`.

## MOB-11 — Library media-URL type error
**Classification:** Documentation (field name) + diagnostic evidence missing (media type/status). **Evidence:** source verified.

Public library reads expose `coverImagePath` — a **storage path/key**, e.g. `library/<file>.jpg` — not `coverImageUrl`, `thumbnailUrl` or `imageUrl`. Build the URL as `{baseUrl}/files/{coverImagePath}` and fetch it without an `Authorization` header; the API never rewrites the key into an absolute URL. **What would move it:** mobile supplies the HTTP status and content type it observes.

## MOB-12 — Subscription display copy AR/EN
**Classification:** Deferred decision (product/content). **Evidence:** source verified.

No bilingual copy fields exist. **Why deferred:** product authors the copy keyed to the plan version and benefit keys. No generated values.

## MOB-13 — App-rating feature checklist
**Classification:** Deferred decision (feature undefined; admin/retention unresolved). **Evidence:** source verified.

`POST /api/v1/feedback/app-rating` exists and is authenticated; there is no admin read or retention contract. **Why deferred:** product answers the checklist; no admin surface is built here.

## MOB-14 — `/choose-type` mobile applicability
**Classification:** Deferred decision (optional). **Evidence:** none (web-first).
**What would move it:** mobile states the navigation/deep-link applicability.

## MOB-15 — Per-flow payment availability
**Classification:** Deferred decision (per-flow signal is planned, not in this slice). **Evidence:** source verified.

**Current contract:** `GET /api/v1/platform/status` exposes only the **global** `availablePaymentMethods` and `subscriptionCardEnrollmentAvailable`; there is no per-flow field shipped. The intent gate still rejects per flow. **Why deferred:** a shared per-flow evaluator is a separate approved change.

---

# Appendix — Evidence still needed (consolidated)

| Item | What would move it |
|---|---|
| Q13 fifth screen | The owner/Mastermind names the fifth Admin screen |
| WEB-04 | The seed script's path/source, or confirmation it is outside this workspace |
| WEB-06 | Current revision + authored AR/EN + original SVGs |
| WEB-07 / MOB-14 | The `/choose-type` product detail / mobile applicability |
| WEB-08 | Which `LegalAudience` the public pages use + AR/EN content owners |
| WEB-12 | Current method/path/status/trace/revision evidence |
| WEB-14 | Frontend build config + only the relevant failures |
| WEB-15 | A fresh owner-portal mismatch report (page, payload, expected vs actual, revision) |
| WEB-16 | The frontend field→control/label/required mapping |
| MOB-01 | Build/revision, UTC timestamp, redacted request/response, flow/plan id, method |
| MOB-03 | Canonical + human-readable error capture |
| MOB-05 | Screenshots of the booking facts the app shows |
| MOB-07 | Retry/backoff description and confirmation of no blind writes |
| MOB-11 | HTTP status + content type observed |

**Deferred product/content decisions (not code):** public legal audience and content owners (WEB-08), contact reply-time promise, supplier-card visibility (WEB-13), partner testimonials (WEB-09), authored AR/EN copy (MOB-08, MOB-12), app-feedback admin/retention (MOB-13), per-flow payment signal (MOB-15), `/choose-type` (WEB-07/MOB-14), and any extra growth series (Q14).
