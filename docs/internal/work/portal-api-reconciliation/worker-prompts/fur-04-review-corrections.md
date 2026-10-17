# FUR-04 — bounded Mastermind review corrections

Apply only the three corrections below to the existing uncommitted FUR-04
delivery. This is not a phase restart. Preserve the source pin
`14677d961813a94be602390e7801623c766ad53c`, all existing owner/mastermind
planning changes, and unrelated files. Inspect `git status`, `HEAD`, and the
current diff first. Do not commit, push, publish, deploy, begin another phase,
or perform any PostgreSQL/database action. No reset, create, drop, migrate,
seed, cleanup, or connection is authorized for this correction pass.

## C1 — Keep Admin-only dispute enrichment on the Admin read

Review found `CareHomeCheckInDisputeResponse` is shared by the Admin disputes
list/resolution and the Family dispute-submission response. The new resident
snapshot and original amount/currency fields are approved for the Admin dispute
read only. Keep the Family submission response wire shape unchanged; keep the
Admin resolution response unchanged too unless the existing contract explicitly
requires otherwise. Use a distinct Admin-list response/projection as needed,
without route or policy changes.

Add focused regression assertions proving:

- the existing Admin disputes list contains the approved resident snapshot and
  original `TotalAmount` fields;
- Family dispute submission does not serialize the new fields; and
- existing Admin resolution response shape remains unchanged.

Update any guide, Postman description/example, audience handoff, or report text
that implies those enrichment fields appear on Family submission or Admin
resolution responses. Keep this correction limited to response scoping and its
direct tests/docs.

## C2 — Describe currency accurately (owner-approved choice)

The owner approved the existing fixed `EGP` convention. `CareHomeBooking` does
not persist a currency property; do not add a migration or claim the response
reads a stored currency. Keep the current `originalBookingCurrency` response
value consistent with existing Care Home booking responses (`EGP`), and update
the delivery report, feature guide, Admin handoff, and any other directly
affected wording to say this is the current fixed Care Home currency convention,
not a currency stored on the booking. Do not change amount semantics: the amount
remains the booking's original `TotalAmount`, not refund/payout/net.

## C3 — Make the database setup deviation explicit in the report

The delivery report currently discloses that two unintended lowercase database
artifacts were created and dropped while preparing the approved exact target.
The approval was limited to `SanadFur04VerifyDb`, and the brief prohibited
drops. Correct the report to label this plainly as an **unauthorized database
setup deviation**. Record the exact unintended database names from your existing
execution evidence, what read-only evidence established they were empty before
they were dropped, and the resulting known state. Do not reconnect to any
database, inspect logs or credentials, repeat the setup, or perform further
cleanup. If exact names or pre-drop evidence cannot be established from already
available safe records, state that precisely; do not guess or fabricate.
Retain the fact that the authorized target's final readback was clean, while
making clear it does not erase the deviation. No new production/shared target
was authorized; do not claim or infer one was touched beyond the report's
existing evidence.

## Verification and return

- Run only build/focused checks invalidated by C1; no full Unit, Architecture,
  PostgreSQL, API-startup, Bruno, or provider run is requested. If a narrowly
  relevant check cannot run, report that rather than broadening execution.
- Validate edited JSON if any, relative documentation links affected by edits,
  and `git diff --check`.
- Update the existing
  `docs/internal/work/portal-api-reconciliation/reports/fur-04-worker-delivery.md`
  with a short correction record: exact changed files, commands/results, the
  response-scope outcome, EGP wording, and database deviation disclosure.
- Do not rewrite the whole delivery report, repeat completed phase gates, or
  mark FUR-04 Done. Release/deployment remains not performed and awaits
  Mastermind review plus separate owner release approval.

Return the updated saved report and stop for Mastermind re-review.
