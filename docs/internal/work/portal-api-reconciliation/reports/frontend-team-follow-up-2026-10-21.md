# Frontend team — follow-up handoff (Admin / Care Home / Landing)

**Audience:** Frontend web team (Admin, Care Home Owner, and public Landing surfaces).
**Date:** 2026-10-21.
**Status: the plan is APPROVED — it is NOT implemented and NOT deployed.** No code,
config, migration, data, or deploy change has been made. **PROPOSED** = planned only.
**EXISTING** = source-inspected in the current codebase (read from source now); it
is **not** a newly verified live-deployment result. No delivery dates are promised.

This report is ready to forward as-is. It asks for the details we need from you to
finish the approved scope, and it records the parts we are deliberately **not**
building yet.

---

## How to read this report

- **EXISTING** = source-inspected in the current codebase — a description of the
  current behavior read from source, **not** a live-deployment verification.
- **PROPOSED** = planned/approved work, not yet built. Names for proposed payloads
  are descriptive, **not** frozen wire contracts.
- Each request has a **response ID** (`WEB-01` …), an **owner/team**, a
  **Required / Optional** flag, and a **links-to** reference.
- **Answer item-by-item**: `yes` / `no` / `not applicable`. For open items give the
  substantive detail requested. If blocked or negative, give the blocker and the
  minimum you need. Attach **redacted** evidence only.
- **Never send** tokens, secrets, keys, DB credentials, or personal identifying
  values. Synthetic or redacted samples only.

---

## 1. Current backend capabilities you can build against (EXISTING)

| Area | What source shows today | Notes |
|---|---|---|
| Landing sections | 11 stored section types + a virtual `Stats` read. Public section read returns bilingual title/body, **raw `contentJson`**, `imagePath`, plus resolved `testimonials` and `faqs`. | `contentJson` is returned as stored (raw). Resolved `testimonials`/`faqs` are separate read-side collections. **A missing/hidden section is not statically revived** — only published + visible sections are served. |
| Landing stats | Anonymous stats read exposes a live facility count (real count, not hardcoded). | Recompute on read. |
| Landing media upload | Admin upload accepts **JPEG / PNG / WebP**. | SVG is approved as a planned addition (WEB-06), not yet available. |
| Legal / help | Authenticated legal and help-center reads exist; they **derive** the audience from the caller. An **explicit `audience` query parameter is PLANNED, not deployed** (WEB-01). | See section 2. |
| Admin support inbox | Admin ticket list/detail exist; currently expose the sender's user id only. | Enrichment (display name + account type) is planned; per-screen mapping is WEB-16. |
| Subscriptions (admin/family) | Plan version + pricing/limits exist; **no bilingual name/description/benefit copy** yet. | Bilingual copy is planned (mobile report covers consumption). |

---

## 2. Audience behavior (source-inspected; your adoption is already committed)

- **Audience value `5` is `LegalAudience`, not `AccountType`.** `AccountType` uses a
  separate value space (`CareHomeOwner` = `8`). Do not treat them as the same enum.
- **You have already committed to sending `LegalAudience = 5` once the backend
  value is available.** That is recorded as **settled** — we are **not** asking you
  to re-confirm the decision. WEB-01 asks only for **adoption coordination**
  (which build ships it and any wiring on your side).
- Today the legal/help reads **derive** the audience; an explicit `audience` query
  parameter is **planned, not deployed** (WEB-01).
- **Multi-account behavior (approved, PROPOSED):**
  - `audience` **omitted** with **multiple** eligible audiences → coded
    `400 AudienceRequired`.
  - `audience` **omitted** with **exactly one** eligible audience → selected
    automatically.
  - An explicit `audience` that is not owned by the caller → `403`.

---

## 3. Deliberately NOT being built yet (DEFERRED — do not block your work on these)

| Item | Status | Why |
|---|---|---|
| Supplier count on Landing | **Deferred** | No supplier domain exists. **Omit the value** rather than showing `0`. |
| `/choose-type` backend | **Deferred pending your requirements** | We need the product detail in WEB-07 before design. |
| App-rating backend | **Deferred pending requirements** | Full checklist is in the mobile report (`MOB-13`). |
| Revalidation webhook / preview mode | **Later feature requests** | ISR is **already accepted** as the current approach (WEB-11) — no action needed here. |

---

## 4. What we need from you

