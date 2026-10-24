# Mobile report follow-up — locked next slice

## Control record

- Owner approved and locked this scope on 2026-10-24 and directed work to start
  immediately after the current portal-report release is completed and verified.
- Phase: approved; implementation has not started. Release: not_requested.
- Activation dependency: verify the release receipt for
  [the current slice](portal-report-follow-up-2026-10-24.md), then pin the actual
  released revision and remaining local delta before implementation.
- Mastermind must resolve the exact file manifest and Worker model at activation.
  Reuse these settled requirements; do not repeat product intake.
- Scope approval does not authorize commit/push/deploy, Production configuration
  or database writes, real charges/refunds, or changes in the Paymob merchant
  account. Obtain exact-target authority before those operations.

## 1. Recurring subscription Card enrollment

Existing enrollment code selects Card 3DS and sends `subscription_plan_id` when
the selected plan has a provider subscription plan mapping. The public
`subscriptionCardEnrollmentAvailable` flag currently checks Card enablement plus
the presence of SecretKey, CardIntegrationId and Card3dsIntegrationId. It is not
proof of valid provider configuration or per-plan availability. The reported live
false value has not yet been independently diagnosed.

Required work:

- Inspect effective configuration on the exact reported deployment without
  disclosing credentials; identify the actual reason for unavailable enrollment.
- Verify merchant recurring capability and the correct Card 3DS integration.
- Audit offered plan mappings against environment, price, currency and billing
  interval. Respect published-plan immutability/versioning; do not silently
  rewrite existing sold terms or historical subscriptions.
- Verify webhook configuration and HMAC validation.
- Prove checkout, 3DS, verified callback, persisted provider subscription identity
  and paid subscription. Separately prove renewal handling and duplicate-callback
  protection; initial payment alone does not establish recurrence.
- Record evidence and environment precisely. Sandbox evidence is not Production
  recurrence evidence. Configure the real prerequisites under separate target
  authority; never force the public flag to true.

No new endpoint is presently required. Any newly discovered behavioral change
outside this contract returns to the Owner. Wallet remains deferred/unverified.

## 2. Viewer-specific post capabilities

Add `isOwn`, `canEdit`, and `canDelete` boolean properties to the mobile
`CommunityPostResponse`, consistently across:

- `GET /api/v1/community/posts`
- `GET /api/v1/community/posts/{postId}`
- `GET /api/v1/community/recommendations`
- `GET /api/v1/community/favorites`
- Create/edit responses using the same DTO.

Locked semantics matching existing app mutation contracts:

- `isOwn`: authenticated user matches the stored author.
- `canEdit`: own post in PendingReview or Published.
- `canDelete`: own post that is not Deleted.
- Another viewer receives false for all three. Admin moderation is governed by
  its separate Admin routes; these flags do not grant app moderation powers.
- Compute using server-authenticated identity and stored ownership. Anonymous
  authors may see their own capabilities, but author identity, name and avatar
  remain hidden under the existing anonymity rules.
- Keep mutation authorization authoritative; flags describe the current snapshot
  and do not bypass validation or concurrent-state checks.
- Editing a published post returns it to pending review; document the resulting
  disappearance from published reads. Do not broaden unpublished-post visibility.

Acceptance: owner/other-viewer and anonymous/non-anonymous coverage on every read
surface, consistent create/edit responses, lifecycle/auth alignment, and updated
OpenAPI, consumer docs and Postman examples. No schema migration is expected.

## 3. Completion notes contract

The accepted request is `POST /api/v1/caregiver/bookings/{bookingId}/complete`
with `{ "notes": "..." }`; notes are optional and success is 204 No Content.
The mobile app already sends the correct name.

- Correct the stale `caregiverNotes` request in
  `docs/postman/app/Sanad.App.Caregiver.postman_collection.json` to `notes`.
- Clarify request `notes` versus booking-detail response `caregiverNotes`; retain
  the existing response field. Do not introduce a needless API rename or alias.
- Verify the submitted notes persist and are returned through booking readback;
  a success status alone is insufficient because notes are optional.

## Completion and next action

At activation, save the exact implementation manifest and verification commands
here. Complete scoped implementation, relevant tests and synchronized consumer
contracts; record actual results and any provider/environment limitations. Request
separate release authority after local acceptance. Until the current release is
verified, this remains the queued approved slice with no Worker dispatched.
