# Owner decision menu — open items after the mobile team's reply

Date: 2026-10-22. Source: the mobile team's response to the October 22 report + previously deferred items from the team-replies slice. Each item: context, options, recommendation. Nothing here is implemented without your choice.

---

## 1. MOB-08 — proposed display texts (client copy)

**Context:** the mobile team proposed five Arabic user-facing strings distinguishing our documented semantics (null = nothing recorded; `[]` = empty list; permission-denied vs not-shared are independent). They are display copy in the mobile app, not backend content.

**Options**
- **A — Approve the five strings as proposed.** Mobile applies them; no backend change. Zero cost.
- **B — Approve with edits** (tone/wording changes you want). Mobile applies the edited set.
- **C — Reject and re-spec.** Sends the decision back to them.

**Recommendation: A.** The five strings map exactly to the semantics we documented and the mobile team verified; they never equate missing data with "empty" or coerce unavailable amounts to zero. Approving as-is closes the item today.

---

## 2. MOB-12 — subscription plan copy (AR/EN)

**Context:** the bilingual plan fields (names, descriptions, feature lists per plan version + feature keys) were released with T10; only authored content is missing. Mobile is blocked only on adoption after content exists.

**Options**
- **A — Content authors AR/EN now** against the current plan versions and existing feature keys; you (or the content owner) deliver the approved text to whoever manages plan content in the CMS.
- **B — Defer authoring** until a pricing/plan restructure lands (risk: authoring twice).
- **C — English first, Arabic later.**

**Recommendation: A (with a note).** The fields are live and verified; author once now. Drop C — mixed-language catalogs were exactly what both teams complained about. If a plan restructure is imminent, author only names + short descriptions now and defer feature-list prose.

---

## 3. MOB-13 — app-rating policy

**Context:** `POST /api/v1/feedback/app-rating` exists, is authenticated, accepts rating 1–5. No policy around it. Three sub-decisions:

**(a) Resubmission**
- **A — Allow a new submission anytime** (current behavior: each POST is a new row; client already expects this). Zero backend work.
- **B — One-per-account, update-in-place** — small backend slice (update semantics + migration).
- **C — One-per-account, immutable.**

Recommendation: **A** now. If rating churn becomes noise later, upgrade to B deliberately.

**(b) Admin surface**
- **A — No admin surface**; treat as product telemetry (readable in the database when needed).
- **B — Add an admin read** (ratings list/average per day) — small additive slice.

Recommendation: **A** initially. Build B only if Product actually wants a dashboard.

**(c) Retention / privacy**
- **A — Attributed + retained** (each rating stored with the authenticated account; no anonymous option; no deletion flow). Document this in the consumer guide.
- **B — Anonymous option** (nullable identity) — backend change.
- **C — Retention window** (e.g., auto-purge after N months) — backend job + policy.

Recommendation: **A**. Ratings are low-sensitivity, authenticated, non-public. Revisit if a platform-wide data-retention policy is ever adopted.

---

## 4. MOB-07 — duplicate-visit risk (new finding from the mobile reply)

**Context:** mobile's auth layer retries a failed request once after a `401` + token refresh — including writes. I verified `CreateCareHomeVisitCommandHandler` has **no server-side idempotency** (only a capacity guard), so a retry can in theory create the same visit twice.

**Options**
- **A — Accept the risk short-term; fix it client-side.** Mobile stops auto-retrying non-idempotent writes on auth-refresh (retry reads only; on uncertainty show guidance to check visits). Zero backend work, closes the theoretical window in the one client that has it.
- **B — Add `Idempotency-Key` to visit creation** (like partner-contact). Small additive slice: header + dedup storage (additive migration) + 200-same-receipt / 409-on-different-content semantics, tests, docs. Robust for every client, present and future.
- **C — Implicit soft-dedup** (same family + elderly + facility + start time → return existing). No header, but surprising semantics for genuinely distinct visits at the same slot; not recommended.

