# Mobile team — follow-up handoff

**Audience:** Mobile app team.
**Date:** 2026-10-21.
**Status: the plan is APPROVED — it is NOT implemented and NOT deployed.** No code,
config, migration, data, or deploy change has been made. **PROPOSED** = planned only.
**EXISTING** = source-inspected in the current codebase (read from source now); it
is **not** a newly verified live-deployment result. No delivery dates are promised.

This report is ready to forward as-is. It asks for the diagnostics and product
details we need, and records what is deliberately **not** being built yet.

---

## How to read this report

- **EXISTING** = source-inspected in the current codebase — the current behavior as
  read from source, **not** a live-deployment verification.
- **PROPOSED** = planned/approved, not yet built; names are descriptive, **not**
  frozen wire contracts.
- Each request has a **response ID** (`MOB-01` …), an **owner/team**, a
  **Required / Optional** flag, and a **links-to** reference.
- **Answer item-by-item**: `yes` / `no` / `not applicable`. For open items give the
  substantive detail requested. If blocked or negative, give the blocker and the
  minimum you need. Attach **redacted** evidence only.
- **Never send** tokens, secrets, JWTs, signed/private URLs, DB credentials, or
  personal identifying values. Redacted or synthetic samples only.

---

## 1. Current backend capabilities (EXISTING)

| Area | What source shows today |
|---|---|
| Payment availability | Anonymous platform-status read exposes `availablePaymentMethods` (card/wallet). Intent flows apply a per-flow gate that can still reject. |
| Booking payment | Booking + subscription intents exist; a webhook endpoint receives provider callbacks. |
| Care-home visit | Family care-home visit create requires an **accepted, paid, in-window, not-checked-out** booking. |
| Caregiver booking detail | `GET /api/v1/caregiver/bookings/{bookingId}` returns a booking detail record. |
| Visit / medical records | `VisitReport` and `MedicalReport` records exist; a family visit-report read exists. |
| Reviews | A `BookingReview` record exists per booking. |
| Community | Posts, comments, replies, recommendations reads exist. Comment/reply reads expose an author id; posts expose an anonymity flag; **no author display name/avatar** yet. |
| Library | Category/content/detail/recommendation reads exist; covers are served via a public file path. |
| Subscriptions | Plan version + pricing/limits exist; **no bilingual copy** yet. |
| Payouts | Caregiver payout read and amount fields exist. Whether **unavailable amounts render as null is a PLANNED rule, not proven existing** (treat as PROPOSED). |

---

## 2. Approved changes that will affect the app (PROPOSED)

- **Payment truthfulness:** platform status and the intent gate will share one
  evaluator so availability can no longer contradict itself. No provider
  configuration values are exposed.
- **Caregiver booking detail** is planned to gain nullable `visitHistory`,
  `upcomingVisits`, and `familyReview`. `visitHistory` and `familyReview` come from
  **real implemented records** — `VisitReport` / `MedicalReport` / `BookingReview` —
  verified against the code at build time. **The `upcomingVisits` source is not
  agreed**: it depends on the scope answer in MOB-09. We make **no promise** to map
  other/future bookings as visits and no synthetic schedule.
- **Author fields** on community reads will gain a display name (+ avatar where
  real). Anonymous posts stay anonymous.
- **Subscription bilingual copy** will be added as version-owned fields.
- **Community blocking (approved, PROPOSED):** a **one-way, hide-posts-only** block
  owned by the Community domain. **Chat scope is unchanged.**

---

## 3. Supplied diagnostics already on file (captured 2026-10-20 UTC)

These are your own already-supplied **stable correlation codes** (not secrets), kept
so replies can be matched without re-sending anything:

- **Payment:** canonical response id **`JRFP`**; secondary prose response id
  **`JRFL`**.
- **Resident eligibility:** canonical response id **`JRHN`**; secondary prose
  response id **`JRHD`**.
- Shared trace prefix **`0HNPDUOAG`**, sequence **`:00000001`**.

We are **not** asking you to replay or duplicate the original logs — this evidence
is sufficient. Only include **newer** evidence if you already have it (MOB-02).

---

## 4. Deliberately NOT being built yet (DEFERRED — do not block on these)

| Item | Status |
|---|---|
| App-rating backend | **Deferred pending your requirements** (see `MOB-13`). |
| `/choose-type` backend | **Deferred pending requirements** (mobile applicability is optional — `MOB-14`). |
| Supplier count | Deferred; frontend omits it. Not a mobile item. |

---

## 5. What we need from you

