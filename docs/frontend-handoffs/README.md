# Frontend reports and implementation handoffs

Owner-requested structure, established 2026-10-16. This is the audience-facing
communication index, not a second implementation backlog.

## Find a report

| Audience | Phase | Report | Meaning |
| --- | --- | --- | --- |
| Admin — Admin portal | FUR-00 — Alignment | [2026-10-16 reconciliation](admin/admin-portal/phase-00-alignment/2026-10-16-portal-report-reconciliation.md) | Ready for owner review/forwarding; not proof of frontend receipt or completion. |
| Admin — Care Home Owner portal | FUR-00 — Alignment | [2026-10-16 reconciliation](admin/care-home-owner/phase-00-alignment/2026-10-16-portal-report-reconciliation.md) | Ready for owner review/forwarding; not a mobile-app audit. |
| Admin — Admin portal | FUR-01 — Contract accuracy | [2026-10-16 contract accuracy](admin/admin-portal/phase-01-contract-accuracy/2026-10-16-contract-accuracy.md) | Accepted/deployed `f9231fb`; release update links receipt. Covers FR-20261016-30–35, 37, 39, 43, 45, 46 admin portions. |
| Admin — Care Home Owner portal | FUR-01 — Contract accuracy | [2026-10-16 contract accuracy](admin/care-home-owner/phase-01-contract-accuracy/2026-10-16-contract-accuracy.md) | Accepted/deployed `f9231fb`; inventory/visits/finance metadata and unchanged onboarding rules. |
| App — Shared (Family/Caregiver/Elderly) | FUR-01 — Contract notice | [2026-10-16 contract notice](app/shared/phase-01-contract-accuracy/2026-10-16-contract-notice.md) | Accepted release; convention notice only, no mobile frontend audited. Auth shapes and unread-count truth. |
| Admin — Care Home Owner portal | FUR-02 — Verification continuity | [2026-10-16 verification continuity](admin/care-home-owner/phase-02-verification-continuity/2026-10-16-verification-continuity.md) | Backend deployed `5cd3526`; signup recovery IDs, Expired resend, 502 retry rule, superseded recovery. Client adoption unconfirmed. |
| App — Shared (Family/Caregiver/Elderly) | FUR-02 — Verification continuity | [2026-10-16 verification continuity](app/shared/phase-02-verification-continuity/2026-10-16-verification-continuity.md) | Backend deployed `5cd3526`; shared auth wire notice only, no mobile frontend audited or adoption confirmed. |

App means **Family, Caregiver and Elderly clients only**, by owner direction.
There is no audience-specific mobile report for this portal intake; shared auth
or notification changes may later warrant a clearly scoped App handoff without
claiming that mobile screens were audited.

The [execution plan](../internal/work/portal-api-reconciliation/plan.md#frontend-follow-up-sequence--2026-10-16)
owns task status and approval boundaries. The
[full audit](../internal/work/portal-api-reconciliation/frontend-report-audit-2026-10-16.md)
owns the detailed evidence for the supplied report. Existing feature guides,
Postman and actual OpenAPI describe implemented API contracts; a proposed report
must not override them or claim that a new endpoint exists.

## Naming and audience rules

Use this layout for future frontend-facing issue reports and delivery handoffs:

```text
docs/frontend-handoffs/
  README.md
  report-template.md
  admin/
    admin-portal/
      phase-00-alignment/
        2026-10-16-portal-report-reconciliation.md
    care-home-owner/
      phase-00-alignment/
        2026-10-16-portal-report-reconciliation.md
  app/
    family/
    caregiver/
    elderly/
    shared/
```

Under each client, use `phase-XX-descriptive-name/YYYY-MM-DD-specific-topic.md`.
Only current report directories are created now; mobile directories are examples,
not evidence of reviewed or implemented work. Dates use Africa/Cairo. Admin
contains both web portal audiences; App contains only Family/Caregiver/Elderly.
An owner-portal finding must never be presented as tested mobile-client behavior.

- Use the active slice's phase number and a descriptive phase name. Include its
  full project ID in the report, e.g. FUR-01, to distinguish historical PORTAL-01.
- Keep independent topics in separate files within the phase, not one growing
  miscellaneous report. Initial alignment is an intentional cross-topic summary.
- Refer to original report numbers and stable issue IDs. This intake uses
  `FR-20261016-01` through `FR-20261016-46`; subissues may add suffixes.
- Internal worker briefs, private diagnostics and detailed test evidence stay in
  the active internal slice. These audience reports link to them after sanitizing.
- Shared findings can appear in both audience summaries, but link to the same
  canonical contract/evidence. Do not duplicate competing DTO definitions.
- A later delivery report links back to the alignment issue and forward to the
  implemented contract, tests, source revision and deployment receipt. An open
  promise is not implementation evidence.
- Do not move/delete historical files automatically. Date them and add a pointer
  when necessary; preserve the evidence they actually recorded.

## Required content

Use [the template](report-template.md). Every report distinguishes:

1. What the frontend is doing correctly.
2. What assumption/usage needs correction; distinguish source-contract mismatch
   from a frontend code defect proven by inspecting frontend code.
3. Confirmed backend problems or missing capabilities and the responsible slice.
4. Historical/already resolved claims versus current unverified observations.
5. Pending owner decisions, expected frontend action and required additional evidence.
6. Source, test and deployment truth, including exact revision when known.

Do not publish secrets, tokens, OTPs, personal records, raw production logs or
private backup paths. No sending to another thread/team is implied by creation.

## Delivery vocabulary

| Label | What may be claimed |
| --- | --- |
| Proposed | Contract/behavior is not yet approved; frontend must not depend on guessed DTOs. |
| Contract approved | Exact scope accepted; capability still may not exist. |
| Implemented locally | Source exists; state revision and actual tests. |
| Integrated | Accepted into the intended main revision; not synonymous with deployed. |
| Deployed | Exact target/revision verified; include receipt and runtime limitations. |
| Historically resolved | Existing evidence addresses an older complaint; not a fresh end-to-end test. |
| Needs evidence | Cause/current behavior not established; state exactly what is missing. |

Each file is a dated snapshot. On new evidence, add a dated correction section or
issue a linked follow-up; never erase the earlier limitation or rewrite an old
"proposed" report as though it had already been deployed. New reports must be
added to this index. Execution status remains in the canonical slice plan.
