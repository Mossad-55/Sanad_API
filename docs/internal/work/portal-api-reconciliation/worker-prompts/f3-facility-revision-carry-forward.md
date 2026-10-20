# F3 — Approved-facility revision carry-forward (RELEASE VERIFIED)

**Mastermind acceptance (2026-10-20):** phase `accepted`; release `authorized`; now
**`completed`** — the delegated release was verified end to end (implementation
`607d33fcb651bed25239050989cb94a8cb36de41`, receipt `3dba7b98357e8a72e07bdc1d580d4032bdb4e5e3`
pushed, deployed remote revision `607d33f`, 9/9 applied heads matching, zero migrations
applied in all nine contexts, service active `NRestarts=0`, env/unit/appsettings/keyring
fingerprints unchanged, live OpenAPI proves the additive nullable `openRevisionId`; no
authenticated Production request was made).
Reviewed the actual diff myself: carry-forward clones revision-bound document records
(type/status/verifier/time/expiry/CreatedOnUtc preserved, so an older verified copy can
never supersede a newer rejected replacement and ordering within a revision is stable),
clones media by kind/position over the same storage keys, protects a shared file on draft
removal (`stillReferenced` check before `DeleteAsync`), covers both entry points
(draft-save handler and media-first `PrepareProfileMediaRevision`, incl. loading
`Documents`), leaves GET read-only, and adds nullable `openRevisionId` additively
(positional record change is compile-checked at every construction site). Accepted
evidence: strict solution build 0 warnings/0 errors, **58/58** focused CareHomes tests
(7 new, incl. expired/rejected/ordering/shared-file cases) and **22/22** real localhost
HTTP checks (owner isolation, stale 409, submit+approve without re-upload, shared-file
survival, no asset resurrection, 3/3 own sessions logged out). Explicit and accepted
limitations: `Submit` has never validated document expiry (validity is enforced at
`Review(Approved)`/`Reactivated` — pre-existing behavior, deliberately unchanged, so the
expiry case is proven at approval); the local adapter is ~1.5k lines (thin L1 adapter, not
a copied harness — do not imitate the size later); the fixture added ~31 INSERT-only rows
across three revisions and three dedicated identities; the first adapter run failed on a
PowerShell closure bug and was fixed. The legacy-draft missing-versus-removed ambiguity
stays unrepaired. Release executor may stage ONLY the F3 manifest, publish clean pushed
source, deploy/verify the existing target; no migration expected. Mastermind alone closes.

Phase: `in_progress` (Owner authorized); task: `ready_for_review`; release: authorized
conditionally after Mastermind acceptance, no release by Worker. Base:
`ea3c3bb6b50b58b39bbea954767621d70029e763`. Worker A: `opencode/beam-free#high`.

**Release verified (2026-10-20, delegated release executor).** Implementation commit
`607d33fcb651bed25239050989cb94a8cb36de41` is on `main`/`origin/main`, published from a
clean detached checkout of that exact revision (local clone of the pushed repo, detached
HEAD, clean tree), and deployed to the existing Production target `72.62.92.144:8091`,
service `sanad-api.service`, directory `/root/sanad-api`, environment
`/etc/sanad/sanad.env`. Package: `deploy/releases/f3-607d33f/sanad-api-f3-607d33f.tar.gz`,
11,895,649 bytes, SHA-256
`a5d52d730b4138508dc22d3585f307fb314134e1cdcc1068feeca58806819870` (build-machine and
server hashes matched; the archive contains no `appsettings*.json`, `.env`, key or cert
entry). Only the exact eleven-file F3 manifest was staged and committed; every unrelated
local edit was preserved untouched (governance/handoff/README/agent configs/tools/
contracts, the E03 prompts, and the concurrent F5/F6 briefs plus their in-flight
Identity/Cms/Community/Families edits).

Preflight revalidated the live target before any write: the service was active with
`NRestarts=0` at the prior F4 revision `4d3cab488bd6a57447352d265c9955378f1b243c`,
Production, database reachable, and all nine applied heads already equal to the manifest
heads — F3 adds no migration, so **zero pending migrations** were confirmed for all nine
contexts (source heads identical to F4). A private pre-cutover backup of the app
directory plus copies of the host env file and unit were written under
`/root/sanad-releases/f3-607d33f-20261020/` (directory mode 700, files 600; no secrets
printed). The service was stopped, only the verified archive was overlaid, then started.
Startup checked all nine contexts and applied **zero** migrations each.
`Verify-SanadDeployment.ps1` passed: manifest revision `607d33f`, remote revision match,
Production, all 9 required/applied heads match, startup log 9/9 contexts, anonymous
`GET /api/v1/lookups/services` HTTP 200. Post-cutover the service is active (`running`,
`NRestarts=0`) and the unit / env-file / `appsettings.json` /
`appsettings.Development.json` / DataProtection-key SHA-256 fingerprints are
byte-identical to the pre-cutover values; the host storage root `/var/sanad/files` is
untouched.