| ID | Request | Owner/team | Required? | Links to |
|---|---|---|---|---|
| **MOB-01** | Payment failure diagnostics: your **current** app build + git/repo revision, backend URL, environment, and **UTC** timestamp; a **redacted** request/response pair with status + trace id; the **flow/plan id**; and the **payment method** used. | Mobile | Required | Brief T1 |
| **MOB-02** | Only **if newer evidence is already available**: send it (same fields as MOB-01/MOB-03). Do **not** replay or duplicate the 2026-10-20 logs — the on-file evidence above is sufficient. | Mobile | Optional | Brief T1 |
| **MOB-03** | Visit failure diagnostics: the same capture fields as MOB-01. **Preserve both** the canonical structured response **and** the secondary prose/error message, and explicitly flag any discrepancy between them. | Mobile | Required | Brief T2 |
| **MOB-04** | Identifier handling (**rule — no reply needed**): redacted/synthetic **stable correlation values** are fine and sufficient. Never send tokens, secrets, JWTs, or signed/private URLs. **No separate private channel is being provisioned** — redact in place. | Mobile | No reply needed | Brief T2/T5 |
| **MOB-05** | Booking facts you can see: status, paid, accepted, stay start/end dates, checked-out, and the **intended-start timezone**. Provide from **screenshots if accessible**; we are **not** asking for database access. | Mobile | Required | Brief T2/T5 |
| **MOB-06** | Eligibility selection: a screenshot/flow of how the user picks the **eligible resident** today. | Mobile | Required | Brief T2 |
| **MOB-07** | Write hygiene: describe the app's current retry/backoff behavior on failure, and confirm it does **not** blindly retry writes (duplicate submissions). | Mobile | Required | Brief T5 |
| **MOB-08** | Medical/booking UI: the **required-vs-optional field list**, any **existing endpoint/response mismatch**, a **redacted representative** example for booked status **and** consent state, and the desired copy for **"missing"** versus **"empty"**. | Mobile | Required | Brief T5 |
| **MOB-09** | Scope semantics: is **"upcoming"** the **same booking** or **other bookings**? State it explicitly. This gates the `upcomingVisits` source — we will **not** map future/other bookings as visits without your answer. | Mobile | Required | Brief T5 |
| **MOB-10** | Author anonymity = **null** (no identity revealed), no role expansion — **already agreed; no reply needed.** Recorded for completeness. | Mobile | No reply needed | Brief T5 |
| **MOB-11** | Library: exact **content ids**, the **media-URL type error**, player-platform **HTTP status + content-type**, and the **access pattern**. **No private/signed/token URLs** — redact secrets. A small **own/licensed** demo media sample is optional. | Mobile | Optional (sample) / Required (diagnostics) | Brief T5 |
| **MOB-12** | Subscription display copy: supply the **approved authored AR/EN** names, descriptions, and benefit copy **keyed to the existing plan version + benefit keys**. We will **not** generate financial values. | Mobile + Product/Content | Required | Brief T10 |
| **MOB-13** | App-rating feature — answer the full checklist in section 6. All of it is **undefined and deferred until you supply it.** | Mobile + Product | Required (to unblock) | Brief deferred (O-3) |
| **MOB-14** | `/choose-type`: primarily a web feature. State only whether the app needs any **navigation/deep-link** applicability; mobile work here is **optional**. | Mobile | Optional | Brief T9 |
| **MOB-15** | Confirm the app can consume the corrected **per-flow payment availability** signal once it ships (so the app stops showing a method the flow will reject). | Mobile | Required | Brief T1 |

---

## 6. App-rating checklist (MOB-13) — all undefined until supplied

Please answer each line; "undefined" is a valid answer.

1. **Surface:** OS store rating / deep link **vs** in-app feedback **vs** both?
2. **Identity/auth:** who may rate; is a signed-in account required?
3. **Scale & content:** star scale (e.g. 1–5), required text, optional categories/tags, attachments?
4. **Frequency:** one-time, multiple times, or update-an-existing-rating?
5. **Triggers:** what event prompts it, and at what point; is there an opt-out / "don't ask again"?
6. **Admin:** must admins read ratings, see status, take actions, reply, and send notifications?
7. **Privacy/retention:** anonymous option? retention period?
8. **Store links:** if you link out only, provide the **store URL / app ids**.

---

## 7. Rules for evidence you send

- Redact all personal and secret values; redacted or synthetic samples only.
- Redacted/synthetic **stable correlation values** (like the codes in section 3) are
  the right way to cross-reference requests. Never tokens, secrets, JWTs, or
  signed/private URLs.
- Always include **UTC** timestamp and the **app build / repo revision**.
- For any discrepancy, show both the canonical response and the prose message.
- If an item does not apply, answer `not applicable` and say why in one line.

---

## 8. Single response template

Copy this block once per response ID:

```
ID: MOB-__
Answer: yes | no | not applicable | undefined | partial
Owner/team:
Details / requested fields / decision:   (substantive content for open items)
If no / blocked — reason and the minimum needed to unblock:
Redacted evidence (link, path, or inline redacted text — NO tokens/secrets/PII/signed URLs):
Timestamp (UTC) and app build or repo revision:
```

Send all blocks back together. This is a requirements/diagnostics round only — no
implementation has started.
