# Notifications slice — execution handoff

## Goal
Deliver the owner-approved backend Notifications slice per `plan.md`, Lane A (Push, Codex) and Lane B (email overhaul, assignee TBD).

## Current state
Lane A and Lane B are implemented on `codex/notifications-lane-a`; base `fe91d64d0f8b753d336f2fe8d3dc6b35e1a1e82b`. Lane B implementation SHA: `b3af6a936888c5a92d711e85932a4f585c75f2dc`. Lane A registration-token correction SHA: `1bfa056523e9d9e70ea0829ce0db07e7ad5e2872`. Per `tasks.md`, reports identify implementation SHAs; the Integrator verifies each is an ancestor of the branch tip and reviews the complete branch range, including closeout commits.

## Evidence and open items
All five Lane A and three Lane B reports exist. Latest verification at `1bfa056523e9d9e70ea0829ce0db07e7ad5e2872`: solution build with `--warnaserror` passed with 0 warnings/errors; focused Notifications/email tests 55 passed; full suite architecture 1 passed and unit 2,682 passed, 3 skipped, 0 failed; `git diff --check` and both changed Postman JSON validations passed. FCM now uses the owner-approved registration-token contract via `Message.Token`; a scoped CS0618 suppression is required by FirebaseAdmin 3.7.0. No live Firebase call; SMTP accepted the 18 test samples noted below. Migration was authored only. OTP/invitation remain direct Identity sends, not outbox rows. Effective runtime: GPT-6 Codex desktop; exact runtime build identifier is not exposed to the repository shell.

## Local integration
The Notifications branch was merged into local `main` with merge commit `9005f145a4bdec7a24f86e50452b72547cd0d3cd`; Lane A implementation/report commits precede Lane B in the branch history. All eight Ready items record that merge SHA. `origin/main` remains unchanged pending explicit owner confirmation to push.

## Post-integration follow-ups
- After Lane A then Lane B reach `main`, verify the additive notifications migration against an explicitly authorized disposable database; do not apply it during branch implementation.
- Run a real Firebase send only after per-run owner authorization and with the designated test device; otherwise retain Development-stub evidence. No service-account key or token belongs in reports or logs.
- Confirm runtime host configuration and processor health on the authorized deployment environment; deployment/VPS changes remain owner-controlled.
- Revisit the event matrix rows whose producers were absent from the implementation base (caregiver bookings, subscriptions/payouts, community moderation, and chat) when those producer slices are available. Templates alone do not enqueue those events.
- Decide whether OTP and invitation delivery should remain direct Identity sends or move into the notification email outbox; current behavior is direct and is outside this completed slice.
- Pre-integration test attempt (2026-10-10): focused email-template/push tests passed (47/47, no skips). Per the owner's instruction, the actual renderer and SMTP sender submitted 18 synthetic sample emails to the seeded Family Owner address: all 16 base template kinds plus upcoming-renewal and retired-plan copy variants. SMTP accepted all 18; inbox delivery remains for the owner to confirm. Sample bodies and credentials were not logged. The user-scope Firebase setting is present but did not validate as service-account JSON or as an existing file path on this workstation; its value was not displayed. No device token/FID is registered yet.
- FirebaseAdmin 3.7.0 marks `Message.Token` obsolete in favor of `Message.Fid`; the owner confirmed the plan's registration-token contract. The sender uses `Message.Token` with a narrowly scoped CS0618 suppression. Real provider delivery remains unverified until the owner authorizes a per-run send and provides a test device/token.

## Next action
Obtain the owner's explicit confirmation before pushing local `main` to `origin/main`. Keep the live Firebase send gated on per-run authorization and a designated test device.
