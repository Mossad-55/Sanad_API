#requires -Version 5.1
<#
.SYNOPSIS
    Refund timeout-reconciliation sweep — DRY-RUN by default. AUTHORED, NOT RUN.

.DESCRIPTION
    W2-V0 / S7 verification asset. This script selects unknown-outcome refund
    rows (the money core's `TimedOut` ledger entries, Owner decision #17) from a
    VERIFIED DISPOSABLE LOCAL PostgreSQL database and prints the reconciliation
    decision it WOULD take. It is intentionally a definition: it has never been
    executed against any target, and it must not be run until the Mastermind
    grants the sole runtime slot for the exact run.

    Structure (in execution order — the flag gate runs first, then the
    target guards, then the client-type gate):
      G1. Explicit run flag — nothing at all is executed (not even the read-only
                             selection) unless `-IUnderstandThisRuns` is passed.
      G2. Host allowlist   — the connection string host must be localhost /
                             127.0.0.1 / ::1. Every other host is refused.
      G3. Database allowlist — the database name must match the disposable
                             pattern ^ahlena_(refund|verify|disposable)[a-z0-9_]*$
                             (belt-and-braces against the shipped default names
                             `AhlenaDb` (legacy `SanadDb`), `SanadBrunoTestDb` and any production name).
      G4. Client-type gate — the resolved IPaymobClient must be
                             DevelopmentPaymobClient. Because selection is
                             key-presence-gated (W0 D4), this script refuses to
                             proceed if `Paymob:SecretKey` is present in the
                             process environment it inherits. NOTE: this is a
                             tripwire only — a key hidden in appsettings or
                             user-secrets is invisible here; the authoritative
                             assertion is the checklist's config contract check
                             (gateway-double-contract-check.ps1) against the
                             exact configuration the disposable target loads.
      G5. Dry-run default  — with `-IUnderstandThisRuns` but WITHOUT `-Apply`,
                             the script performs the read-only selection and
                             prints decisions; it writes NOTHING. `-Apply` is
                             deliberately inert: a reconciliation requires real
                             provider evidence, which a sweep cannot invent —
                             it records nothing and only re-affirms that in the
                             receipt. Never a DELETE, TRUNCATE, repair or
                             schema change.

    The dry-run never issues an UPDATE or DELETE. It reads only.

.PARAMETER ConnectionString
    Npgsql connection string for the disposable target. An explicit parameter;
    there is no default and no environment fallback. The host and database-name
    guards apply to it.

.PARAMETER IUnderstandThisRuns
    Mandatory acknowledgement gate. Without it the script prints a notice and
    exits 0 having done nothing.

.PARAMETER Apply
    Optional. Kept for authorized-run symmetry, but deliberately inert: the
    sweep NEVER records a reconciliation, because that requires real provider
    evidence this script cannot invent (#17). Default and every mode is
    write-free.

.PARAMETER SweepNowUtc
    Optional. The instant the sweep treats as "now", in ISO-8601 UTC, for a
    deterministic/dated run. Defaults to the current UTC instant.

.PARAMETER PsqlPath
    Optional. Path to the local `psql` client. Defaults to `psql` on PATH.

.EXAMPLE
    # What a separately authorized dry-run looks like (NEVER run without slot):
    #   .\refund-reconciliation-dryrun.ps1 -ConnectionString '<disposable cs>' -IUnderstandThisRuns

.NOTES
    Authored NOT RUN. No real charges, no resets, no destructive repair, no
    shared-host TestClock change, no migrations, no secrets printed.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string]$ConnectionString,

    [switch]$IUnderstandThisRuns,

    [switch]$Apply,

    [string]$SweepNowUtc,

    [string]$PsqlPath = 'psql'
)

$ErrorActionPreference = 'Stop'

# ---------------------------------------------------------------------------
# Guard G4 — explicit run flag. Nothing happens without it.
# ---------------------------------------------------------------------------
if (-not $IUnderstandThisRuns) {
    Write-Host 'refund-reconciliation-dryrun: acknowledged NOTHING.'
    Write-Host '  This is an authored-not-run S7 asset. It does nothing without'
    Write-Host '  -IUnderstandThisRuns, and even then it is DRY-RUN unless -Apply'
    Write-Host '  is separately authorized by the Mastermind (sole runtime slot).'
    exit 0
}

$DisposableDatabasePattern = '^ahlena_(refund|verify|disposable)[a-z0-9_]*$'

