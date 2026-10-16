# Admin portal — Phase 00 — frontend report reconciliation

Date: 2026-10-16 (Africa/Cairo). Phase: FUR-00.
Communication state: ready for owner review/forwarding; not yet sent by this task.
This is an alignment report, **not a promise that proposed endpoints are live**.

## Baseline

Source inspected: `4b65369d760c8af6b2242ebd2022a6cb10057580`.
API deployment in the audit: `9167e7111c5321b063fa9d893001e77dd42a32da`.
Detailed evidence and limitations are in the
[full audit](../../../../internal/work/portal-api-reconciliation/frontend-report-audit-2026-10-16.md).
The [follow-up plan](../../../../internal/work/portal-api-reconciliation/plan.md#frontend-follow-up-sequence--2026-10-16)
owns implementation status. No frontend repository was inspected: the corrections
below concern supplied assumptions/contracts, not proven line-level client bugs.
IDs `FR-20261016-NN` refer to original report item #NN.

## What you are using correctly

- Register uses Arabic/English full names and `phoneNumber`; OTP verification uses
  request ID plus code, not email/purpose. The older auth guide is wrong here (#30).
- Assessment picker JPEG/PNG/WebP is correct; SVG is not accepted (#46).
- Numeric `DayOfWeek` is correct: 0 Sunday, 1 Monday, through 6 Saturday (#33).
- Missing owner settlement reads, caregiver payout selector, moderation inspection
  and owner disputes are valid capability requests, not misuse (#7, #9–11).
- Support-contact `Cms.Help.SupportContactNotFound` can be shown as an unconfigured
  state. It is an intentional first-run 404, not a deployment failure (#37).

## Earlier complaints already addressed

| Issue | Current truth | Frontend action / evidence |
| --- | --- | --- |
| FR-20261016-04 | Unready suspended/blocked accounts return 409 `Identity.Admin.ActivationNotReady`; verified ready accounts can reactivate. | Handle the safe conflict; do not force Active or infer every account fails. [Accepted fix](../../../../internal/work/portal-api-reconciliation/reports/phase-01-reactivation.md). |
| FR-20261016-06 | Approved geography import records 27 governorates / 349 Cities / 5714 Areas, retaining legacy selections. | Refresh the lookup data/cache; do not request another reset. [Executed receipt](../../../../internal/work/portal-api-reconciliation/reports/geography-import-preserve-add.md). |
| FR-20261016-32, FR-20261016-34, FR-20261016-35 | Principal numeric enum names/values, original 28 success schemas and five required query flags were published. Additional metadata gaps still exist. | Regenerate/review types against current OAS, not an old cached document. [Metadata evidence](../../../../internal/work/portal-api-reconciliation/reports/phase-03-api-metadata.md). |
| FR-20261016-36 | Library accepts camelCase and PascalCase section keys and returns safe field/index errors. Domain rules remain required. | Keep positive unique orders, Article sections and positive Video duration; do not send quoted numbers. [Library evidence](../../../../internal/work/portal-api-reconciliation/reports/phase-02-library.md). |
| FR-20261016-37 | Category body no longer requires Id; route owns it. | Omit body Id; do not use it to select the category. Same metadata evidence above. |

These are source/deployment-audit conclusions, not newly executed authenticated
admin scenarios today. If behavior differs, send the exact new evidence below.

## Contract assumptions to correct

| Issue | Correction | Backend/docs responsibility |
| --- | --- | --- |
| FR-20261016-30, FR-20261016-31 | Login/refresh/verification response IDs can be `{value: guid}` while request IDs are plain GUIDs. Preserve per-operation shapes; no global flattening is approved. | FUR-01 will reconcile inaccurate auth examples to actual DTOs. |
| FR-20261016-32 | Keep Family/caregiver BookingStatus 1–10 separate from CareHomeBookingStatus 1–9. SOS history action is not SOS status. Help aggregate is a dictionary/map, not an invented `statusCounts` array. | FUR-01 contract documentation/serialization evidence; full values in audit. |
| FR-20261016-34 | Platform-charge current is 204 when absent, 200 with DTO when present. Unread count is a bare integer. Availability is POST. Several bookings/ledger/policy schemas already exist. | Publish only genuinely missing metadata; platform-charge create needs actual 201 GUID metadata. |
| FR-20261016-39, FR-20261016-43 | Caregiver certificate review/download is SuperAdmin + SupportAdmin. Global user management is SuperAdmin-only. Payout operations are SuperAdmin + FinanceAdmin; finance rules are SuperAdmin + SupportAdmin. | FUR-01 fixes contradictory prose. New role grants require approval; similar policy names are not interchangeable. |
| FR-20261016-45 | Care Home required review/document-reject reasons: 1000; caregiver payout-account reason: 500; caregiver payout reason/evidence: 2000; payout reference: 200. | Document resource-specific limits, not one global 500 cap. |
| FR-20261016-46 | JPEG/PNG/WebP accepted, not SVG. Multipart overhead counts toward request-size limit. | Correct guide; do not change the correct picker to SVG. |
| Part B | Do not merge unrelated 1–4 status enums, use a five-state booking enum, or interpret every 404 as not deployed. Mandatory-certificate reject and revoke have different suspension effects. | Keep resources distinct. Reject makes caregiver Unavailable; revoke additionally suspends an Active caregiver profile. This does not itself mean Identity login is always suspended. |

## Genuine backend work or evidence still needed

| Issues | What remains | Delivery connection; not yet implementation |
| --- | --- | --- |
| #1, #12 | OTP resend reliability and restricted-login recovery. Configuration presence was confirmed, but provider validity/exact exception was not. No fixed OTP/admin verification backdoor is approved. | FUR-02; fake-provider failure and persisted-state tests before changes. |
| #2, #3 | October 11 finance query defect has an existing typed-ID fix. Current reported 500 is not reproduced. | FUR-03 exact Npgsql/disposable fixture verification; backend fix only if a remaining defect is demonstrated. |
| #5 | Audit snapshot: six of seven caregiver profiles lack matching Identity rows. Cause/recovery unknown. | FUR-09 diagnostic/disposition; do not hide rows or delete data. |
| #25, #27–29 | Missing revision media IDs, dispute display data, owner identity enrichment and actor display semantics. | FUR-04. Current-versus-historical actor role must be explicit. Media guard query translation risk also needs proof. |
| #7, #8 | Moderation list/detail is missing. Report storage/counters, reject reason persistence and timed blocking are not all existing operations. | FUR-07; exact reporting/blocking scope requires owner decision. ContentAdmin must not gain global suspension implicitly. |
| #10 | Eligible caregiver booking selector missing; current payout commands enforce more than completed/paid. | FUR-06 reuse all existing policy/account/delay/already-paid rules. |
| #34 | Help detail/history and selected owner/mutation/unread-count metadata still incomplete. | FUR-01; verified response contracts, no guessed frontend DTOs copied into schemas. |

## Requests that are new features, not backend defects

| Issues | Current disposition |
| --- | --- |
| #13, #14 | User counts/growth: E01 contract, including permitted roles and precise metrics/time buckets. Do not broaden user-detail access. |
| #15 | Platform controls: E04 enforcement contract; settings CRUD alone cannot implement maintenance/payment behavior. |
| #16 | Family-home CMS: E05; no complete equivalent publication model currently exists. |
| #17 | Medical access audit: E03. Some existing reads already create audit/activity records, but not the requested complete caregiver grant/deny ledger. |
| #18 | Missed check-ins: E06; define expected population/window/timezone first. |
| #19 | Internal caregiver notes: E07; new persistence/visibility rules. |
| #20, #21 | Recommendation: keep gateway secrets server-managed and defer application-registry screens. Neither is a promised new API. |
| #22 | Media cleanup: E08, reference-aware, including protected historical/published media. No blind deletion. |
| #23 | Supplier remains postponed. |

These phase links refer to the canonical plan and audit, not finished contracts.
Future implementation reports will cite actual source revision, tests, API guide
and deployment receipt. Until then, show an explicit unavailable/pending feature
state without guessing payloads or treating all resource 404s as route absence.

## Evidence requested, not broad retesting

For a still-observed failure provide: method/path/query; timestamp plus timezone;
selected account/role; sanitized request and response including error code and
trace ID; API revision if available. Never send tokens, credentials, OTPs or
personal documents. Start with the exact remaining finance/OTP failure if present.
Do not suspend real accounts or send providers live traffic merely to reconfirm
historical observations; coordinate safe fixture-backed tests with the backend.

**Next action:** owner reviews/forwards this report; frontend acknowledges only
the corrections relevant to its implementation and supplies new evidence where
behavior still differs. No backend feature delivery date or new API contract is
promised by this alignment report.
