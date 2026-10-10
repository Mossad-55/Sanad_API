# Notifications slice — execution handoff

## Goal
Deliver the owner-approved backend Notifications slice per `plan.md`, Lane A (Push, Codex) and Lane B (email overhaul, assignee TBD).

## Current state
Lane A and Lane B are implemented on `codex/notifications-lane-a`; base `fe91d64d0f8b753d336f2fe8d3dc6b35e1a1e82b`; implementation SHA `b3af6a936888c5a92d711e85932a4f585c75f2dc`.

## Evidence and open items
All five Lane A and three Lane B reports exist. Build passed with 0 warnings; full verification: architecture 1 passed, unit 2,682 passed, 3 skipped; Postman JSON and diff checks passed. No live Firebase/SMTP call; migration was authored only. Lane A is not yet in `main`, so Lane B cannot integrate first. OTP/invitation remain direct Identity sends, not outbox rows. Effective runtime: GPT-6 Codex desktop; exact runtime build identifier is not exposed to the repository shell.

## Post-integration follow-ups
- After Lane A then Lane B reach `main`, verify the additive notifications migration against an explicitly authorized disposable database; do not apply it during branch implementation.
- Run a real Firebase send only after per-run owner authorization and with the designated test device; otherwise retain Development-stub evidence. No service-account key or token belongs in reports or logs.
- Confirm runtime host configuration and processor health on the authorized deployment environment; deployment/VPS changes remain owner-controlled.
- Revisit the event matrix rows whose producers were absent from the implementation base (caregiver bookings, subscriptions/payouts, community moderation, and chat) when those producer slices are available. Templates alone do not enqueue those events.
- Decide whether OTP and invitation delivery should remain direct Identity sends or move into the notification email outbox; current behavior is direct and is outside this completed slice.
- Pre-integration test attempt (2026-10-10): focused email-template/push tests passed (47/47, no skips). Seeded user addresses use `@test.sanad.local`, so they cannot receive mail at Gmail; SMTP settings exist in the process but no live send was made. The user-scope Firebase setting is present but did not validate as service-account JSON or as an existing file path on this workstation; its value was not displayed. No device token/FID is registered yet.
- The sender currently assigns its persisted `token` value to Firebase Admin 3.7.0's `Message.Fid`. Firebase documents `Fid` as an Installation ID and `Token` as a registration token; the plan/API call for registration tokens. A trial switch to `Message.Token` compiled with CS0618, violating the zero-warning gate, so it was reverted. Resolve the target identifier contract (and corresponding client/API input) before a real push send.

## Next action
Owner provides an exact Gmail recipient and decides whether the push contract should use Firebase Installation IDs (updating the approved API/client contract) or registration tokens (requiring an approved SDK/version path); then we can prepare the live test targets.
