# Opus work package — frozen contract (owner-approved)

Scope: library, caregiver payout reads, community additions, rating
unification. **Chat is excluded** — it belongs to the Chat lane with its own
frozen contract in `docs/chat/plan.md`. No implementation has started. This
document freezes the product contract; `tasks.md` owns execution order and
`handoff.md` owns live status. Changing anything below needs a new explicit
owner decision.

## 0. Rating unification (locked)

The server currently requires `ratingValue` as a string and silently fails
mobile calls sending `{value: 4}`. The server is fixed to accept
`POST /api/v1/community/posts/{postId}/rating` with `{ "value": 1–5 }`
(integer, range-validated). No backward-compat shim (current behavior is
broken, nothing working to preserve). Postman collection and any
`ratingValue` references move to `value`.

## 1. Community additions (locked)

- **Categories:** new `Community` entity + migration; admin CRUD under
  `CommunityModeration` (SuperAdmin + ContentAdmin); public active-only list
  `GET /api/v1/community/categories` → `[{id, arabicName, englishName}]`.
- **`categoryId`** optional on post creation; when supplied it must exist and
  be active (unknown → `400 Community.InvalidCategory`; inactive → `409`
  `Community.CategoryInactive`, mirroring the onboarding inactive-lookup
  precedent). Moderation flow unchanged and orthogonal.
- **Search/filter/sort** on `GET /api/v1/community/posts`:
  `search=` (ILIKE titles + contents, both languages), `categoryId=`,
  `fromDate/toDate=` (UTC on creation), `sort=Newest|Oldest|MostLiked`
  (default Newest). Bare-list envelope unchanged.
- **Favorites list:** `GET /api/v1/community/favorites?page=&pageSize=` (same
  post shape), newest-favorited-first. The toggle endpoint is unchanged.
- **Per-user state** on post list/detail responses (additive, safe):
  `categoryId`, `categoryArabicName`, `categoryEnglishName`, `isLiked`,
  `isFavorited`, `currentUserRating` (null when the caller never rated).

## 2. Chat deltas (locked, folded into `docs/chat/plan.md` rules)

- Open by `bookingId` only (backend resolves the counterpart; booking must
  exist, involve the caller, and not be cancelled/expired/refunded) or by
  `recipientUserId` for direct chats; exactly one is required.
- Cursor renamed to `beforeId` + `pageSize`; message items gain
  `conversationId`; `createdOnUtc` replaces `sentOnUtc` everywhere.
- New `Location` message type `{latitude (-90…90), longitude (-180…180),
  label?}` with the same participant-only privacy as text.
- Read marking moves to `POST` (idempotent `204`, unchanged semantics).
- Attachments download at `GET /api/v1/chat/attachments/{id}/file`
  (participant check server-side from the attachment's conversation).
- `accountType` is the Identity `AccountType` integer; care-home participants
  resolve to the facility (name/cover) over the owner's user id. Direct
  booking-less care-home chats require the new additive `contactUserId` on
  facility detail (authenticated callers).
- Realtime stays polling + in-app alerts; only `message.created` and read
  updates surface. No SignalR work.

## 3. Library, family-facing (locked, new CMS content)

Lives in the **CMS module** under the existing `CmsContent` policy
(SuperAdmin + ContentAdmin). No new module, no new policy.

- **Categories:** admin-managed lookup + full CRUD; public active-only list
  `GET /api/v1/library/categories` → `[{id, arabicName, englishName}]`.
- **Content:** `Article | Video`, bilingual titles/summaries, structured
  article sections (ordered, with bullet lists), `videoUrl` (external
  YouTube/Vimeo/embed URL — no video hosting/transcoding in V1) + duration,
  cover upload (multipart, validated like service icons), `isPremium`,
  view/like counters, `Draft/Published/Archived` lifecycle mirroring posts.
- **Premium gating:** `canAccess = !isPremium || active subscription grants
  PremiumContent` (benefit key 8). Gated detail returns metadata only with
  `canAccess: false`.
- **List/search/filter:** `GET /api/v1/library/content?type=&categoryId=&search=&sort=Newest&page=&pageSize=`
  (`sort=Newest|MostViewed`, default Newest); bare-list envelope with
  per-item `isLiked/isSaved/isPremium/canAccess`.
- **Detail:** `GET /api/v1/library/content/{id}` (+ sections or video
  fields); **recommendations:** `GET /api/v1/library/recommendations`
  (same-category newest + top-viewed fallback, documented, no ML).
- **Saved/liked/views:** `GET …/saved`, `PUT …/{id}/saved {value}`,
  `PUT …/{id}/liked {value}` (explicit set, idempotent),
  `POST …/{id}/view` (increments per call, documented).

## 4. Caregiver payout reads (locked, reconciled with merged T4)

Payouts stay per-booking `Paid/Failed` rows, admin-recorded. No batch
payouts, no Processing state, no minimums exist — none are created.

- Statuses keep the merged `Paid` label; `Transferred ≡ Paid` is documented,
  not renamed. The `Processing` bucket is deleted from the summary surface.
- `GET /api/v1/caregiver/payouts/summary` →
  `{currency, unpaidAmount (eligible-unrecorded, computed),
  transferredAmount (Σ Paid), failedAmount (Σ Failed),
  nextExpectedPayoutOnUtc (earliest eligibility time among unpaid bookings
  with a verified account, else null)}`.
- `GET /api/v1/caregiver/payouts?status=&page=&pageSize=` → items
  `{id, amount (= gross), currency, status, bankNameArabic, bankNameEnglish,
  maskedIban (****+last4, unchanged), transferReference, createdOnUtc,
  transferredOnUtc (= paidOnUtc), failureReason}`.
- `GET /api/v1/caregiver/payouts/{id}` → same + `bookingItems` as a
  one-element array `[{bookingId, caregiverAmount, currency}]`
  (matches the coded mobile shape; forward-compatible).
- `GET /api/v1/caregiver/payment-policy` → admin-authored
  `descriptionArabic/descriptionEnglish` (added to policy create = new
  version), `requiresVerifiedBankAccount: true` (derived from the enforced
  Verified gate, documented), `minimumPayoutAmount: null` and
  `expectedProcessingDays: null` (honest nulls — no such rules exist).
- `platformFeeAmount` stays informational (family-paid, never deducted);
  `netAmount` always equals gross (structural invariant, tested).

## Documentation commitment

New `docs/app/library/` guide (categories, content lifecycle, premium
gating, saved/liked/views, recommendations); community deltas in
`docs/app/community/`; caregiver payout reads in
`docs/app/caregivers/payouts.md`; rating contract fixed everywhere
(`value`). Postman: new App library folder + request updates, all JSON
validated. Every workflow and condition is specified as the frontend
reference.
