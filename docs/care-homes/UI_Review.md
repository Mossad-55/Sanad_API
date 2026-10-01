# Care homes — UI evidence

Status: UI inspection complete; backend planning only. Both source directories were visually reviewed: `UI/Home Cares` (19 PNGs) and `UI/Family Bookings and Visits` (10 PNGs).

[Confirmed decisions](Care_Homes_Decisions.md) is authoritative for owner answers, including the consolidated 24-question response. [Care homes tasks](Care_Homes_Tasks.md) owns implementation status. Do not treat sample UI text or earlier unresolved observations below as overriding those decisions. Original screenshots are retained unchanged; backend/UI contract notes must explain necessary UI changes.

## Observed screen inventory

| Folder / files | Observed requirements; sample data is not a business rule |
|---|---|
| `1 - Home care Facility Setup/البيانات الأساسية.png` | Arabic/English facility name and description, phone/email/full address, draft save |
| `1 - Home care Facility Setup/الغرف والأسرة.png` | Cover/gallery uploads, facility amenities; despite filename, this is primarily media/facilities |
| `1 - Home care Facility Setup/الخدمات الطبية.png` | Medical specialties, additional services, custom specialty option |
| `1 - Home care Facility Setup/الغرف والأسعار.png` | Room types, beds, total/available counts, daily/monthly pricing, extra fees/refundable deposit affordance |
| `1 - Home care Facility Setup/الشروط والترخيص.png` | Admission conditions visible to families and private operating license upload, PDF/JPG/PNG |
| `1 - Home care Facility Setup/المراجعة.png` | Review submitted sections, attachment preview, submit; copy says editing locked during review and status email sent |
| `1 - Home care Facility Setup/تقديم الطلب.png` | Submitted/pending-review state, submitted-file view, support; 24–48 hours is UI copy needing confirmation, not an enforced SLA |
| `2- Home dashboard/Home Dashboard.png` | Occupancy, visits today, pending bookings, revenue chart, physical room/resident cards, booking accept/reject, upcoming visits, recent activity, export |
| `3 - Bookings and Visits/الطلبات الواردة.png` | Filtered/paginated incoming requests, status/date/type filters, approve/reject/detail, counts/growth |
| `3 - Bookings and Visits/الطلبات.png` | Resident name/age/medical history, Family contact, selected room/location/start/duration, private attachments, activity history, internal notes, accept/reject/contact |
| `4 - Room Management/إدارة أنواع الغرف والتوفر.png` | Room-type cards and per-unit room numbers, occupied/available/maintenance, resident assignment, quick check-in, fault log, export |
| `4 - Room Management/إضافة نوع غرفة جديد.png` | Bilingual room name/description, monthly price, total/current available counts, images (5 MB UI hint), search visibility |
| `4 - Room Management/تعديل تفاصيل الغرفة.png` | Room-type fields, images, amenities, monthly price and manual counts; at least five images is a UI recommendation, not a validated minimum |
| `5 - Analysis/إحصائيات الإيرادات.png` | Revenue, pending payments, paid invoices, channel/bank breakdown, period chart, transaction list, PDF/report export |
| `5 - Analysis/مطالبة مالية.png` | Resident/category/room/date, item quantities/prices, draft/final actions, tax/admin fee summary, medical-profile snippet, wallet charge hint |
| `6 - Settings/المعلومات الأساسية.png` | Names/description/logo, contact/website, map/location; map and address examples are inconsistent sample data |
| `6 - Settings/المرافق والخدمات.png` | Amenities/gallery, specialty selection, service toggles/custom service, preventive maintenance link |
| `6 - Settings/سعات العمل.png` | Weekly opening hours, holiday closure, multiple Family visiting windows, advance booking and outside-hours exception policies |
| `6 - Settings/التراخيص والشهادات.png` | Multiple document types/numbers/expiry/status, renewal upload (10 MB UI hint), email/SMS/medical-director reminder toggles, notification test, support/compliance indicators |

## Family booking/visit UI review — 2026-10-02

| Screen group | Observed behavior; sample content is not an approved business rule |
|---|---|
| Care Homes; filter دور الرعاية | Search, monthly-price range, facility-type filter/reset, cards with photo, star rating, capacity, monthly price, location and service tags. Filter sample lower/upper amounts are inconsistent; validate bounds. |
| Care Home Details | Gallery/rating, services, description, amenities, map, monthly price, visit and stay actions. Map sample is not authoritative geography. |
| Book a Visit; Visit Booked Successfully | Date/time selection, visitor contact, visitor count (1/2/3/4+), free one-hour visit copy, contact-to-confirm copy and summary. Exact count beyond 4, slot capacity and pending-versus-confirmed status need agreement. |
| 1- Book stay | Manual resident name/age and guardian contact; owner explicitly replaces resident fields with an elderly dropdown and elderlyId submission. |
| 2- Book stay | Multiple care needs and additional medical notes. Decide how existing elderly medical data is reused and what extra information may be supplied. |
| 3-Book stay | Single/shared/suite room-type selection with monthly EGP prices and proposed start date; no end/duration field. Per-bed versus whole-room charging and stay duration remain unresolved. |
| 4- Book stay | Resident/contact/care/room/price/start review; copy says contact within 24 hours, possible inspection, first payment at contracting. Payment copy conflicts with approved prepaid flow and must be aligned. |
| Stay Booked Successfully. | Request sent, pending review, resident/room/monthly-price summary and return navigation; this is not facility acceptance. |


## Repository evidence and scope

The earlier read-only scout found no Care homes domain/controllers/tests or operator account type. Existing caregiver review used SuperAdmin/ContentAdmin. The owner now explicitly includes correcting this to SuperAdmin/SupportAdmin in this slice; ContentAdmin is CMS-only. Recheck only relevant code deltas at execution, not the full historical audit.

The confirmed backend scope includes the supplied operator and Family screens, Admin API capabilities, caregiver rating/top-10 integration and the bounded caregiver authorization correction. It excludes frontend implementation, unrelated caregiver booking expansion and future feature plans. Family elderly selection sends elderlyId; payment precedes facility acceptance despite older screenshot copy. Notifications are in-app/email, not SMS.

[Main goal](../Project_Main_Goal.md) · [Handoff](../Mastermind_Handoff.md) · [Governance](../governance/AGENTS.md)