**Recommendation: A now, queue B as a small hardening slice.** A removes the only known duplicate path today at zero cost; B is the durable fix and is worth doing when convenient (it also protects the future web frontend writing visits). Skip C.

---

## 5. MOB-15 — per-flow payment availability signals

**Context:** current contract: `availablePaymentMethods` is global; `subscriptionCardEnrollmentAvailable` is a separate signal; per-flow availability is not shipped. Mobile acknowledged this and will not wire anything until a final contract exists.

**Options**
- **A — Do not ship per-flow signals.** Global methods + enrollment signal remain the contract; mobile's only work is reading `subscriptionCardEnrollmentAvailable` (they already flagged this as their adoption point).
- **B — Ship per-flow availability** for specific flows (e.g., subscribe vs pay-per-visit): additive fields on those flow responses; needs your input on which flows differ and how.

**Recommendation: A.** There is no current product evidence that methods genuinely differ per flow. Choose B only when a real business requirement appears (e.g., wallet accepted for visits but not subscriptions); it is a small slice when needed.

---

## 6. WEB-08 / T9-B — public legal content (the one remaining deferred feature)

**Context:** the public website needs some legal content without login. Today `LegalAudience` has no public member — nothing legal is published anonymously, by design, pending this decision.

**Options**
- **A — Minimal public set, no approval workflow.** Add a `Public` audience; publish Terms of Use + Privacy Policy (and optionally a Help intro) to everyone; authenticated audiences keep the existing 8→5 mapping; CMS admins author and approve as today. Small, bounded slice (audience enum + endpoint semantics + docs).
- **B — Full public legal center** with per-document audience choice including Public, plus a formal content-ownership/approval workflow (Content role, publish states). Bigger product + backend surface.
- **C — Keep everything authenticated**; the public site stays without Terms/Privacy (a launch blocker for most public web properties).

**Recommendation: A.** Every public web property needs Terms + Privacy visible without login; A delivers that with the smallest surface and reuses the existing CMS authoring path. Revisit B only if you plan a large public content operation with non-admin editors.

---

## 7. Contact reply promise

**Context:** partner-contact/support messages create an inbox row and (for support) a reply flow; no time expectation is promised anywhere, deliberately.

**Options**
- **A — Keep honest silence** (current state; nothing promised).
- **B — Documented non-binding expectation**, e.g. "we usually reply within 2 business days" in the consumer docs and the frontend/mobile contact confirmation screens. Zero backend work; wording is yours.
- **C — Enforced SLA** (aging indicators, escalations in the admin inbox) — real backend slice.

**Recommendation: B with "2 business days, non-binding"** if you are comfortable setting that expectation — it answers a recurring team question cheaply. If unsure, stay on A; C only makes sense once inbox volume justifies workflow tooling.

---

## 8. Supplier card visibility (WEB-06/07)

**Context:** what the public care-home card shows before the full profile. The frontend asked; no specific field list was supplied.

**Options**
- **A — Keep the current public card as-is** (discovery fields already released: identity, cover, room types, published pricing data as documented).
- **B — Expose more marketing fields** (rating, region highlights, etc.) — needs a specific field list from the frontend, then a small additive slice.
- **C — Reduce** what is public.

**Recommendation: A.** No change without a concrete frontend requirement; inventing fields without their card spec wastes scope. Ask the frontend team to send the exact fields their design needs as fresh evidence (same discipline as WEB-15).

---

## 9. Q13 (fifth Admin screen name) and WEB-15

Not yours unless you want them to be: both wait on the frontend team (Q13 = what to call the fifth admin screen; WEB-15 = fresh evidence on the owner-portal field mismatches). You may simply **answer Q13 yourself** if you know the screen's intended name.

---

## Recommended decision set (if you want the fast path)

Approve **1A**, **2A**, **3A/A/A**, **4A (queue 4B)**, **5A**, **6A**, **7B**, **8A** → zero unplanned backend work except two small optional slices (4B visit idempotency, 6A public legal) which I can plan on your word.