function Get-ConnectionStringParts {
    <#
        Parses an Npgsql connection string without ever echoing it. Returns a
        hashtable with only the Host, Port and Database. Passwords, usernames
        and every other key are dropped and never returned.
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        throw 'ConnectionString is required and there is no default or environment fallback.'
    }

    $builder = New-Object System.Data.Common.DbConnectionStringBuilder
    try {
        $builder.set_ConnectionString($Value)
    }
    catch {
        throw 'ConnectionString is not a parsable connection string.'
    }

    $host_ = ''
    $database = ''
    $port = 0
    foreach ($key in @($builder.Keys)) {
        switch -Regex ($key) {
            '(?i)^(host|server|data source)$' { $host_ = [string]$builder[$key] }
            '(?i)^(database|initial catalog)$' { $database = [string]$builder[$key] }
            '(?i)^port$' { [void][int]::TryParse([string]$builder[$key], [ref]$port) }
            default { }
        }
    }

    return [pscustomobject]@{
        Host = $host_
        Port = $port
        Database = $database
    }
}

function Test-LoopbackHost {
    [CmdletBinding()]
    param([string]$HostName)

    if ([string]::IsNullOrWhiteSpace($HostName)) { return $false }
    $candidate = $HostName.Trim([char[]]@('[', ']'))
    if ($candidate -eq 'localhost') { return $true }

    $address = $null
    if ([System.Net.IPAddress]::TryParse($candidate, [ref]$address)) {
        return [System.Net.IPAddress]::IsLoopback($address)
    }
    return $false
}

# ---------------------------------------------------------------------------
# Guards G1 + G2 — host and database allowlists.
# ---------------------------------------------------------------------------
$parts = Get-ConnectionStringParts -Value $ConnectionString

if (-not (Test-LoopbackHost $parts.Host)) {
    throw ('Refusing to run: connection host must be localhost, 127.0.0.1 or ::1. ' +
        'The host shown here is not a loopback address.')
}

if ($parts.Database -notmatch $DisposableDatabasePattern) {
    throw ('Refusing to run: database name must match the disposable pattern ' +
        $DisposableDatabasePattern + '. Refusing any shared, production or ' +
        'shipped default database name.')
}

# ---------------------------------------------------------------------------
# Guard G3 — client-type gate. Selection is key-presence-gated (W0 D4), so a
# present key means the REAL client would be bound regardless of environment.
# ---------------------------------------------------------------------------
$paymobSecretKey = [Environment]::GetEnvironmentVariable('Paymob__SecretKey')
$paymobSecretKeySection = [Environment]::GetEnvironmentVariable('Paymob:SecretKey')
$resolvedClientType = if ([string]::IsNullOrWhiteSpace($paymobSecretKey) -and
    [string]::IsNullOrWhiteSpace($paymobSecretKeySection)) {
    'DevelopmentPaymobClient'
} else {
    'PaymobClient'
}

if ($resolvedClientType -ne 'DevelopmentPaymobClient') {
    throw ('Refusing to run: the resolved IPaymobClient would be PaymobClient, not ' +
        'the deterministic double. Paymob__SecretKey is present in this environment, ' +
        'and selection is key-presence-gated, not environment-gated. Clear the key ' +
        'for this process before any refund reconciliation run.')
}

# ---------------------------------------------------------------------------
# The dry-run sweep. Read-only selection of unknown-outcome rows.
# ---------------------------------------------------------------------------
function Invoke-SafePsqlScalar {
    <#
        Runs a single read-only scalar query and returns its trimmed text. The
        connection string is passed through the PGPASSWORD/PGCONNINFO-free path:
        the query and the connection are supplied to psql via the environment,
        and the connection string is never printed.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Query
    )

    $previousConn = $env:PGDATABASE
    $previousHost = $env:PGHOST
    $previousPort = $env:PGPORT
    try {
        # psql accepts a full connection URI/string via the PG* variables only in
        # decomposed form; use the decomposed host/port/db the guards already
        # parsed. The credential is supplied to the CLI out-of-band by the
        # operator's .pgpass, never by this script.
        $env:PGHOST = $parts.Host
        if ($parts.Port -gt 0) { $env:PGPORT = [string]$parts.Port }
        $env:PGDATABASE = $parts.Database

        # Native-command stderr (NOTICE, .pgpass warnings) must not become a
        # terminating NativeCommandError; the exit code below is the truth.
        $previousEap = $ErrorActionPreference
        $ErrorActionPreference = 'Continue'
        try {
            $output = & $PsqlPath -X -A -t -v ON_ERROR_STOP=1 -q -c $Query 2>&1
        }
        finally {
            $ErrorActionPreference = $previousEap
        }
        if ($LASTEXITCODE -ne 0) {
            throw "psql returned exit code $LASTEXITCODE for a read-only query."
        }
        return ($output | Out-String).Trim()
    }
    finally {
        $env:PGDATABASE = $previousConn
        $env:PGHOST = $previousHost
        $env:PGPORT = $previousPort
    }
}

