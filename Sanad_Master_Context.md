# SANAD CARE — MASTER CONTEXT (File 1 of 2)
**Product truth. Changes ONLY by owner ruling. State/commands live in `Sanad_Operations.md` (File 2).**
Synced: 2026-09-17 · Released baseline: `main` @ `f787a5c2f1a22baddfd540b431f62ace7ed52d8f` (Slice C deployed; owner-confirmed)

**Latest closure:** Slice C is deployed. Owner reports build/tests passed, migration applied, Bruno 60/60 with exit 0, VPS caregiver-conflict preflight 0, and confirms `DEPLOYED`. Next workstream: approved booking cancellation/refund and attendance/elderly-report changes before Subscriptions (Phase S). These are recorded owner confirmations, not an independent VPS inspection. Older unassigned/unchecked entries below must be read with the later owner rulings and deployed slice records; remaining UI gaps are not implicitly closed.


**Continuation precedence — verified 2026-09-17:** Read the owner-approved 2026-09-17 cancellation/attendance/report section and consolidated Phase S package section before older screen inventories. Those sections supersede older pending questions and general roadmap wording. “Approved” means approved product behavior, NOT implemented/tested/deployed. Operations contains the current-code audit, gap checklist, open-decision boundary and continuation instructions. Do not ask the owner to repeat settled package, cancellation, role, report or distribution choices. Exactly two control files remain authoritative.

---

## 1. PRODUCT AT A GLANCE
Elderly-care platform (AR/EN). Verified surface: 189 endpoints / 21 controllers.
**Roles:** Family (Owner=1 / Editor=2 / Viewer=3) · Caregiver (Medical / Companion, hybrid allowed) ·
Elderly (SMS-login) · Admin (Super=5 / Content=6 / Support=7).
**Modules built:** Auth+OTP · Account settings · Families (members, dependents, medications, notes,
assessment quiz F-Q, invitations, bookings+Paymob, access log, leave/transfer, delete) ·
Caregivers (onboarding, profiles, certificates, pricing, schedules, discovery, self-delete) ·
Admin (81 endpoints) · Public (lookups, splash) · Support tickets.

## 2. OWNER DOMAIN LAWS (locked rulings — never re-litigate)
1. **Anonymize & retain** — account/family deletion never hard-deletes (healthcare/legal retention).
2. **Q1=B** — user chooses which account to delete; hybrid self-delete = caregiver side only.
3. **Medical ↔ Companion exclusivity** — one user may NOT hold both (enforcement = account-switch slice).
4. **Family Owner cannot leave without transferring ownership** (409 OwnerProtected).
5. **Elderly edits profile image only.**
6. **Delete guard** — no account deletion with active bookings (built) or unsettled earnings (Phase I).
7. **Bruno red line** — destructive/happy-path mutating calls never in collections; negative-first only.
8. Repo carries **owner-named commits only**; AI authors never on `main`.
9. **Notification channels: email + in-app ONLY, never SMS** (SMS stays for security OTPs only).
10. **Notifications = one superset preferences object**; each role's screen shows its own toggles.
11. **Elderly emergency call v1** = contact card: family owner + caregiver of active/upcoming
    booking (if any). Full SOS broadcast stays Phase I.
12. **Billing:** change-payment-method = YES · invoice PDF = server-side, ONE fixed branded
    template, final Phase S modification · NO admin-configurable PDF formats.
13. **No app-store rating feature** (owner ruling 2026-09-13).
14. **Delivery order (owner ruling 2026-09-13):** Settings & Profile workstream → Subscriptions
    slice → owner-led FULL-APP UI review (find anything missing) → then Phases G/H/I.
15. **SET-12/SET-13 CMS rulings (owner-confirmed 2026-09-14):** admin writes use existing
    `CmsContent` (SuperAdmin + ContentAdmin only); legal lifecycle is Draft → Published → Archived
    with one current published version per document type/audience and retained history; support
    phone + email is one global hotline for all app roles.

### Slice C clarification — owner-confirmed 2026-09-15
Account switching is dashboard/experience selection, not strict active-role authorization. Family/caregiver hybrids retain both owned permissions; preserve existing authorization behavior. Medical/Companion exclusivity remains mandatory. Original delivery order remains unchanged; earnings stays Phase I.

### Phase S owner decisions — consolidated 2026-09-16 (documentation authorized)
This section supersedes the earlier preliminary Phase S rulings. Owner explicitly authorized updating both control documents, then a read-only explanation of current booking cancellation/refund implementation before supplying modifications. Confirmed decisions below are product truth; unresolved items are NOT silently locked or implementation-ready. No subscription implementation authorized by this documentation update.

