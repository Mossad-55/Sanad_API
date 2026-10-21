# Firebase push service-account key rotation — receipt

Date: 2026-10-21 (Africa/Cairo). Rotation executed `2026-10-21 02:04–02:07` UTC
against the Production host and closed out the same day. **Status: complete.**
The replacement key is installed, live and verified; the leaked key has been
revoked by the Owner; and every plaintext copy and residue from the incident has
been removed (see "Closeout").

## Why

The E06 release disclosed a redaction incident: one diagnostic command printed the
multi-line content of `Push__Firebase__ServiceAccountJson` (see
`e06-missed-check-ins-release.md`, "Redaction incident"). This receipt closes that
exposure by replacing the credential in use with a newly generated key.

## Where the credential lives

- Live value: `/etc/sanad/sanad.env`, mode `600 root:root`, loaded by
  `sanad-api.service` via `EnvironmentFile=`.
- Storage format, verified byte-for-byte before the swap:
  `Push__Firebase__ServiceAccountJson='` + `json.dumps(obj, indent=2)` + `'`,
  followed by a blank line. Proof of the format rather than an assumption: the
  previous value was re-encoded from its own parsed object and compared to the
  file — **byte-identical**. Reproducing the old bytes exactly is what makes the
  rewrite below safe.
- systemd strips the outer single quotes, so the running process receives the JSON
  itself. Verified against `/proc/<MainPID>/environ` (ground truth), not
  `systemctl show -p Environment` (whose rendering of this multi-line value is
  misleading and reports it as absent — a false negative encountered during
  reconnaissance).
- `appsettings.json` on the host carries no Firebase key (`0` matches), so the
  environment file is the single configured source.

## Why the Owner had to generate the key

Key creation/deletion via the Google APIs was attempted first and is **not
possible with the credentials on the host**:

| Attempt | Result |
| --- | --- |
| Exchange the env credential for an OAuth token (`cloud-platform`) | 200 — the credential is live |
| `iam.googleapis.com` service-account keys list/create/delete | 403 — **the IAM API is disabled on project `ahlenaapp`** (`1031696058836`) |
| `serviceusage.googleapis.com` service read | 403 `PERMISSION_DENIED` |
| `cloudresourcemanager.googleapis.com` `getIamPolicy` | 403 `PERMISSION_DENIED` |
| `gcloud` / any other Google credential on the host | none |

The service account (`firebase-adminsdk-fbsvc@ahlenaapp.iam.gserviceaccount.com`)
is deliberately least-privileged: it can mint Firebase-scoped tokens and nothing
else. The security posture is correct, and it is also why the leaked key cannot
mint further keys or read project IAM — its reachable blast radius is
Firebase/messaging, not project administration. The Owner therefore chose
**Option A**: generate the replacement in the Firebase console and hand over only
the file path.

## The swap

- Old (leaked) key `private_key_id`: `a23d6823ec60ee738dae63a3baa7c40a7cce62ba`.
- New key `private_key_id`: `45a61328041365fd6f38e454fd34724f9e7c9379`, same
  project, same service account, same field set as the old JSON.
- New key file: 2367 bytes, SHA-256
  `b5b16c07aa2f676b993a2be18ada9f62c038d7f2b8fa6e6b962139af90199868`; uploaded
  to the host and the hash verified before use; deleted from the host after the
  install.
- New key proven usable for its purpose **before** touching the live file, and
  again from the installed value after the restart: JWT-bearer exchange for scope
  `https://www.googleapis.com/auth/firebase.messaging` returned **200** both times.
- Environment file SHA-256: before `e5b4ec5e4f1f3a575eb4023ef6aa081ec87aa614d95a7b93877eb51b86877a65`
  → after `3624eff2600c45b44545a35d3af4b29cc65e321205ce8fb956cf5c31f9a569af`.
- Rewrite guarantees asserted before writing: the value span was located exactly
  once; the splice changed nothing but that span (the file with the block removed
  is identical before and after); all **36** key names survived; the rewritten
  value round-tripped back through JSON to the new `private_key_id`. Written to a
  temporary file and `os.replace`d into place, preserving mode `600`.
- Rollback reference: `/root/env-rotation-20261021/sanad.env.before` (mode `600`,
  SHA-256 equal to the "before" hash above), held until the old key is revoked and
  then removed.
- `systemctl restart sanad-api.service` → `is-active active`, `NRestarts 0`,
  `GET /api/v1/deployment/info` 200, and the live process reports the new
  `private_key_id`. The install script would have restored the previous file and
  restarted had the API not answered within 90 s; that path was not taken.

## Plaintext copies of the leaked key removed

The value was not only in the live file. Twelve release backups under
`/root/sanad-releases/` each held a plaintext copy of the whole environment file,
and therefore of the leaked key. All twelve were redacted in place — the variable
name is retained, the value replaced with a placeholder, file modes preserved:

```
/root/sanad-releases/e04-bdc0605-20261019/sanad.env
/root/sanad-releases/e04-bdc0605-20261019/host-config/sanad.env
/root/sanad-releases/e05-e2be9ba-20261021/host-config/sanad.env
/root/sanad-releases/e06-90f02c7-20261021/host-config/sanad.env
/root/sanad-releases/f1-0dbb74b-20261020/backup/sanad.env
/root/sanad-releases/f2-24ad602-20261020/host-config/sanad.env.before
/root/sanad-releases/f3-607d33f-20261020/host-config/sanad.env.before
/root/sanad-releases/f4-4d3cab4-20261020/host-config/sanad.env.before
/root/sanad-releases/f5-9997927-20261020/host-config/sanad.env.before
/root/sanad-releases/f6-bc32be0-20261020/host-config/sanad.env
/root/sanad-releases/f7-d6d950c-20261020/host-config/sanad.env
/root/sanad-releases/f8-7a65168-20261021/host-config/sanad.env
```

Scrubbing them is not merely tidiness: once the old key is revoked, an env backup
that restores the old value would silently break push, so a restored-from-backup
configuration had to stop carrying a dead credential. A residual sweep for the
old `private_key_id` now finds no copy anywhere on the host except the
deliberately retained rollback reference above. A sweep for private-key PEM blocks
found no other service-account material (only unrelated `node_modules` strings and
the system snakeoil TLS certificate).

## Closeout — the exposure is closed

The one step this credential could not perform was carried out by the Owner, and
the residue was then removed:

1. **The leaked key was revoked.** The Owner deleted the key with id
   `a23d6823ec60ee738dae63a3baa7c40a7cce62ba` for
   `firebase-adminsdk-fbsvc@ahlenaapp.iam.gserviceaccount.com` in the Firebase
   console (Project settings → Service accounts). This is the action the host
   credential was not permitted to take — the IAM API is disabled on the project
   (see "Why the Owner had to generate the key").
2. **The rollback reference and helper residue were removed**:
   `/root/env-rotation-20261021/` (the pre-rotation copy of the environment file,
   held only until revocation), and the two throwaway diagnostics
   `/root/fb-key-health.py` and `/root/vps-health.sh`.
3. **The local copy of the revoked key was deleted** with the Owner's consent:
   `ahlenaapp-firebase-adminsdk-fbsvc-a23d6823ec.json` in the machine download
   folder.

Post-closeout verification (2026-10-21, after the removals):

- The **live** credential is the new key: `private_key_id`
  `45a61328041365fd6f38e454fd34724f9e7c9379`, present exactly once in
  `/etc/sanad/sanad.env`. A JWT-bearer exchange for scope
  `https://www.googleapis.com/auth/firebase.messaging` returned **200** — the
  installed key still authenticates for its one purpose.
- `/etc/sanad/sanad.env` SHA-256 is unchanged at
  `3624eff2600c45b44545a35d3af4b29cc65e321205ce8fb956cf5c31f9a569af` (the
  post-rotation value), the service is `active` with `NRestarts 0`.
- A residual sweep for the revoked id over `/root` and `/etc/sanad` now returns
  **0** hits: no copy of the dead key remains anywhere on the host.
- No third key file exists on either side — the whole user profile and the VPS
  were searched.

One residual, by design, is left to the Owner: the machine download folder still
holds the **new** key file
`ahlenaapp-firebase-adminsdk-fbsvc-45a6132804.json` (2367 bytes, SHA-256
`b5b16c07aa2f676b993a2be18ada9f62c038d7f2b8fa6e6b962139af90199868`). It is
redundant — the same key is installed and live in `/etc/sanad/sanad.env` — but it
is a private key sitting outside the host, so it should be deleted once the Owner
is satisfied the install is settled. Deleting it is the Owner's call; it was not
removed here.

## Limitations

- No end-to-end push delivery was exercised; there is no device token to target.
  The claim proved here is narrower and exact: the installed credential
  authenticates and mints a `firebase.messaging`-scoped token, and the running
  process carries the new key.
- The rotation is a host-configuration change only. No application code, schema,
  migration or data changed; no other service was restarted.
- The Production API was restarted once as part of the swap (that is what loads
  the new value); the restart is the only interruption and the app returned to
  200 with `NRestarts 0`.
- **Revocation itself is not independently verifiable from here.** Listing or
  reading service-account keys requires the IAM API, which is disabled for this
  project and this least-privileged credential (403). The evidence that the old
  key is dead is therefore the Owner's console deletion plus the destruction of
  every copy of the old material; the positive evidence retained in this receipt
  is that the *new* key still authenticates and the live process carries it.
