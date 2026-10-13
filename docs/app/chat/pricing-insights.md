# Caregiver pricing insights

Read with the [Chat Plus plan](../../internal/work/chat-plus/plan.md) (section 3)
and [additive contract details](../../internal/work/chat-plus/contracts.md).
Examples here are statically authored from the verified behavior — never
executed against a live API or database.

## Route and auth

```text
GET /api/v1/caregiver/pricing-insights?areaId={guid}&serviceId={guid}
```

- `CaregiverAccess` policy, Normal JWT. `401` without a token, `403` for the
  wrong actor/access policy (existing auth behavior).
- Both query ids are **required**: nonempty GUIDs naming **active** catalogue
  entries. Missing, malformed, empty, unknown, or inactive ids answer `400`
  with code `Caregivers.PricingInsights.InvalidFilter`.

## Response

```json
{
  "currency": "EGP",
  "marketHourlyPrice": { "min": 40, "max": 80 },
  "mostRequestedEightHourDayPrice": { "min": 200, "max": 400 },
  "updatedOnUtc": "2026-10-11T08:00:00Z"
}
```

- `currency` is always `"EGP"`.
- Either range object may be `null` **independently** (see null semantics).
  Ranges are never zero-filled and never partially null (`min`/`max` always
  travel together).
- `updatedOnUtc` is the calculation instant `t` (one injected UTC clock read
  per query), not a stored refresh timestamp.

## Exact calculation

- One snapshot/read transaction covers both pools so the response never mixes
  concurrently committed data.
- Window: `[t − 90 days, t]`, measured by `CompletedOnUtc`, inclusive at both
  ends.
- Eligible sample: a distinct Families caregiver booking with status
  `Completed`, `PaidOnUtc` set, valid `CompletedOnUtc` inside the window, an
  EGP price snapshot, positive `BaseCaregiverFee`, and reliable booked
  area/service attribution. Booked **fee snapshots** only — platform fees and
  tax excluded, never mutable current caregiver prices. Cancelled, expired,
  declined, refunded, unpaid, malformed, or unattributable bookings are
  excluded.
- `marketHourlyPrice`: **Hourly** shift samples only. Snapshotted base fee
  divided by booked hours (supported overnight rule; duration must be
  positive; ambiguous/invalid legacy durations excluded). Each rate rounded to
  two decimals with banker's rounding (`MidpointRounding.ToEven`), then
  `min`/`max`.
- `mostRequestedEightHourDayPrice`: **EightHourShift** samples only, using the
  snapshotted base fee as the eight-hour day price. The name is a frontend
  field label — not a popularity ranking and not an hourly-times-eight
  formula. Medical home visits / 12h / 24h rates belong to neither pool.

## Null semantics

Each pool independently needs **at least 5 distinct eligible bookings**
(counts are bookings, not joined row counts):

| Hourly pool | Eight-hour pool | Response |
|---|---|---|
| ≥ 5 | ≥ 5 | both ranges present |
| 4 | ≥ 5 | `marketHourlyPrice: null`, eight-hour range present |
| 0 | 0 | both `null` |

A `200` with null ranges means "no data", never an error.

## Attribution write path

`Booking` carries nullable immutable `bookingAreaId` + `bookingServiceId`,
recorded at `POST /api/v1/family/bookings/checkout`:

- Both together or neither. One supplied, empty GUID, unknown id, or a pair
  not selected by that booked caregiver fails booking validation (`400`
  through the owning booking vocabulary).
- Persisted in the same creation transaction; retained after later
  catalogue/profile changes — never backfilled from present coverage,
  selections, or free-text address.
- Neither supplied preserves legacy checkout behavior but contributes no
  pricing sample. One booking feeds one pair and at most one eligible pool.

## Error reference

| Condition | HTTP | Code |
|---|---|---|
| No/invalid token | `401` | existing auth behavior |
| Wrong actor/access policy | `403` | existing auth behavior |
| Missing/malformed/empty/unknown/inactive `areaId` or `serviceId` | `400` | `Caregivers.PricingInsights.InvalidFilter` |

Matching Postman request:
[Sanad.App.Chat.postman_collection.json](../../postman/app/Sanad.App.Chat.postman_collection.json)
(folder `11.Pricing insights` — supply a **caregiver** JWT, not the default
app token).