**Delivery/order:** Installed Android and iOS customer apps for families, caregivers and seniors; eventual Apple App Store and Google Play launch; Egypt-only initial market, EGP. No customer website/PWA authorized. Preferred/required provider is Paymob for bookings AND subscriptions; no owner approval for Apple/Google billing. Digital-feature subscription store-policy compatibility remains unresolved and must be resolved before billing implementation/release claims. Development/testing or delayed publication does not waive eventual distribution requirements. Recurring merchant enablement, supported payment methods, replacement, retries and proration execution remain unverified.

**Current sequence:** documentation and current-code audit completed → approved booking cancellation/refund corrections and hardening → approved attendance/visit reports and elderly medical reports → Phase S subscriptions → UI review/approved gaps → G/H/I. The owner supplied and approved the senior/report changes in the 2026-09-17 section below; do NOT ask for them again. Arrival/check-in was audited: existing start/complete primitives exist, full report workflow does not.

**Subscription rules:** One subscription per family, device-independent, covering family members and elderly profiles subject to role authorization. Owner alone manages financial actions. Auto-renew required. Recommend keeping current plan until renewal before upgrading; if proceeding immediately, prorated upgrade required (exact calculation/anchor/credit/failure rules unresolved). Downgrades take effect at next renewal. Ordinary cancellation turns off renewal without a cancellation-only refund; paid access continues through period end, subject to legal/provider refund exceptions. Seven-day payment-failure grace with email/in-app notifications is owner intent; retry/recovery mechanics remain to be specified. Free fallback is proposed, with retention of records/bookings; overflow handling needs final detail. Payment-method change required. Fixed branded server-side billing invoice PDF remains final Phase S modification, no admin-defined formats.

**Initial packages (owner-confirmed):**
| Benefit / limit | Free | Premium | Premium Plus |
|---|---|---|---|
| Price | EGP 0/month | EGP 299/month | EGP 2,499/year |
| Chatting | Included | Included | Included |
| Library | Included | Included | Included |
| Community/forum | Included | Included | Included |
| Family Activity Timeline | Not included | Included | Included |
| Basic forum/caregiver search | Included, no numerical quota | Included | Included |
| Advanced search filters | Not included | Included | Included |
| Medical-summary export and secure sharing | Not included | Included | Included |
| Premium content | Not included | Not included | Included |
| Family members | 3 | 10 | Unlimited |
| Bookings | 5 per monthly allowance period | 20 per monthly billing period | Unlimited |
| Rollover | None | None | Not applicable |
Higher plans include lower-plan benefits. No invented annual Premium/monthly Premium Plus. Every plan presentation shows ALL benefits, including unavailable/restricted ones. Plan configuration does not prove an advertised feature is already implemented. Family-wide restrictions apply to everyone; when chat is disabled, in-app chat/calls are unavailable for all family members, still subject to release availability and permissions.

Free allowance resets monthly without payment (proposed anchor: Free activation date); no unused booking rollover on any plan. Numeric booking allowances replenish, not lifetime caps. Membership is capacity, not replenishing slots. Owner confirmed members, not separate families. Exact membership counting/invitation/elderly-capacity policy still needs precision (proposal includes Owner and excludes elderly dependent profiles). Booking counted milestone, grace/upgrade allowance behavior and restoration after cancellation/refund remain unresolved and must align with the upcoming booking rules. Refund and allowance restoration are separate decisions. Free search uses basic functionality with paid advanced filters, not a numerical search cap/result truncation; actual advanced filters still require inventory/approval.