| ID | Request | Owner/team | Required? | Links to |
|---|---|---|---|---|
| **WEB-01** | Adoption coordination for `LegalAudience = 5`: tell us which app/build will send it and any wiring needed on your side. (Your commitment to send value `5` is already recorded — no need to re-confirm that decision.) | Frontend web | Required | Brief T3 (audience determinism) |
| **WEB-02** | Confirm you consume the **existing** shape: bilingual title/body + **raw `contentJson`** + resolved `testimonials`/`faqs` — existing source evidence, **not** a new build request. Say if your parser expects typed JSON instead of raw. | Frontend web | Required | Brief T8 |
| **WEB-03** | Bilingual **nested** contract — **delta only**: the base single-language schema was already supplied in your report 2, so send only the **changes** needed for the nested bilingual shape, plus concrete AR/EN content examples. We will **not** guess a migration of your existing schema. | Frontend web + Backend | Required | Brief T8/T11 |
| **WEB-04** | Seed script: you referenced `scripts/seed-landing.mjs`. It is **not present** in this repo — supply its path/source (or an equivalent), or confirm it does not exist. | Frontend web | Required | Brief T8 |
| **WEB-05** | For the seed helper: a **sample dry-run payload per section** for **all 11** stored section types. **No JWT, no `--apply`, no force flags.** | Frontend web | Required | Brief T8 |
| **WEB-06** | Remaining **hardcoded** navbar / SEO / footer content and assets: exact repo revision, authored **AR + EN** texts, URLs, icon sets; **SVG originals/screenshots** where relevant. | Frontend web | Required | Brief T11 (site settings) |
| **WEB-07** | `/choose-type` product detail: available **choices**, **account destination mappings**, deep links, target audience, auth requirement, a **screenshot of the current page**, desired behavior, and whether each choice is **navigational** or needs **backend data**. | Frontend web (product) | Required | Brief T9; deferred until supplied |
| **WEB-08** | Public legal pages: the **actual intended audience**, the **AR/EN doc owners**, and the **authoritative texts**. Please do not ask us to pick an audience arbitrarily. | Frontend web + Product/Content | Required | Brief T9 |
| **WEB-09** | Real partner testimonials: **optional**, only if you explicitly want them. If yes, supply consent, source, and the partner's real role. We will **not** invent a paid supplier domain or anonymous testimonial roles. | Frontend web + Product | Optional | Brief T8/T9 |
| **WEB-10** | Contact form: expected **reply/handling** behavior (who owns replies and roughly how fast, in words). Also, if you hold hardcoded **company labels** or a **city** value, give the source. **Do not re-ask the destination** — it is settled as the existing admin support inbox. | Frontend web | Required | Brief T9 |
| **WEB-11** | **Acknowledge only:** ISR (≈60 s) is **already accepted** as the current Landing freshness approach — **no action needed**. Webhook-triggered revalidation and preview remain **later** feature requests with **no timeline**. | Frontend web | No reply needed | Brief T8 |
| **WEB-12** | Reactivation / draft-finance: send evidence **only if** you observe a **fresh** regression — method, path, status, response body, trace id, **UTC** timestamp, app/build or repo revision, and the synthetic fixture state. **Do not force a run.** Context: you reported the **new admin screens are not wired** while the **old flows were tested on Oct 11**; so route presence is **not** end-to-end behavior. Reference the **historical Oct-11 record** (not raised as a T1/T3 defect). | Frontend web | Conditional (fresh regression only) | Historical Oct-11 record |
| **WEB-13** | Supplier count: confirm you will **omit** it in the UI (deferred, no fake `0`) rather than showing a placeholder number. | Frontend web | Required | Brief T9/T11; O-2 |
| **WEB-14** | Images: send your `next/image` configuration (domains/loader) and **only** the build failures that are actually relevant. Not a broad restatement. | Frontend web | Optional | Brief T8 |
| **WEB-15** | Owner-portal: send **current** field mismatches and role issues **with fresh evidence** (page, payload, expected vs actual). Not a broad requirements restatement. | Frontend web | Required | Brief T4/T11 |
| **WEB-16** | Admin UI mapping (per screen, as previously requested): for each Admin screen already discussed, send the field→UI mapping — backend field, UI label (AR/EN), control type, required/optional, and the source endpoint. | Frontend web | Required | Brief T4/T11 |

---

## 5. Rules for evidence you send

- Redact anything personal or secret. Synthetic or redacted samples only.
- For any UI issue, include: page/route, the exact action, expected vs actual, and a
  redacted screenshot or payload.
- For any regression claim, include a timestamp in **UTC** and the **build/repo
  revision**.
- If an item does not apply, answer `not applicable` and say why in one line.

---

## 6. Single response template

Copy this block once per response ID:

```
ID: WEB-__
Answer: yes | no | not applicable | partial
Owner/team:
Details / requested fields / decision:   (substantive content for open items)
If no / blocked — reason and the minimum needed to unblock:
Redacted evidence (link, path, or inline redacted text — NO tokens/secrets/PII):
Timestamp (UTC) and app/build or repo revision:
```

Send all blocks back together; no live implementation has started, so this is a
requirements/handoff round only.