$nowUtc = if ([string]::IsNullOrWhiteSpace($SweepNowUtc)) {
    [DateTime]::UtcNow.ToString('o')
} else {
    ([DateTime]::Parse($SweepNowUtc, [System.Globalization.CultureInfo]::InvariantCulture,
        [System.Globalization.DateTimeStyles]::AdjustToUniversal)).ToString('o')
}

Write-Host 'refund-reconciliation-dryrun'
Write-Host ("  target host     : {0}" -f $parts.Host)
Write-Host ("  target database : {0}" -f $parts.Database)
Write-Host ("  resolved client : {0} (deterministic double asserted)" -f $resolvedClientType)
Write-Host ("  sweep now (utc) : {0}" -f $nowUtc)
Write-Host ("  mode            : {0}" -f $(if ($Apply) { 'APPLY (authorized)' } else { 'DRY-RUN (writes nothing)' }))
Write-Host ''

# Select every refund case whose latest entry is a TimedOut row, i.e. the
# money core's HasUnresolvedTimeout = true set (decision #17).
# Schema facts (money-core migration 20261022210000):
#   - entries join to cases via refund_case_id (the unique principal key);
#   - entry_type is a SMALLINT of the RefundLedgerEntryType enum - TimedOut = 7;
#   - amount is numeric(18,2) in EGP MAJOR units (piastres in the domain).
# "Latest" is ordered by entry_sequence, the ledger's persisted per-case append
# position, matching the domain aggregate's LatestEntry (version-7 ids are
# time-ordered at millisecond precision only and are NOT a sortable latest
# key); occurred_on_utc is display metadata only.
$unknownOutcomeQuery = @'
SELECT e.refund_case_id,
       e.amount,
       e.occurred_on_utc,
       COALESCE(e.provider_reference, '') AS provider_reference
FROM finance.refund_ledger_entries e
JOIN LATERAL (
    SELECT c.id
    FROM finance.refund_ledger_entries c
    WHERE c.refund_case_id = e.refund_case_id
    ORDER BY c.entry_sequence DESC
    LIMIT 1
) latest ON latest.id = e.id
WHERE e.entry_type = 7 -- RefundLedgerEntryType.TimedOut
ORDER BY e.occurred_on_utc ASC;
'@

Write-Host 'Unknown-outcome refund candidates (latest entry = TimedOut):'
$raw = Invoke-SafePsqlScalar -Query $unknownOutcomeQuery

if ([string]::IsNullOrWhiteSpace($raw)) {
    Write-Host '  (none)'
}
else {
    foreach ($line in ($raw -split "`r?`n" | Where-Object { $_ -and $_.Trim() -ne '' })) {
        $cells = $line -split '\|'
        $caseId = $cells[0]
        $amount = if ($cells.Count -gt 1) { $cells[1] } else { 'unknown' }
        $occurred = if ($cells.Count -gt 2) { $cells[2] } else { 'unknown' }
        $providerRef = if ($cells.Count -gt 3) { $cells[3] } else { '' }

        # Print the decision the sweep WOULD take. Nothing is written in dry-run.
        $decision = 'RECONCILE-REQUIRED (obtain provider statement, then ReconcileTimeout with evidence; do NOT retry blindly)'
        Write-Host ("  case {0}: amount={1} EGP (major units), timedOutAtUtc={2}, providerRef={3}" -f `
            $caseId, $amount, $occurred, $(if ($providerRef) { '<present>' } else { '<none>' }))
        Write-Host ("    would take: {0}" -f $decision)

        if ($Apply) {
            # The ONLY permitted write. Append a zero-amount reconciliation is
            # NOT done here: ReconcileTimeout requires real evidence, which a
            # sweep cannot invent. Apply therefore only records the intent and
            # refuses without a separate evidence file. Kept deliberately inert.
            Write-Host '    apply: SKIPPED — reconciliation requires provider evidence; the sweep cannot invent it. Supply evidence and reconcile through the money-core handler.'
        }
    }
}

Write-Host ''
if ($Apply) {
    Write-Host 'RESULT: APPLY requested. No reconciliation was written: the sweep never fabricates evidence (decision #17).'
}
else {
    Write-Host 'RESULT: DRY-RUN — nothing was written. Receipt: no ledger rows changed.'
}
exit 0
