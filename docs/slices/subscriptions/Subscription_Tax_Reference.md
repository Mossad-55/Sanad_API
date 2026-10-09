# Subscription tax reference

Status: historical contract reference retained after legacy worker-brief cleanup. This is not an active task queue.

## Canonical contract

- Subscription catalog prices are tax-exclusive EGP amounts. Family quotes and payment intents calculate tax server-side; clients cannot override price, tax, discount, currency, or renewal dates.
- The active tax rule is effective only when `effectiveOnUtc` is at or before the quote time. Monetary values use the server's two-decimal banker's-rounding convention.
- Tax-rule configuration is SuperAdmin-only through the `SubscriptionPlanAdmin` policy. Supported routes are `POST /api/v1/admin/subscriptions/tax-rules`, `GET /api/v1/admin/subscriptions/tax-rules/current`, and `GET /api/v1/admin/subscriptions/tax-rules/history`.
- Rates are constrained to `0` through `100`, rounded to two decimals; versions are positive, unique, and effective timestamps are UTC. Replacing the active rule preserves history; concurrent active-rule changes return `409 Subscriptions.Tax.ActiveConflict`.
- Missing tax configuration blocks quote/payment flows with `409 Subscriptions.Quote.TaxNotConfigured`. Invalid values return `400 Subscriptions.Tax.Invalid`.

## Evidence and ownership

The public behavior is documented in [Admin subscriptions](../../admin/subscriptions.md) and [Family subscriptions](../../app/families/subscriptions.md). Shared platform-fee and tax configuration now lives in the Finance surface ([Finance platform charges](../../admin/finance-platform-charges.md), `FinanceOperationalAdmin`: SuperAdmin or SupportAdmin); the routes above remain as the compatibility adapter. The deleted worker briefs were historical routing/correction notes; they are not implementation authority and are not an executable backlog.

The former dedicated PostgreSQL concurrency check was optional local regression evidence. It must remain disposable and guarded; no database reset or provider action is authorized by this reference.