**Medical/log scope:** Keep existing Owner-only Medical Access Log separate from broader authorized family-visible Family Activity Timeline (owner's تسجيل الوصول). Never expose private medical audit contents via shared timeline. Paid Medical Summary interpreted from owner acceptance as PDF export and secure sharing, not denial of existing medical-record access. Secure sharing details/consent/expiry and exact summary fields remain to be bounded; no AI diagnosis implied. Medical PDF is distinct from billing invoice PDF. Caregiver arrival/check-in is NOT this timeline.

**Promotions:** Admin-configured percentage offer for selected plan with start/end claim window; discount lasts ONE billing period, then ordinary renewal price. Targeted expiring coupon sent via email/in-app to Owner for the family; once per family, one discounted period, no stacking. Campaign eligibility/verified-payment redemption/idempotency and marketing preferences required; detailed operational choices remain bounded design work. Examples such as 30% off EGP 300 are illustrative, not new package prices. Native-store promotion restrictions remain part of unresolved distribution/provider compatibility.

**Plan management:** Versioned edits; preserve old price/benefits for continuing grandfathered subscriptions, not only current paid period. Deactivated legacy plan closed to new sales but remains supported for existing subscribers. Admin can manage initial names/prices/benefits/limits and publication through appropriate versioning. Exact lapse/rejoin/recovery grandfathering rules pending.

**Screen scope retained:** Admin EGP revenue/subscriber analytics (including Free), active-plan count/latest modification, plan cards with prices/cycles/benefits/limits/subscribers, bilingual names, edit/deactivate, dynamic old/new actor/timestamp audit, CSV reports (definitions/columns pending). Family plans/offers/current benefits/status/renewal/price/payment-method visibility; financial actions Owner-only. Broader billing visibility, metric semantics, invoice/legal numbering details, advanced filters, summary content/sharing, full proration and failure handling remain open. This record is NOT a declaration that the full billing contract is locked.

### Booking cancellation, attendance and elderly reports — OWNER APPROVED 2026-09-17
Owner: “I Approve everything” after the focused cancellation/report recommendations. This locks the product decisions below, not unchosen early-termination financial rules, future clinical thresholds, unresolved subscription billing/provider compatibility, or an implementation/release verdict. Supersedes earlier pending/unspecified statements on these topics.

**Pre-visit cancellation policy:** Applies to accepted bookings BEFORE visit start. Family Owner/Editor may cancel; Viewer cannot. Before caregiver acceptance, family cancellation gives full refund if paid, no caregiver flag, retained “Cancelled by family” history. Caregiver rejection before acceptance gives full refund if paid, no flag, retained “Rejected by caregiver” history visible to admin. After acceptance, caregiver cancellation before start gives full refund and exactly one durable caregiver incident flag. Family cancellation less than 60 minutes after server-recorded acceptance gives full refund; at exactly 60 minutes or later gives NO refund. Family cancellation never gives a caregiver flag. Full refund means full captured booking amount including platform fee; unpaid cancellation cannot refund uncaptured money. Once visit is InProgress, neither ordinary cancellation nor this one-hour rule applies: separate early-termination/admin-review workflow, financial treatment not yet chosen. No invented earnings allocation or automatic suspension.

**Reasons/history:** For either side cancelling an accepted booking, reason category AND nonempty note are mandatory. Categories: Emergency / حالة طارئة; Medical issues / أسباب صحية; Transportation issues / مشكلات المواصلات; Account deletion / حذف الحساب; Other / أسباب أخرى. Family, assigned caregiver and authorized admin can see cancellation reason. Account deletion selection is a reason only, never an account deletion or bypass of active-booking deletion guards. Preserve actor/action/timestamps/acceptance snapshot/reason independently of booking refund status. Maintain separate refund entitlement (full vs none), processing state, caregiver flag and admin action. A caregiver flag is an incident for discretionary admin review, including illness/emergency; it is not automatic suspension. Refund success must not erase flags or rejection/cancellation history. Rejections and family cancellations remain visible in caregiver-associated history without becoming flags.

**Attendance / إضافة تقرير زيارة:** Assigned Medical and Companion caregivers can submit visit reports. Recommended approved first version uses server-recorded start/check-in and end/check-out, no mandatory GPS/family OTP. Caregiver-reported attendance is not independently verified presence. Store arrival and departure (وقت الوصول / وقت المغادرة) separately from report submission time; late writing must not extend recorded attendance. Original times and audited correction reason/actor/time retained; prevent duplicate actions, departure-before-arrival and foreign-booking submissions. Offline/late entry requires a separately bounded correction flow, not silent backdating. Existing /start and /complete are primitives to extend, not proof the report UI is supported.

**Reports / التقارير:** Separate dated records linked to booking, elderly and family, NOT overwrites of medical profile or ordinary family notes. One family screen with All / Medical reports / Visit reports filters (الكل / التقارير الطبية / تقارير الزيارة). Both types: caregiver AR/EN full name, applicable title/specialization preserved at publication, visit/measurement time and submission time, notes, optional private photo with appropriate consent. Visit report: arrival/departure, observed elderly condition, activities/notes, optional photo. Medical report: Medical caregivers only within verified professional scope; blood pressure systolic/diastolic mmHg, pulse beats/minute, temperature °C, measurement time, author assessment, notes, optional photo. Companion caregivers do NOT get structured medical-report entry in v1; describe observations in visit reports. Medical caregivers may write both report types for same booking without duplicating attendance. Missing measurements display Not recorded / غير مسجل, never zero/normal.

**Assessment:** Author-selected, not automatically diagnosed: No immediate concern observed / لا توجد ملاحظات مقلقة حاليًا; Needs follow-up / يحتاج إلى متابعة; Urgent concern / يستدعي تقييمًا عاجلًا; Not assessed / لم يتم التقييم. Label as caregiver-recorded assessment; general assessment does not establish every reading as normal. Validate entry structure/units/times without inventing universal clinical safe/danger thresholds. Future automatic flags require qualified clinical review and explicit scope. Urgent concern submission does not summon emergency help; report/notification is not emergency response. Do not imply diagnosis/prescribing beyond verified scope.

**Access/retention:** Family Owner, Editor AND Viewer may read their family's elderly reports, notes and photos. Assigned caregiver authors only their booking's reports. No public reports/photos; authorized access and audit, publication corrections retained/versioned, no silent overwrite/hard deletion. Reports remain readable without Premium; paid medical-summary PDF export/secure sharing remains separate. Existing Owner-only Medical Access Log is unchanged; do not confuse reports, caregiver attendance and broader Family Activity Timeline. Exact report count/edit windows, required measurements, media validation/storage/retention and admin medical-report access must be bounded during implementation design without silently expanding access.

**Order:** These approved booking/attendance/report changes and hardening precede subscription implementation. Operations holds full gap checklist and static audit. Documents updated only; no worker/code/migration/deployment performed under this approval.

### Owner-approved staged worker delivery workflow — 2026-09-17
**Stage checkpoint (2026-09-18):** B1-A (domain policy/facts + tests), B1-B1 (persistence: EF mapping + append-only save guard, 57/57 suite, reviewed migration `AddBookingCancellationFacts`), and B1-B2 family-cancel activation — production `c651e33b`, independent tests `2a690063` (owner gate 187/187), negative-first Bruno gate adjudicated GREEN with fact-row SQL evidence, gate collection `4c4fd09a` — are ALL MERGED AND CLOSED on `main` at `4c4fd09a434b1d7ff310532fb8563c66349a117a` (single branch, PRs 21/22 merged-by-ff, all side branches deleted). Live in the family cancel path: Owner/Editor role gate (403 viewer), category + non-blank note mandatory on Confirmed cancels (400 shapes) and category rejected pre-acceptance, policy-first `Decide` on an untouched aggregate, refund only on `FullCapturedRefund` entitlement (kept-money NoRefundDue does no provider call), exactly one fact via `IBookingCancellationFactRecorder` in one atomic save, 409 on the unique-index race. Known interim until B1-C: policy-denied cancellations still render the legacy Failed-style refund derivation in read models and admin refund-retry is ungated — safe only because nothing deploys mid-chain. IN FLIGHT: B1-B3 assigned-caregiver confirmed-cancel endpoint + decline/reject-path facts (worker prompt issued, B1-booking-cancellation/prompt-b1b3-implementation.txt; worker PR pending); then test worker -> caregiver Bruno gate -> closure; later hardening slices (refund claim/concurrency, expiry, notifications); B1-C (admin read models/refund-retry gating) must close before any deploy. No deployment, no migration application; deployed baseline remains `f787`. Standing rules: owner may fix mechanical build-blockers in flight (usings, approved tokens, ctor-arg alignment) with same-reply reporting, logic-adjacent always stop-and-report; Bruno cancel gates are one-consumable (seed one unpaid pending booking per run; framework 404 on empty id = no-seed signal, not a contract failure). Owner controls compile preflight, migrations, full gates, merge and deployment. Operations holds the full execution ledger.

The interrupted unpublished all-in-one B1 worker is superseded. Start clean from released main f787a5c; do not wait for/import its unsubmitted work. Trial separate production-code and test-writing workers NOW, rather than waiting for old B1 deployment. Each session has a small coherent scope; no phase-wide implementation. Active first assignment is B1-A domain-only policy/facts, followed by coordinator review and an independent test worker against the exact stable source revision. Application/API/persistence/refund safeguards and read models follow in separately bounded stages with tests. Domain-only/partially wired code is NOT independently mergeable/deployable. Approved business rules and full hardening checklist remain unchanged.
Coordinator reviews implementation -> fixes -> pins revision -> test worker in separate workspace/task branch -> coordinator reviews/integrates production+tests on one unmerged feature -> owner pre-merge compile/migration/full gates -> authorized final merge/recommit -> final main verification -> owner deployment confirmation -> safe branch cleanup. Draft PR only if authorized publication is available; otherwise complete files/patch with exact baseline, never claim unpublished code is in GitHub. Worker does not execute runtime commands, migrations, merge or deploy. Canonical author+committer must be established before approved final integration, not repaired by rewriting published main afterwards. No GitHub Merge button. Coordinator gives short state-guarded operational commands at each actual step, not speculative branch switching/pulling now. Detailed owner fetch/checkout/main-update timing is in Operations.

### Owner execution guidance rule — confirmed 2026-09-17
**Pacing clarification:** Provide all currently safe, state-verified steps in one bounded batch with explicit success gates and stop-on-error instructions. Owner reports results/failures together; do not require a reply after every command. Do not invent later merge/deployment steps before required evidence exists.

Coordinator must proactively produce a ready-to-send prompt.txt whenever worker review requires corrections, without waiting for owner to repeat a request. Keep scope bounded and current revision pinned; preserve existing owner execution/merge controls.

Current and future coordinators MUST provide easy, step-by-step operational guidance tailored to verified CURRENT repository state, not a static command recipe. Before advising local/GitHub-changing operations, establish the relevant owner checkout/branch/HEAD/dirty state and live remote/PR target; assistant workspace is not owner checkout. Give short normal commands for one bounded next action, explain expected result and stopping condition, inspect returned evidence before next dependent action. Do not guarantee commands against unseen state, repeat completed work, discard changes, or guess branch/commit identities. Preserve owner approval/control and existing ban on owner-run file-edit scripts. Operations contains procedure details; another mastermind must follow this without asking owner to restate it.

### Consolidated PR review rule — owner-confirmed 2026-09-17
Every coordinator/mastermind must review the whole bounded PR change and relevant dependencies before sending corrections. Consolidate all findings identifiable at that revision into ONE prioritized, ready-to-send worker prompt, not serial one-issue requests. After correction, review the complete changed surface and regression interactions, not only whether requested lines changed. Owner must not repeat this preference. This is disciplined review, not a guarantee of zero later findings: disclose genuinely new regressions or test-discovered defects, consolidate them too, and never suppress a correctness/security issue to avoid another pass. Keep fix scope bounded; if too large for one worker session, provide one consolidated plan with explicitly sequenced small assignments rather than overloading the worker. Current PR17 b257bd1 already passed static review to independent tests; do not reopen or resend fixes without new evidence.

### Small-fix ownership clarification — owner-confirmed 2026-09-17
Coordinator handles small test/build errors and analyzer cleanup directly with owner using complete corrected files or authorized existing-branch delivery. Do NOT create worker prompts or extra PRs for minor corrections. This narrows earlier automatic-correction-prompt rule: use worker prompts for substantial worker-scoped rework, not trivial fixes. Preserve no owner-run file-edit scripts, dynamic state-checked Git guidance and existing owner merge/deploy controls. Consolidate small fixes into existing feature/PR; do not rewrite main or discard local edits.

### Delivery simplification — owner correction 2026-09-17
Keep ONE active feature PR; separate implementation/test roles do not require a new PR for every stage/session. Consolidate reviewed B1-A implementation+tests and reconcile owner local state before further assignments. Premature B1-B1 prompt withdrawn. Use separate test workspace with patch/file delivery into existing feature where appropriate; no extra PR without explicit need/owner approval. Small corrections coordinator+owner, larger corrections same worker/current PR. Batch state-safe instructions, no needless checkpoints or branch proliferation. Preserve bounded work and all financial/verification safeguards; consolidation of feature is not merge into main. Supersedes earlier default stacked PR instructions. Operations carries exact cleanup state.

### Owner requested foundation merge / main-only cleanup — 2026-09-17
Owner now explicitly requests both existing PRs integrated and main-only cleanup before more work, superseding the prior rule to wait for all B1-B/C integration before merging B1-A. This is a requested DORMANT FOUNDATION milestone: new domain policy/types and test source only, NOT a declaration that approved booking cancellation/refund APIs, flags, persistence or provider safeguards are implemented. Exact combined source70eba860d245aab6fda990816fa8e41ac3e5f1a3 contains PR17+PR18 and canonical small assertion fix. Remaining business scope and full hardening checklist are unchanged. B1-B1 prompt withdrawn/no next worker started.
Foundation integration is now LIVE VERIFIED: PR17 and PR18 both merged; repository main is70eba860d245aab6fda990816fa8e41ac3e5f1a3. Branch cleanup remains pending; deployed baseline remainsf787 until owner confirms any later deployment. Coordinator currently read-only on GitHub; Operations records capability limitation, evidence gaps and safe integration requirements. Do not ask owner to repeat settled requirements, do not fabricate merge/deployment or test success, and do not discard local edits. Preserve all historical decisions/audits with latest precedence rather than losing information during cleanup.

## 3. SETTINGS SECTION SPEC (draft — owner screens will confirm/amend)
### Family member
| # | Screen | Status |
|---|---|---|
| A1 | Settings hub | ✅ (UI-side) |
| A2 | Account & profile (email/phone change → forced re-verify, avatar) | ✅ SET-1 |
| A3 | Language AR/EN | ✅ SET-2 |
| A4 | Notification prefs (checkIn, medication, booking, community) | ✅ SET-3 · per-channel push = Phase G |
| A5 | Security (password, active sessions, revoke) | ✅ auth |
| A6 | Medical access log (Owner only) | ✅ SET-6/F16 |
| A7 | Family management (members, invites, roles, transfer, leave) | ✅ SET-4/8a |
| A8 | Subscription & invoices | ⬜ Phase S |
| A9 | Privacy policy & terms (CMS) | ⬜ unassigned — owner screens decide |
| A10 | Help: support ✅ · FAQ ⬜ · rating ⬜ Phase I | mixed |
| A11 | Delete: family-delete ✅ SET-8b/8c · family-only SELF-delete ⬜ — owner screens decide | mixed |
### Caregiver
| # | Screen | Status |
|---|---|---|
| B1 | Hub | ✅ |
| B2 | Account & profile | ✅ |
| B3 | Visibility toggles (profile/rating/phone/location) | ⬜ unassigned — owner screens decide |
| B4 | Notifications (event categories, medical reminders) | 🟡 toggles ✅ · delivery = Phase G |
| B5 | Security | ✅ |
| B6 | Support | ✅ SET-7 |
| B7 | Earnings / wallet / payouts | ⬜ Phase I |
| B8 | Delete account (D11 guard, pure/hybrid) | ✅ SET-8d |
### Elderly
| C1 | Profile image edit only | ✅ |
### Admin (back-office)
Global settings ⬜ · support hotlines ⬜ · commission config ⬜ (15% hardcoded) · subscription
packages ⬜ Phase S · user management ⬜ · financial monitoring ⬜ Phase I.

## 4. SCREEN INVENTORY (owner-supplied — grows with each batch)
Owner statements are the spec; deviations found in code are gaps to fix.
Legend: ✅ built · 🟡 partial · ⬜ not built. Naming (AR/EN) minted per owner delegation 2026-09-13.

### BATCH 1 — 2026-09-13 (owner-supplied): Family settings · Elderly profile · Companion caregiver

**Section names (locked):** Family=إدارة العائلة · Subscription & Billing=الاشتراك والفواتير ·
App Settings=إعدادات التطبيق · Notifications=تفضيلات الإشعارات · Privacy & Security=الخصوصية والأمان ·
Privacy Policy=سياسة الخصوصية (incl. User Rights=حقوق المستخدم) · Terms=الشروط والأحكام ·
Access Log=سجل الوصول · Help & Support=المساعدة والدعم (Help Center=مركز المساعدة) ·
Earnings=الأرباح · Emergency Call=اتصال الطوارئ.

**F1 · Profile header + edit** (all family roles)
Shows: avatar, full name (AR or EN by language setting), role badge (Owner/Editor/Viewer), email.
Edit: full name AR + EN, email, phone. — ✅ BUILT (bilingual `ArabicFullName`/`EnglishFullName`
verified in `User.cs`; email/phone change → forced re-verification; avatar GET/PUT).

**F2 · Family management** (إدارة العائلة)
Owner: invite, change roles, remove members, transfer ownership (before leave/delete).
Editor: invite + book for elderly. Viewer: view only. Member card: full name (AR/EN), email, role.
Member profile view: avatar, full name, email, status, join date, last activity, role.
— ✅ BUILT (SET-11): invites/roles/remove/transfer/leave ✅ (SET-4/8a) · `FamilyMemberResponse`
includes avatar, status, last-activity (last login), role, names AR/EN, email, and `JoinedOnUtc`.
Role-permission matrix is locked: owner-only remove/role-change/transfer; owner/editor invite+book;
viewer read-only.

**F3 · Subscription & Billing** (الاشتراك والفواتير) — ⬜ **Phase S** (full spec recorded):
plan name · status (active/expired) · price · renewal date · payment type (card/wallet) ·
benefits 3–5 CMS-managed points · change plan · change payment method (owner Q pending) ·
billing list: invoice no `INV-YYYY-MM-######` · amount · date · downloadable PDF
(PDF = later final modification, mobile handles for now — owner note; server-side template TBD).

**F4 · App settings** — language AR/EN — ✅ BUILT (SET-2).

**F5 · Notifications** (تفضيلات الإشعارات) — ✅ BUILT (SET-10): five family toggles: daily check-in
alerts (تنبيهات تسجيل الوصول اليومية = `checkInAlerts`) · medication reminders · booking updates ·
community · family activity alerts (تنبيهات نشاط العائلة = `familyActivityAlerts`).

**F6 · Privacy & Security** (الخصوصية والأمان) — change password ✅ · session list with status ✅ ·
revoke one session ✅ (`DELETE /auth/sessions/{id}`) · logout / logout-all ✅. ALL BUILT.

**F7 · Privacy Policy** (سياسة الخصوصية) — ⬜ **SET-12**: CMS sections (title+description,
title+description+bullets), حقوق المستخدم section, admin-managed, versioned, per-audience.

**F8 · Terms & Conditions** (الشروط والأحكام) — ⬜ **SET-12**: multiple text sections, per-role
(caregiver gets role-specific terms), same CMS.

**F9 · Access Log** (سجل الوصول, Owner only) — ✅ BUILT (SET-6): entries = action + actor name +
relative time (e.g. عرض الملف الطبى · مسعد أحمد · منذ ساعتين). Verified: owner's examples map 1:1
to `ElderlyActivityType` enum (ViewMedicalProfile/UpdateMedications/ShareMedicalProfile/AddNote/
ScheduleAppointment/ReviewMedications). Nothing static — all real events.

**F10 · Help & Support** (المساعدة والدعم — ALL roles) — contact support (ticket ✅ SET-7) ·
call phone + send email ⬜ **SET-13** (admin-configurable hotlines) · Help Center + common
questions (FAQ) per user type, admin CMS ⬜ **SET-13** · ~~rate app~~ REMOVED by owner ruling
(app-store concern, not needed).

**F11 · Account actions** — add another account + dashboard switching ✅ (Slice C deployed) · logout ✅ ·
**remove account (family-only self-delete) ✅ SET-15** — owner screens settled D1: BUILD IT.
Rules: anonymize & retain · sessions revoked · Owner must transfer ownership first (409).

**E1 · Elderly profile** — full name ✅ · picture ✅ (`GET dependents/{id}/photo`) · phone ✅
(exposed from the identity user in **SET-11**) · edit = profile pic ONLY ✅ ·
**Emergency call (اتصال الطوارئ) with assigned caregiver name+phone ⬜** — no caregiver-assignment
concept exists; v1 design = owner decision (prompt raised). Full SOS broadcast stays Phase I.

**CG1 · Companion caregiver profile** — pic ✅ · ratings ⬜ Phase I · professional title ✅ ·
years of experience ✅ (`YearsOfExperience` verified).
**CG2 · Edit info** — names AR/EN ✅ · email/phone ✅ · DOB ✅ · gender ✅ · specialization ✅ ·
bio ✅ (`Biography`) · professional title ✅ · pricing ✅ · schedule ✅ · service areas ✅ (`AreaIds`).
**CG3 · Earnings (الأرباح)** — ⬜ **Phase I**: this day/week/month/total + transactions list
(family name · amount · date · status pending/failed/successful). Spec recorded; ledger = Phase I.
**CG4 · Notifications** — new orders ✅ toggle · booking updates ✅ · messages from families ✅ toggle
· system notifications ✅ toggle (delivery = Phase G) → **SET-10** role-aware superset. Channels:
**email + in-app ONLY, never SMS** (owner law).
**CG5 · Privacy toggles** — ✅ BUILT (SET-14): show profile / show rating / show phone / share
location. The show-rating toggle stays hidden until Phase I ratings ship. Change phone ✅ · sessions ✅.
**CG6 · Terms for caregiver role** — ⬜ SET-12 (per-audience).
**CG7 · Help & Support + add/remove account + logout** — same as F10/F11 (caregiver self-delete ✅
SET-8d; add account + dashboard switching ✅ Slice C deployed).

### BATCH 2+ — awaiting owner (medical caregiver, admin, bookings, discovery, onboarding…)

## 5. PHASE ROADMAP
- ✅ Phase A/B/B2/C/D/E — caregivers domain, auth, admin+splash, onboarding, families, bookings+Paymob
- ✅ Phase SET (SET-1→8d) — accounts, settings, preferences, security, deletions — DEPLOYED
- ✅ Test-lane backfill (SET-8e-B): merged, local gate **40/40 green** 2026-09-13 — DEPLOYED
- 🟡 **1) SETTINGS & PROFILE WORKSTREAM** (owner-ordered first; one PR per slice):
  · Slice A1: SET-10 notification superset + SET-11 member/elderly cards + role-matrix lock tests
    — ✅ deployed in `02ed88f`
  · Slice A2: SET-14 caregiver visibility toggles + SET-15 family-only self-delete
    (leave+anonymize&retain, owner-transfer 409) — ✅ deployed in `fe5238c`
  · Slice B: SET-12 legal docs (privacy policy sections + حقوق المستخدم + terms, versioned,
    per-audience) · SET-13 help center (per-role FAQ + one global admin hotline: support
    phone/email) — ✅ deployed by owner from `76e4d946`; build/tests passed; Bruno 53/53;
    **DEPLOY: YES**. Process retrospective recorded in File 2.
  · Slice C: account add/switch ("add another account" screen) + medical↔companion exclusivity
    — ✅ DEPLOYED, owner-confirmed 2026-09-15, main `f787a5c`. Dashboard switching retains both
    owned permissions; new account claims require normal token refresh. Domain guard and unique
    filtered DB index enforce caregiver-type exclusivity. Owner migration committed/applied;
    build/tests passed; Bruno 60/60, exit 0. No earnings/wallet/payout scope pulled forward.
  · Planned A1/A2/B/C slices are closed; remaining screen gaps/deferred work stay tracked.
