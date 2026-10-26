# Frontend evidence recon — items 13 and 11 (2026-10-26)

Read-only DB recon on the rehearsal (VPS `72.62.92.144`, DB reached host-side as
peer `postgres` via psql — `su postgres` socket access — against `SanadDb` on
port 5433; every query wrapped in `BEGIN TRANSACTION READ ONLY … ROLLBACK`).
No writes, no auth/login/API calls, no config changes, no restarts. No
credentials or secret-file paths are included here. This is an evidence
snapshot; state can change after any future action.

---

## Item 13 — legacy orphan caregiver profiles

**Join used:** `caregivers.caregivers c LEFT JOIN identity.users u ON u.id = c.user_id`
(orphan = no `identity.users` row exists for the referenced `c.user_id`).

**Proven:** exactly **14** caregiver rows total; **6 are orphans**; all 6
because their referenced `user_id` has **no matching `identity.users` row**
(dangling reference — the identity rows no longer exist / were never kept).
For each orphan additionally verified absent in:
`identity.user_accounts` (0 rows for all 6 user_ids) and `chat` (no
conversation participant and no message sender referencing any orphan
user_id). Since the identity rows are gone, **no display name, phone or email
is recoverable from `identity.users` for any of the 6** — the caregiver row
itself carries no name columns.

The other **8 caregiver rows are non-orphan** (linked) — count only, no other
fields reproduced here (ids
`01a0a606-867c-7f9a-902e-cf5921d8fcf8`, `01a17319-e1c4-7b19-813f-2dc716425238`,
`01a17542-2014-…`, `01a17542-2158-…`, `01a17542-2163-…`, `01a17542-219f-…`,
`01a17542-21f0-…`, `01a17542-2245-…`).

### Orphan caregiver rows — exact

| # | caregiver_id | referenced user_id (dangling) | bilingual display name | created (UTC) |
|---|---|---|---|---|
| 1 | `01a06152-dbc4-775e-9769-2108ba12acea` | `01a04327-a1fa-7cd3-81b9-da49837e0edf` | **unavailable** (identity row absent) | 2026-09-02 08:53:35.301863+00 |
| 2 | `01a0761b-8da1-7ee9-9dd6-514b6e4eb2ee` | `01a0761a-0368-7e59-92ae-182a792e88e1` | **unavailable** (identity row absent) | 2026-09-06 09:45:12.354781+00 |
| 3 | `01a0761f-f18d-7d47-a15f-7ccb85e0f69d` | `01a0432a-4b7e-7bef-ab39-341908b44680` | **unavailable** (identity row absent) | 2026-09-06 09:50:00.077996+00 |
| 4 | `01a07f9f-5dc5-7bb6-b761-d20093e4dd40` | `01a07f9c-21a8-7b33-880d-19fc211cfa66` | **unavailable** (identity row absent) | 2026-09-08 06:05:45.799629+00 |
| 5 | `01a080f8-c541-76d3-ba16-ca64fd8d91a9` | `01a080f5-1b9a-7f76-97bb-1ab369c47150` | **unavailable** (identity row absent) | 2026-09-08 12:23:02.213811+00 |
| 6 | `01a0a5ec-a8b0-78f7-81c1-46a03528f47c` | `01a0a5eb-b7b2-77cf-a7ff-a6a1db06898e` | **unavailable** (identity row absent) | 2026-09-15 16:35:45.456862+00 |

Observed details (all six, uniform): created 2026-09-02 … 2026-09-15 — before
the 2026-10-25 demo fixtures; caregiver `type` values are 1 (medical, rows 1
and 4) and 2 (companion, rows 2, 3, 5, 6); `status` values 1 (rows 2, 6) and 4
(rows 1, 3, 4, 5).

**What this proves:** the Owner's accepted decision ("the 6 legacy orphan
caregiver profiles stay untouched — no deletion") maps to exactly these 6
caregiver ids; the orphanage reason is uniform — a dangling `user_id` with no
`identity.users` row — so the frontend should expect them to render without any
name/phone/email. No chat or account rows reference them, so they cannot
interfere with chat lists or account lookups.

### SQL (item 13)