Anonymous live smoke (no account created, no fixture/data write, no provider call, no OTP,
local adapter **not** run against Production): `GET /api/v1/care-homes/facilities/mine`
→ HTTP 401 and `GET /api/v1/care-homes/facilities/mine/media/{id}/file` → HTTP 401. The
published schema (`deploy/releases/f3-607d33f/openapi-live.json`, HTTP 200) carries
`CareHomeOwnerProfile.openRevisionId` as a nullable string, while the same document
fetched from the pre-cutover F4 revision contained no `openRevisionId` — the live
F3-only additive field is proven published. Release is `verified`; the coordinating
Mastermind closes the phase (this executor does not mark it `completed`).
Session is returned by dispatch and recorded by Worker in Current result.

Read root/governance, Mastermind handoff and [approved F3 scope](../contracts/frontend-follow-up-2026-10-20.md#f3--approved-facility-revision-carry-forward-a2aa13).
F2 is deployed; preserve all unrelated dirty work (configs/README/bridge/governance/
intake/contracts/audits/E03 notes). F4 runs concurrently on Community-only files.
No overlapping ownership, subagents, commits, migrations or production operations.

## Behavior and acceptance

- New editable revision of an Approved facility inherits unchanged documents/media
  via new revision records referencing existing files. Approved history stays frozen;
  preserve media kind/order and document verification actor/time/expiry. Do not
  mark expired/rejected documents valid. Replacements go through normal review.
- Cover both draft-save and media-first revision creation. Return nullable
  `openRevisionId`; GET must not create a revision or backfill assets.
- Submission/review use the revision's explicit effective assets; keep existing
  two-photos-including-cover and four-document-type requirements. Never silently
  restore deliberately removed assets at submit. Shared storage references must
  protect approved/history files when a draft media record is removed/replaced.
- Existing incomplete drafts: inspect what existing data can distinguish. No bulk
  repair or guessed inheritance. Report ambiguous missing-versus-removed history
  to Mastermind; new-revision work can proceed independently. No new migration or
  persisted marker unless Mastermind first approves exact necessity/manifest.
- Verify phone-only edit/submit with unchanged valid assets; replacements/deletions,
  rejected/expired docs, approved-history reads/file availability, stale versions,
  retry without duplicates, owner isolation and no GET mutation.

## Exact writable manifest

- `src/Modules/CareHomes/Domain/Sanad.Modules.CareHomes.Domain/Facilities/CareHomeFacility.cs`
- `src/Modules/CareHomes/Domain/Sanad.Modules.CareHomes.Domain/Facilities/CareHomeDocument.cs`
- `src/Modules/CareHomes/Domain/Sanad.Modules.CareHomes.Domain/Facilities/CareHomeProfileMedia.cs`
- `src/Modules/CareHomes/Application/Sanad.Modules.CareHomes.Application/Facilities/CareHomeProfileCommands.cs`
- `src/Modules/CareHomes/Application/Sanad.Modules.CareHomes.Application/Facilities/CareHomeProfileMediaCommands.cs`
- `src/Modules/CareHomes/Application/Sanad.Modules.CareHomes.Application/Facilities/CareHomeDocuments.cs`
- New if needed: `src/Modules/CareHomes/Application/Sanad.Modules.CareHomes.Application/Facilities/CareHomeRevisionCarryForward.cs`
- `tests/Sanad.UnitTests/CareHomes/CareHomeFacilityTests.cs`
- `tests/Sanad.UnitTests/CareHomes/CareHomeProfileCommandTests.cs`
- `tests/Sanad.UnitTests/CareHomes/CareHomeProfileMediaTests.cs`
- `tests/Sanad.UnitTests/CareHomes/CareHomeDocumentUploadTests.cs`
- `tests/Sanad.UnitTests/CareHomes/CareHomeReadbackTests.cs`
- `docs/care-homes/owner-onboarding.md`
- `docs/postman/care-homes/Sanad.CareHomes.Owner.postman_collection.json`
- New if needed: `tools/Invoke-F3LocalHttpVerification.ps1` (small adapter to L1, no copied F2 framework).
- This brief: assigned task/current result only; do not update handoff/phase status.

Everything else is read-only unless Mastermind grants bounded manifest amendment.
Avoid unrelated cleanup. Preserve existing code style. Consumer docs describe
implemented behavior only, not pending historical-draft repair.

## Parallel verification gate

Implement and author focused tests/docs now. F4 holds the first shared build/test/
localhost fixture execution slot. F3 must **stop at implementation handback before
running dotnet builds/tests, starting API or writing fixtures**; report exact pending
commands and needed minimal fixtures. No polling/wait loops. Mastermind resumes F3
after F4 releases that slot. Static diff/link/JSON checks may run immediately.
Final acceptance requires strict successful build/affected tests, zero build/test
warnings, real local HTTP proof using minimal reusable synthetic records, private
assets and own-session cleanup. Isolated destructive tests do not reset SanadDb.

## Current result

F3 bounded verification handback. Worker session `ses_ea583e853ffeTIcovs3SHLpugj`
(provider `river`, model `deepseek-ai/DeepSeek-V4.1-Flash`). Base
`ea3c3bb6b50b58b39bbea954767621d70029e763`; worktree HEAD at dispatch
`17ea8a8dc66bb115092579eb8b5e5fb0f11be4bf` (F2/F4 close-out commits, not touched).
Task state: `ready_for_review`. No production, SSH, deploy, publish, commit or push
was performed; no migration, schema or provider call.

Files changed by this handback (approved manifest only):
- `tests/Sanad.UnitTests/CareHomes/CareHomeProfileCommandTests.cs` — tests 1-3 plus shared helpers.
- `tests/Sanad.UnitTests/CareHomes/CareHomeProfileMediaTests.cs` — test 4 plus helpers.
- `tools/Invoke-F3LocalHttpVerification.ps1` — new bounded local-HTTP adapter over L1,
  loopback default `http://localhost:5241` (F4 keeps 5235/5237).

Focused tests added (real domain/handler calls, fresh-context reads, explicit timestamps,
no wall-clock ties):
1. `ApprovedFacility_KeepsNewerUsableDocumentLatest_WhenOlderVerifiedDocumentIsExpired` —
   approving a revision that carries an older expired verified document plus a newer usable
   verified one; the new revision preserves inheritance order, the newer row stays effective
   (`IsUsableOn`), the older clone stays unusable, submit succeeds, and approval resolves the
   latest usable row.
2. `ApprovedFacility_ExpiredInheritedDocument_StaysUnusable_AndBlocksApproval` — an inherited
   expired verified document is neither auto-restored nor flipped to non-expiring; the in-place
   re-verification exchange throws `Only pending care-home documents can be verified`, and
   approval is refused with `must be Admin-verified and valid before approval`.
3. `ApprovedFacility_RejectedReplacement_StaysLatestAndBlocksApproval` — a rejected replacement
   in the editable revision is not superseded by the older verified clone (approval refused);
   the rejected row survives the correction round and the next revision inherits only the
   approved revision, never the rejected document.
4. `Submit_after_removing_inherited_media_keeps_the_approved_file_and_does_not_restore_the_asset` —
   removing an inherited draft media row removes only the draft row; the approved row and its
   `LocalDiskFileStorage` file survive (`File.Exists` plus content readback), a re-save does not
   resurrect it, and submit refuses with `CareHomes.Facility.ProfileMediaRequired` until a real
   replacement is uploaded, after which submit succeeds.

Gates (each run once, no loops):
- `dotnet build Sanad.slnx -c Release --nologo /p:TreatWarningsAsErrors=true` →
  **succeeded, 0 Warning(s), 0 Error(s)**, exit 0.
- `dotnet test tests/Sanad.UnitTests/Sanad.UnitTests.csproj -c Release --no-restore
  /p:TreatWarningsAsErrors=true --filter "FullyQualifiedName~CareHomeFacilityTests|FullyQualifiedName~CareHomeProfileCommandTests|FullyQualifiedName~CareHomeProfileMediaTests|FullyQualifiedName~CareHomeDocumentUploadTests|FullyQualifiedName~CareHomeReadbackTests"`
  → **Passed! Failed: 0, Passed: 58, Skipped: 0, Total: 58**, exit 0.
- `tools/Invoke-F3LocalHttpVerification.ps1` over real local HTTP →
  **passed=True, 22/22 checks, exit 0**.
- `git diff --check` → exit 0 (CRLF notices only); PowerShell parse of the adapter → OK
  (`#requires -Version 5.1`, 8807 tokens, ASCII only).

Local-HTTP proof (read-only target verification + fixture guard both PASS before any write).
All 22 checks PASS; one-line evidence:
- `fixture.identities-provisioned` — 3 dedicated Active identities with expected ids/account types.
- `fixture.facility-approved` — one facility `01a15aa7-...-6ed0fe` created through the real
  owner/admin flows (201 create, profile save, 4 documents, cover+gallery, submit, 4 verifies, approve).
- `read.open-revision-null` — GET owner profile 200, `status=5`, `openRevisionId` null.
- `read.get-creates-nothing` — second GET unchanged `version=14 documents=4 media=2`; no revision/asset backfill.
- `edit.phone-only-creates-open-revision` — PUT profile 200, stays `status=5`, new non-null
  `openRevisionId` distinct from the unchanged `approvedRevisionId`.
- `edit.inherited-assets-share-approved-files` — new revision inherits 4 documents / 2 media rows
  with identical private storage keys, status, kind and position; approved-revision signature unchanged (psql readback).
- `edit.resave-adds-no-duplicate-inheritance` — re-saving the same edit keeps the same open revision, no duplicate rows.
- `edit.stale-version-refused` — PUT with a stale `expectedVersion` → 409 `CareHomes.Facility.Conflict`.
- `isolation.other-owner-profile-not-found` — another owner GET `/mine` → 404 `CareHomes.Facility.NotFound`.
- `isolation.other-owner-media-not-found` — another owner GET `/mine/media/{id}/file` → 404.
- `submit.without-reuploading-assets` — submit 200, `status=2`, `submittedRevisionId` = inherited revision, no re-upload.
- `review.approve-carried-forward-revision` — Admin approve 200, `status=5`, inherited revision becomes approved (no re-verify).
- `media.remove-inherited-draft-image` — DELETE inherited draft media → 204.
- `media.approved-file-survives-inherited-removal` — approved media file still readable 200 (67 bytes) after the draft row is removed.
- `media.read-does-not-resurrect-removed-asset` — read path returns 0 draft galleries, approved gallery present, no resurrection.
- `media.replacement-upload` / `media.replacement-submit` / `media.replacement-approve` — replacement
  gallery upload 200 (new id), submit 200, Admin approve 200 `status=5` with the replacement revision approved.
- `session.logout.owner|owner-second|admin` — 204 each; `session.no-live-fixture-session` — 0 live fixture sessions.

Fixture rows added (INSERT-only, guarded, idempotent; reruns add nothing; nothing updated/deleted/reset):
`identity.users` 3, `identity.user_accounts` 3, `care_homes.facilities` 1, `care_homes.profile_revisions` 3,
`care_homes.documents` 12, `care_homes.profile_media` 6, `care_homes.review_history` 3 (~31 rows) plus 7
private files. `care_homes.facilities` total is 2 with `facilitiesOurs=1`: the single pre-existing unrelated
row was not touched. The fixture facility is left stable: Approved, no open revision, revision 3 approved,
cover plus one gallery.

Limitations and ambiguity reported (no product policy invented):
- **Submit does not enforce document expiry.** `CareHomeFacility.Submit` validates only that the four
  document types exist per revision; the validity rule lives in `Review(Approved)`/`Reactivated`/discovery.
  Deliverable 1(b) assumed a submit-time refusal, so test 2 asserts the actual behavior (refusal at approval
  plus the non-fakeable re-verification exchange). This premise discrepancy is reported, not implemented.
- **Adapter size.** `tools/Invoke-F3LocalHttpVerification.ps1` is ~1.5k lines (comments plus psql/readback
  plumbing), larger than the brief's "few hundred lines" target. It remains a thin adapter: it dot-sources
  `Invoke-LocalFixtureVerification.ps1 -LibraryOnly`, reuses the L1 environment/HTTP/process/parse helpers and
  copies no F2 framework.
- **The local-HTTP gate ran twice.** The first attempt exited 1: 7 checks passed, then the
  `read.get-creates-nothing` assertion threw `Get-F3Array is not recognized`, because `GetNewClosure()`-bound
  assertion scriptblocks cannot resolve script-scope helper functions when the adapter runs as a child script.
  Fixed by dropping `GetNewClosure()` from the eight main-flow assertions (evaluated synchronously, so plain
  scriptblocks resolve script-scope functions and variables) and by guarding the draft-media DELETE when no
  gallery is found. Recorded evidence is the second run; the first run's only writes were the fixture identities
  and the facility bootstrap, and its `fixture.facility-approved` check passed then.
- **Fixture exceedance.** The brief's "minimal dedicated synthetic approved facility, no more than about 10 rows"
  note is exceeded (~31 rows): the real owner/admin flows need 4 documents and a cover-plus-gallery per revision
  across three revisions (bootstrap plus two carry-forward cycles), and three dedicated identities are needed
  because no shared account satisfies `CareHomesOperationalAdmin` (`content.admin@test.sanad.local` is ContentAdmin).
  No bulk seeding and no reset was used.
- Local Development `SanadDb` only; credentials, connection strings, tokens and payloads were never printed.
- Pre-existing ambiguity from implementation handback still stands: existing incomplete drafts cannot be
  distinguished as missing inheritance versus intentional removal; no bulk repair or guessed inheritance.
