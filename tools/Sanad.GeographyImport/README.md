# Sanad V1 geography importer — operator tool

Standalone .NET 10 tool. **Not registered with the API, hosted service, migration
or Development seeder.** It operates only when explicitly invoked. It uses the
three existing `caregivers` lookup tables; no schema change or source-key column.
No live run is implied by the existence of this tool.

The owner authorized local implementation/testing and a disposable PostgreSQL
rehearsal, then approved commit/push, publication and deployment. Deployment does
not automatically run this tool. Existing VPS lookup replacement/reset remains
separate; commands below are an operator handoff, not an automatic data-change order.

## Input and credentials

- Supply the exact reviewed `candidate.json` bytes. Accepted V1 SHA-256:
  `E048767A00F5DE28B32E8F4E1E2B2BCBC4FA52C19BEA0C0D14D085DDB5AB8A8E`.
  Counts are pinned to 27 governorates / 347 Cities / 5712 Areas.
- Copy the reviewed file byte-for-byte into the operator package. Do not
  reformat it or rely on checkout line-ending conversion on another platform;
  verify with `Get-FileHash` or `sha256sum` before use. A changed hash is refused,
  not silently canonicalized. The catalogue remains a historical V1 baseline.
- Inject `SANAD_GEOGRAPHY_CONNECTION` into the process from approved protected
  secret storage. No connection-string CLI argument, appsettings discovery,
  dotenv loading or module configuration fallback exists. Do not paste secrets
  into shell history or reports. Never send tokens over public HTTP.
- Preview role needs SELECT and transaction access. Apply role also needs
  INSERT and the required PostgreSQL explicit table-lock privileges. Use the
  owner-designated target and verify its schema/migration state beforehand.

## Preview — no database mutations

After explicit owner authorization for the exact target:

```text
dotnet Sanad.GeographyImport.dll preview --catalogue candidate.json --report preview-01.json
```

The report file must not already exist. Preview reads the three tables within a
consistent read-only PostgreSQL transaction, then returns a per-source decision:

- `Insert`: new UUID recorded in the report, with its planned parent UUID.
- `Keep`: both Arabic and English names match exactly under the same parent;
  the existing UUID/name/state/timestamps are preserved.
- `KeepInactive`: exact match but already inactive; never auto-reactivated.
- `Conflict`: partial Arabic-or-English match, multiple matches (with target
  IDs), inactive parent/ancestor for a new record, or unresolved parent.

All unmatched/manual target records are listed as preserved extras, including
Smouha. No fuzzy matching, Arabic folding, alias matching, rename, reparenting,
reactivation, deletion or row splitting is performed. Differences such as
`الإسكندرية` versus `الاسكندرية` may produce a partial-match conflict. Review
those explicitly; do not delete manual data or bypass the check. Resolving a
conflict is a separate owner-approved admin/reconciliation action, followed by
a fresh preview. Existing target orphans are refused without repair.

Exit 0 = conflict-free preview. Exit 2 = review/refusal/conflicts. Exit 1 =
operational failure/cancellation. Retain the JSON, candidate, tool revision,
printed target fingerprint and exact report SHA-256. Preview is not permission
to apply; a conflict-free preview is not proof of complete modern geography.

## Apply — exact owner order, maintenance and backup required

Owner-approved preserve-and-add links (2026-10-15): add
`--reconciliation sanad-vps-v1` to **both** preview and apply. This opt-in profile
is pinned to the approved VPS target fingerprint, exact candidate hash and four
existing IDs/names/parents: Beheira/Behera, Minya/Menia, Asyut/Assiut and the
explicit Damanhur city/district grouping. It never edits those records or the
five existing caregiver selections. Unknown profiles, other targets and changed
link records fail closed. The plan records the profile and apply recomputes it;
do not hand-edit plan JSON. Other legacy entries stay as preserved extras, so
final counts include those extras rather than exactly the candidate totals.

Before apply:

1. Obtain explicit approval of the target, reviewed report, candidate hash and
   proposed inserts/preserved IDs. Pin the deployed tool revision/package too.
2. Take and verify the approved backup/restore procedure. Record its private
   operator reference. The tool requires a reference, not a false claim it has
   tested backup restoration itself.
3. Stop and drain application/admin writers and any other database writers that
   can alter these lookups; retain the owner maintenance-order reference. Keep
   them stopped until verification finishes. **Required:** current application
   duplicate checks are read-before-write and have no database unique index.
   An in-flight request that read before the import could insert a duplicate
   after commit; table locks alone do not eliminate that race.
4. Confirm the preview is less than 24 hours old. Use a fresh preview whenever
   any lookup row, active flag, name, timestamp or manual entry changed.

```text
dotnet Sanad.GeographyImport.dll apply --catalogue candidate.json --plan preview-01.json --plan-sha REVIEWED_REPORT_SHA256 --confirm-target PRINTED_TARGET_FINGERPRINT --backup-reference APPROVED_BACKUP_LABEL --maintenance-reference APPROVED_MAINTENANCE_LABEL
```

The transaction acquires a fixed writer lock on all three tables before reading
the snapshot, refuses target/catalogue/report drift, and independently rebuilds
the reviewed plan (including UUIDs) before inserting anything. Only new rows are
inserted in parent-first order; final whole-table snapshot verification precedes
commit. PostgreSQL timestamp precision is accounted for. New records are active
only under active ancestors. A failure rolls back the transaction; no automatic
retry occurs. A reused old plan becomes stale after a successful insert. A fresh
repeat preview yields zero inserts and preserves all IDs.

The required backup and maintenance labels are operator attestations, not proof
that a backup exists or writers are stopped. Keep the reviewed plan, stdout exit
status, backup and maintenance references in a protected operations record. Do
not hand-edit the reviewed JSON. Cancellation or output failure after a commit
can make console status inconclusive: inspect the actual database and generate
a fresh preview before retrying. Do not assume a failed command always means
the server committed nothing.

After authorized apply, while writers remain stopped: read back counts, IDs,
active chains, preserved/manual entries and public lookup behavior through the
approved secure route. Restart writers only under the owner's operational
order. Rollback after an already committed import is an explicit restore/data
reconciliation decision, not an automatic delete operation.

## Local verification and limitations

Focused tests use unique SQLite `:memory:` databases and synthetic records,
including initial/repeat import, unchanged inactive/manual records, conflicts,
stale/tampered/expired plans and injected mid-transaction failure rollback.
The actual 6086-record candidate is exercised in both planning and relational
memory tests. Npgsql mapping/SQL generation is tested without connecting.

SQLite tests do not prove PostgreSQL lock behavior or a live target's data,
privileges, triggers, schema, collation, backup or performance. The owner-authorized
disposable PostgreSQL 18.4 CLI rehearsal subsequently passed: 6086 initial inserts,
zero on repeat, zero orphans; the temporary loopback server was stopped.
Actual target read-only preview/apply remain separately authorized operator gates;
the local rehearsal does not certify the VPS. See the
[closeout](../../docs/internal/work/portal-api-reconciliation/reports/phase-06-importer.md).
No API routes,
wire formats, CORS, auth policies, migration heads or provider settings change.

References for the implementation's transaction/lock choices:
[PostgreSQL explicit locking](https://www.postgresql.org/docs/current/explicit-locking.html)
and [EF Core transactions](https://learn.microsoft.com/en-us/ef/core/saving/transactions).