```sql
SELECT c.id, c.user_id, c.type, c.status, c.created_on_utc,
       (u.id IS NOT NULL) AS user_row_exists,
       u.arabic_full_name, u.english_full_name, u.phone_number, u.email, u.status
FROM caregivers.caregivers c
LEFT JOIN identity.users u ON u.id = c.user_id
ORDER BY c.created_on_utc, c.id;

-- absence checks for the 6 orphan user_ids:
SELECT ua.user_id FROM identity.user_accounts ua
JOIN caregivers.caregivers c ON c.user_id = ua.user_id
LEFT JOIN identity.users u ON u.id = c.user_id
WHERE u.id IS NULL;                                   -- -> 0 rows

SELECT cv."Id" FROM chat."Conversations" cv
JOIN caregivers.caregivers c ON c.user_id IN (cv."ParticipantOne", cv."ParticipantTwo")
LEFT JOIN identity.users u ON u.id = c.user_id
WHERE u.id IS NULL
UNION ALL
SELECT m."Id" FROM chat."Messages" m
JOIN caregivers.caregivers c ON c.user_id = m."SenderUserId"
LEFT JOIN identity.users u ON u.id = c.user_id
WHERE u.id IS NULL;                                   -- -> 0 rows
```

---

## Item 11 — chat threads for the demo accounts

**Join used:** `chat."Conversations" cv LEFT JOIN identity.users` on
`"ParticipantOne"`/`"ParticipantTwo"` (roles from
`identity.user_accounts.account_type`), messages aggregated from
`chat."Messages"` per `"ConversationId"`.

**Proven: exactly 6 conversations exist in the whole DB; 4 involve demo
accounts.** 31 messages total; 27 in the demo threads. The 2 non-demo threads
belong to two real (non-fixture) users — recorded here by id only, personal
emails partially masked: `01a140b7-a694-7fe6-a753-1a3e2e3f70b2` (Scope 0,
4 messages, last message 2026-10-19 23:10:24.873109Z, participants
`m••7@gmail.com` and `r••5@gmail.com`) and
`01a15663-37b6-76a8-951c-dde8de64e614` (Scope **1, booking-scoped**,
`BookingId 01a1559c-10e3-76b1-ba13-7753b1b7de77`, same pair, **0 messages** —
an empty thread).

### Demo chat threads — exact (times UTC)

| # | thread_id (ConversationId) | Participant 1 | Participant 2 | msg_count | last_message_utc | extra state |
|---|---|---|---|---|---|---|
| 1 | `01a17542-4024-7d48-b304-d0a5ba30df12` | user `01a17542-12d2-7bd4-bd85-59d13a5bd98f`, account_type 1 (family owner) — الكيرجيفر الأول التجريبي / Demo Family Owner `[demo1:family.owner]` (demo1.family.owner@test.sanad.local) | user `01a17542-157f-7a21-bffd-92d53c18116f`, account_type 2 (caregiver) — الكيرجيفر الأول التجريبي / Demo Caregiver One `[demo1:caregiver.1]` (demo1.caregiver.1@test.sanad.local) | **10** | 2026-10-23 13:03:00+00 | not hidden; both read (2026-10-24 12:00Z) |
| 2 | `01a17542-4062-7e0c-b356-a70fe178eb67` | user `01a17542-12d2-7bd4-bd85-59d13a5bd98f`, account_type 1 — Demo Family Owner `[demo1:family.owner]` | user `01a17542-16ee-70f5-9305-7f2d174ab28c`, account_type 8 (care-home owner) — صاحب دار الرعاية الأول التجريبي / Demo Care Home Owner One `[demo1:carehome.owner1]` (demo1.carehome.owner1@test.sanad.local) | **6** | 2026-10-23 12:35:00+00 | **`HiddenForParticipantOne = true`** — hidden from demo1.family.owner's list |
| 3 | `01a17542-4068-79a9-91a4-d4dd754c8b8a` | user `01a17542-14f4-7d50-9c15-dfdddf31ea26`, account_type 1 — مالك العيلة التاني التجريبي / Demo Second Family Owner `[demo1:family.owner2]` (demo1.family.owner2@test.sanad.local) | user `01a17542-161d-76d6-a2da-b0fa047c9a78`, account_type 3 (companion) — المرافق الأول التجريبي / Demo Companion One `[demo1:caregiver.3]` (demo1.caregiver.3@test.sanad.local) | **4** | 2026-10-23 12:21:00+00 | **block row exists**: family.owner2 blocked caregiver.3 on 2026-10-24 12:00Z (`chat."Blocks"` `01a17542-4072-7ab0-8cde-e514428491ea`) |
| 4 | `01a17542-408e-739d-a393-ba7f8a56afa4` | user `01a17542-12d2-7bd4-bd85-59d13a5bd98f`, account_type 1 — Demo Family Owner `[demo1:family.owner]` | user `01a17542-15c7-7c9e-9eb1-85b9667c83f9`, account_type 2 (caregiver) — الكيرجيفر التاني التجريبي / Demo Caregiver Two `[demo1:caregiver.2]` (demo1.caregiver.2@test.sanad.local) | **7** | 2026-10-23 12:42:00+00 | not hidden; family.owner read (2026-10-24 12:00Z); caregiver.2 has **no read-state row** |

