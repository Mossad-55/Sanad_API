# Elderly daily check-in

The authenticated Elderly account can submit one final daily reassurance answer for its own profile:

`POST /api/v1/elderly/check-ins`

Authorization is an Elderly JWT with `Normal` access. The server resolves the profile from the authenticated identity; the request does not accept an Elderly or Family ID.

```json
{ "answer": false }
```

The answer is evaluated against the profile's stored IANA timezone and the resulting profile-local calendar date. Only one record is allowed for an Elderly profile and local date. A retry with the same answer returns the saved record. A retry with the opposite answer is rejected; there is no same-day edit.

Successful responses contain `id`, `elderlyId`, `answer`, `localDate`, `answeredAtLocalTime`, `answeredOnUtc`, and `timeZoneId`. `answeredOnUtc` is UTC; the date and local time are the profile-local values.

When `answer` is `false`, the completed check-in requests a durable in-app alert for every active linked Family member whose `checkInAlerts` preference is enabled. The alert is categorized as `ElderlyCheckIn` / `NegativeCheckIn`, targets the Elderly entity, and is deduplicated per recipient, Elderly, and local date. A same-answer retry is therefore also the retry boundary for alert creation. Push is only attempted when a provider is available, no SMS is sent, and push/provider retry semantics, reminders, and missed-check-in semantics are deferred.

Families persistence and Notifications persistence are separate contexts. The check-in row can be committed before alert fan-out completes; this is not an atomic cross-context transaction or an outbox guarantee. Retrying the same negative answer reuses the saved check-in and retries alert fan-out without creating duplicate durable inbox rows.

Errors:

- `404 Families.ElderlyCheckIn.NotFound`: no identity-bound Elderly profile or the owning Family is inactive/deleted.
- `409 Families.ElderlyCheckIn.InvalidTimeZone`: the stored timezone cannot be resolved; the request fails closed before creating a check-in.
- `409 Families.ElderlyCheckIn.AlreadyAnswered`: the local day already has the opposite answer.

The check-in route does not implement reminders, missed-state calculation, Family status/history reads, SMS, or guaranteed push delivery.
