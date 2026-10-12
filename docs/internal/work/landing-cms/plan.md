# Landing CMS + public landing reads — frozen plan (v1, read-only, no implementation)

Owner-approved 2026-10-12: CmsContent policy (SuperAdmin + ContentAdmin), the
10 observed sections are final, all audit recommendations taken. Nothing here
authorizes code, migration applies, seeds, provider calls, or runtime.
Implementation starts only after explicit owner approval of this package.

## 0. Golden rule (frozen)

Editorial content is managed, operational data is computed — **no displayed
number is ever stored**. Every figure the public sees is either CMS-managed
copy or a live aggregation. Bilingual everywhere (Arabic-first, English twin),
same conventions as the Library package.

## 1. Canonical section registry (closed set — new types need a new decision)

| Key | Sections | CMS-managed | Live-bound |
|---|---|---|---|
| `hero` | Title, description, 2 buttons (label + target), cover image, stats strip | Texts, buttons, image | Stats from §2 |
| `partner-logos` | Logo list (image + link each) | Uploads + order | — |
| `about` | Title, body, 3 checkmark lines, image | All texts + image | — |
| `features` | 4 cards (title + text + icon key) | All (icon = closed key set, not uploads) | — |
| `activity-cards` | 2 cards (title, text, image, CTA) | Texts + images | Live approved-facility count on the care-homes card |
| `steps` | 5 ordered steps (title + text) | All + order | — |
| `dashboard-promo` | Title, checklist lines, laptop image | All | — |
| `testimonials` | Curated review refs (§3) | Selection + order + visibility | Names, texts, 1–5 stars from real approved reviews |
| `faq` | Referenced HelpCenter entries | Selection + order (refs only, never copied text) | Bodies resolve live from HelpCenter |
| `contact-form` | Required fields config, target inbox note | Field config | Submits into the real `SupportRequest` flow |
| `footer` | Link groups, contact lines | All | — |
| `stats` | Virtual (rendered inside hero) | — | `GET /landing/stats`: families, caregivers by type, approved facilities |

Draft/Published lifecycle + `isVisible` + `displayOrder` on every type, mirroring
the Library precedent. Publishing is explicit (no autosave-to-live); preview of
drafts requires a short-lived preview token. Copy/duplication creates a new
draft, never clones live state.

## 2. Endpoint catalog (frozen contract)

Public (`[AllowAnonymous]`), published-only unless noted:

| # | Verb + route | Purpose | Success | Failures |
|---|---|---|---|---|
| 1 | `GET /api/v1/landing/sections` | Ordered visible published sections with resolved content | 200 | — (empty list when nothing published) |
| 2 | `GET /api/v1/landing/sections/preview?token=` | Same, including drafts (token-gated) | 200 | 401 bad/expired token |
| 3 | `GET /api/v1/landing/stats` | Live counters (families, caregivers, approved facilities) | 200 | — |

Admin (`CmsContent`: SuperAdmin or ContentAdmin):

| # | Verb + route | Purpose | Success | Failures |
|---|---|---|---|---|
| 4 | `GET /api/v1/admin/landing/sections` | Full list incl. drafts | 200 | — |
| 5 | `POST /api/v1/admin/landing/sections` | Create draft section (closed type key) | 201 | 400 validation/unknown type |
| 6 | `PUT /api/v1/admin/landing/sections/{id}` | Edit draft (unknown ids 404; published rows immutable — edit creates/updates the draft twin) | 200 | 400 / 404 |
| 7 | `POST /api/v1/admin/landing/sections/{id}/publish` | Publish (validates completeness first) | 200 | 400 incomplete / 404 |
| 8 | `POST /api/v1/admin/landing/sections/{id}/visibility` | Toggle `isVisible` `{value: bool}` | 204 | 404 |
| 9 | `DELETE /api/v1/admin/landing/sections/{id}` | Delete draft only (published history retained) | 204 | 404 / 409 published |
| 10 | `POST /api/v1/admin/landing/sections/reorder` | Set `displayOrder` batch | 204 | 400 duplicate/unknown ids |
| 11 | `POST /api/v1/admin/landing/media` | Image upload (jpeg/png/webp, 5 MB, cleanup on failure) | 201 | 400 content/size |

## 3. Business rules and cases

1. **Closed registry.** Unknown `type` on create → 400. No free-form sections in v1.
2. **Bilingual required** on publish (Arabic + English titles/bodies); drafts may be partial.
3. **Publish validation**: complete copy + at least the type's required assets (hero needs its cover); incomplete → 400 naming the gap.
4. **Testimonial refs** point at approved reviews (caregiver/family rating rows); dangling refs resolve as skipped (never 500), and admin sees a warning count.
5. **FAQ refs** must exist in HelpCenter; deleted entries drop from public reads silently.
6. **Stats are computed per request** (no caching in v1; counts only, no PII).
7. **Contact form** validates like any public intake (required fields, lengths, idempotency key) and enters the real support flow; no separate ticket table.
8. **History/audit**: every publish/visibility/delete writes an audit row (actor, section, action, UTC). No update/delete paths for history.
9. **Media**: private storage, signature-checked, 5 MB cap, orphan cleanup on failed commands (Library precedent verbatim).

Cases to pin: empty public list, draft invisibility without token, bad/expired token 401, unknown type 400, incomplete publish 400, published immutability, reorder determinism, dangling testimonial/FAQ refs skipped, stats equal to direct counts, delete-draft vs delete-published 409, media invalid content/size 400.

## 4. File manifest (exclusive)

- `Modules/Cms/Domain/.../Landing/` — section entity (type key, bilingual maps, order, visibility, lifecycle), testimonial refs, audit rows, typed ids.
- `Modules/Cms/Application/.../Landing/` — public queries (sections, preview, stats), admin CRUD/publish/visibility/reorder/media, validators, errors.
- `Modules/Cms/Infrastructure/.../` — configs + **one additive migration (authored only)** + `ICmsDbContext` sets.
- `Sanad.API/Controllers/LandingController.cs` (1–3) + `AdminLandingController.cs` (4–11, `CmsContent`) + request DTOs.
- `tests/.../Cms/LandingTests.cs` (+ SQLite twins for every new query).
- `docs/app/public/landing.md` + Postman App-public/Admin additions + JSON validation.
- `docs/internal/work/landing-cms/{tasks.md,handoff.md,reports/}` tracking.

## 5. Verification (incl. live API)

Per phase: zero-warning build; focused tests (InMemory + SQLite twins); docs + Postman synced; diff-check; contract-map 0/0. Joint gate: full unit + arch once. **Live gate per phase** on the disposable target: public reads return 200 with seeded CMS rows; admin CRUD cycle on a disposable section; stats equal to direct DB counts; sessions revoked; API stopped. Mutating live checks need per-run approval; unrun = limitation, never pass. No full-collection Bruno.

## 6. Decisions recorded (locked 2026-10-12)

Permissions CmsContent; 10 sections final; all audit recommendations taken (canonical types, reuse FAQ/HelpCenter/SupportRequest/storage, computed stats, testimonial entity, preview tokens, history/audit, order field, visibility); deferred: scheduled publishing, version rollback, A/B testing, freeform HTML, manual stat overrides.
