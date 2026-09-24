# API contract coverage matrix

This matrix is the completeness checkpoint for API changes. A route census alone is not enough: each surface must connect its product requirement to implementation, authorization, tests, Bruno, public documentation, and Postman.

## Mechanical evidence

Run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/Verify-ApiContractMapping.ps1
```

The checker normalizes route parameters and compares controller actions with every Postman collection in both directions. The current audit result is:

| Evidence | Count | Status |
|---|---:|---|
| Controller actions | 238 | complete |
| Postman API requests | 270 | complete |
| Controller actions without Postman | 0 | complete |
| Orphan Postman API requests | 0 | complete |
| Postman JSON files parsed | 7 | complete |

## Role and resource requirements

| Surface | Required visibility and lifecycle | Implementation | Docs | Postman | Bruno/tests |
|---|---|---|---|---|---|
| Admin subscriptions | Plan list/detail, family-subscription list/detail, create, publish, retire, coupon reads/mutations, tax reads/mutation | `AdminSubscriptionsController`, subscription queries/commands | `docs/admin/subscriptions.md` | Admin collection | Unit tests; admin-read Bruno requires approved local Super Admin fixture |
| Admin caregivers | Paged list/detail, certificate file/read/review, approve/reject/correction, suspend/reactivate | `AdminCaregiversController` | `docs/admin/caregivers-review.md` | Admin collection | Caregiver review tests and existing Bruno coverage |
| Admin bookings | Closed booking list/detail, cancellation history, refund retry | `AdminBookingsController` | `docs/admin/bookings.md` | Admin collection | Cancellation/refund tests and existing Bruno coverage |
| Admin assessments | Question/tier list/detail, authoring, activation state, submissions | `AdminAssessmentsController` | `docs/admin/care-assessments.md` | Admin collection | Assessment tests and existing Bruno coverage |
| Admin lookups/CMS | List/detail reads paired with create/update/activate/deactivate/publish flows | Admin CMS controllers | `docs/admin/*.md` | Admin collection | Existing unit/Bruno coverage |
| Family subscriptions | Plan catalog/current snapshot, quote, initial payment intent, renewal payment intent, renewal controls, seven-day grace settlement | `FamilySubscriptionsController`, subscription commands | `docs/app/families/subscriptions.md` | Family collection | Subscription tests and renewal Bruno contract |

### Bounded Paymob Card enrollment requirement

| Requirement | Owner role | Route(s) | Authorization | Source handler | Persistence/data source | Focused tests | Bruno | Public docs | Postman | Status |
|---|---|---|---|---|---|---|---|---|---|---|
| Start initial Card enrollment only for a local plan with a positive `paymobSubscriptionPlanId` and configured Card 3DS integration; Wallet and renewal remain manual | Family Owner; Super Admin configures the plan mapping | `POST /api/v1/family/subscriptions/payment-intent`; admin `POST/GET /api/v1/admin/subscriptions/plans` and `GET /api/v1/admin/subscriptions/plans/{planVersionId}` | `FamilyAccess` + Owner for payment; `SubscriptionPlanAdmin` + Super Admin for plan authoring/read | `CreateSubscriptionPaymentIntentCommandHandler`; `PaymobClient.CreateSubscriptionPaymentIntentAsync`; `CreateSubscriptionPlanVersionCommandHandler`; catalog/admin plan query handlers | `SubscriptionPlanVersion.PaymobSubscriptionPlanId` mapped to `paymob_subscription_plan_id`; provider subscription identity and callback persistence are covered by the shared Paymob callback requirement below | `SubscriptionPaymentTests`; `AdminSubscriptionsControllerTests`; persistence mapping tests | Pending; mastermind owns negative-first gate | `docs/app/families/subscriptions.md`; `docs/admin/subscriptions.md`; `docs/operations/configuration.md`; `docs/operations/security.md` | Admin collection: create/list/detail plan; Family collection: create initial payment intent | Implemented; Bruno pending; provider callback settlement and subscription-ID persistence are tracked in the shared callback requirement below; Wallet/renewal initiation remains manual |
| Accept and settle Paymob subscription callbacks, including initial identity creation, renewal success/failure/overdue events, durable event idempotency, and terminal grace-expired acknowledgment | Paymob provider; anonymous callback with provider HMAC; Family Owner state is mutated only after identity/amount checks | `POST /api/v1/payments/webhooks/paymob` | `[AllowAnonymous]`; subscription callbacks require body HMAC-SHA512 over `{trigger_type}for{subscription_data.id}`; renewal callbacks require `paymob_request_id` | `PaymobWebhookController.HandlePaymob` / `HandleSubscriptionCallback`; `HandlePaymobSubscriptionCallbackCommandHandler` | `PaymobSubscriptionIdentity` shared provider registry; `PaymobSubscriptionCallback` append-only ledger; `SubscriptionPaymentAttempt` and `FamilySubscription` provider state/renewal state | `SubscriptionProviderCallbackTests` (31 focused tests covering five triggers, HMAC, request identity, idempotency, ordering, grace, conflicts, and persistence) | Safe negative-first Bruno requests cover all five trigger spellings; no automated provider mutation gate | `docs/app/families/subscriptions.md`; `docs/admin/subscriptions.md`; `docs/operations/security.md`; `docs/operations/configuration.md`; `docs/architecture/overview.md` | `docs/postman/app/Sanad.App.Public.postman_collection.json` — Postman fixtures are manual-only: five explicitly named requests for `CREATED`, `Subscription Created`, `Successful Transaction`, `Failed Transaction`, and `Failed Overdue Transaction`, with body `hmac` | Implementation is complete in source/docs/Postman/Bruno; only migration application remains owner-deferred |
| Family bookings/reports | List/detail and lifecycle/payment/report visibility | Family controllers | `docs/app/families/*.md` | Family collection | Existing lifecycle Bruno suites |
| Caregiver app | Profile, pricing, schedule, availability, booking list/detail/lifecycle/reports | Caregiver controllers | `docs/app/caregivers/*.md` | Caregiver collection | Existing caregiver Bruno suites |
| Public/auth/support | Public reads, authentication/session/account, legal/help/support | Public/auth controllers | `docs/auth/`, `docs/app/public/`, `docs/app/settings/` | Auth/public collections | Existing auth/public Bruno suites |

## Closeout rule

Every new or changed row must have zero missing implementation, authorization, tests, documentation, Postman, and Bruno entries—or an explicit owner-deferred status in the active phase checklist. A resource with mutations but no safe way to list or inspect its resulting state is incomplete.
