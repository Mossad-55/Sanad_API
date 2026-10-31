# Fixture refund-outcome seam (internal, S3c)

> INTERNAL ONLY — do not copy any of this into consumer-facing docs. This
> describes a rehearsal-fixture mechanism, not a product feature.

## What it is

`FixtureRefundSeamPaymobClient` is a decorator registered in front of the
real `IPaymobClient` (Families infrastructure composition, `AddFamiliesInfrastructure`).
One decorator therefore covers every refund caller in the deployed app:

- care-home refund / retry / expiry (`CareHomeRefundHandlers`,
  `CareHomeBookingExpiryService`),
- Families admin booking refund retry (`AdminBookingCommands`),
- the Finance duplicate-claim execution gateway
  (`SubscriptionRefundGateway`, which delegates to the same client).

Non-refund operations (`CreatePaymentIntentAsync`,
`CreateSubscriptionPaymentIntentAsync`, `UpdateSubscriptionAmountAsync`)
always delegate untouched. The real client stays registered under the
DI key `paymob-inner`; the seam is the sole non-keyed `IPaymobClient`.

## Configuration keys (default OFF)

```json
"FixtureRefundSeam": {
  "Enabled": true,
  "RefundOutcomes": {
    "<fixture provider transaction id>": "Rejected" | "Unknown" | "Completed"
  }
}
```

- `FixtureRefundSeam:Enabled` — default `false`. Absent section or
  `false` = strict pass-through with zero behaviour change (even for ids
  present in the map).
- `FixtureRefundSeam:RefundOutcomes:<txnId>` — the allowlist. Only these
  exact ids (ordinal match) are intercepted; any other refund call delegates
  to the real client.

Scripted outcomes return exactly the shapes the real client returns, so no
caller classification changed:

| Outcome     | Returned shape                          | Caller effect                            |
| ----------- | --------------------------------------- | ---------------------------------------- |
| `Rejected`  | `Paymob.RefundRejected` (502 mapping)   | claim released / Failed state            |
| `Unknown`   | `Paymob.RefundOutcomeUnknown` (409)     | ambiguity marker / Pending               |
| `Completed` | success with reference `fixture-refund-<txnId>` | normal confirmed-refund path     |

Logging: one informational line per intercepted call (fixture transaction
id + outcome). Additionally, one informational STATE line is emitted at
startup and on every options change (`FixtureRefundSeamState`):

```
Fixture refund seam state: enabled=<bool>, allowlist entries=<n>, allowlisted transaction ids=[<sorted ids>]
```

That state line is the checkable gate evidence (see the deploy step below).
No amounts, no secrets — the ids are synthetic fixture ids that the plan
manifest already prints. When the configuration is INVALID after a change
(seen only in configurations that drifted after startup), the state line is
withheld and an error line reports the validation failures instead.

## Hard guarantees

1. **Non-Production only.** `Enabled=true` while the host environment is
   Production FAILS application startup
   (`FixtureRefundSeamOptionsValidator` + `ValidateOnStart`). Enabled with an
   unresolvable environment is also refused.
2. **Validated config.** Outcome values are validated at startup EVEN while
   the seam is disabled — unknown values refuse startup. The seam reads its
   options LIVE through `IOptionsMonitor` on EVERY refund call. The
   registered validator is enforced by the options pipeline on EVERY
   monitored-value creation — reloads included — so the live read and the
   validation cannot disagree:
   - a reload to VALID values is honoured on the next call (no restart, no
     stale cache — values are parsed per call);
   - a reload to INVALID values (malformed outcome, empty allowlist while
     enabled, or an enablement attempt under Production) makes the
     monitored options unreadable: the options pipeline throws
     `OptionsValidationException` (the change callback re-validates
     eagerly, so the reload site may observe it) and the decorator FAILS
     CLOSED — every refund call is refused with
     `Paymob.RefundOutcomeUnknown` and the real provider is never touched
     until the configuration is corrected.
3. **Allowlist, not a switch.** An id absent from the map is never
   intercepted, for any reason. NOTE the bounded fail-closed exception:
   while the monitored options are INVALID (rule 2), the allowlist itself
   is unknowable, so pass-through is suspended and ALL refund calls are
   refused until the configuration is corrected. Non-refund operations are
   unaffected.
4. **Enabled means ON.** `Enabled=true` with an EMPTY `RefundOutcomes` map
   FAILS application start: an enabled seam with zero allowlisted ids is a
   silent no-op that only looks on, which would make a `--seam-deployed`
   provisioning declaration unsafe. The same refusal re-arms on every
   reload while enabled.