- ⬜ **2) SUBSCRIPTIONS SLICE — NEXT** (Phase S, pulled forward per owner): plans, status, price, renewal,
  payment type, CMS benefits, change plan/method, billing list INV-YYYY-MM-###### + server PDF
- ⬜ **3) FULL-APP UI REVIEW** (owner walks every screen; batches 2+; gaps → slices)
- ⬜ **4) Phases G / H / I** (chat+calls+notification delivery per locked channel rules · care homes only · earnings ledger+ratings+SOS)
  · **Phase H owner ruling — 2026-09-16:** implement Care Homes only. Marketplace is excluded
    from the current implementation plan, not permanently deleted from the product possibilities.
    Retain Marketplace as a future reconsideration note; do not implement or schedule it unless
    the owner explicitly changes the plan.

### Narrow owner exceptions — 2026-09-15
- Owner explicitly accepts the GitHub author/committer identity of Slice C merge `107ae449a9706d4619dd572b83d5564147ee75c4` as a one-time exception. Keep its history unchanged; canonical identity requirements still apply to future commits.
- Owner explicitly permits one owner-run EF migration-generation command for `EnforceCaregiverAccountExclusivity`. This does not authorize source-edit scripts, database updates, merge/deploy commands, or general command blocks. Verify the actual owner checkout first; generate only, then review the migration files before application.

