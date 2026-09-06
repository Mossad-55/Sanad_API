# Sanad API — session prompt / roadmap

Living checklist. Update this file and `README.md` (the master product status) on every slice.

Session branch: `arena/01a075b4-sanad-api` (do not switch). The agent **commits and pushes** here. You **pull** (or merge to `develop` / production) and **deploy**. The agent does not deploy Hostinger for you.

## Done

- Identity auth (no social login), splash CMS, caregiver lookups / onboarding / admin review
- Families: bootstrap, dependents, invitations, assessment, medical profile, meds, notes
- Discovery + bookings + Paymob intention / webhook
- Admin + caregiver **visibility** of cancelled / failed-refund / refunded bookings
- Admin **retry refund**: `POST /api/v1/admin/bookings/{id}/refund` (Failed only)
- Paymob HMAC harden: official field order, query **or** body **or** `X-Paymob-Hmac`, hex normalize, `[AllowAnonymous]`, `Bookings.NotFound` → 200 after valid HMAC

## Next

1. **Ratings and reviews** HTTP (caregiver `average_rating` / `reviews_count` already exist)
2. Booking cancellation **fee tiers**
3. Production CORS lock-down
4. Paymob refund auth (Intention `sk_` vs classic void/refund) — still a live-gateway risk if `502 Paymob.GatewayError` persists

## HMAC / Paymob notes

- Webhook: `POST /api/v1/payments/webhooks/paymob` anonymous
- HMAC-SHA512 over Paymob `obj` fields in dashboard order
- HMAC may arrive as `?hmac=`, JSON `hmac`, or `X-Paymob-Hmac`
- App must poll booking detail; SDK callback is UI-only
