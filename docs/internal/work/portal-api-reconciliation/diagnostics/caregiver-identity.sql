-- PORTAL-04 caregiver / Identity integrity diagnostic (read-only, operator-run).
--
-- Authority: diagnosis only. No Production connection, DML, DDL, temporary-table
-- creation on Production, repair statement, unbounded export, or side-effecting
-- function is authorized by PORTAL-04. The operator reviews this file, connects
-- manually with an authorized read-only role, pastes one section at a time,
-- and returns sanitized counts/IDs. The script never connects by itself and
-- never reads inherited environment variables.
--
-- How to run (see caregiver-identity-runbook.md for the full handoff):
--   1. Open a manual psql (or equivalent) session on the authorized target.
--   2. Run Section 0 (preflight) first on EACH module target you will inspect.
--   3. Run Sections 1-3 on the Caregivers-target database (they reference the
--      identity schema ONLY as it is visible from that same database; if the
--      Identity module points at a different physical database, Sections 2-3
--      cannot see its users from here — run Section 5 instead, independently,
--      on the Identity target, and compare privately).
--   4. Run Section 4 per selected caregiver id (bounded; one id per execution).
--   5. Run Section 6 on the Families-target database for bookings/reports
--      (a join in one database can never inspect another database's tables).
--   6. Wrap ad-hoc runs as below; every statement below is SELECT-only and ends
--      with an explicit ROLLBACK. Nothing is written.
--
--      BEGIN READ ONLY;
--      SET LOCAL statement_timeout = '5s';
--      ... paste ONE section ...
--      ROLLBACK;
--
-- Parameters used below (substitute literal values; never paste PII):
--   :statusFilter  integer or NULL  (caregiver status 1..7; NULL = no filter)
--   :typeFilter    integer or NULL  (caregiver type 1=Medical, 2=Companion)
--   :page          integer >= 1      (admin list page; default 1)
--   :pageSize      integer 1..100    (admin list page size; default 10)
--   :caregiverId   uuid              (single selected caregiver id, Section 4)
--   :limit         integer           (result cap for open-ended listings; default 100)
--
-- Enum reference (from current domain; do not renumber in reports):
--   caregivers.caregivers.type:   1 = Medical, 2 = Companion
--   caregivers.caregivers.status: 1 = Onboarding, 2 = PendingReview,
--                                 3 = NeedsCorrection, 4 = Active, 5 = Suspended,
--                                 6 = Rejected, 7 = Deactivated
--   identity.users.status:        1 = PendingVerification, 2 = Active,
--                                 3 = Suspended, 4 = Blocked
--   identity.user_accounts.account_type: 1 = Family, 2 = MedicalCaregiver,
--                                 3 = CompanionCaregiver, 4 = Elderly,
--                                 5 = SuperAdmin, 6 = ContentAdmin,
--                                 7 = SupportAdmin, 8 = CareHomeOwner,
--                                 9 = FinanceAdmin
-- Expected caregiver account type by caregiver type:
--   caregivers.type 1 (Medical)   -> account_type 2
--   caregivers.type 2 (Companion) -> account_type 3
--
-- Name/phone columns are intentionally NOT selected below: classification needs
-- only ids, status/type codes, and counts. Keep exports free of PII.

-- ============================================================================
-- Section 0 — schema preflight (run on each target before dependent sections).
-- A missing schema/table here means: stop that section on this target and
-- record "not inspectable here", instead of letting one failed statement
-- silently invalidate the rest of a transaction.
-- ============================================================================

SELECT table_schema, table_name
FROM information_schema.tables
WHERE (table_schema, table_name) IN (
    ('caregivers', 'caregivers'),
    ('caregivers', 'caregiver_certificates'),
    ('caregivers', 'caregiver_service_selections'),
    ('caregivers', 'caregiver_language_selections'),
    ('caregivers', 'caregiver_area_selections'),
    ('caregivers', 'caregiver_ratings'),
    ('caregivers', 'caregiver_payout_accounts'),
    ('caregivers', 'caregiver_payouts'),
    ('caregivers', 'caregiver_payout_account_reviews'),
    ('identity', 'users'),
    ('identity', 'user_accounts'),
    ('identity', 'user_identity_documents'),
    ('families', 'bookings'),
    ('families', 'medical_reports'),
    ('families', 'visit_reports'))
ORDER BY table_schema, table_name;

-- Sanitized target fingerprint for private comparison (database + schemas only;
-- no secrets, no connection string). Run on each module target and compare the
-- labels privately to decide whether two modules share one physical database.
SELECT current_database() AS database_name;

SELECT schema_name
FROM information_schema.schemata
WHERE schema_name IN ('identity', 'caregivers', 'families', 'finance', 'care_homes')
ORDER BY schema_name;

-- ============================================================================
-- Section 1 — list/count reconciliation (mirrors production queries verbatim).
-- GetAdminCaregiversAsync LEFT JOINs identity.users and returns one row per
-- caregiver (including unmatched identities). CountAdminCaregiversAsync counts
-- caregivers.caregivers only. TotalCount >= page items is therefore expected;
-- any other relationship indicates a defect in these two statements.
-- ============================================================================

-- 1a. Production count query (exact predicate shape).
SELECT COUNT(*)
FROM caregivers.caregivers c
WHERE (:statusFilter IS NULL OR c.status = :statusFilter)
  AND (:typeFilter IS NULL OR c.type = :typeFilter);

-- 1b. Production list query (exact SELECT/JOIN/ORDER/LIMIT shape; page window).
SELECT
    c.id         AS caregiver_id,
    c.user_id    AS user_id,
    c.type       AS caregiver_type,
    c.status     AS caregiver_status,
    c.availability,
    -- Presence flags only; names/phones stay out of diagnostic exports.
    (u.id IS NOT NULL)              AS identity_user_present,
    (u.phone_number IS NOT NULL)    AS identity_phone_present,
    c.updated_on_utc
FROM caregivers.caregivers c
LEFT JOIN identity.users u
    ON u.id = c.user_id
WHERE (:statusFilter IS NULL OR c.status = :statusFilter)
  AND (:typeFilter IS NULL OR c.type = :typeFilter)
ORDER BY c.updated_on_utc DESC, c.id
LIMIT :pageSize OFFSET (:page - 1) * :pageSize;

-- 1c. Reconciliation summary: totals must satisfy
--     matched + missing_user = list_total = count_total (same filters).
SELECT
    COUNT(*)                                            AS list_total,
    COUNT(u.id)                                         AS matched,
    COUNT(*) - COUNT(u.id)                              AS missing_user
FROM caregivers.caregivers c
LEFT JOIN identity.users u
    ON u.id = c.user_id
WHERE (:statusFilter IS NULL OR c.status = :statusFilter)
  AND (:typeFilter IS NULL OR c.type = :typeFilter);

-- ============================================================================
-- Section 2 — missing Identity users (bounded listing, ids + codes only).
-- Each row here renders in the admin list with null names/phone. This proves
-- the display shape, NOT the cause: the row may be a stale fixture, a
-- cross-target artifact (Section 5), or a genuinely orphaned reference.
-- ============================================================================

SELECT
    c.id               AS caregiver_id,
    c.user_id          AS user_id,
    c.type             AS caregiver_type,
    c.status           AS caregiver_status,
    c.updated_on_utc
FROM caregivers.caregivers c
LEFT JOIN identity.users u
    ON u.id = c.user_id
WHERE u.id IS NULL
  AND (:statusFilter IS NULL OR c.status = :statusFilter)
  AND (:typeFilter IS NULL OR c.type = :typeFilter)
ORDER BY c.updated_on_utc DESC, c.id
LIMIT :limit;

-- ============================================================================
-- Section 3 — account-type / role classification for PRESENT users.
-- Only meaningful where identity.users is visible from this database
-- (same-target deployment). On a split-target deployment, run Section 5 on the
-- Identity target instead and reconcile the id sets privately.
-- ============================================================================

-- 3a. Per-caregiver classification (one row per caregiver, bounded).
-- expected_account: 2 for Medical, 3 for Companion.
SELECT
    c.id               AS caregiver_id,
    c.user_id          AS user_id,
    c.type             AS caregiver_type,
    c.status           AS caregiver_status,
    u.status           AS identity_status,
    CASE WHEN c.type = 1 THEN 2 WHEN c.type = 2 THEN 3 END AS expected_account_type,
    EXISTS (
        SELECT 1 FROM identity.user_accounts a
        WHERE a.user_id = c.user_id
          AND a.account_type = CASE WHEN c.type = 1 THEN 2 WHEN c.type = 2 THEN 3 END
    )                  AS has_expected_caregiver_account,
    EXISTS (
        SELECT 1 FROM identity.user_accounts a
        WHERE a.user_id = c.user_id
          AND a.account_type NOT IN (2, 3)
    )                  AS has_other_account_type,
    CASE
        WHEN u.id IS NULL THEN 'missing_user'
        WHEN NOT EXISTS (
            SELECT 1 FROM identity.user_accounts a
            WHERE a.user_id = c.user_id
              AND a.account_type = CASE WHEN c.type = 1 THEN 2 WHEN c.type = 2 THEN 3 END
        ) THEN 'present_user_missing_expected_account'
        ELSE 'matched'
    END                AS classification
FROM caregivers.caregivers c
LEFT JOIN identity.users u
    ON u.id = c.user_id
WHERE (:statusFilter IS NULL OR c.status = :statusFilter)
  AND (:typeFilter IS NULL OR c.type = :typeFilter)
ORDER BY c.updated_on_utc DESC, c.id
LIMIT :limit;

-- 3b. Mismatched type/role detail: present users whose account set does not
-- contain the expected caregiver account (shows actual account codes only).
SELECT
    c.id               AS caregiver_id,
    c.user_id          AS user_id,
    c.type             AS caregiver_type,
    a.account_type     AS actual_account_type
FROM caregivers.caregivers c
INNER JOIN identity.users u
    ON u.id = c.user_id
LEFT JOIN identity.user_accounts a
    ON a.user_id = c.user_id
WHERE NOT EXISTS (
        SELECT 1 FROM identity.user_accounts a2
        WHERE a2.user_id = c.user_id
          AND a2.account_type = CASE WHEN c.type = 1 THEN 2 WHEN c.type = 2 THEN 3 END)
ORDER BY c.id, a.account_type
LIMIT :limit;

-- 3c. Duplicate user_id references (meaningful: user_id has only an index, no
-- unique constraint and no cross-module FK — see CaregiverConfiguration).
SELECT
    c.user_id          AS user_id,
    COUNT(*)           AS caregiver_rows,
    MIN(c.id)          AS first_caregiver_id,
    MAX(c.id)          AS last_caregiver_id
FROM caregivers.caregivers c
GROUP BY c.user_id
HAVING COUNT(*) > 1
ORDER BY caregiver_rows DESC, c.user_id
LIMIT :limit;

-- ============================================================================
-- Section 4 — dependent references for ONE selected caregiver (bounded).
-- Uses counts/ids/statuses only: never document content, names, phones,
-- emails, banking values, or payloads. Run once per :caregiverId.
-- Tables below live in the Caregivers target EXCEPT families.* (Section 6).
-- If a table is absent on this target (preflight), skip it and record the gap.
-- ============================================================================

-- 4a. Caregiver row + identity presence (single row).
SELECT
    c.id               AS caregiver_id,
    c.user_id          AS user_id,
    c.type             AS caregiver_type,
    c.status           AS caregiver_status,
    (u.id IS NOT NULL) AS identity_user_present,
    c.updated_on_utc
FROM caregivers.caregivers c
LEFT JOIN identity.users u
    ON u.id = c.user_id
WHERE c.id = :caregiverId;

-- 4b. Documents: certificates (statuses only, no file paths).
SELECT
    verification_status,
    COUNT(*) AS certificate_count
FROM caregivers.caregiver_certificates
WHERE caregiver_id = :caregiverId
GROUP BY verification_status
ORDER BY verification_status;

-- 4c. Owned children counts (selections).
SELECT 'service_selections' AS child_table, COUNT(*) AS child_count
FROM caregivers.caregiver_service_selections WHERE caregiver_id = :caregiverId
UNION ALL
SELECT 'language_selections', COUNT(*)
FROM caregivers.caregiver_language_selections WHERE caregiver_id = :caregiverId
UNION ALL
SELECT 'area_selections', COUNT(*)
FROM caregivers.caregiver_area_selections WHERE caregiver_id = :caregiverId;

-- 4d. Ratings received (count only).
SELECT COUNT(*) AS rating_count
FROM caregivers.caregiver_ratings
WHERE caregiver_id = :caregiverId;

-- 4e. Finance/payout references (ids + status codes only; no banking values).
SELECT id AS payout_account_id, status AS payout_account_status, revision
FROM caregivers.caregiver_payout_accounts
WHERE caregiver_id = :caregiverId;

SELECT id AS payout_id, booking_id, status AS payout_status
FROM caregivers.caregiver_payouts
WHERE caregiver_id = :caregiverId
ORDER BY id
LIMIT :limit;

SELECT r.id AS review_id, r.payout_account_id, r.decision
FROM caregivers.caregiver_payout_account_reviews r
INNER JOIN caregivers.caregiver_payout_accounts a
    ON a.id = r.payout_account_id
WHERE a.caregiver_id = :caregiverId
ORDER BY r.id
LIMIT :limit;

-- ============================================================================
-- Section 5 — Identity-target counterpart (run INDEPENDENTLY on the Identity
-- module's database when targets differ; a join in one DB cannot inspect
-- another DB's users). Paste the privately supplied user_id values as literals
-- (full ids only; never expand truncated ids by guesswork).
-- ============================================================================

-- 5a. Existence + status for a supplied id set (operator pastes full ids).
-- SELECT id, status FROM identity.users WHERE id IN (...);  -- template only.
SELECT id, status
FROM identity.users
WHERE id = :userId;

-- 5b. Account types for that user (expected 2 for Medical, 3 for Companion).
SELECT user_id, account_type, created_on_utc
FROM identity.user_accounts
WHERE user_id = :userId
ORDER BY account_type;

-- 5c. Identity-side document reference (existence only, no images/paths).
SELECT id AS document_id, verification_status
FROM identity.user_identity_documents
WHERE user_id = :userId;

-- ============================================================================
-- Section 6 — Families-target dependents (run INDEPENDENTLY on the Families
-- module's database). Counts/ids/statuses only.
-- ============================================================================

-- 6a. Bookings referencing the caregiver (statuses only).
SELECT id AS booking_id, status AS booking_status
FROM families.bookings
WHERE caregiver_id = :caregiverId
ORDER BY id
LIMIT :limit;

-- 6b. Reports referencing the caregiver (counts only).
SELECT 'medical_reports' AS report_table, COUNT(*) AS report_count
FROM families.medical_reports WHERE caregiver_id = :caregiverId
UNION ALL
SELECT 'visit_reports', COUNT(*)
FROM families.visit_reports WHERE caregiver_id = :caregiverId;