## 6. RULES OF ENGAGEMENT
**Owner ruling (2026-09-15, reinforced): NEVER generate user-run commands or scripts for editing files.** This covers source, tests, configuration and documentation: no scripted replacements, shell writes, patch-application commands or equivalent file-editing instructions. Assistant edits workspace files directly and delivers complete corrected files for GUI replacement, or uses authorized GitHub delivery. This supersedes all historical scripted-edit/patch handoffs. The owner identified the already-issued Git commit/push block as the final command block; do not issue further command blocks unless the owner explicitly revises that instruction. Git staging/commit/push do not rewrite source contents, but do change repository metadata and remote state; explain that distinction honestly. Build/test, migration, merge and deployment gates remain required and owner-controlled, not silently waived by this delivery constraint.
Owner sends screens → mastermind verifies each function against live code → status table per batch →
owner confirms → gaps become slices (prompt → PR → audit → merge → gates → deploy).
Nothing the owner specifies is dropped; phase-deferred items are labeled, never silent.
Any slice that changes an EF model requires an owner-generated migration before the standing build/test/Bruno gates; the worker never creates migration files. The owner must run a compile preflight before `dotnet ef migrations add`; if compilation fails, stop for a repair PR and do not generate a migration from a broken tree. When the owner asks for a repository check, inspect the actual repository, branches/ref SHAs, files/tree, commit authors/committers, and implementation before giving commands; every command block must match the verified OS, shell, path, branch, and current state. Bruno request counts and folder lists must be derived from the current repository immediately before issuing the gate command; never reuse a stale expected count. The active control plane is exactly these two files: File 1 is product truth and File 2 is operations, contracts, audits, verdicts, and retrospective; do not create additional active project-control files. Worker instructions are delivered as a prompt file inside the current phase folder — a delivery artifact, never a third control authority — and their issuance/state is recorded in File 2. **Workspace hygiene rule (owner-locked 2026-09-18):** every generated phase deliverable (worker prompts, correction files, archives, review scratch) lives in ONE workspace folder named for the current phase/stage (e.g. `B1-booking-cancellation/`); the ONLY files kept at the workspace root are the two active control files; `uploads/` is the owner's input area and is never auto-cleaned. Dead or superseded files are cleared as soon as they stop being used — every mastermind applies this without being asked, at session start and on phase closure.