All 4 demo threads: `Scope = 0` (direct, not booking-scoped), `BookingId` NULL,
`CreatedOnUtc = 2026-10-22 12:00:00+00`. Read states for demo threads live in
`chat."ConversationReadStates"`.

### Structure symptoms that explain an empty chat list (all read-only facts)

- **`HiddenForParticipantOne = true` on thread 2** — demo1.family.owner has 3
  threads but only 2 of them (`4024`, `408e`) are visible to that account; the
  care-home-owner thread `4062` is hidden for it and appears only for
  demo1.carehome.owner1.
- **No threads exist at all for any `r2.*` account** (r2.carehome.owner,
  r2.caregiver, r2.support, r2.superadmin, r2.family.owner, …) — an r2 login
  legitimately sees an empty chat list; there is nothing to show.
- The only other empty thread (`01a15663-…`, 0 messages) is a non-demo,
  booking-scoped thread.
- A **block row** (family.owner2 → caregiver.3) exists, which filtering may
  hide or deprioritize depending on the list implementation.
- The 6 orphan caregiver user_ids reference **no** chat rows (see item 13), so
  they cannot be why a list is empty.

### SQL (item 11)

```sql
SELECT cv."Id", cv."Scope", cv."BookingId",
       cv."HiddenForParticipantOne", cv."HiddenForParticipantTwo", cv."CreatedOnUtc",
       u1.id, u1.arabic_full_name, u1.english_full_name, u1.email,
       u2.id, u2.arabic_full_name, u2.english_full_name, u2.email,
       count(m."Id") AS msg_count, max(m."SentOnUtc") AS last_message_utc
FROM chat."Conversations" cv
LEFT JOIN identity.users u1 ON u1.id = cv."ParticipantOne"
LEFT JOIN identity.users u2 ON u2.id = cv."ParticipantTwo"
LEFT JOIN chat."Messages" m ON m."ConversationId" = cv."Id"
GROUP BY cv."Id", u1.id, u2.id
ORDER BY cv."CreatedOnUtc";

-- roles:
SELECT ua.user_id, string_agg(ua.account_type::text, ',') AS account_types
FROM identity.user_accounts ua GROUP BY ua.user_id;

-- per-sender breakdown:
SELECT cv."Id", m."SenderUserId", count(*) AS msgs,
       min(m."SentOnUtc") AS first_utc, max(m."SentOnUtc") AS last_utc
FROM chat."Conversations" cv
JOIN chat."Messages" m ON m."ConversationId" = cv."Id"
GROUP BY cv."Id", m."SenderUserId" ORDER BY cv."Id", first_utc;

SELECT * FROM chat."Blocks";
SELECT rs."ConversationId", rs."UserId", rs."ReadOnUtc"
FROM chat."ConversationReadStates" rs
ORDER BY rs."ConversationId", rs."ReadOnUtc";
```

**What this proves (item 11):** the demo chat corpus is exactly 4
direct-scope threads with 27 messages, all fixture-created 2026-10-22 with
traffic on 2026-10-23; the frontend's reported empty chat list is reproducible
from persisted state in three concrete ways (hidden flag on one thread for the
family owner, a block row on another pair, and no threads at all for r2
accounts) — the exact account + request used by the frontend still picks which
one applies.

**Note for psql on this host:** the `chat` schema tables are PascalCase
(`"Conversations"`, `"Messages"`, `"Blocks"`, …) — unquoted references fold to
lowercase and fail; the `caregivers`/`identity` tables are all-lowercase.
