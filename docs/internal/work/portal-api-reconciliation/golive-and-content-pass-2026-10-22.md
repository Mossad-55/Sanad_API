# Go-live & Content pass — handoff package (Owner-ordered, 2026-10-22)

Two assignments, both approved by the Owner. Nothing here needs backend work — the code is live (release `1e3d2b0`).

---

## 1. Publish the public legal pages (admin action — go-live)

**Who:** a SuperAdmin or ContentAdmin using the CMS legal admin (`api/v1/admin/legal-documents`, or the admin UI over it).
**What is live already:** anonymous `GET /api/v1/public/legal/{documentType}` (documentType = `1`/`PrivacyPolicy`, `2`/`TermsAndConditions` — numeric or name). Until a document with audience **Public** is **Published**, the public pages answer `404 Cms.Legal.NotPublished` (verified live 2026-10-22).

**Steps:**
1. Review the legal text that should be world-readable (this is the actual business/legal review — the API does not produce content).
2. In the legal admin, create (or set the published document's audience to) **Public = 0** for `PrivacyPolicy` and for `TermsAndConditions`, then **Publish** them.
3. Publishing a Public document makes it readable by **anyone, with no token, immediately** — treat this as a go-live action (the admin doc carries the same warning).
4. Tell the Mastermind when done — the public endpoints will be re-verified live (expected: `200` with the bilingual published sections for both types) and the receipt amended.

**Effect once done:** the public website can legally render Terms + Privacy without login. Help Center remains authenticated — unchanged.

---

## 2. Content pass (authored copy — the API never generates it)

**Who:** Content (with the admin plan-version fields for entry).

**a) Subscription plan copy (AR + EN):**
- Authored **names, descriptions, and feature lists** for the current plan versions, in both Arabic and English.
- Entered via the admin plan-version fields (released with the T10 bilingual fields); tied to the **real plan versions and existing feature keys** — never invented by the backend.
- Constraints: display-only copy; it must not contradict price/cycle/limit fields.

**b) Arabic mirror of the reply expectation:**
- EN authoritative (live in `docs/app/public/partner-contact.md`): "The team usually replies within 2 business days. This is an expectation, not a guaranteed SLA."
- Content approves the Arabic wording mirroring it; it will be added to the guide (and handed to both teams for their contact-confirmation screens).

**c) MOB-05 holistic note for Content (no action unless they want it):** all user-facing rating/feedback texts were already approved via decision 1 (MOB-08 five strings — mobile's side).

---

## Tracking

- [ ] Public Privacy + Terms published → Mastermind live re-verification
- [ ] Plan AR/EN copy delivered/entered
- [ ] AR mirror of the reply expectation approved → guide updated
