# Phase 3 report — testimonial/FAQ refs + contact flow

Branch: `opencode/landing-cms`
Base: `main @ b0ac0d0`
Prior: `acc7ef4` (Phase 2 closeout)
Final: `b142b80` (implementation; report committed separately after)

Owner decisions applied (2026-10-12):
- Testimonials resolve `CaregiverRating` (`kind: caregiver`) + family
  `BookingReview` (`kind: family`); names via Family (reviews via booking),
  anonymous/unresolvable → “عائلة موثقة” / “Verified family” fallback;
  dangling refs skipped with admin warning counts.
- Contact reuses authenticated `POST /api/v1/support/contact` (no new
  endpoint, no new table); field-config schema + flow documented.
- Refs stored as ordered ContentJson arrays with optional `hidden` flag;
  FAQ resolution ignores audience, `IsActive` only; admin warning counts.

## Changed files

New:
- `Landing/LandingRefResolver.cs` (tolerant parsing — malformed JSON never
  throws; batch + per-ref live resolution; skip-missing with counts).
- `tests/.../Cms/LandingRefsTests.cs` (4 tests).
- This report.

Edited:
- Responses: public items gain `Testimonials`/`Faqs`; admin items gain
  `DanglingRefCount`.
- Public sections handler + admin list/create/update/publish handlers now
  resolve refs (ctor takes `IFamiliesDbContext` + `ICaregiversDbContext`;
  DI auto-wires; no csproj changes — refs already referenced).
- Existing test call sites updated for new ctors.
- `docs/app/public/landing.md` (resolution semantics + contact flow),
  `docs/admin/landing.md` (ref authoring schema), App-Public Postman
  (+ contact request in “04. Landing”), `tasks.md`.

No migration, no new routes, no new packages.

## Gates

- Build: `dotnet build Sanad.slnx --no-restore -m:1 --nologo
  /p:TreatWarningsAsErrors=true` → 0 warnings, 0 errors.
- Focused tests (Reads + Admin + Refs): 21 passed, 0 failed, 0 skipped.
  InMemory proves both-kind resolution, fallbacks, hidden/dangling/unknown
  handling, ref-order, malformed-never-500, warning counts; SQLite twins
  prove translation (incl. a pre-existing-style FK lesson: twin ratings
  need a real caregiver row).
- `git diff --check`: clean (after trimming two EOF blank lines my rewrite
  introduced). Postman JSONs parse. Contract-map: 590 requests, 0 missing /
  0 orphan Postman.
- Live gate (approved, disposable DB, port 5235): target tables
  (`caregiver_ratings`, `BookingReviews`, `help_faqs`) are EMPTY on the
  disposable DB, so real-target resolution is proven by automated tests;
  live verified skip-missing with disposable dangling-ref sections:
  testimonial + faq created/published (201/200), public 200 with empty
  resolved lists, admin warnings 1 + 1; contact ticket via family owner
  200 with ticketId.
- Cleanup: 2 sections + 1 ticket deleted (public `[]` again), 2 task
  sessions revoked (scoped `landing-p3%`), API stopped, port free.

## Notes and deviations

- `BookingReview` table/columns are PascalCase (`families."BookingReviews"`)
  — probe-only detail, no code impact.
- Contact has no idempotency key upstream (pre-existing support flow gap);
  documented as-is per reuse decision, not fixed here (Identity-owned).
- No push, no merge, no seeds written, no production contact.