## Which transaction ids are allowlisted (provisioner-derived)

The rehearsal provisioner (`Ahlena.PortalProvisioner`, run with
`--seam-deployed`) derives these deterministically per run tag; the `plan`
manifest prints the exact ids/outcomes — copy them from there:

| Group (provisioner id)                        | Allowlist key derivation                       | Outcome   |
| --------------------------------------------- | ---------------------------------------------- | --------- |
| `carehome-refund-failed`                      | numeric `StableNumber("spf:<tag>:carehome-refund-failed")` (decimal string) | `Rejected` |
| `caregiver-refund-failed`                     | `spf-<tag>-seam-caregiver-failed-txn`          | `Rejected` |
| `duplicate-claim-approved-awaiting-execution` | `spf-<tag>-seam-claim-approved-txn`            | `Rejected` |
| `duplicate-claim-ambiguous`                   | `spf-<tag>-seam-claim-ambiguous-txn`           | `Unknown`  |

The two duplicate-claim groups execute/approve in-process in the provisioner
with a scripted / refusing gateway — never a provider call. The allowlist
exists so a LATER operator click (refund retry on the Failed bookings,
Execute on the approved claim, reconciliation on the ambiguous claim) can
never reach the provider.

## Deployment sequencing & cleanup

- Deploy the revision WITH the seam compiled in (it is inert while
  `Enabled` is absent/false), then set `FixtureRefundSeam` in the
  rehearsal environment config, then RESTART the app (or otherwise trigger a
  config reload), then VERIFY the gate, and only then run the provisioner:

  1. Get the exact allowlisted ids for the run tag:
     `--mode plan --seam-deployed` (the plan manifest prints every seam
     group's allowlist id/outcome).
  2. Set `FixtureRefundSeam` with EXACTLY those ids, then restart the app.
  3. **Gate verification (mandatory):** confirm the startup log line
     `FixtureRefundSeamState` reports `enabled=True` with allowlist entries
     matching, and `allowlisted transaction ids=[...]` containing EXACTLY
     the ids the plan manifest printed — no fewer, no different ids. If the
     ids do not match the plan manifest, STOP: do not run
     `--mode provision --seam-deployed` until they do.
  4. Only then: `--mode provision --seam-deployed`.

  This verification is required because the provisioner's `--seam-deployed`
  flag is an OPERATOR DECLARATION, not a live probe: the startup log line is
  the observable proof that the running process actually carries the
  configuration the declaration claims.
- Live-read caveat (bounded, and why the gate step exists): because options
  are read live on every refund call, a config section added WITHOUT a
  restart is also honoured on subsequent calls — but only the observed
  startup log line proves what the RUNNING process carried when the
  provisioner armed the fixture rows.
- Invalid-reload behaviour (what to expect in the logs): a config change
  that makes the section invalid after startup (malformed value, emptied
  allowlist while enabled, or Enabled=true under Production) does NOT run
  silent — the options pipeline throws `OptionsValidationException` (its
  change callback re-validates eagerly) and every refund call is refused
  fail-closed with `Paymob.RefundOutcomeUnknown` until the configuration is
  corrected and the app is restarted (or a valid reload re-validates the
  options). The allowlisted ids are NEVER sent to the real provider in this
  state.
- THE SEAM CONFIGURATION MUST STAY IN PLACE WHILE ANY SEAM-ARMED ROW
  REMAINS IN THE ENVIRONMENT. The seam-created rows are exactly the states
  a later operator click could push toward the REAL provider: the two
  Failed-refund bookings (retry → provider), the Approved duplicate-claim
  (Execute → provider) and the Ambiguous claim + its capture
  (reconciliation → provider). Disabling or dropping `FixtureRefundSeam`
  while any of them exists would expose those clicks to the real provider —
  that is a refusal path, not a cleanup step. Keep `Enabled=true` with the
  same allowlist for the whole lifetime of those rows.
- Reaping the armed rows (deleting/reverting them so the environment is
  clean) is an operator action that is NOT authorized by this work; no
  deletion was performed and none is planned by the seam. ONLY after ALL
  seam-armed rows have been reaped by an authorized operator step may the
  `FixtureRefundSeam` section be removed from the rehearsal config. The
  provisioner's reruns fail their inertness invariant when exempted rows
  are removed while the seam declaration remains — keep the cells
  consistent either way.
