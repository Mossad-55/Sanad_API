# SANAD CARE — OPERATIONS (File 2 of 2)

## B1-B1 MERGED TO MAIN / CLOSURE CLEANUP — 2026-09-18 (latest)
Owner ran the approved integration and reports all gates green (build, targeted tests 57/57, migration generation, commit). Coordinator final audit before merge: pushed head 747946fc3abcab1e166f1fe221c65a6c36013ca9 canonical Mossad A+C, contents exactly the four reviewed files (csproj +4/-0, migration +62, designer +1125, snapshot +77/-0 additive, line endings normalized by git). Live verification after merge: main == 747946fc, head commit canonical; PR19 closed merged=true by fast-forward reachability (no GitHub Merge button involved). PR19 carried the complete dormant B1-B1 stage: EF mapping + append-only save guard, independent tests (57/57 final tree), reviewed migration AddBookingCancellationFacts (table families.booking_cancellation_facts, unique booking_id index, Restrict FK, no data changes), API EF-design tooling reference.
State: dormant persistence milestone on main; NOT deployed; migration generated+reviewed but applied nowhere (applies at API startup under an authorized deployment; deployed baseline remains f787). No API surface change, so no Bruno gate this stage.
Remaining closure actions issued to owner: (1) close PR20 in the GitHub UI as superseded-by-canonical-commit bbe252fd (its content is byte-identically integrated on main; never the Merge button); (2) guarded cleanup batch: local branch deletions while on main (arena/01a0b436 via -d tip==main; arena/01a0b449 -D justified — d067ed31's complete content is verifiably on main via the canonical cherry-pick matching precomputed tree 0976640e, no unique work), remote branch deletions with leases expecting 747946fc (01a0b436) and d067ed31 (01a0b449); final ls-remote must show only main. Next bounded stage after cleanup confirmation: B1-B worker scoping (application/API activation: family Owner/Editor role gate on family cancel, caregiver confirmed-cancel endpoint+handler bound to assigned caregiver, fact recording through policy/fact types, NoRefundDue-vs-failed refund eligibility persistence, admin retry gating alignment; Bruno gates return there). Worker rules unchanged: code-only, no migrations/execution/merge; exact scope pinned at main 747946fc when issued.

## PART 2 FIX VERIFIED / PART 3 BLOCKER — API STARTUP LACKS EF DESIGN — 2026-09-18 (latest)
Feature branch head f680edd533d0bc2170cd8b5855fc3e623f4c46e2: fix(tests) commit canonical Mossad A+C over bbe252fd (canonical test cherry-pick) over 22934984 (canonical production) over main 70eba860. Owner Part 2 rerun: build pass, targeted gate 57/57 green (owner-reported before moving to Part 3).
Part 3 blocker (owner-paraphrased, not verbatim): migration generation reports Sanad.API.csproj does not reference EF design-time tooling. Live verification: Sanad.API.csproj has NO Microsoft.EntityFrameworkCore.Design reference; Families + Identity Infrastructure DO reference it (v10.0.11 central) but with PrivateAssets=all, which blocks transitive flow into the startup project's design-time assets — exactly the classic 'startup project doesn't reference Microsoft.EntityFrameworkCore.Design' condition. Same package layout existed during earlier successful Slice B/C generations (mechanism of that success not re-audited; the direct startup reference is the documented remedy regardless).
Fix delivered (coordinator small-fix, owner-approved proposal): complete corrected B1-booking-cancellation/Sanad.API.csproj adding the Design PackageReference with identical PrivateAssets/IncludeAssets metadata as the module projects; versionless (Directory.Packages.props pins 10.0.11). Build-tooling metadata only, zero runtime impact; one-time setup reused by all future module migrations. Flow issued: GUI-replace csproj on arena/01a0b436-sanad-api -> guarded block (exactly-one-file check -> csproj diff must show only the added block -> Release build restores+compiles -> re-run Part 3 EF generation unchanged) -> owner pastes build/EF output + status/diff --stat + the two migration files + snapshot for coordinator review BEFORE any commit. After review: ONE canonical commit carries csproj fix + migration artifacts together. If the same error recurs: paste verbatim EF output + dotnet ef --version (no paraphrase) for re-diagnosis. No DB writes authorized.

## PR19/20 INTEGRATED / TEST-FILE FAILURE ANALYSIS + SMALL FIX — 2026-09-18 (latest)
Owner completed Part 1 integration: feature branch arena/01a0b436-sanad-api carries production (22934984, canonical, tree cf552014) + canonically cherry-picked test commit (expected tree 0976640e verified by guards; owner reported success), pushed. Part 2: Release build PASSED; targeted gate ran 57 tests: 55 green, 2 RED — both inside BookingCancellationFactMappingTests.AssertHasValueConverter (line 323), Expected Guid / Actual null. Guard tests ALL GREEN (append-only behavior evidence now exists owner-machine).
Root cause (coordinator analysis, EF Core 10 API contract): line 323 asserts property.GetProviderClrType() == Guid. The relational provider-CLR annotation is only written by the GENERIC HasConversion<T>() form (enum-to-int columns passed this probe), while the LAMBDA form .HasConversion(id => id.Value, value => new XId(value)) — the module's long-standing strong-ID pattern, proven in production bookings migrations — stores the conversion on the configured ValueConverter and leaves that annotation null. EF Core 10 ValueConverter.ProviderClrType is abstract non-null (verified in release/10.0 source), so asserting ModelClrType/ProviderClrType on the converter is the correct probe. Test-helper-only defect; NO production defect established.
Small-fix rule applied (no worker prompt): coordinator delivered ONE corrected complete file B1-booking-cancellation/BookingCancellationFactMappingTests.cs (only the helper rewritten; enum annotation probe retained; everything else byte-identical to PR20's reviewed content). Flow issued to owner: GUI-replace the file on arena/01a0b436-sanad-api -> guarded block (exactly-one-modified-file check -> Release build -> targeted filter FullyQualifiedName~BookingCancellationFact, expect total 57 / 57 passed -> canonical commit fix(tests) -> fast-forward push). Then Part 3 (owner migration generation AddBookingCancellationFacts, generation only, coordinator reviews before commit) proceeds as previously issued. No DB writes authorized. New expected head will be verified live after push before the migration step evidence is accepted.

## PR19 IDENTITY FIXED (VERIFIED) / PR20 B1-B1 TESTS REVIEW — PASS / INTEGRATION ISSUED — 2026-09-18 (latest)
PR19 amended commit 22934984878506867bda615912e194da20d39141 live-verified: tree cf552014 (unchanged), parent 70eba860, author AND committer canonical Mossad <mosad55522@gmail.com>. Identity correction closed.
PR20 (test worker) head d067ed31c0f79e0e0ac938ab4c3611de34ad622b on arena/01a0b449-sanad-api, base arena/01a0b436-sanad-api, mergeable=clean, +851/-0 exactly the two assigned test files. Worker pinned by tree correctly (cf552014). Commit A/C = Arena Agent <agent@arena.ai> — AI identity must never enter main history; the integration path below fixes identity AND ancestry in one move (no GitHub Merge button). Structural note: PR20 parents main 70eba860 (worker session constraint), so its branch alone lacks the PR19 production edit; integration is required before any compile.
STATIC REVIEW VERDICT: PASS (nothing compiled here; no merge approval). Mapping tests verified complete: all 14 members' columns/nullability theories, enum provider-int incl. nullable reason_category, reason_note maxlength pinned to Booking.MaximumReasonLength + literal 500, unique index name+IsUnique, FK required/Restrict/principal strong key/no navigations, Booking free of fact navigation (EF + CLR reflection), IFamiliesDbContext anti-ripple with positive controls, no shadow properties, design-time model usage, honest InMemory-conversion limitation. Guard tests verified: Added persists+round-trips via fresh context (sync+async), Modified/Deleted rejected on ALL FOUR entry points incl. acceptAllChangesOnSuccess overloads, message assertions exactly match production wording (entity/state/fact id), store unchanged after rejection, detached neither blocked nor persisted, unrelated Booking save unaffected, discard-unblocks-then-append contract. All references cross-checked against production source: BookingCancellationBookingFactory EXISTS as internal static class inside BookingCaptureEvidenceAdapterTests.cs (same assembly+namespace, builds bookings via public factories — no invented helper); ForFamilyCancellation signature exact; Feedback.Create trims; BookingCaptureEvidence.Captured; FullCapturedRefund/FamilyCancellationWithinGraceWindow/CurrentPolicyVersion members confirmed. EF Core 10 source checked for the GetSchema default-schema fallback (release/10.0 RelationalEntityTypeExtensions) — schema assertions sound. No blocking finding; no correction prompt issued (nothing small to fix either).
NEXT ISSUED TO OWNER (one batch, stop-on-error, report together): (1) canonical integration: on arena/01a0b436-sanad-api at 22934984, cherry-pick -n d067ed31, verify staged == exactly the two test files, canonical commit, verify parent/tree — expected combined tree precomputed via merge-tree: 0976640ed81d895ef081057d825b5f7546a83d8a — fast-forward push (no force); (2) Release build then targeted test filter FullyQualifiedName~BookingCancellationFact (matches exactly the two new classes); (3) only if green: owner EF migration GENERATION ONLY (AddBookingCancellationFacts, FamiliesDbContext, Families Infrastructure project, API startup, Release --no-build; factory needs ConnectionStrings__FamiliesDatabase or falls back to ConnectionStrings__IdentityDatabase — presence-only, never print values), then paste status/diff plus the migration files for coordinator review BEFORE any commit. PR20 stays open until cleanup; close later as superseded-by-canonical-commit, never Merge button. No DB writes authorized; migration application/deploy remain separately gated. Unique fact index is NOT double-refund prevention; B1-B handler work stays queued.

## PR19 B1-B1 STATIC REVIEW — SOURCE PASS / IDENTITY FIX REQUIRED — 2026-09-18 (latest)
Live review of draft PR #19 (arena/01a0b436-sanad-api -> main), single commit 0284696e6edc3db4afd5859b03e6d4bf270c7e07, parent exactly 70eba860 (baseline pinned), tree cf55201475e77b98a7fba63ba7867f8e535468f4, mergeable=clean. Diff = exactly the two assigned files (+137/-0): new BookingCancellationFactConfiguration + FamiliesDbContext edit. No other repo file touched; no migration/snapshot/startup/DI/tests/domain changes. Branch name differs from suggested work/b1b1-cancellation-persistence (Arena session branch) — precedent: not a defect.
SOURCE VERDICT: PASS (static only; not compiled/tested, no merge approval). Verified: 14 fact members mapped 1:1 with snake_case names; strongly-typed ID conversions compile against readonly record struct BookingCancellationFactId(Guid Value); all enums HasConversion<int> incl. nullable reason_category (EF null passthrough); reason_note HasMaxLength(Booking.MaximumReasonLength) nullable; unique index ux_booking_cancellation_facts_booking on booking_id (one fact per booking, doubles as FK index); HasOne<Booking>().WithMany() FK Restrict with NO navigation added to Booking and BookingConfiguration untouched; IFamiliesDbContext untouched so fakes compile. Guard: all four save entry points overridden, parameterless/single-token delegate to the bool overloads so the guard runs exactly once before base save on both paths; Modified/Deleted -> InvalidOperationException naming entity/state/id; Added/Unchanged/Detached allowed; known raw-SQL/bypass limitation documented. PR body claims match the code exactly; execution statement honest (nothing run).
PRE-INTEGRATION CORRECTION (process, HIGH): commit author/committer is Mossad-55 <57729640+Mossad-55@users.noreply.github.com> — NOT canonical Mossad <mosad55522@gmail.com>. Standing law + B1-A precedent: main-bound history must be canonical at integration (B1-A main integration preserved worker commits verbatim). Fix = amend author+committer on the SAME tree cf552014 (no content change) and force-with-lease push the draft branch; owner command block issued with guards (clean tree, fetch head == 0284696e, parent == 70eba860 after amend, tree unchanged, identity exact, lease expecting old head). No GitHub Merge button involved; PR auto-updates.
LOW disclosures (non-blocking, folded into test-worker scope): (1) guard state visibility goes through ChangeTracker.Entries which respects AutoDetectChangesEnabled=false — documented limitation class; tests cover default-path behavior; (2) nullable-enum HasConversion<int>() correctness asserted via design-time EF model metadata, NOT InMemory round-trips (conversions are skipped by the InMemory provider).
NEXT: owner amend batch -> coordinator re-verifies amended head (canonical identity + tree cf552014 + parent 70eba860) -> independent TEST worker (prompt delivered: B1-booking-cancellation/prompt-b1b1-tests.txt, pinned by tree) writing mapping-metadata + append-only guard tests in a separate workspace, draft PR targeting arena/01a0b436-sanad-api (B1-A tests precedented targeting the feature branch, not main) -> coordinator review/integration -> owner compile preflight -> owner-generated migration (reviewed) -> gates. No migration from worker, no DB writes authorized, no deployment claim.

## OWNER RULE — WORKSPACE HYGIENE / PHASE FOLDERS — 2026-09-18 (latest)
Owner ruling: mastermind keeps the shared workspace clean — files no longer used are cleared, and every generated deliverable goes into a folder named for the current phase. Rule recorded in Master §6 (updated copy delivered at workspace root) and applies to every future mastermind without re-asking.
Concrete shape: root = ONLY the two active control files (updated copies the owner replaces from). uploads/ = owner input area, never auto-cleaned. Current phase folder `B1-booking-cancellation/` holds the active worker prompt and will hold its correction/test artifacts; per-agent stage artifacts are named explicitly (e.g. prompt-b1b1-implementation.txt) so multiple bounded stages coexist without clobbering. Obsolete items (superseded prompts, merged/delivered correction files, review scratch) are deleted as soon as dead; durable history lives in git archive tags and these control files, not in leftover workspace files.
Applied immediately: workspace was already lean (prompt.txt + updated Operations only; no stray clones/zips/scratch). The active B1-B1 worker prompt is re-homed: /home/user/B1-booking-cancellation/prompt-b1b1-implementation.txt (content unchanged, still pinned at main 70eba860). Earlier entries saying "prompt.txt at root" are superseded in path only. Owner action unchanged: send the prompt file + both control files to the implementation worker.

## B1-A FOUNDATION CLOSED / B1-B1 PROMPT ISSUED — 2026-09-17 (latest)
Owner gate results: full Release build done at 70eba860 and targeted three-class unit gate (BookingCancellationPolicyTests, BookingCancellationFeedbackAndFactTests, BookingCaptureEvidenceAdapterTests) PASSED, owner-reported; exact count not supplied, static expectation ~117. Owner confirmed earlier: no full-suite run and no Bruno run — both correct for this dormant stage (zero API surface, zero EF delta).
Cleanup batch completed owner-side: switched to main, local main == origin/main == 70eba860, tree clean (## main...origin/main); local work branches deleted; historical local branches archive-tagged and deleted. Coordinator live-verified remote: heads = ONLY main 70eba860; archive tags pushed and pointing at the recorded tips: archive/old-arena-01a09bd4 -> 3f61aa2808d9e0cc11cd361c2667b8263149bca7 and archive/old-set-c-account-switch -> df1099319106f016cac8b4f0c3caaa71320052c3 (owner used 'old-' tag names differing from the coordinator script; SHAs verified correct). B1-A dormant foundation milestone is CLOSED: merged, canonical identities, gates green per owner reports. Deployed baseline remains f787; no deployment claimed for the foundation.
B1-B1 IMPLEMENTATION worker prompt ISSUED as /home/user/prompt.txt (fresh prompt; the withdrawn earlier B1-B1 prompt is not reused), pinned at merged main 70eba860d245aab6fda990816fa8e41ac3e5f1a3. Scope exactly two production files: NEW BookingCancellationFactConfiguration (snake_case mapping mirroring BookingConfiguration conventions, unique booking_id index, Restrict FK, no Booking/navigation changes) + EDIT concrete FamiliesDbContext (DbSet + append-only guard blocking tracked Modified/Deleted facts on sync+async save paths; IFamiliesDbContext deliberately untouched to avoid fake ripples). No tests/migrations/snapshot/startup/API/handler/payment edits by worker; pending-model drift after this stage is expected and NOT to be suppressed. Coordinator verified baseline source before writing the prompt (BookingCancellationFact member list, FamiliesDbContext has no SaveChanges override today, configuration conventions confirmed in BookingConfiguration).
Next: owner sends prompt.txt + both control files to the implementation worker (suggested branch work/b1b1-cancellation-persistence from exact 70eba860, draft PR to main if publication authorized, else complete-file return). Coordinator static review -> independent TEST worker (mapping metadata + guard behavior) against the pinned revision -> owner compile preflight -> owner-generated migration (name decided then, reviewed separately, no DB writes automatically authorized). Reminder for all later stages: the unique booking fact index is NOT double-refund prevention; handler/refund-claim concurrency remains separately bounded B1-B work; B1-C admin read models after that. No API policy activation or deployment verdict at B1-B1.

## REPO CHECK / EVIDENCE RECONCILIATION — 2026-09-17 (latest)
Owner asked for a full repo check and clarified evidence: they ran a full build, not a full test, and did not run Bruno. Coordinator live read-only verification: remote now advertises ONLY main at 70eba860d245aab6fda990816fa8e41ac3e5f1a3; both arena work branches (arena/01a0af5e-sanad-api, arena/01a0af97-sanad-api) are already deleted from origin. PR17 merged=true (head 70eba860, base f787); PR18 merged=true (head 70eba860, base b257bd1). Main ancestry f787 -> 01617c1 -> a363b20 -> b257bd1 -> 162df33 -> 36c8673 -> 70eba860; all six new commits canonical Mossad author+committer. Compare f787...70eba860: exactly 16 added files, 2555 insertions, 0 deletions, 0 modified pre-existing files = 13 dormant domain files + 3 test files. No controllers/DI/EF mappings/migrations. Matches the reviewed B1-A dormant foundation exactly; no production behavior shipped. Deployed baseline remains f787; no deployment claim for the foundation.
Evidence reconciliation: full Release build = compile gate satisfied (owner-reported). No full test suite required for this dormant stage; sole pending runtime evidence is the targeted three-class unit gate (BookingCancellationPolicyTests, BookingCancellationFeedbackAndFactTests, BookingCaptureEvidenceAdapterTests) against exact tree 70eba860, because the +3/-2 assertion cleanup postdates the last green targeted run at 36c8673. Full-suite run NOT requested now; AuthApiHostTests DB-isolation caution still applies to any full run. Bruno correctly not run: B1-A changed zero API surface. Bruno gate resumes at B1-B/C API stages and pre-deploy, with request count re-derived from the live repo at that time (standing inventory last verified 60 request files at f787).
Next steps issued: (1) targeted three-class dotnet test filter, Release --no-build, owner reports summary + exit code; (2) guarded local cleanup batch (switch/ff-verify main == 70eba860, ancestry-guarded deletion of local arena work branches, archive-tag then delete historical local arena/01a09bd4 and set-c-account-switch, push archive tags, final ls-remote main-only check). Remote branch deletion NOT needed (verified already absent). After gate + cleanup evidence, coordinator regenerates the B1-B1 cancellation-fact persistence worker prompt pinned at merged main 70eba860; withdrawn prompt stays withdrawn. B1-B1 introduces EF source delta: owner-generated migration only after review and compile preflight; no worker migrations, no DB writes. No API policy activation, no deployment verdict.

## PR17 + PR18 MERGED — LIVE VERIFIED — 2026-09-17
Owner stopped at PR status checkpoint, uncertain about Merge button. Coordinator independently verified GitHub API: PR17 closed/merged=true; PR18 closed/merged=true. Live remote main and both work branches all70eba860d245aab6fda990816fa8e41ac3e5f1a3. Owner's Git integration succeeded; no button or additional merge required. Prior integration-pending status superseded. Foundation only, no API policy activation/deployment claim. Runtime evidence gaps unchanged, do not invent final test count/exit.
Next only cleanup: delete remote arena/01a0af5e-sanad-api and arena/01a0af97-sanad-api using atomic deletion with leases expecting70eba860; normal local test branch deletion while on main; historical local branch deletion only after archive tags succeed as previously instructed. Remote branches not deleted at this checkpoint; local final status not yet reported. Owner confusion resolved by coordinator checking live PR states instead of making owner inspect UI. No GitHub Merge button.


## OWNER REQUESTS FULL MERGE/CLEANUP COMMAND BATCH — 2026-09-17
Owner clarifies wants coordinator commands NOW, not a write-access explanation. Issue full bounded owner batch: clean status/exact70eba860 guard; corrected-tree build/targeted tests if not already green; normal nonforce push combined70eba860 to implementation branch (PR18 integration); switch main and ff-only merge exact70eba860 then normal push main (PR17 integration). Fast-forward keeps reviewed canonical commits, no new merge identity and no GitHub Merge button. Verify both GitHub PRs actually merged and remote main exact70eba860 before cleanup. Delete remote feature refs only with explicit leases expecting70eba860 to avoid deleting new work. Prune origin metadata only after correct origin confirmed; not essential to merge. Local old unmerged histories not discard: archive tags first if removing old local branches; do not use force deletion without preserved tags. No deploy command; domain foundation merge only. Commands issued, results pending; don't record merge as done. Any error/mismatch -> stop, no force push main/reset. Owner reports at test branch70eba860 but local status still to verify. Both PRs may auto-mark merged by commit reachability; must verify, not assume. Postfix runtime green requires owner evidence; no invented pass.


## CURRENT OWNER DIRECTIVE — MERGE B1-A FOUNDATION / MAIN-ONLY CLEANUP — 2026-09-17
Owner explicitly requests BOTH PRs integrated, clean main only, complete records, no more owner processing now. This supersedes coordinator proposal to keep B1-A unmerged until B1-B/C and pending one-feature consolidation plan. Interpret as acceptance of a DORMANT B1-A foundation milestone, NOT claim complete booking cancellation/refund feature or release/deployment. Do not start B1-B1; prompt.txt remains withdrawn.
Owner reports local branch arena/01a0af97-sanad-api and HEAD70eba860d245aab6fda990816fa8e41ac3e5f1a3. No working-tree status supplied in this reply; local cleanliness NOT confirmed. Earlier exact analyzer correction was published at that SHA and verified +3/-2. No reason to ask for branch/HEAD again unless changed.
Live API recheck: PR17 open/unmerged draft headb257bd19785935446f376b91c7bf301c67bfc376 targets main; PR18 open/unmerged draft head70eba860d245aab6fda990816fa8e41ac3e5f1a3 targets implementation branch. Remote main remainsf787a5c2f1a22baddfd540b431f62ace7ed52d8f; three remote branches remain. Latest test head contains BOTH PR contents via ancestry and owner canonical assertion fix. Do not double-apply/squash-cherry-pick tests/production separately or lose review history. All reviewed commits canonical Mossad A+C; no identity repair needed at present.
EXECUTION LIMIT: this coordinator sandbox has no configured GitHub write credentials (prior verified), and owner forbids repeat credential probes. No remote merge/push/PR close/branch delete performed. Current request cannot be claimed completed using public read-only APIs. Owner requests no further commands now; do not pretend action, request secrets or give another long command loop. Requires authorized GitHub write-capable execution under owner's control; GitHub Merge button prohibition/worker code-only remain unless explicitly revised.
When authorized execution available: verify unchanged live refs and gates, preserve both PR contents in one canonical main integration from combined70eba860; ensure PR18 history is integrated into its base if both PR statuses must show merged (not just manually closed), then complete PR17/main integration through owner-approved procedure. Verify actual GitHub merged states, main content/tree and identities before exact-tip safe branch deletion. Local clean-main cleanup requires verified local dirty state, cannot infer from GitHub; do not discard old local arena/01a09bd4 or set-c histories without review. No forced rewrite of released main or unguarded branch deletion.
EVIDENCE: owner reported successful Release build with one xUnit2029 and all selected tests passed at36c8673. Assertion-only cleanup70eba860 statically reviewed; post-cleanup build/test count+exit codes not explicitly supplied. Do not invent final-green evidence or reopen historical SliceC gates. No migration needed for unmapped B1-A; B1-B1 would create model delta but has NOT started. No API runtime business behavior changed, no production refund/role/timer/flag persistence activation, no deployment claimed.
Preserve complete approved rules/checklists/worker lessons below. Two control files updated now; no source, repository or deployment mutation. Next actionable blocker is execution capability/gate completion for owner's requested foundation integration, not new worker prompt or repeated business questions.


## COORDINATOR RESET — CONSOLIDATE BEFORE MORE WORK — 2026-09-17
Owner strongly objects to premature additional worker/PR/branch proliferation and repeated slow correction loops. Acknowledge coordinator error: issued B1-B1 before consolidating reviewed implementation/test branches and reconciling local edits. B1-B1 prompt WITHDRAWN, prompt.txt now explicit do-not-execute notice. No next worker starts until current feature state consolidated. New rule: one active feature PR; implementation/test roles do not imply separate open PR per session. Test worker may use isolated workspace and return files/patch for integration into existing feature; no new PR unless genuinely needed and owner explicitly approves. Small corrections coordinator+owner; substantial correction return to same worker/current PR, never create extra PR merely for fix. Maintain bounded sessions without multiplying delivery branches.
Live heads reverified: mainf787 unchanged; implementation arena/01a0af5e-sanad-api b257bd1; tests arena/01a0af97-sanad-api70eba860 includes reviewed production ancestry, tests and owner assertion fix. No evidence code lost or main changed. Last owner-local state after push unknown; do not assert clean based on remote. Next obtain one minimal owner status/HEAD output then batch safe consolidation: fast-forward existing IMPLEMENTATION feature branch to reviewed combined70eba860, keep PR17, close redundant PR18 once content preservation verified, delete test branch only with exact-head safeguards and owner control; never merge domain-only stage into main. No GitHub Merge button, no force/reset/discard. Feature update is not main merge/release. Branch/user-local edits require actual-state guards. Prior post-fix build/test execution still unconfirmed; no new invented pass.
Workflow speed: same worker completes substantial corrections, coordinator supplies complete files for minor fixes, one consolidated findings pass, batch safe commands. Stop stacking further PRs. Future worker prompts must clearly state IMPLEMENTATION or TEST worker, exact existing feature branch and delivery expectations. Both controls updated; no GitHub/local source mutation executed by coordinator.


## OWNER FIX PUBLISHED / NEXT SMALL PERSISTENCE STAGE — 2026-09-17
Live GitHub verified: mainf787; PR17 draft b257bd1; PR18 draft head70eba860d245aab6fda990816fa8e41ac3e5f1a3 parent36c8673b96ace20bef5edfeba385c9cadc827eec. Only newest diff is expected single assertion analyzer cleanup +3/-2. Canonical author+committer Mossad <mosad55522@gmail.com>. Same PR18, no extra fix PR. Owner previously reports build/test success on preceding source; final corrected-tree build/test summaries/exit codes not supplied, so don't assert final runtime green. Publication verified independently, not inferred owner local status. No need to block next source-generation assignment on repeat read-only loops; final gate evidence remains pending.
Owner asks move on. Delivered new prompt.txt for B1-B1 production-source-only persistence foundation, pinned70eba860 (already contains B1-A production+tests in ancestry), suggested work/b1b1-cancellation-persistence targeting arena/01a0af97-sanad-api if draft PR publication authorized. Exact small scope new BookingCancellationFactConfiguration + concrete FamiliesDbContext DbSet/tracked append-only guard; no application interface/handlers/DTOs/payment/test edits. Map scalar fields, unique BookingId, noncascade Booking FK, no fabricated legacy backfill. Guard sync/async tracked fact modifications/deletion, retain raw-SQL limitations. Independent mapping/guard tests after review. Domain-only stage not merged; integration lineage stacked, no main mutation.
This stage introduces EF SOURCE model delta; no migration generated/edited by worker, no API startup/pending-model suppression. Owner migration generation after source+test review/compile preflight and explicit commands; no DB writes automatically authorized. Next later B1-B handler/refund-claim concurrency must still be separately bounded; unique fact row is NOT double-refund prevention. Admin read models/entitlement retry gating B1-C, wider hardening/attendance/reports/S stay queued. Two-file source scope chosen to avoid IFamiliesDbContext change rippling into many fakes now. All new mapping names/indices/guards are technical proposals subject to coordinator review, not new financial policy.


## OWNER REQUESTS ONE BATCH OF SAFE STEPS — 2026-09-17
Owner wants all currently safe steps upfront and reports failures rather than waiting between commands. Coordinator rechecked live branches: implementation b257bd1, test branch arena/01a0af97-sanad-api36c8673, mainf787. Issued bounded batch based on owner known clean detached36c8673 plus exact one-file assertion correction: verify HEAD/diff whitespace; create local branch arena/01a0af97-sanad-api without force; Release build and targeted3-class test gate; only after both exit0 stage exact corrected test file, commit canonical author+committer using per-command config/explicit author, inspect identity; normal non-force push HEAD to SAME remote test branch. No new PR, no main change, no migration/deploy. Stop on any failure/mismatch/existing local branch/unexpected identity; no force/reset/rebase. Do not repeat build/tests if owner already completed exact corrected-tree checks with exit0 and no intervening source change. All steps are issued, not yet owner-confirmed execution. Await concise build/test/commit/push results; then verify PR18 new head/content/identity and continue bounded next stage. Safe batching supersedes unnecessary per-command handholding, not guards or owner final merge approval.


## OWNER SMALL-FIX DIFF CONFIRMED / BATCHED DIAGNOSTICS — 2026-09-17
Owner confirms exactly one file changed +3/-2 for corrected BookingCancellationFeedbackAndFactTests.cs at detached36c8673. Owner requests faster progress, fewer unnecessary checkpoints. Batch safe dependent steps with explicit stop-on-failure, avoid repeated status-only loops; preserve financial/merge safety gates. Next: owner Release build then targeted three-class tests --no-build only if build exit0; send combined concise summaries/exit codes. No new migration/API/Bruno needed for dormant domain slice. Working file correction uncommitted and must be retained/anchored into existing feature after diagnostics; no main merge yet. Prior test success owner-reported, this recheck covers exact changed assertion/new binary. No new worker prompt/PR for small fix.


## OWNER DIAGNOSTICS / SMALL FIXES COORDINATOR-OWNED — 2026-09-17
Owner at reviewed detached36c8673 reports Release solution build succeeded with one xUnit2029 warning in BookingCancellationFeedbackAndFactTests line333; reports all selected tests passed. Count/exit code not provided; do not invent them. Owner stopped attempted worker prompt creation (tool aborted) and explicitly rejects worker prompts/new PRs for small test/build fixes: coordinator handles small corrections directly with owner, reserving workers for substantial bounded work. No extra PR for this warning.
Coordinator fetched complete file from exact36c8673, verified old assertion once, changed only Assert.Empty(filtered constructors) to Assert.DoesNotContain(constructors, predicate), preserving semantics. Delivered /home/user/corrections/BookingCancellationFeedbackAndFactTests.cs; owner destination D:\Sanad_API\tests\Sanad.UnitTests\Families\BookingCancellationFeedbackAndFactTests.cs. No owner-run editing script. No published commit/PR modification/runtime execution. Owner still detached36c8673 until further output; direct replacement creates one intended local diff. Need verify local diff/state before later branch/commit integration; never discard or leave detached commit unanchored. Keep existing PR18/feature workflow; no new PR required. Previous prompt.txt should not be sent again; last completed assignment is already reviewed. Next owner replaces full file, confirms/diff checks, then coordinator supplies focused recheck/publication steps as necessary. Full feature not merge/release-ready yet.


## OWNER PR18 CHECKOUT CONFIRMED / DIAGNOSTICS NEXT — 2026-09-17
Owner fetched refs/pull/18/head, verified FETCH_HEAD36c8673b96ace20bef5edfeba385c9cadc827eec, switched --detach exact SHA, and showed clean ## HEAD (no branch) in D:\Sanad_API. Main unchanged; old local branches untouched. No more fetch/checkout needed absent new cause.
Next authorized owner diagnostics: dotnet build Sanad.slnx -c Release; inspect LASTEXITCODE, stop on nonzero. Only after successful build: dotnet test tests/Sanad.UnitTests/Sanad.UnitTests.csproj -c Release --no-build --filter "FullyQualifiedName~Sanad.UnitTests.Families.BookingCancellationPolicyTests|FullyQualifiedName~Sanad.UnitTests.Families.BookingCancellationFeedbackAndFactTests|FullyQualifiedName~Sanad.UnitTests.Families.BookingCaptureEvidenceAdapterTests"; inspect LASTEXITCODE. Restricted new domain tests only; no API server, DB migration, Bruno or full AuthApiHost test execution.117 cases is static expectation, not passing evidence; actual owner test discovery/output authoritative. Await build/test summaries and errors. No merge/deploy approval; B1-B/C remain pending. Assistant did not run dotnet.


## OWNER LOCAL CHECKOUT VERIFIED / PR18 CHECKOUT NEXT — 2026-09-17
Owner PowerShell output from D:\Sanad_API: clean main f787a5c2f1a22baddfd540b431f62ace7ed52d8f tracking origin/main. Other local branches arena/01a09bd4-sanad-api at3f61aa2 (upstream gone), set-c-account-switch atdf10993 (stale tracking ref). Leave both untouched, no branch deletion required for diagnostics. Live ls-remote rechecked mainf787 and refs/pull/18/head36c8673b96ace20bef5edfeba385c9cadc827eec.
Next issued action: fetch public URL refs/pull/18/head, inspect FETCH_HEAD exact36c8673, stop on mismatch/failure; switch --detach exact reviewed36c8673, status. Detached diagnostic checkout does not merge/change main or require local test branch. Await owner checkout evidence before build/test commands. No migration/DB command. Owner previous clean evidence applies now, stop if unexpected modifications appear; do not force/discard.


## PR18 CORRECTIONS REVIEWED — OWNER LOCAL PREFLIGHT NEXT — 2026-09-17
Live draft PR18 head36c8673b96ace20bef5edfeba385c9cadc827eec parent162df330ade6d54b4d325960fd1f023e33459a26; base implementation b257bd19785935446f376b91c7bf301c67bfc376 unchanged. Canonical author+committer verified Mossad <mosad55522@gmail.com>. PR17 still draft/open b257bd1. Test diff vs implementation exactly3 added files1606lines, no production edits. Read complete correction diff and prior full-file review: constructor argument case fixed; positive adapter now category+note, note-only remains negative; nullable defaults now present nullable DateTime.MinValue plus explicit valid null-start test; non-UTC start regression added. PR metadata accurately distinguishes117 static cases from executed results and references coordinator-approved rules. No further blocking static finding in reviewed correction surface; not compiled/tested, no merge/deploy approval.
Next action owner read-only local preflight in D:\Sanad_API: Set-Location, git status --short --branch, git rev-parse HEAD, git branch -vv. Await exact output before giving fetch/checkout commands for reviewed combined test head36c8673. No pull/reset/switch/merge now. PR18 contains implementation ancestry plus tests and can be diagnostically checked without first merging either PR. B1-A has no mapped EF delta: no migration to generate at this stage. After verified safe local checkout, owner compile and targeted domain tests are diagnostic gates, not full-feature release gates; B1-B/C remain unimplemented.
No new worker correction needed; last prompt.txt correction assignment is completed at reviewed head and must not be reissued. Coordinator did not execute runtime commands or modify checkout/GitHub. Record result after each owner step and keep stage unmerged.


## PR18 FULL STATIC TEST REVIEW — CONSOLIDATED CORRECTIONS — 2026-09-17
New draft test PR18 https://github.com/Mossad-55/Sanad_API/pull/18, branch arena/01a0af97-sanad-api, head162df330ade6d54b4d325960fd1f023e33459a26 parent/baseb257bd19785935446f376b91c7bf301c67bfc376, correctly targets implementation arena/01a0af5e-sanad-api, not main. PR17 unchanged b257bd1 draft. Canonical author AND committer verified Mossad <mosad55522@gmail.com>. Three new test files1501lines, no production/package/project/shared-fixture edits. Reviewed all files against source signatures/state transitions. Downloaded scratch source under .review/pr18 only; no checkout change/runtime execution.
Consolidated blockers: (1) PolicyTests.IllegalActorActionCombinations direct positional record constructor uses named startedOnUtc instead of StartedOnUtc -> C# case-sensitive compile error. (2) AdapterTests.FromBooking_PreservesStatusAcceptanceAndSuppliedArgumentsAndMutatesNothing uses CreateOptionalNote for Confirmed positive case then expects Decide success; correct implementation rejects missing category. Use valid category+note positive, preserve negative regression. (3) PolicyTests.DefaultTimestamps_AreRefusedAsMissing uses bare default in DateTime? positions -> null, third valid null-start scenario incorrectly expects exception; distinguish nonnull default(DateTime)/MinValue vs absent nullable explicitly, add valid null-start assertion. Missing acceptance test otherwise duplicated, not present-default coverage.
Other same-pass cleanup: PR '110 executed cases' contradicted no-run statement, change to static/expected counts; provenance note ignored separate owner Master, supply exact approved60min/categories/AR values and confirm they match; adapter comment 'captured-but-unsettled ... nothing captured' inaccurate for pending/failed attempts—clarify no succeeded evidence, not real settlement proof. No production defect established by these test setup errors. Boundary/category/flags/capture uncertainty/immutable source facts coverage otherwise good for domain-only scope. No claim all runtime/analyzers green.
Automatically replaced prompt.txt with consolidated SAME-test-PR correction instructions pinned162df33, three fixes plus evidence cleanup, no production or scope expansion. Owner should not pull/merge/run commands yet. Next re-review updated test head then state-aware owner local diagnostic preparation, still no domain-only merge into main. Workers continue code-generation only; full gates/remaining B1-B/C safety work retained.


## MANDATORY ONE CONSOLIDATED REVIEW / FIX HANDOFF — 2026-09-17
Owner requires all discoverable fixes for a reviewed PR revision delivered together, and same behavior from future masterminds. Coordinator must inspect full bounded diff plus relevant callers/invariants/validation/error paths/compatibility and new interactions BEFORE verdict. Build a consolidated prioritized finding list with evidence, expected behavior and regression coverage; generate prompt.txt immediately in same turn. Do not stop at first issue or drip-feed known corrections. Re-review entire correction surface and interactions (e.g. optional feedback must not weaken accepted-booking category requirement), not isolated requested lines. Prior PR17 extra pass illustrated this risk; prevent repetition through review discipline.
Consolidation does NOT mean a giant worker session: one complete correction plan can contain sequenced small assignments when safety/context budget requires, with dependencies explicit. New test findings/regressions still require disclosure/correction; never promise all defects caught or hide known issue to preserve one-pass appearance. Current reviewed PR17 head b257bd19785935446f376b91c7bf301c67bfc376 remains ready for independent test-source worker, not merge-ready; no new source evidence/fix found or worker instruction changed in this record. Keep current prompt.txt test assignment unchanged. Next mastermind reads this rule without asking owner to repeat it.


## PR17 THIRD REVIEW — READY FOR INDEPENDENT TEST SOURCE — 2026-09-17
Reviewed live draft PR17 head b257bd19785935446f376b91c7bf301c67bfc376 parenta363b208b136100d61b76becd3bdf108264ee55f, base mainf787 unchanged, branch arena/01a0af5e-sanad-api. Canonical author+committer Mossad <mosad55522@gmail.com>. Latest diff only policy/fact/feedback validation; PR body now refreshed. Shared RequireForAcceptedBooking validates category+bounded nonblank note; Decide calls it for Confirmed; Fact derives accepted status and checks required-flag consistency then revalidates. Prior note-only accepted bypass corrected. Other prior fixes remain. Static review passes to independent test-writing stage, NOT compile/runtime/merge/release approval. No builds/tests/migrations or owner checkout change.
Automatically replaced prompt.txt with independent B1-A unit-test worker assignment pinned b257bd1. Suggested work/b1a-cancellation-tests in separate workspace, draft PR base arena/01a0af5e-sanad-api if publication authorized (not main). Implementation worker should pause; don't move reviewed implementation under test worker. Three compact logical groups: policy matrix, feedback/fact invariants, capture adapter. Includes category-less accepted regression,59:59/60:00 tick boundaries, capture ambiguity, immutability/optional note/read-only categories. Test source only, no production edits/runtime execution. Coordinator reviews test result/defects next before owner commands or B1-B. No branch created by coordinator.
No migration or policy activation in dormant B1-A. Internal decision factory is assembly-internal, not literally accessible ONLY by policy; normal external API construction protected, no claim of reflection-proof invariants. DB uniqueness/immutable persistence remain later B1-B requirements. Owner should not pull/merge yet; next action send current prompt.txt + controls to independent test worker.


## PR17 SECOND REVIEW — ONE BLOCKER / PROMPT DELIVERED — 2026-09-17
Reviewed live draft PR17 head a363b208b136100d61b76becd3bdf108264ee55f parent01617c119a9cb855c729ba3a808ae8725951c581, basef787 unchanged. Canonical author+committer verified Mossad <mosad55522@gmail.com>.13 new files vs main, no existing-file modifications. Prior R1 external forging/feedback swap, R2 missing capture -> NoRefundDue, R3 optional reason representation, R4 mutable category list substantially corrected. No compile/tests executed; raw source/API inspection only.
Remaining HIGH blocker: Confirmed cancellation validates only Feedback!=null. New CreateOptionalNote gives nonnull feedback Category=null; both Family/Cancel and Caregiver/Cancel accepted policy paths and Fact.Create allow category-less accepted cancellation. Must require known category+nonblank bounded note for accepted status at policy and defensive recording boundary; preserve optional category-less notes before acceptance. No owner decision needed, this is approved rule enforcement.
PR description stale (01617c1,12files,old bool capture/NoCapturedPaymentRecorded behavior); request metadata summary refresh, not new scope. Automatically replaced prompt.txt with final narrow correction pinned a363b2, same PR/branch, no tests or widening. Owner no pull/merge now. Next source re-review then independent test worker. No new branch/PR mutation or application edits by coordinator.


## COORDINATOR MUST DELIVER ACTIONABLE CORRECTIONS — 2026-09-17
Owner explicitly requires proactive worker prompts whenever review finds required modifications; do not merely list findings and require owner to ask for prompt again. Deliver concise verdict plus updated separate prompt.txt in same turn. No extra permission loop for preparing the correction artifact; actual worker execution/publication remains subject to existing controls. Applied now: prompt.txt replaced with narrow PR17 correction prompt pinned01617c119a9cb855c729ba3a808ae8725951c581 on arena/01a0af5e-sanad-api. Four fixes: validated decision/fact construction, capture ambiguity vs entitlement, optional preacceptance note retention, immutable category collection. Same draft PR, no tests/wider stage, no owner commands now. Next coordinator checks updated head then automatically prepares independent test-worker artifact if source contract acceptable. No application/GitHub mutation by coordinator.


## PR17 STATIC REVIEW — CHANGES REQUESTED — 2026-09-17
Live draft PR17 https://github.com/Mossad-55/Sanad_API/pull/17 branch arena/01a0af5e-sanad-api -> main; head01617c119a9cb855c729ba3a808ae8725951c581, parent/basef787a5c2f1a22baddfd540b431f62ace7ed52d8f. Commit author AND committer verified Mossad <mosad55522@gmail.com>. 12 additions, no pre-existing file modifications; one ID in BuildingBlocks Domain consistent existing convention. Domain-only dormant policy, no mappings/controllers/tests/migrations; nothing to merge/release. Branch differs from suggested name, not by itself defect. Draft currently targets main; keep unmerged, later coordinator creates/retargets integration branch only with live-state approval. Read-only public API/raw-source review, no fetch/checkout/code edits/runtime tests. PR description statements not accepted as runtime evidence.
Findings to fix BEFORE independent test worker:
R1 HIGH: BookingCancellationDecision public positional record is directly constructible and with-mutable. BookingCancellationFact.Create trusts all fields including IsReasonFeedbackRequired, entitlement, flag, policy version and timestamps. Caller can forge accepted decision with feedbackRequired=false/no feedback, or mutate refund/flag without policy. Seal validated decision construction (nonpublic creation/read-only fields, no externally writable init) or validate all invariants at fact boundary. Bind recorded feedback to validated decision/input; currently Fact.Create accepts unrelated feedback after Decide. Do not solve by trusting caller discipline or repeated wall-clock policy reevaluation of historical facts.
R2 HIGH: Policy !HasCapturedPayment short-circuits every state to NoRefundDue. FromBooking uses only succeeded transaction collection, ignores PaidOnUtc/legacy booking transaction evidence. Paid/confirmed inconsistent or incompletely loaded history becomes permanent no-refund entitlement; exactly60m family cancellation also loses true policy denial reason. Separate policy eligibility from capture verification; fail explicitly on ambiguous/inconsistent capture evidence or retain eligible+unresolved, never equate missing capture record with proof no funds owed. Do not invent Paymob calls. Truly unpaid PendingPayment remains no refund. Approved late-after-cancel reconciliation stays later scope.
R3 MEDIUM: Optional pre-acceptance free-text reason cannot be preserved in durable Fact: only input is BookingCancellationFeedback requiring category+nonblank note. Passing null discards old reason; supplying category invents data/new requirement. Add validated optional pre-acceptance note path without compulsory category, while accepted cancellation keeps BOTH mandatory. No handlers/schema changes in this correction.
R4 LOW: BookingCancellationReasonCategories.All is public mutable array despite readonly reference; expose immutable/read-only collection so consumer cannot mutate closed category set.
Correct aspects: strict<60 boundary, supplied UTC time, accepted feedback validation on normal policy path, correct normal actor/status restrictions/flags, no external side effects, old production behavior unchanged. Durable representation is only unmapped source, not persisted yet; no uniqueness/concurrency guarantee claimed from this stage.
Next: small correction pass on SAME PR/head, domain-only, no tests/handler expansion. Coordinator rechecks corrected exact head then prepares independent test worker prompt. Owner should NOT pull/test/merge yet. Follow-on tests must independently cover forged decision prevention, missing capture ambiguity, optional preacceptance reason preservation and immutable categories plus original policy matrix. No need to fix canonical commit identity at current head. API online review comments were NOT posted (no write access used).


## MANDATORY DYNAMIC OWNER GUIDANCE — 2026-09-17
Owner explicitly requires exact easy guided commands dynamically derived from current local AND GitHub state, not static copy/paste recipes. Applies to every future mastermind/coordinator. At each relevant operational step verify actual target path/shell, working tree/staged/untracked work, current branch/HEAD, pertinent remotes/PR head/base and identities as needed. Use existing current evidence where still valid; don't rerun entire historical audit at every step. Read-only GitHub/assistant checks are coordinator work; request minimal owner read-only output only for owner-local facts we cannot inspect. Never infer D:\Sanad_API from /home/user/Sanad_API_current.
Provide one bounded next action with short ordinary commands, correct shell/path, exact reviewed SHA/branch guards where applicable, brief purpose, expected success and STOP-on-failure/unexpected-state instruction. Wait for output before dependent mutation. Do not issue speculative pull/switch/rebase/reset/merge/delete/migration instructions before state and authorization established. No blanket force, loss of local work or stale PR assumptions. File-edit scripts remain banned; source corrections via authorized delivery. Identity checks/corrections before final integration; no rewriting released main. Owner approval controls merge/database/deploy. Static examples in historical records are not executable current instructions. Keep handoff state/one next action updated so another coordinator continues without repeating settled work. No commands requested from owner now while awaiting first worker result.


## OWNER HANDOFF / PR / LOCAL UPDATE TIMING — 2026-09-17
Both control files updated so the staged process survives session limits. Latest replacement workflow supersedes old all-in-one B1 and trial-after-old-B1-deploy instructions. Active B1-A worker scope domain-only; independent tests after coordinator-reviewed exact revision. No worker output/publication yet verified by this entry.
1. Implementation worker returns small source change and exact revision; draft PR if environment permits authorized commit/push/PR. Otherwise complete source/patch against exact baseline for authorized delivery. Never assume worker can publish; PR existence != tested/merge-ready.
2. Coordinator reviews source vs approved requirements, gets fixes, pins exact revision/snapshot. Then separate test worker gets stable code and approved rules, owns test source in isolated workspace/branch; reports production defects rather than silently changing policy.
3. Coordinator reviews test assertions/coverage and integrates approved changes through authorized delivery on one unmerged feature. A separate test PR may target feature branch, not main. No two workers editing same checkout, no merging untested/incomplete stage into main. Repeat bounded stages until cancellation feature plus mandatory safety and tests coherent. No migration generation by workers.
4. Owner need not pull every worker commit. When coordinator is ready for diagnostic gate, give short commands based on owner's ACTUAL checkout status/HEAD/branches/remotes. Require clean tree or explicit preservation before switching. Fetch first to inspect remote revisions (does not itself replace working files); then checkout/switch exact reviewed feature revision to test. Do NOT pull feature branch into main. Stop on unexpected SHA/dirty state; no reset/discard scripts. Owner Windows D:\Sanad_API is not assistant checkout.
5. Owner runs pre-merge compile preflight. If model changed, owner generates migration under explicit narrow authorization; coordinator reviews migration-only diff; owner publishes reviewed migration through approved identity procedure; full required build/tests/Bruno against final revision. If code changes afterwards, previous gates are not automatically evidence for changed revision. Model updates do not authorize DB writes; target/preflight and migration application remain separately controlled. Failure -> stop/fix, no merge approval from partial gates.
6. Coordinator checks final diff, canonical author AND committer Mossad <mosad55522@gmail.com>, migration/test evidence. Owner approves/controls established merge/recommit process; GitHub Merge button banned. Do not assume normal ancestry merge after squash or repair main identity by history rewrite. Provide concrete identity/merge commands only after actual PR/commit state known.
7. After final integration, verify main SHA/tree matches reviewed change. If another environment updated main, owner fetches and fast-forward-updates local main after clean-state guard; if owner's merge already updated it, no redundant pull needed. Verify exact final main before deployment. Same reviewed content/tree permits carry-forward of relevant evidence, changed content requires affected gates; no blanket rerun historical deployment work.
8. Owner deploys through approved target-aware process and confirms smoke/deployment outcome. Coordinator updates both records and only then safely cleans merged branches with exact-tip/content checks. No worker merge/delete/deploy. Persist checkpoint after each handoff: branch/SHA or snapshot, reviewed vs pending files, tests written vs owner-run evidence, migration state, unresolved blockers, one next action. Never claim current B1 implemented/tested/merged based solely on prompts.
No pull/switch/migration/merge command is required NOW; wait for first worker result. Prompt.txt remains separate delivery artifact, not embedded in controls.


## REPLACEMENT WORKFLOW ACTIVE — OWNER RESTART RULING — 2026-09-17
Owner explicitly said ignore interrupted worker/old prompt and generate split workflow now because no work submitted on GitHub. This supersedes waiting for old artifacts and the earlier trial-only-after-B1 timing. Do not import or wait for unpublished worker work; preserve unknown workspaces without deleting. Live heads rechecked: only main f787a5c2f1a22baddfd540b431f62ace7ed52d8f; assistant checkout clean HEAD same. No code mutations/branch creation/provider calls/tests.
Replaced /home/user/prompt.txt with replacement workflow + ONLY active B1-A implementation assignment. Old all-in-one B1 prompt superseded, not to be executed. No embedded full prompt in control documents.
Plan: B1-A domain-only side-effect-free cancellation policy and standalone durable-facts value types (no automatically mapped Booking changes/no EF delta) -> coordinator review/exact revision -> independent test worker. Then separately prompted B1-B handler/API/persistence/refund-claim/concurrency work, split further if needed -> independent tests. B1-C read-model/admin history/refund eligibility consistency -> independent tests. Coordinator integrates complete feature and owner gates/migration/merge/deploy after full safety review. A/B/C are unmerged work stages, not releases. Single integration feature fix/b1-booking-cancellation-policy; suggested first task branch work/b1a-cancellation-domain, neither created yet. Separate worker workspaces/pinned revisions; no merge of domain-only/partially wired financial feature into main.
Active first implementation worker owns domain files ONLY: no tests/controllers/handlers/DTOs/EF configuration/migrations/project edits, no old-path behavior changes. It returns exact policy contract and test checklist, complete files/patch if push unavailable. Subsequent test prompt generated after coordinator review, no speculative test code against changing interfaces. First domain stage does NOT resolve duplicate-refund/DB concurrency or enforce new API policy; those remain mandatory subsequent safety work before merge. Full earlier checklist preserved; report/attendance/subscription order unchanged. Current worker restrictions/owner-controlled execution intact.


## INTERRUPTED B1 WORKER — REMOTE CHECK — 2026-09-17
Owner reports worker could not continue or submit PR due to system/session limits; requests review of new branch. Live public GitHub ls-remote heads currently shows ONLY main f787a5c2f1a22baddfd540b431f62ace7ed52d8f; open PR API returns []. Assistant checkout clean main f787, no local B1 branch or linked worker worktree. Cached origin/set-c-account-switch is stale metadata for deleted historical branch, not new worker output. No new source found in this checkout; cannot infer worker's separate workspace contents or whether work exists in a fork/unpushed branch. No mutations, tests or credential probes. Next: obtain exact worker branch/fork URL, or preserved partial source ZIP/patch and changed-file/remaining-work summary. Do not restart implementation or discard partial work until artifacts reviewed; split remaining work into bounded sessions after triage. No new PR/merge/deployment claim.


## OWNER RULE — WORKER SESSION SIZE / BOUNDED SLICES — 2026-09-17
Owner explicitly requires limiting code volume per worker session to avoid context exhaustion and tangled implementations. Coordinator must size assignments as small coherent reviewable slices, not a whole phase or giant checklist. Long test code counts toward workload. State a narrow outcome, allowed code areas, exclusions, acceptance criteria and stop point in each prompt. No arbitrary line/file limit substitutes for dependency-aware scope: larger-than-expected design/model/test changes trigger early report and coordinator split/replanning, not silent expansion or rushed completion.
Worker must stop at a coherent checkpoint if session capacity is insufficient, report completed/partial/not-started work, exact revision or file snapshot, remaining tests/risks and next action in response. Partial output is NOT merge/deploy-ready. Save source normally and never discard unrelated work; no new authority/handoff file unless owner requests artifact. Coordinator records durable continuation in Operations. Complete dependent safety/test pieces before merge; splitting sessions is not permission to release half a financial workflow.
Current B1 worker already active: do not issue competing assignment or assume current scope is small merely because labeled B1. B1 combines policy/history/read models/refund concurrency and substantial tests; worker should report expansion/session pressure early and stop rather than implement excluded reconciliation/schedulers/reports/subscriptions. Coordinator can split remaining effort into reviewed follow-up sessions on same unmerged feature, preserving safety dependencies. Future two-worker experiment must obey same session-size rule for both production and tests. No prompt artifact silently replaced while active worker uses it; no new worker started by this record.


## TWO-WORKER TRIAL TIMING CONFIRMED — 2026-09-17
Owner confirms: keep current B1 combined implementation+test worker unchanged. Complete coordinator review/fixes, owner pre-merge diagnostics and migration workflow if required, owner-controlled approved merge, deployment and owner-confirmed closure first. Then trial split implementation/test workers immediately on the upcoming suitable slice/phase (not necessarily subscriptions; approved booking hardening/attendance/reports still precede S).
Trial sequence: implementation branch/draft PR where authorized -> coordinator source review/corrections -> independent test worker on exact stable revision/separate workspace -> integrate and review production+tests as one complete feature change -> owner pre-merge gates incl compile preflight and owner-generated/reviewed migration where needed -> approved owner-controlled merge/recommit -> deployment confirmation -> safe branch cleanup. Do NOT merge untested implementation into main before test worker finishes. Ensure canonical author+committer before publication/final merge, not post-merge history repair. No GitHub Merge button; worker still code-generation only, no runtime/EF/merge/deploy. Measure actual elapsed time and rework/quality; no guaranteed speedup. This is future workflow approval, not authorization to merge/deploy current B1 without gates. No B1 output or completion reported yet.


## OWNER-APPROVED WORKFLOW EXPERIMENT — NEXT SLICE — 2026-09-17
Owner wants to trial separate implementation and test-writing workers next phase/slice to reduce elapsed time, especially long test files alongside CQRS/controllers/DTO implementation. This is an experiment, not a proven speedup or relaxed test requirement.
Current B1 worker is working (owner report); do NOT change its prompt mid-flight or duplicate its assigned tests. Let it return production/test source; coordinator reviews, optionally uses independent test audit/additions.
Next suitable slice: implementation worker owns production source (domain/CQRS/controllers/DTOs/persistence); test worker independently derives cases from approved business rules. Test planning can happen in parallel; test CODING begins against stable interfaces and an exact implementation SHA or complete versioned snapshot. Fully parallel coding only if contracts are stable. Separate workspaces/branches, never shared mutable checkout; single designated owner for fixtures/project files. Test worker reports production defects rather than silently redefining business policy. Coordinator integrates and checks assertions/coverage, not just test counts. Owner retains runtime gates; neither worker runs dotnet/tests/EF/Bruno/npm, generates migrations, merges or deploys.
Evaluate elapsed time saved vs handoff/rework/merge overhead, coverage and defects found before retaining the workflow. Bring this recorded approach forward at next slice planning; do not ask owner to repeat the proposal. No second worker started or current implementation changed by this record.


## PROMPT DELIVERY FORMAT — OWNER REVISION 2026-09-17
Owner now explicitly requested a separate file called prompt. Delivered /home/user/prompt.txt containing the B1 worker instructions. Supersedes previous chat-only prompt instruction. Master and Operations remain separate authoritative control files; prompt.txt is a worker delivery artifact, not a third product/state authority. No embedded full prompt in Operations. No application changes or worker execution.


## B1 WORKER HANDOFF STATE — 2026-09-17
Owner clarified delivery format: Master Context and Operations remain two separate control documents; full worker prompts are delivered IN CHAT ONLY, not embedded in either document and not a third standalone file. Supersedes previous instructions to store worker prompt text in Operations. Removed the embedded B1 prompt; retain this operational state only.
B1 planned scope: approved pre-visit cancellation rules, durable actor/history/flags, no-refund vs failed-refund eligibility, consistent role/read-model enforcement and essential refund/transition concurrency safeguards. Starting main f787a5c2f1a22baddfd540b431f62ace7ed52d8f clean at preflight; intended branch fix/b1-booking-cancellation-policy not created. Worker code generation only; no execution/migration/merge/deployment. Full gap checklist below remains authoritative. Wider provider settlement reconciliation, expiry/late-payment recovery, notifications, attendance/reports and subscriptions remain queued, not claimed fixed. Any safety prerequisite discovered must be escalated rather than skipped.
Next: owner supplies chat prompt and the two separate documents to authorized worker, then returns source result for review before owner diagnostic/migration steps. No application changes made during prompt preparation/removal.

## CLEANUP CLOSED — 2026-09-17
Owner reported successful deletion of remote set-c-account-switch. Independently verified live public git ls-remote --heads: ONLY main at f787a5c2f1a22baddfd540b431f62ace7ed52d8f remains. Assistant checkout status clean, HEAD unchanged f787. Old local remote-tracking refs, if present, are stale metadata, not live GitHub branches; no unapproved destructive cleanup of old clone. Supersedes pending remote deletion above/below. Open PRs were none at preflight; not re-queried in closure. No code/test/migration/deployment changes. Cleanup complete; next bounded worker preparation for approved pre-visit cancellation/refund policy and hardening, before attendance/reports and subscriptions. Preserve all explicitly open policy decisions and worker execution constraints.


## CLEANUP APPROVED / WORKSPACE CLEAN — 2026-09-17
Owner approved narrow cleanup. Executed guarded restoration in /home/user/Sanad_API_current: HEAD f787a5c2f1a22baddfd540b431f62ace7ed52d8f/main, no staged changes, exactly nine csproj diffs, each byte-verified BOM-only before any write. Restored original committed bytes; git status --porcelain empty. Added public origin https://github.com/Mossad-55/Sanad_API.git; no credential probe/push. No commit or code change; old dirty clone untouched. This supersedes earlier dirty-checkout blocker for assistant checkout only, not owner's Windows checkout. Remote configuration may not survive workspace snapshots.
Remote set-c-account-switch deletion remains owner execution pending. Approved target df1099319106f016cac8b4f0c3caaa71320052c3; use explicit --force-with-lease=refs/heads/set-c-account-switch:df1099319106f016cac8b4f0c3caaa71320052c3 with --delete against public repository URL so changed remote tip blocks deletion. No assumption deletion succeeded until owner output/live read. No tests/migrations/deployment or worker prompt performed. Next: owner branch deletion, verify result, then bounded worker preparation.


## REPOSITORY CLEANUP PREFLIGHT — 2026-09-17
Owner requested branch/repository check before worker. Read-only audit, no cleanup yet. Live public GitHub heads: main f787a5c2f1a22baddfd540b431f62ace7ed52d8f; set-c-account-switch df1099319106f016cac8b4f0c3caaa71320052c3. Open PRs none. PR16 closed/merged into107ae449; its head is still df109931. Branch head tree and accepted squash tree both9025e89ee3ac95b512b29c38dd8e1783bf8ddec7: no unique content in old branch relative to squash. Old remote branch is eligible for owner-approved deletion; no push/delete attempted and no credentials probed.
Local /home/user/Sanad_API_current: main f787, no other local branch/stash/linked worktree, no staged diff. Nine modified csproj files now byte-verified as ONLY removal of three-byte UTF8 BOM; remaining bytes identical to HEAD. No functional project changes in those nine. Recommend restoring exactly these to committed bytes after owner approval; never reset old dirty /home/user/Sanad_API.
Local git remote -v empty: origin remote configuration is missing in current persisted workspace although stale origin/* tracking refs remain. Initial ls-remote origin failed for that reason, not evidence of GitHub failure. Direct public HTTPS ls-remote and API succeeded. Recommend restoring public origin URL https://github.com/Mossad-55/Sanad_API.git, but credentials/Git config do not persist reliably in workspace snapshots; check before future operations. No claim about owner's D:\Sanad_API state. Next: obtain narrow cleanup approval, restore BOM-only files/origin in assistant checkout; remote branch deletion owner-controlled, sandbox writes previously unavailable. No worker prompt yet.


## READ FIRST — CONTINUATION / COMPLETENESS RECONCILIATION — 2026-09-17
Owner requested a complete durable handover before any worker prompt, including owner requirements AND assistant audit/rework/hardening findings, without repeated questions or work. This entry and CURRENT approval section below supersede historical “next owner input”, clean-clone, preliminary immediate-downgrade, unchosen package, and unspecified medical-profile statements. Historical entries retained as evidence, not instructions to rerun completed work. Exactly two files only; no standalone prompt/audit/handoff artifact. No worker prompt issued yet.

### Read order / next action
1. Master: continuation precedence, consolidated Phase S packages, then OWNER APPROVED 2026-09-17 booking/attendance/reports.
2. Operations: this entry, CURRENT approval and complete gap checklist, then current-code audit and standing operational safeguards. Use source paths already recorded; targeted verification of changed code is legitimate, repeating the full audit without new cause is not.
3. Next mastermind should prepare bounded implementation planning/worker scope for cancellation/refund hardening first, then attendance/reports, then subscriptions. Do not collapse every checklist item into an unbounded one-shot implementation. No pre-approval questions on settled requirements. Ask only a genuinely blocking open decision, with a short concrete example and recommendation.
4. Before later authorized code mutation reconcile the nine pre-existing csproj edits without resetting/copying them; snapshot actual checkout/branch/HEAD/tree/identity/wiring. Source baseline remains released f787; no pending release verification to repeat. No credential probing. Worker code generation only; owner build/test/Bruno and migration-generation procedures remain mandatory. No owner-run file-edit scripts.

### Settled decisions — DO NOT RE-ASK
- Installed mobile Android/iOS apps for family/caregiver/senior, eventual Apple+Google launch, Egypt/EGP, no customer website. Owner wants Paymob only for bookings and subscriptions; Apple/Google billing is NOT approved. Earlier website/PWA suggestion and assertion that delayed store publication resolves policy are superseded/corrected.
- Family subscription across devices and elderly profiles, Owner-only subscription financial management, auto-renew, immediate prorated upgrade option after suggesting waiting to renewal; downgrade at renewal; ordinary cancellation no refund/paid access retained; seven-day grace. Do not confuse subscription cancellation with booking refunds.
- Packages and cumulative benefits in Master: Free0/month members3 bookings5/month; Premium299/month members10 bookings20/billing month; Premium Plus2499/year unlimited members/bookings; no rollover. All benefits shown on all cards, including unsupported ones. Free basic searches without quota/result truncation; advanced filters paid. Unlimited annual bookings eliminate need to ask annual allowance replenishment question. Arabic input welcome; translate terms, never treat old example prices as approved.
- Distinguish four concepts: Owner-only Medical Access Log; paid broader Family Activity Timeline; caregiver attendance; medical/visit reports. Medical-summary PDF export/secure sharing is distinct from billing invoice PDF. Existing medical-record and new care-report reading not premium-gated.
- Family cancellation Owner+Editor only; all family roles read care reports. Before acceptance family cancel/caregiver reject full paid refund, no flag, retained separate history. After acceptance but before start caregiver cancel full refund+one durable incident flag; family cancel <60m full, >=60m none, no flag. Mandatory accepted-cancellation category AND note. Flag does not mean automatic suspension; admin decides even for emergency/illness. Do not lose flag/history after refund. Categories and exact Arabic labels in Master.
- Both caregiver types write assigned visit reports; Medical additionally structured medical reports within scope. Server check-in/out, no mandatory GPS/family OTP; no fabricated proof of physical presence. Departure separate from submission time. Optional private consented photo. Family report feed both types; author name/title snapshot, measurement vs submission times, units and missing values. Author-selected assessment labels, no automatic medical diagnosis; urgent report is not SOS. Published corrections retain history.
- Public percentage campaign selected plan + dates, one discounted billing period. Targeted expiring family coupon to Owner email/in-app, one use per family/one period/no stacking. Continuing grandfathered subscribers keep old price/benefits; no new sales of retired plan. Fixed branded server billing invoice PDF last S; no admin PDF formats. H Care Homes only; earnings I; Marketplace unscheduled.

### Retained detailed recommendations / examples (NOT extra owner-approved rules)
These are design guidance already discussed, not reasons to ask the owner to restate the business requirement:
- Coupon backend: campaign + targeted family grant + redemption/delivery audit; configure percent/eligible plan/cycle/window; UTC storage with Cairo display; authenticated Owner review/deep link; server computes eligibility and amount, never trust client discount. Reserve claim to prevent two-device duplicates, verify provider success before consuming, release abandoned/failed reservation safely; reconcile late verified success rather than void paid access. Track provider transaction once; no consumption on notification open. Preview audience/message/expiry before send, respect marketing preferences/consent; email/in-app only, no SMS or medical details/raw card secrets. If 30% public offer beats a 5% coupon, show better eligible offer, do not stack or consume unused coupon. Example300×70%=210 first period then300; targeted5%=285, NOT changes to real299 Premium price. Clarify one-use per family != globally one shared code; prevent forwarding from granting unauthorized family redemption where provider flow allows binding.
- Grace example: due5Oct10:00 -> deadline12Oct10:00; proposed reminders immediate/midway/about24h before/access-ended. Proposed recovery on9Oct settles5Oct period, next5Nov; actual Paymob billing anchor/retry-stop rules unverified. No promise of infinite retries or automatic7day store configuration.
- Upgrade messaging uses next actual renewal, not calendar month end; annual renewal may be far away. Suggest keep plan/reminder if waiting, never promise unsupported scheduled change. Exact direct Paymob proration/credit/anchor execution remains unchosen; no double credit/refund or credit for unpaid time.
- Secure medical-summary link recommendation: selected authorized fields, expiry/revocation and audit; PDF already downloaded cannot be revoked, neither can screenshots. On-screen records remain accessible under roles; booked-caregiver necessary access governed separately. Not AI diagnosis. Exact source fields and sharing permission/expiry still need bounded design.
- Membership proposed count includes Owner/Editors/Viewers, excludes dependent/caregiver unless actual family member; preserve existing over-limit members/data/bookings, restrict new usage rather than delete. Invitation reservation/elderly-capacity still not finally specified. No automatic assumption that plan upgrade resets spent allowance.
- Analytics suggestions were families as subscribers (Free included), covered-user count separately, collected EGP revenue vs accounting recognition; charts/CSV financial meanings unchosen. Suggested CSV groups Plans/Subscriptions/FinancialTransactions and illustrative invoice number INV-YYYY-MM-######, not locked schemas/legal numbering. Family non-Owner finance visibility not inferred from permission to read medical reports.
- Grandfather lapse/rejoin/latest-plan-only after voluntary exit was recommendation, precise recovery/lapse semantics open. Legacy plans remain supported/security-maintained, not abandoned.

### Open decisions — do NOT mislabel approved or block unrelated work
- Started-visit early termination/admin-review financial outcome (no rule approved), no-show/penalty/earnings allocation; do not create default forfeiture or full refund.
- Booking allowance consumption/restoration milestone, pending/grace/upgrade behavior; refund and allowance restoration distinct. Precise membership/invitation/elderly limits and advanced search filters need inventory/design.
- Report cardinality, publication/edit windows, late/offline correction controls, mandatory measurement subset, media limits/retention and authorized admin medical-report access. Owner already chose all THREE family roles can read; do not re-ask that. Existing Medical-only creation recommendation approved; do not add companion clinical permissions.
- Admin financial permission: current ContentAdmin can retry refunds; owner has not selected revised least-privilege roles or override policy. Required cancellation reason visibility to admin does not imply all admins may see all medical reports.
- Paymob-only digital subscription store eligibility, merchant recurring enablement, cards/wallet recurrence, replacement/proration/retry/webhook contracts. Full billing contract is not release-ready; do not silently introduce store purchases or claim exemption. Broader invoice/tax/provider handling and original plan screen/CSV details remain bounded S design work.
- Backend report/attendance audit completed; no mobile UI source was inspected, owner described screens. Do not assert frontend completion or authorize a new website.

### Public provider reference record (research, not merchant enablement)
Paymob: https://paymob.com/en/subscriptions ; https://wizard.paymob.com/ ; official PaymobAccept/API-Postman-Collections Subscription Module collection (no explicit proration wording found, not proof unsupported). Booking integration is booking-specific create/refund and HMAC confirmation, not a subscription service.
Apple: https://developer.apple.com/app-store/review/guidelines/ (digital vs physical/multiplatform); https://developer.apple.com/app-store/subscriptions/ ; https://developer.apple.com/help/app-store-connect/manage-subscriptions/enable-billing-grace-period-for-auto-renewable-subscriptions/ . Native upgrades documented as unused-old refund/full-new-charge/renewal reset; downgrades renewal; native monthly/yearly grace3/16/28, NOT7. These are constraints/research, not approved Sanad provider choices.
Google: https://support.google.com/googleplay/android-developer/answer/9858738 ; https://support.google.com/googleplay/android-developer/answer/10281818?hl=en ; https://support.google.com/googleplay/answer/11174377?hl=en ; https://developer.android.com/google/play/billing/subscriptions ; https://developer.android.com/google/play/billing/promo . Digital purchase rules, country/program-specific exceptions (Egypt not in list checked); allowed upgrade ReplacementMode depends on product transition; native subscription promo codes are free-trial mechanisms, percentage discounts use offers. Google seven-day grace can differ from longer billing recovery/hold. Web checkout link is not automatically an exemption; no reader-app exception assumed for Sanad. Earlier Stripe/peer examples were reference only, not Paymob guarantees.

### Documentation reconciliation result
Confirmed both owner changes and assistant findings preserved: existing API map/real204 vs annotated200; role gaps; caregiver endpoint missing; admin summary loss after refund; no-refund vs failed distinction; refund-before-save/idempotency/multiple-payment hazards; HMAC/late-after-cancel callbacks; timeout expiry gap; timezone calculation; no actual refund settlement evidence; report model/endpoint absence; clinical/privacy/retention constraints; tests-to-author and owner execution boundaries. Historical implementation statements retained as baseline, not falsely marked fixed. Master stale “medical-profile unspecified” and “next S immediately” wording corrected. No application/runtime changes in this reconciliation. Full worker prompt remains unsent pending next authorized step.


## CURRENT — OWNER APPROVED CANCELLATION / ATTENDANCE / REPORTS — 2026-09-17
Owner approved all recommendations from the final clarification. Both control documents consolidated now. Master is authoritative for approved business policy; existing audit below remains evidence of current code, NOT the new behavior. No source edits, worker prompt, tests, build, EF generation, database writes, commits, push or deployment. Released HEAD still f787a5c2f1a22baddfd540b431f62ace7ed52d8f. Same nine pre-existing csproj edits remain untouched; no clean-checkout claim. No remote/credentials re-probe or repeated historical release gates.

### Approved contract summary
Owner+Editor cancellation only. Before acceptance: family cancel or caregiver reject -> full captured refund, no caregiver flag, durable categorized history. After acceptance but before start: caregiver cancel -> full refund + one incident flag; family cancel elapsed <60 minutes -> full refund, >=60 minutes -> no refund and no caregiver flag. Category+note mandatory for accepted-booking cancellations, visible to parties/admin. Five reason categories per Master. Flag independent of refund and discretionary admin action, never automatic suspension. Started visit uses separate early-termination/admin review; financial rules still OPEN.
Both caregiver types create assigned visit reports; Medical caregivers additionally create structured medical reports within professional scope. Family Owner/Editor/Viewer read reports/photos. Attendance server timestamps independent of report submission; no mandatory GPS/OTP in v1. Author-selected assessment, no automatic clinical diagnosis. Reports not paywalled; paid medical-summary export/sharing is separate. Detailed fields and status labels in Master.

### Consolidated missing / rework / hardening checklist (NOT implemented)
- [ ] Add production caregiver confirmed/pre-start cancellation endpoint+handler. Domain method only currently; bind to assigned authenticated caregiver.
- [ ] Restrict family cancellation to Owner/Editor (existing handler allows family membership without role gate).
- [ ] Implement acceptance-based <60/>=60-minute refund policy before visit starts, using server time; serialize acceptance/start/cancel races and reject invalid states. Current code always attempts full eligible refund.
- [ ] Persist explicit NoRefundDue vs refund-failure state. Update admin refund retry eligibility so a policy non-refundable cancellation cannot be retried as failed refund. Define explicit admin exception rules only if owner asks.
- [ ] Mandatory accepted-cancellation reason enum+nonempty note; validate max lengths; retain/reveal to authorized parties/admin. Pre-acceptance feedback requirement not extended implicitly.
- [ ] Durable actor/action history and caregiver incident flag independent of Refunded status; exactly once, no automatic suspension; account deletion reason cannot bypass deletion guard.
- [ ] Repair caregiver admin cancellation summary: existing GetCaregiverCancellationSummaryQuery counts ONLY current CancelledByCaregiver status and takes latest5, so refunded incidents disappear. Add separate rejection/family-cancel history for review, not flags; preserve all history with bounded pagination.
- [ ] Retain legacy uncertainty: refunded historical records may not conclusively identify cancelling actor. Do not fabricate backfilled flags or blame from unstructured notes.
- [ ] Refund lifecycle reconciliation: current adapter equates HTTP2xx+JSON to success, optional ID; refund webhook ignored. Persist attempt/provider results and distinguish request acceptance from settled outcome, with retry/reconciliation design.
- [ ] Refund idempotency/concurrency and recovery after gateway success+DB failure; provider side-effect currently before SaveChanges. Prevent parallel duplicate attempts, address timeout/unknown results, do not claim exactly-once merely from HTTP retries.
- [ ] Fix multi-payment bookkeeping: current handler refunds first succeeded transaction but MarkRefunded marks all succeeded attempts refunded. Match actual refunds to actual captured transactions; preserve immutable amounts/currency/ledger evidence.
- [ ] Handle successful/failed callbacks arriving after family cancelled PendingPayment; existing MarkAsPaid/RecordPaymentFailure guards reject cancelled state. Reconcile captured funds without resurrecting cancelled booking.
- [ ] General unpaid/paid acceptance-timeout processing: no production scheduled expiry worker found. Paid unanswered bookings can stay pending and reserve slots; existing late-payment callback expiry is not sufficient. Clarify no-response trigger timing using existing acceptance window, safely refund paid expiries without flags unless separately approved.
- [ ] Cancellation/refund notifications, truthful mobile response/detail status and refund progress. Existing commands return204 despite annotations200; align API contract. Do not promise refund arrival SLA without provider evidence.
- [ ] Review admin financial authorization: current CaregiversAdmin permits SuperAdmin/ContentAdmin, not SupportAdmin. No new finance-admin role approved; choose least-privilege rule explicitly before changes.
- [ ] Define started-visit early termination/admin-review financial rules (OPEN); do not apply approved pre-start refund policy to delivered care. Earnings allocation remains Phase I, not newly approved here.
- [ ] Align future subscription booking counted milestone/restoration and grace/upgrade allowances with cancellation outcomes (OPEN); refund != automatic allowance restoration.
- [ ] Attendance: extend existing /start and /complete (status+server timestamps+optional completion notes only) with trustworthy state/time guards and correction audit. No existing GPS/photo/arrival-verification proof. Check-in/out separate from report publication; avoid fabricated checkout times for offline/late events.
- [ ] New visit-report persistence/API: no dedicated entity/handler/controller found for condition, notes, optional photo and attendance-linked report.
- [ ] New structured medical-report persistence/API: no BP/pulse/temperature report implementation found. Existing medical profile is blood type, height/weight, chronic conditions/allergies/history; not longitudinal measurements.
- [ ] New combined family report list/detail+type filters with pagination and exact family/elderly/booking ownership guards. Existing medications/notes/medical-profile APIs remain separate and intact.
- [ ] Enforce report authorship: assigned caregiver; Medical only for structured medical reports within scope; Companion visit observations only. Preserve verified AR/EN author name/title/specialization at publication.
- [ ] Report read authorization Owner/Editor/Viewer; private photos with consent, bounded upload/type/size validation and protected retrieval. Do not implicitly grant all admins access to medical contents. Audit access/corrections; no hard deletion or silent rewriting.
- [ ] Store units, measured time, submission time, missing vs measured values, author-selected assessment labels. No automatic normal/danger clinical rules. Urgent label must not imply SOS delivery.
- [ ] Bound report cardinality, draft/publish/edit windows, late-entry corrections, required readings, timezone display/storage and media retention before implementation. Proposed APIs should be labeled proposed, not existing.
- [ ] Preserve free access to care reports and existing medical records; do not confuse paid summary export, existing Owner medical audit, broader family timeline, and attendance check-in.
- [ ] Focused tests to author for all transitions, 59:59/60:00 boundary, role/foreign-family/foreign-caregiver failures, flags surviving refund, no-refund retry rejection, required reasons, callback/retry races, expiry, report access/privacy and duplicate attendance. Static audit of existing tests is NOT new passing runtime evidence. Owner executes authorized diagnostics/gates; worker remains code-generation only.

### Current evidence and next step
Additional static sources reviewed before approval: Families Application/Bookings/CaregiverCancellationQueries.cs; API AdminCaregiversController.cs (detail summary, suspend endpoint); Families Application/Elderlies/ElderlyMedicalProfiles.cs; Domain/Elderlies/Medical/ElderlyMedicalProfile.cs; Domain/Notes/ElderlyNote.cs; FamilyController, ElderlyNotesController, MedicationsController; Booking.StartVisit/CompleteVisit; HasActiveCaregiverBookings; caregiver professional-title/specialization domain fields. Existing start/complete != visit reports; existing admin count != durable flag history. No full frontend inspected; UI described by owner.
Next: bound small implementation slices for approved booking policy/hardening, attendance/reports before subscriptions; settle specifically marked open decisions rather than ask owner to repeat approved rules. Reconcile preserved dirty project state before any code mutation. Future model changes follow compile preflight -> owner-generated migration -> review -> owner commit/gates; no automatic migration execution authorized by this approval. Full Phase S Paymob-only/store compatibility and recurring provider controls remain unresolved, not overridden by “approve everything” about these cancellation/report recommendations.


## CURRENT OWNER-AUTHORIZED DOCUMENT UPDATE AND BOOKING AUDIT — 2026-09-16
Owner authorized both documents now, then a factual endpoint/rule walkthrough of current family/caregiver cancellation/refunds; owner will supply modifications afterwards. No application correction, tests, migration, worker prompt, commit, push or deployment authorized/executed in this audit. Master consolidated Phase S confirmed package decisions and unresolved constraints; earlier preliminary Phase S entries below are historical, superseded where conflicting. Full billing contract is NOT implementation-ready: Paymob-only digital subscriptions vs eventual App Store/Google Play release in Egypt unresolved; merchant recurring/proration enablement unverified. No customer web/PWA scope.

Snapshot: /home/user/Sanad_API_current, branch main, HEAD f787a5c2f1a22baddfd540b431f62ace7ed52d8f, tree 770a8e5a6d466d66f63499e7e56918a72a6ca3d6; canonical Mossad <mosad55522@gmail.com> author/committer. Contrary to earlier clean-clone record, current working tree has nine pre-existing modified csproj files (Contracts/Domain building blocks; Caregivers Domain/Presentation; Cms Domain; Families Domain/Presentation; Identity Domain/Presentation). Origin of edits unverified; preserved untouched. Audited .cs sources are not in that diff, so match committed released tree. No remote/credential probe, no inference about owner's Windows/VPS tree, no repeated deployment gates. This is static source review, not runtime/payment-provider evidence.

### Current booking endpoint map
- FamilyAccess: GET /api/v1/family/bookings?tab=1|2|3; GET /api/v1/family/bookings/{id}; POST /checkout; POST /{id}/payments/intent; POST /{id}/cancel (body reason).
- CaregiverAccess: GET /api/v1/caregiver/bookings?tab=1|2|3; GET /{id}; POST /{id}/accept; POST /{id}/decline (reason); POST /{id}/start; POST /{id}/complete (optional notes). NO caregiver cancel endpoint/command wired for confirmed bookings. Controllers resolve caregiver from authenticated user and handlers scope booking to that caregiver.
- CaregiversAdmin policy currently SuperAdmin OR ContentAdmin (not SupportAdmin): GET /api/v1/admin/bookings?page=1&pageSize=10&finance=0|1|2|3; GET /{id}; POST /{id}/refund, no amount body, retries eligible full refund.
- Provider: POST /api/v1/payments/webhooks/paymob, anonymous transport with Paymob HMAC verification; amount checks and settled-attempt replay no-op. Refund callbacks acknowledged/ignored, not reconciled.
- Non-generic successful commands return actual HTTP 204 via ApiControllerBase despite controller annotations advertising 200. Generic detail/checkout/intent/admin-refund responses return 200.

### Current rules and examples
Checkout: Owner/Editor only; checks elderly belongs to family, caregiver overlap (PendingPayment/PendingCaregiverApproval/Confirmed/InProgress reserve slot), server-side pricing with hardcoded 15% platform fee, stored price snapshot, acceptance deadline min(creation+24h, booking start). DateOnly/TimeOnly combined without explicit Cairo conversion in that calculation; timezone contract needs attention. Initial PendingPayment; verified success → PendingCaregiverApproval; accept within deadline → Confirmed; start → InProgress; complete → Completed. Start/complete enforce status, not visit-time/GPS/arrival proof.
Family cancellation: same-family membership enforced, but no Owner/Editor role check (Viewer is not excluded), no creator-only restriction. Allowed PendingPayment/PendingCaregiverApproval/Confirmed; disallowed InProgress/Completed/all ended statuses. Reason trims, max500, whitespace becomes null (no nonempty cancellation validator found). No hours-before-visit cutoff, penalty, partial-refund calculation or platform-fee withholding. Chooses first succeeded payment transaction with provider ID; if present attempts its full recorded Amount. No succeeded transaction → no automatic provider refund attempt. Ordinary gateway failure Result does NOT fail cancellation: save canceled state and return success. Successful refund changes booking status to Refunded. Unpaid family cancellation is permitted, no money to refund.
Caregiver decline: only PendingCaregiverApproval, only assigned caregiver. Same reason normalization, full succeeded-transaction refund attempt and ordinary-failure behavior. Decline has no acceptance-deadline check. Confirmed caregiver cancellation exists ONLY as Booking.CancelByCaregiver domain method and references in seed/tests; no production application/controller caller found. Domain method only permits Confirmed and does not itself call Paymob. Therefore do NOT claim caregiver post-acceptance cancellation is complete or automatically refunds through a public API.
Example: base EGP1000 + fee150 = paid1150; eligible family cancellation or pre-acceptance caregiver decline attempts refund1150, not1000. Family may cancel confirmed booking one minute before scheduled start (even after scheduled time if still Confirmed) because status, not clock, gates it. Cannot cancel once InProgress through this endpoint.
Refund success: booking Status becomes Refunded; reason/cancellation timestamp retained but previous family/decline/caregiver status not retained as a separate cancellation-actor field. MarkRefunded marks every succeeded payment attempt refunded even though cancellation handler selected/refunded only one; multiple-payment/race behavior requires remediation design. Sequential repeat cancellation rejected by state. No explicit provider refund idempotency/reconciliation/DB concurrency guard located in reviewed paths; provider request occurs before SaveChanges, so crash/save failure or parallel requests may leave inconsistent state/double-attempt risk (not runtime reproduction).
Refund state derived: Succeeded if Refunded status/timestamp; Failed if PaidOnUtc and canceled/declined/expired without refund timestamp; otherwise NotApplicable. Failed does not prove an actual gateway attempt occurred. No persisted pending/refund-attempt history in inspected model. Detail returns RefundState/reason/payment and cancellation/refund timestamps, not refund amount/ETA/provider refund reference.
Admin refund: only derived Failed for paid ended statuses; rejects AlreadyRefunded and noneligible/active/completed. Uses succeeded transaction ID+amount, fallback booking PaymobTransactionId+TotalPayableAmount. Full amount only. Gateway failure returned as error; success MarkRefunded/save/detail200. Not an arbitrary refund endpoint for any booking.
Paymob adapter POST /api/acceptance/void_refund/refund, transaction_id and amount_cents, server token. Treats HTTP2xx with parsable JSON as success (id optional); does not inspect settlement/pending status. Refund webhook ignored. Thus local Refunded is adapter-success interpretation, NOT independently verified receipt of money by customer. DevelopmentPaymobClient is a simulator, not real refund proof.
Expiry: Booking.Expire domain method exists for pending states after deadline. Only production caller found is successful late-payment callback: MarkAsPaid → Expire → full refund attempt; outcomes PaidExpired / PaidExpiredRefundPending. No general scheduled expiry/refund worker found. Paid unanswered bookings can stay pending/reserve slot after deadline; accept rejects late without expiring/refunding. A successful payment callback arriving after family canceled PendingPayment hits MarkAsPaid's PendingPayment guard; no dedicated late-after-cancel refund/reconciliation path found. Similarly failed callback for canceled state hits RecordPaymentFailure state guard. Ordinary replay handling does not solve these state races.

### Audit evidence and next action
Source roots: src/API/Sanad.API/Controllers/{FamilyBookingsController,CaregiverBookingsController,AdminBookingsController,PaymobWebhookController,ApiControllerBase}.cs; API DependencyInjection.cs policy registration; Families Application/Bookings/{BookingCommands,CaregiverBookingCommands,BookingPaymentCommands,AdminBookingCommands,BookingRefundState,BookingQueries}.cs; Families Domain/Bookings/{Booking,PaymentTransaction}.cs; Families Infrastructure/Payments/PaymobClient.cs and Persistence/Configurations/BookingConfiguration.cs. Inspected existing test names for payment/expiry/decline/refund/foreign-family safeguards; did not run them, did not claim complete test coverage or new release verdict.
Current implementation has no subscription booking quota restoration (Phase S not built). No cancellation-specific notification dispatch, configurable cancellation tiers, partial refund, caregiver penalty/no-show settlement or normal expiry/retry scheduler located in reviewed paths. Caregiver arrival/check-in is not proven by existing start endpoint.
Next: owner reviews walkthrough and supplies desired cancellation/refund modifications, then senior/medical-profile changes. Do not implement speculative fixes or introduce proposed financial rules before approval. Keep pre-existing csproj edits untouched and reconcile project state before later authorized code mutation.


## PHASE S OWNER ANSWERS AND PROVIDER CHECK — 2026-09-16
Owner selected: one subscription per family, Family Owner alone manages; automatic renewal; immediate plan changes with proration. Recorded in Master Context. Billing visibility for other roles and detailed packages/entitlements, cycle/price, proration/credit/refund/cancellation/retry rules remain open; do not invent them in worker prompt.
Provider research: official https://paymob.com/en/subscriptions advertises authorized tokenized recurring payments. Official https://wizard.paymob.com/ lists subscriptions as requiring Moto Integration ID; merchant enablement not verified. Inspected official PaymobAccept/API-Postman-Collections Subscription Module collection at current main: plan create/update/suspend/resume; subscription creation through intention, update/suspend/resume/cancel, transaction listing, card-token listing, secondary card addition/deletion and primary-card change; explicit proration wording not found in that collection. This is NOT proof native proration is unsupported; inspect detailed contracts and merchant features before choosing implementation. Wallet auto-renew capability remains unverified, not waived. No payment/provider API calls made; only public documentation fetched.
Existing booking Paymob client/webhook must remain compatible. Worker contract not generated/executable yet; next owner input is subscription screens or concrete package definitions (names, amounts, currency, cycles and benefits/limits). Bound the first slice and settle financial rules before implementing charge logic. Do not claim full Phase S readiness from a clean clone alone.

## PHASE S SNAPSHOT — 2026-09-16, CLEAN CLONE AND INITIAL AUDIT
Active implementation/audit checkout is now /home/user/Sanad_API_current, freshly cloned from https://github.com/Mossad-55/Sanad_API.git. Clean main == origin/main at f787a5c2f1a22baddfd540b431f62ace7ed52d8f, tree 770a8e5a6d466d66f63499e7e56918a72a6ca3d6, canonical Mossad author/committer. Remote set-c-account-switch remains df109931. No open PRs per fresh GitHub API check. Prior accepted GitHub merge identity exception remains historical, not a new violation.
Old /home/user/Sanad_API preserved untouched: it has staged Slice C artifacts, unstaged test repairs and additional csproj edits of unverified origin. Do not reset, delete, or copy them into the new checkout. New clone is the reference for future work. Neither clone is the owner's Windows checkout; no new owner machine-state inference.
Verified in new clone: deployed Slice C migration present, standing Bruno inventory 60 request files. No dotnet/EF/Bruno/npm execution; no application modifications, commits or remote mutations. Write-access preflight performed before promising delivery: git push --dry-run to a probe ref failed `fatal: unable to get password from user`; no probe branch created. GitHub push/PR automation is still unavailable in this sandbox. Do not request credential secrets; use an authorized worker environment or complete-file delivery rather than claiming a PR can be pushed here.
Initial Phase S code audit: no subscription/invoice implementation found in source/docs search. Existing Paymob interfaces, intention input, transactions and callback dispatch are booking-specific (BookingId and ConfirmBookingPaymentCommand). PaymobClient exposes create-intention/refund, not saved-method or recurring-subscription operations; local PaymobOptions supports card/wallet integration IDs. Existing provider code cannot be treated as ready-made subscription billing. Preserve booking checkout/webhooks when introducing billing, and verify provider capabilities before committing to automatic charges.
Before worker contract: settle family subscription ownership/management permissions, manual versus automatic renewal, plan-change timing/proration, then concrete packages/cycles/prices/entitlements. Keep already settled rules: payment-method changes YES; fixed branded server PDF final Phase S modification; no Marketplace in Phase H (Care Homes only), earnings remains Phase I; no owner-run source-edit scripts. Worker prompt remains unissued until sufficiently bounded. Exactly two active control files; contract/prompt stays in Operations/chat, no new standalone prompt file.


## ROADMAP CHANGE — PHASE H, OWNER RULING 2026-09-16
Phase H implementation scope is Care Homes ONLY. Marketplace is excluded from the current delivery plan; retain it as a future reconsideration note because the owner may change the plan. No Marketplace implementation, contract, or delivery slot is authorized. Reintroduce only upon explicit owner ruling. This supersedes older references to Phase H as care homes + marketplace. No other phase/order changes requested. Master Context roadmap updated in the same turn; no application code changes.


## CURRENT RELEASED STATE — SLICE C CLOSED / DEPLOYED
Owner explicitly confirms `DEPLOYED` after final DEPLOY: YES for main f787a5c2f1a22baddfd540b431f62ace7ed52d8f. Record deployment as owner-confirmed, not assistant-executed or independently inspected on VPS.
Closure evidence: reviewed implementation and migration published on main; canonical migration commit author/committer verified; one-time identity exception for merge 107ae449 explicitly accepted; owner reports build/tests passed and migration applied; Bruno 60/60 with exit 0 explicitly confirmed; VPS conflicting caregiver groups 0 owner-reported; owner confirms deployed. Slice C is closed. Earlier blocked/preflight/PR sections below are historical, superseded by this closure.
Delivered: account choices/add-account/dashboard selection, preserved all-owned-role authorization, Medical/Companion exclusivity (domain + unique filtered PostgreSQL index), negative-first Bruno coverage and corrected test fixtures/metadata assertions. Retained-profile re-add guard preserved. Remaining caregiver earnings/payouts stay Phase I; remaining full-app screen gaps are not silently closed.
Next queue: Subscriptions Phase S -> owner-led full-app UI review -> G/H/I. Phase S starts with repository/payment audit and a bounded contract, not blind implementation. Scope: plans/status/price/renewal/payment type, CMS-managed benefits, change plan/payment method, invoice history/numbering, fixed branded server-side PDF as final modification. Product decisions still needed for billing ownership/permissions, renewal/proration/cancellation, entitlements and payment-provider capabilities; do not invent rules.
Lessons carried forward: no owner-run source-edit scripts; deliver full files or authorized GitHub PRs. Prefer short normal operational commands only when authorized. Explicit validator argument types; production EF mapping for persistence tests; design-time EF metadata for schema assertions; full API-host tests may connect/migrate configured databases—verify isolation and migration state first. Verify delivery blobs and Release configuration; distinguish static checks, owner test evidence and actual deployment. Check VPS data before applying unique indexes. Confirm write credentials early. Do not change dependency licensing/default-value settings opportunistically; follow-up notes remain tracked.
Both active control files updated in this closure turn. Do not delete remaining source branch without explicit owner authorization. No further command, source edit, migration or deployment action needed for Slice C closure.


## FINAL PRE-DEPLOY VERDICT — DEPLOY: YES
Owner answered 0 to the explicit VPS conflict-preflight request: record owner-reported zero conflicting caregiver-account groups on the intended VPS database. Database name was not supplied; do not claim independent DB verification. No further query repetition required based on this owner confirmation.
Final fresh repository audit: origin/main f787a5c2f1a22baddfd540b431f62ace7ed52d8f, canonical Mossad author/committer, parent 107ae449 (one-time owner identity exception accepted), migration commit whitespace check passed. Reviewed owner migration is present; full build and tests owner-reported passed; Bruno 60/60 and exit 0 explicitly confirmed; VPS existing-data conflict count owner-reported 0.
DEPLOY: YES for exactly f787a5c2f1a22baddfd540b431f62ace7ed52d8f using the owner's established VPS deployment/restart procedure. No new commands or scripts generated. Deploy the full committed main including migration artifacts, not the pre-migration Slice C branch. Startup automatically applies pending migrations. Owner should verify service starts successfully, migration produces no errors, and normal health/smoke checks pass; report errors rather than claim deployed if startup fails. Assistant has not deployed or modified VPS. Await owner confirmation `deployed` before closing Slice C and updating released status in Master Context. Subscriptions (Phase S) is next after deployment confirmation.

## PREVIOUS BRUNO RESULT — OWNER REPORTS 60 PASSED
Owner explicitly confirms: 60 Bruno requests passed and last exit code = 0. Bruno gate is now owner-confirmed green; no rerun needed. Earlier owner reports build/tests passed and migration applied; target of application still unspecified, not presumed VPS. Local verification gates are owner-reported complete; production preflight and deployment confirmation remain pending.
Fresh remote audit: main remains f787a5c2f1a22baddfd540b431f62ace7ed52d8f, canonical Mossad author/committer, parent accepted one-time GitHub merge 107ae449. Migration diff whitespace check passes; reviewed migration files previously matched uploads. No new commits or deployment observed.
Next: run/read production preflight against intended VPS database before any restart that applies the new unique index. Query current_database plus count of user_id groups having >1 row with account_type IN(2,3); expected 0. Prior local zero must not be reused as production evidence. No need to rerun the passing Bruno suite. DEPLOY verdict pending production preflight/target; no restart or source edits requested.

## PREVIOUS OWNER GATES — BUILD/TESTS PASSED, MIGRATION APPLIED; BRUNO NEXT
Owner confirms build passed, tests passed, and migration applied. Record as owner-reported success, not assistant-executed verification; no detailed new test count/output supplied and database target name still unspecified. Do not reuse the previous 1377 count as a newly verified count. Deployment is not confirmed.
Fresh remote main remains f787a5c2f1a22baddfd540b431f62ace7ed52d8f. Standing Bruno inventory freshly derived from that tree: set-8d-account-delete 7, account-delete-family 3, auth-account 9, family 11, caregiver 10, public 5, legal-help-cms 8, account-switch 7 = 60 request files. local.bru targets https://localhost:7296; checked https launch profile exposes that port. Existing test credentials stay in local environment; do not request secrets.
Next gate: owner runs API from current main in Release/https profile if not already running, then one Bruno command with all eight positional folders from tests/Bruno, --env local --insecure. This gate sends HTTP requests including auth lifecycle/negative business operations; it is not source-file editing. Expect 60 requests passing and exit 0; require actual report. No destructive/happy-path business mutations added. If requests fail, report exact failing requests/status/error code rather than continuing to deployment.
Deployment remains gated by Bruno and final audit. Treat owner migration-applied statement as current test-environment evidence only, not evidence the VPS has been migrated or preflighted. Production existing-data check and target confirmation remain necessary before VPS auto-migration/restart.

## PREVIOUS MIGRATION PUBLICATION AUDIT — VERIFIED, FINAL GATES PENDING
Fresh fetch confirms origin/main f787a5c2f1a22baddfd540b431f62ace7ed52d8f; author and committer both Mossad <mosad55522@gmail.com>; parent 107ae449a9706d4619dd572b83d5564147ee75c4; tree 770a8e5a6d466d66f63499e7e56918a72a6ca3d6. Exactly three migration artifacts changed (515 additions, 1 deletion). Each published file matches reviewed owner upload after line-ending/BOM normalization. git diff --check passes. Owner reports clean main tracking origin/main; source branch still exists and must not be deleted speculatively.
Migration is now committed/published, NOT confirmed applied. No final tests or Bruno results after f787a5c yet. Prior 1372/1377 test result is pre-migration evidence only. Code/migration audit passes; deployment verdict remains blocked pending complete final gates.
Next independent action allowed: owner Release solution build. Before full tests, require explicit confirmation of local intended database name/target and consent to local schema application: AuthApiHostTests boots API, whose startup automatically applies migrations to all configured module databases. Identity EF generation environment variable alone does not prove the API host target. Owner has repeatedly omitted database_name; do not assume a target or auto-apply against unknown DB. No further source edits or branch cleanup needed.

## PREVIOUS MIGRATION COMMIT CHECKPOINT — OWNER STATE VERIFIED
Owner local main at 107ae449a9706d4619dd572b83d5564147ee75c4 tracks origin/main; only changes are modified IdentityDbContextModelSnapshot.cs and the two 20260915115100_EnforceCaregiverAccountExclusivity files. Fresh remote main unchanged. Snapshot stat 6 insertions / 1 deletion reconciled against uploaded raw UTF-8 file: five-line index addition plus BOM on first line; no extra model change. Earlier normalized audit stripped BOM; semantic audit remains valid.
Owner next normal Git commands, individually and stop on any error: stage only the verified Identity Persistence/Migrations directory, inspect cached stat (exactly 3 reviewed files), owner-authored commit, inspect author/committer/parent, then normal non-force push main and status. Commit title feat(identity): add caregiver account exclusivity migration. Parent must be 107ae449; both identities Mossad <mosad55522@gmail.com>. No source-edit scripts, DB application, API startup or final test gate in this checkpoint. If push rejected, stop; never force.
Database conflict count zero recorded but database_name still not supplied. Publication does not apply schema; DB target must be confirmed before next database-affecting gate. No deployment approval.

## LOCAL DATA PREFLIGHT RESULT — ZERO CONFLICTS REPORTED
Owner reports conflicting_users=0 for the supplied read-only query. This clears the existing-caregiver-row conflict check for the database queried; its database_name was not supplied, so do not claim target identity independently verified. No migration application reported or authorized by this result alone. Fresh remote main remains 107ae449a9706d4619dd572b83d5564147ee75c4; named migration absent remotely.
Next obtain owner `git status --short --branch`, `git log -1 --format='%H %s'`, `git diff --stat` and the database_name from the existing query result. These are read-only commands, no source edits or migration execution. Expected local main at 107ae449 and only three reviewed migration artifacts changed (two untracked new files, modified snapshot). Then prepare narrow owner commit/publication step; no speculative staging, API startup, tests or DB update before actual checkout/target reconciliation.

## OWNER MIGRATION FILE AUDIT — APPROVED CONTENT, NOT APPLICATION
Received owner-generated 20260915115100_EnforceCaregiverAccountExclusivity.cs, matching Designer.cs, and IdentityDbContextModelSnapshot.cs. Fresh remote fetch after restoring stripped origin URL confirms files compared against live main; migration not published in main at audit.
Up creates exactly one unique filtered index ux_user_accounts_one_caregiver on identity.user_accounts(user_id), WHERE account_type IN (2, 3). Down drops only that index. No data changes, deletes or unrelated schema modifications. Snapshot differs from live main solely by that index definition; existing unique (UserId, AccountType) index remains. Designer has correct IdentityDbContext/migration ID; target-model body exactly equals supplied snapshot BuildModel body. ProductVersion annotation remains 10.0.11, not an unrelated version drift.
Verdict: migration content approved; do NOT claim migration applied, committed, pushed, full gate passed, or deployment authorized. Need current owner status/commit evidence before publication instructions; current database target and existing conflicts not yet checked.
Immediate next step: owner uses pgAdmin Query Tool on the intended LOCAL database (matching Identity migration connection setting), executes read-only SELECT current_database() and conflict-group count for account_type IN (2,3), HAVING COUNT(*) > 1 per user_id. Expected conflicting_users=0. Nonzero blocks index application; no automatic deletion/type conversion. Do not request secrets. Repeat preflight against deployment DB before deployment. No API startup/full host test run before migration safety checks because startup auto-applies pending migrations. No source-edit commands or database-update command authorized by this audit.


## OWNER CHECKOUT VERIFIED — SIMPLE COMMANDS FOR MIGRATION GENERATION
Owner evidence: clean tracking set-c-account-switch at df1099319106f016cac8b4f0c3caaa71320052c3; local main 76e4d946037ac44ccf6b14007a6b3d627bd8b090; origin/main 107ae449a9706d4619dd572b83d5564147ee75c4; main divergence 0 ahead / 1 behind. EF CLI 10.0.12 available; ConnectionStrings__IdentityDatabase SET, value not requested. Fresh assistant fetch confirms unchanged origin/main 107ae449. Owner explicitly requests normal short commands, not wrapper scripts. One EF-generation exception remains authorized; no database application or scripted source edits authorized.
Next commands, run individually and stop on any error: git switch main; git pull --ff-only; git rev-parse HEAD (must exactly equal 107ae449a9706d4619dd572b83d5564147ee75c4; otherwise stop). Then dotnet build Sanad.slnx -c Release; proceed only on success. Then ONE generation command:
```powershell
dotnet ef migrations add EnforceCaregiverAccountExclusivity --context IdentityDbContext --project src/Modules/Identity/Infrastructure/Sanad.Modules.Identity.Infrastructure/Sanad.Modules.Identity.Infrastructure.csproj --startup-project src/API/Sanad.API/Sanad.API.csproj --output-dir Persistence/Migrations --configuration Release --no-build
```
Then git diff --stat and git status --short for review; send generated migration .cs, .Designer.cs, IdentityDbContextModelSnapshot.cs. Generation changes only migration source artifacts and does not apply the schema. No database update, API startup, unfiltered host tests, commit, push or deploy in this checkpoint. Existing-data conflicts and target DB isolation must be checked before migration application. If named migration exists or any command fails, stop and report; no alternate name/retry.

## HISTORICAL OWNER CHECK — EXACT COMMANDS REQUESTED
Owner now requests specific commands instead of GUI screenshots. This authorizes the current diagnostic command block; it does not revoke the ban on file-edit scripts or extend the single EF-generation exception to database application/deployment.
Fresh remote main remains 107ae449a9706d4619dd572b83d5564147ee75c4 (tree 9025e89ee3ac95b512b29c38dd8e1783bf8ddec7); published branch df109931 has identical tree. Owner local branch/HEAD/cleanliness still require confirmation before synchronization/migration instructions.
Inspected IdentityDbContextFactory: EF creation requires ConnectionStrings__IdentityDatabase environment variable. Check presence only; never ask owner to paste its value. Factory constructs Npgsql context without migration/database application. EF tool availability should be checked with dotnet ef --version before migration generation, not assumed.
Next read-only owner check: PowerShell at D:\Sanad_API, refresh remote refs, report status, HEAD, local main, origin/main and divergence, EF tool version and presence-only connection variable. No source edits, checkout, migration generation/application or builds in this checkpoint. After output, give one verified next action rather than speculative follow-on commands.

## CURRENT OWNER DECISIONS — IDENTITY EXCEPTION ACCEPTED; MIGRATION COMMAND AUTHORIZED
Owner explicitly selected: (1) accept main 107ae449a9706d4619dd572b83d5564147ee75c4 GitHub merge identity as one-time exception without history rewrite; (2) permit ONE owner-run EF migration-generation command only. Record exceptions in Master Context; all other command/source-edit bans and release gates remain intact.
Fresh remote fetch confirms unchanged main 107ae449; EF project/startup paths and IdentityDbContextFactory exist. No migration generated or database accessed by assistant. Before issuing the one authorized mutating EF command, owner must verify actual local main HEAD and clean tree via Git GUI (screenshots acceptable). Last observed local checkout was implementation branch before remote squash merge, not synchronized local main. Do not infer local state from remote or tell owner to run migration on stale branch.
Immediate next owner action: GUI switch to main, fetch/pull without discarding local changes, show branch/latest commit and changes panel. Expected main 107ae449, zero pending changes; if conflicts/divergence/dirty tree, stop and report instead of reset. Then give one exact EF generation command, not update/deploy; review files before application and separately check target DB/existing caregiver-side conflicts before migration application. Source tree compiled in prior owner Release run equals merged tree, but preflight evidence must remain tied to verified checkout.

## PREVIOUS POST-MERGE AUDIT — PR #16 MERGED, RELEASE BLOCKED
Fresh remote/GitHub verification: PR #16 closed and merged at 2026-09-15T11:30:47Z. origin/main is 107ae449a9706d4619dd572b83d5564147ee75c4, single parent 76e4d946037ac44ccf6b14007a6b3d627bd8b090. Tree 9025e89ee3ac95b512b29c38dd8e1783bf8ddec7 exactly equals reviewed df109931 tree; zero diff between published Slice C head and new main, whitespace check passed. Source merge is correct. Remote source branch still exists.
Canonical identity gate does NOT match: new main author is Mossad Ahmed <57729640+Mossad-55@users.noreply.github.com>, committer GitHub <noreply@github.com>, not both Mossad <mosad55522@gmail.com>. This is an owner GitHub author identity, NOT an AI/bot-author claim. Commit metadata is consistent with a GitHub squash merge; the banned web merge path must not be silently treated as compliant. No history repair, force push or branch deletion authorized or performed. Need explicit owner disposition of this identity exception before release workflow continues; do not silently waive standing canonical gate.
EnforceCaregiverAccountExclusivity migration remains absent on main. Filtered unique index exists only in EF configuration, not migration/snapshot. Current standing Bruno inventory verified from remote tree: 7+3+9+11+10+5+8+7=60. Latest test evidence remains 1372/1377 passed; five auth-host tests blocked before assertions by pending model changes. Main merged is NOT deployed/fully gated.
Next: resolve canonical-identity exception with owner, verify owner checkout and target database, perform data preflight and owner migration, full build/test/60-request Bruno gates, then deployment verdict. No commands issued under owner's current ban; necessary history/migration actions require compatible owner-led workflow or explicit narrow reauthorization. Do not generate migration files as worker or suppress EF warning to bypass missing migration.

## HISTORICAL PR #16 VERIFICATION — BEFORE MERGE
GitHub API audit: https://github.com/Mossad-55/Sanad_API/pull/16 is open, non-draft, unmerged; head set-c-account-switch df1099319106f016cac8b4f0c3caaa71320052c3, base main 76e4d946037ac44ccf6b14007a6b3d627bd8b090. Diff remains 20 files / 727 additions / 1 deletion; git diff --check passes. GitHub reports mergeable=true, mergeable_state=clean; this proves no reported merge conflict, not a passing test/deploy gate. Published tree and identities were independently verified immediately before PR creation and head is unchanged.
Implementation review supports owner-controlled merge of this reviewed tree, subject to explicit owner `merge approved`. Release remains BLOCKED: migration, data preflight/test DB isolation, complete build/test/Bruno gates and deployment confirmation outstanding. Latest owner tests: 1372 passed / 5 startup failures, not final green.
No commands may be issued under latest ruling. Need owner's Git GUI application to provide a supported GUI-only merge path; never direct GitHub web Merge button, never merge on owner's behalf, never assume a GUI/identity is configured. Next ask owner for explicit merge approval and which Git GUI they use. No remote mutation performed during audit.

## PREVIOUS VERIFIED STATE — SLICE C PUBLISHED, PR PENDING
Owner commit/push succeeded. Independently fetched origin and verified branch set-c-account-switch at df1099319106f016cac8b4f0c3caaa71320052c3, tree 9025e89ee3ac95b512b29c38dd8e1783bf8ddec7, parent 87a00ad2f56292d6518bf9ba9523b115e9a8ecac. Both Slice C commits have Mossad <mosad55522@gmail.com> as author and committer. Parent ancestry leads directly to baseline 76e4d946037ac44ccf6b14007a6b3d627bd8b090; origin/main remains unchanged there.
Owner output reports clean tracking branch. Published diff: 20 files, 727 insertions, 1 deletion; diff whitespace check passed and all 20 published file blobs match the reviewed corrected workspace. No migration files included. Open-PR API check returned none. Sandbox checkout remains staged/local edits from code generation; do not confuse it with the owner clean checkout or reset it without need.
Latest runtime evidence remains owner Release compilation success and 1372/1377 tests passed, five AuthApiHostTests blocked by pending Identity migration at startup. No final gate or deploy approval.
Next owner action is GUI-only: create review PR via https://github.com/Mossad-55/Sanad_API/pull/new/set-c-account-switch with base main and compare set-c-account-switch. Suggested title: Slice C: account add/switch and caregiver exclusivity. PR is a review artifact only; never use GitHub Merge button. After PR creation, audit head/base and request explicit merge approval before selecting an owner-controlled non-command merge workflow. Migration generation and test-host database isolation remain pending; do not assume GUI equivalents exist for all gates or silently waive them.


## OVERRIDING OWNER CONSTRAINT — NO FILE-EDIT COMMANDS
Owner reinforced on 2026-09-15: NEVER generate user-run commands/scripts that edit files (source, tests, configuration, documentation), including replacements, shell writes or patch application. Use direct assistant workspace edits and complete-file/GUI delivery or authorized GitHub delivery instead. Historical command blocks below are records, not permission to reuse editing commands.
The most recently issued Git block stages, commits and pushes the three reviewed test files; it does not rewrite their contents. It does change the Git index/history, fetched refs and remote branch. Owner has called this the final command block: issue no further blocks unless explicitly reauthorized. Preserve all owner approval/build/test/migration/deploy gates; arrange non-command workflows where available and state capability limits rather than bypassing gates. No execution result for the final Git block has been received yet.

**Living doc — mastermind updates every session. Product truth lives in `Sanad_Master_Context.md` (File 1).**
Last sync: 2026-09-15

## REPO-FIRST VERIFICATION LAW (owner-locked 2026-09-14)
When the owner asks to check the repository, the mastermind must inspect before prescribing any
command. The inspection must cover, in this order: actual repository/cwd and shell context;
working-tree cleanliness; all local and remote branches/ref SHAs; PR head/base and merge state;
commit authors and committers on the relevant ancestry; changed-file list and tree identity;
then the actual implementation, wiring, tests, configuration, migrations, and documented
requirements. Do not infer a branch, path, project, migration, or commit identity from an older
session. Commands are issued only after this inspection and must match the owner's actual OS,
shell, repository path, current branch/SHA, and available tools. If the local owner checkout
(`D:\\Sanad_API`) is not available in the shared workspace, request the exact owner command
output needed rather than pretending that `/home/user/Sanad_API` is the same checkout.

## SESSION SNAPSHOT — 2026-09-15 / SLICE C PRE-CONTRACT
- Owner explicitly selected the original delivery order: Slice C → Subscriptions → full-app UI review → G/H/I. Caregiver earnings remains Phase I; no product-scope amendment.
- Shared Linux clone: `/home/user/Sanad_API`; clean `main` and fetched `origin/main` at `76e4d946037ac44ccf6b14007a6b3d627bd8b090`. This is NOT the owner Windows checkout.
- Session GitHub check found no open PRs; remote advertised main only. Latest four commits have Mossad as author and committer. CMS migration present; standing Bruno inventory 53 requests. Historical passing gates/deployment are owner-recorded, not rerun here.
- Slice C initial code audit: `User.AddAccount` rejects duplicates, Elderly combinations and administrative combinations, but lacks Medical/Companion mutual exclusion. Current callers include registration and seeders.
- JWT generation currently emits all user account types. DeviceSession has no active-account field. AccountController has profile/settings/delete endpoints, not add/switch endpoints. Therefore switching semantics must be explicitly contracted before changing authentication; UI selection and authorization isolation are not interchangeable.
- No implementation, commits, builds, EF/Bruno execution, merges or deployment performed.
- Snapshot incomplete: owner shell/path/HEAD/clean-tree evidence still required before owner command blocks; deeper authorization, registration, refresh and onboarding wiring audit required before exact worker manifest.
- Lessons: ripgrep is unavailable in this sandbox; use grep/find rather than repeatedly failing tool calls. Record findings with evidence and distinguish verified code facts from recorded deployment results. Do not promise zero errors or bypass owner gates for speed.
- Next action: verify owner shell and checkout context, complete Slice C contract with explicit switching/session semantics, then implementation under the existing worker rules.

## OWNER CHECKOUT VERIFIED — SLICE C
- Owner supplied terminal evidence: PowerShell 7.6.6, `D:\Sanad_API`, branch main, clean working tree, successful origin fetch/prune.
- HEAD and origin/main both `76e4d946037ac44ccf6b14007a6b3d627bd8b090`; divergence 0/0. Author and committer are Mossad <mosad55522@gmail.com>.
- Stale local branch `arena/01a09bd4-sanad-api` at `3f61aa2` has a gone upstream. Leave untouched; it does not block Slice C and deletion is not authorized.
- Further audit: registration supports Family/Medical/Companion and rejects existing email/phone; it does not implement adding a side to an authenticated identity. Refresh regenerates all account claims from User, not an active account selection.
- Pending owner clarification before contract: account switching as dashboard selection retaining both owned permissions versus strict selected-role authorization. These are materially different session/security scopes; do not silently select one.
- Lesson: a clean synchronized owner checkout does not require pull, reset, or stale-branch cleanup. Avoid unnecessary mutations.

## SLICE C SCOPE DECISION — 2026-09-15
Owner selected dashboard switching, not strict active-account authorization. Record this in product truth and carry into contract:
- Add a supported account side to the authenticated identity, not an unrelated login or duplicate identity.
- Expose owned account choices; dashboard selection must never grant an unowned account or bypass onboarding/family membership checks.
- Preserve all-owned-account JWT claims and existing refresh semantics. Client refresh is required before a newly added account claim appears in an existing access-token session; do not silently assume an old JWT changes.
- Enforce Medical/Companion mutual exclusion in the domain, with application errors and tests. Audit concurrent adds and existing invalid combinations before choosing persistence constraints.
- No admin/Elderly self-service account additions. No wallet/earnings/subscriptions expansion.
- Contract remains in preparation: finish onboarding/deletion/persistence/concurrency audit and exact file manifest before worker execution. No application edits yet.

## CURRENT STATE
- Last fully recorded pre-Slice-B baseline was `fe5238c` — A2 visibility/self-delete,
  **Mossad-authored (A+C verified)**, with recorded `fb6c304` as an ancestor. **A2 DEPLOYED
  to VPS, owner-confirmed "deployed" 2026-09-14.**
- **Slice B DEPLOYED (owner-confirmed 2026-09-14):** remote `main` is
  `76e4d946037ac44ccf6b14007a6b3d627bd8b090`, authored and committed by
  `Mossad <mosad55522@gmail.com>`. The migration is committed, final build/tests passed, Bruno
  coverage is 53/53, and the owner confirmed `deployed`.
- **SLICE A1 ISSUED** (settings core = SET-10 notification superset + SET-11 member/elderly cards +
  role-matrix lock tests) — prompt `Sanad_Slice-A1_Worker_Prompt.txt`, branch `set-a1-settings-core`,
  baseline `fb6c304`, commit `feat(account,families): settings core - notification superset toggles + member/elderly card fields`. Gate = 41-request suite (+1 notif-prefs GET).
  Verified pre-contract: role matrix ALREADY matches owner spec (invite=Owner/Editor,
  book=Owner/Editor, remove/role-change=Owner only) → tests lock, zero behavior change.
- **A1 CLOSED (deployed, owner-confirmed 2026-09-13).** main = `02ed88f`, 41/41 gate was green,
  migration `AddNotificationPreferencesSuperset` applied at boot on VPS.
- **A2 CLOSED (deployed, owner-confirmed 2026-09-14).** Prompt `Sanad_Slice-A2_Worker_Prompt.txt`
  (v4.4: worker pushes + PRs + STOPS, never merges). Branch `set-a2-privacy-selfdelete`, baseline
  `02ed88f`, commit `fe5238c`:
  `feat(caregiver,identity): visibility preferences + family-only self-delete with ownership guard`.
  SET-14: VisibilityPreferences VO (showProfile=T, showRating=T, showPhone=F, shareLocation=F),
  GET/PUT /caregiver/privacy, discovery excludes showProfile=false, 1 Caregivers migration.
  SET-15: DELETE /account family-only branch — elderly → 409 ElderlyManagedByFamily · owner of
  active family → 409 OwnershipTransferRequired · else leave families + anonymize & retain +
  sessions revoked. New IFamilyAccountGateway (Identity→Families, mirrors ICaregiverAccountGateway).
  Gate = 47-request suite (+privacy GET +3-request account-delete-family folder).
- After A2: Slice B (legal + help CMS) → Slice C (add-account/switch + exclusivity) → subscriptions.
- **SLICE B CLOSED / DEPLOYED:** the owner repaired the bot-authored merge history, applied the
  small owner compile correction, generated the CMS migration, passed build/tests and 53/53
  Bruno requests, and confirmed deployment. Process retrospective is the next activity.
- **FINAL POST-GATE AUDIT — DEPLOY: YES:** migration commit `76e4d946` is owner-authored and
  adds exactly `AddLegalAndHelpCenterContent` plus its designer and snapshot update. Build and
  unit tests passed; Bruno is 53/53 after running the omitted three-request collection. Owner
  confirmed `deployed`. Final audit: `Sanad_Slice-B_Final_PostGate_Audit.md`.
- **PR #14 CLOSED / BRANCH REMOVED (2026-09-14):** stale PR #14 was closed without merge and
  `arena/01a09faa-sanad-api` was deleted. PR #15's branch was also deleted after its tree was
  transferred to owner-authored `main` `93367e7`.
- **PR #15 POST-MERGE AUDIT FAILED, THEN REPAIRED (2026-09-14):** GitHub created bot merge
  `b4dd25c`; the owner replaced it with identical owner-authored `93367e7`. The repair audit
  and post-merge addendum are recorded in `Sanad_Slice-B_Build-Repair_PR15_Audit.md`.
- After Slice B closes: Slice C (add-account/switch + medical↔companion exclusivity), subscriptions
  slice, and UI review.
- Screen-review mode ACTIVE: Batch 1 recorded (File 1 §4); Batch 2+ awaiting owner.

## VIEWER FIXTURE REPAIR — CANONICAL BLOCK (done 2026-09-13; reuse if drift recurs)
1. If invite creation → 409 `PendingInvitationExists` while list is EMPTY: an expired-but-pending
   invitation is blocking. In pgAdmin on local `sanad`:
   `UPDATE families.family_invitations SET status = 5 WHERE invited_email = 'family.viewer@test.sanad.local' AND status = 1;`
2. Owner login + create invitation (one bash block, token auto-extracted — NEVER hand-copy JWTs):
   `OWNER_TOKEN=$(curl -sk -X POST https://localhost:7296/api/v1/auth/login -H "Content-Type: application/json" -d '{"email":"family.owner@test.sanad.local","password":"Test-1234!","deviceName":"fixture-repair","devicePlatform":3,"appVersion":"1.0.0"}' | sed -E 's/.*"accessToken":"([^"]+)".*/\1/') && curl -sk -X POST https://localhost:7296/api/v1/family/invitations -H "Content-Type: application/json" -H "Authorization: Bearer $OWNER_TOKEN" -d '{"email":"family.viewer@test.sanad.local","role":3,"relationshipType":3}'`
3. **Owner's local API has SMTP configured → NO `[DevEmail]` console line; the invite link
   (`sanad://family/invite?token=…`) arrives by real email (Gmail).**
4. Viewer login + accept (expect 204):
   `VIEWER_TOKEN=$(curl -sk -X POST https://localhost:7296/api/v1/auth/login -H "Content-Type: application/json" -d '{"email":"family.viewer@test.sanad.local","password":"Test-1234!","deviceName":"fixture-repair","devicePlatform":3,"appVersion":"1.0.0"}' | sed -E 's/.*"accessToken":"([^"]+)".*/\1/') && curl -sk -o /dev/null -w "%{http_code}\n" -X POST https://localhost:7296/api/v1/family/invitations/accept -H "Content-Type: application/json" -H "Authorization: Bearer $VIEWER_TOKEN" -d '{"token":"<TOKEN_FROM_EMAIL>"}'`
NOTE for VPS suite runs: VPS has its OWN database — viewer membership + `--env-var viewerEmail=… viewerPassword=…` needed there too before family 07–09 can pass on VPS.

## ACTIVE QUEUE (owner-ordered 2026-09-14)
1. **Slice B — ✅ DEPLOYED:** owner-authored `main` `76e4d946`, migration present, build/tests
   passed, Bruno 53/53, and owner-confirmed `deployed`. Next: process retrospective before Slice C.
2. **Slice C — account add/switch + exclusivity law** (one worker PR).
3. **Subscriptions slice** (Phase S pulled forward — full spec File 1 §4/F3).
4. **Full-app UI review** (owner-led; remaining screen batches; gaps → slices).
5. Phases G / H / I.

Closed this cycle: SET-8e-B, A1 (SET-10/SET-11), and A2 (SET-14/SET-15).

## OWNER RULINGS 2026-09-13–14 (via prompts)
notif-model=superset · emergency-call=contact-card (owner+booking caregiver, SOS stays Phase I) ·
billing=keep payment-method change + server-side fixed-template PDF later, no admin PDF config ·
order=settings&profile → subscriptions → full UI review. Laws 9–14 recorded in File 1 §2.
SET-12/SET-13: admin writes use existing `CmsContent` (SuperAdmin + ContentAdmin only) · legal
lifecycle=Draft→Published→Archived with one current version per document type/audience and
retained history · support phone/email=one global hotline for all app roles.

## BATCH-1 VERIFICATION FACTS (2026-09-13, live code @ bd056a3)
- Bilingual names EXIST (`User.ArabicFullName`/`EnglishFullName`) — no slice needed.
- Sessions complete: list + `DELETE sessions/{id}` + logout + logout-all + password change.
- `FamilyMemberResponse`: role, relationship, names AR/EN, email, JoinedOnUtc — MISSING avatar,
  status, last-activity (→ SET-11).
- `ElderlyActivityType` enum = owner's log examples 1:1 (6 event types, nothing static).
- Companion profile HAS: YearsOfExperience, SpecializationId, Biography; Medical adds
  ProfessionalTitleId, CurrentWorkplace. Ratings DO NOT exist (Phase I).
- `DependentResponse` has no phone field (phone lives on identity user → SET-11 exposure).
- NotificationPreferences = 4 bools (checkInAlerts, medicationReminders, bookingUpdates,
  communityNotifications) — family needs +1, caregiver needs +3 (→ SET-10).

## OWNER PRECONDITION (once, before first full Bruno suite)
Viewer fixture repair — invite viewer back into the family (4 curls, handoff §8 / plan doc):
owner login → `POST /api/v1/family/invitations` `{"email":"family.viewer@test.sanad.local","role":3,"relationshipType":3}`
→ token from `[DevEmail]` console line → viewer login → accept. 409 SessionLimit → clear
`identity.device_sessions` via psql.

## SLICE B OWNER MIGRATION — REQUIRED BEFORE GATES
After the owner-author identity repair and worker-branch deletion, generate the EF migration
from the merged CMS model. The worker never creates this file.

```bash
cd /d/Sanad_API
git fetch origin --prune
git switch main
git pull --ff-only

# Migration preflight only; this is not the final standing gate.
# If it fails, stop and request a repair PR. Do not create a migration from a broken tree.
dotnet build Sanad.slnx

dotnet ef migrations add AddLegalAndHelpCenterContent \
  --project src/Modules/Cms/Infrastructure/Sanad.Modules.Cms.Infrastructure/Sanad.Modules.Cms.Infrastructure.csproj \
  --startup-project src/API/Sanad.API/Sanad.API.csproj \
  --context CmsDbContext \
  --output-dir Persistence/Migrations

git add src/Modules/Cms/Infrastructure/Sanad.Modules.Cms.Infrastructure/Persistence/Migrations
git -c user.name="Mossad" -c user.email="mosad55522@gmail.com" commit \
  -m "feat(cms): add legal and help center content migration"
git push origin main
git log -2 --format='%h %an <%ae> %s'   # both commits must be Mossad-authored
```

If EF reports that `AddLegalAndHelpCenterContent` already exists, STOP. Do not create a
second migration; report the exact output.

## STANDING GATES (owner-run, exact)
```bash
cd /d/Sanad_API && git fetch origin && git switch main && git pull --ff-only
git log -1 --format='%h %an <%ae> %s'      # must be Mossad <mosad55522@gmail.com>
dotnet build Sanad.slnx && dotnet test Sanad.slnx --no-build
cd tests/Bruno && bru run collections/Sanad/set-8d-account-delete collections/Sanad/account-delete-family \
  collections/Sanad/auth-account collections/Sanad/family collections/Sanad/caregiver \
  collections/Sanad/public collections/Sanad/legal-help-cms --env local --insecure
# expect: 53 requests, all passed, exit 0 (current files: 7 + 3 + 9 + 11 + 10 + 5 + 8)
# The 3-request account-delete-family folder was omitted from the earlier 50-request run.
```
Deploy = routine VPS restart; migrations auto-apply at boot.

## PROCESS FLOW (v4.5, owner-locked 2026-09-13)
**Worker = CODE GENERATION ONLY.** The worker writes implementation, test code, Bruno files and
docs, pushes the branch, opens the PR, reports, STOPS. The worker never: merges, deletes branches,
runs dotnet/ef/bru/npm, or creates migration files.
**Owner = build machine.** Owner generates migrations from the worker's EF configuration, commits
those migrations on the owner-controlled `main`, pushes `main`, then runs build + tests + Bruno
gate. The owner controls the repair PR merge flow and deployment.
**Mastermind = contracts + audits + verdicts + exact command blocks for every owner step.**
Merge = owner pastes mastermind's author-proof block. Post-merge author check stays standard.
(Reason: worker sandbox cannot download packages reliably; owner machine is the source of truth.)
**⛔ GitHub "Merge pull request" button is BANNED for everyone (2026-09-13).** PRs are review
artifacts only. That button produced bot commits twice: A1 `3d2bad9` and A2 `04b659f` (merge
commit, arena-bot author) — both owner-repaired via `git reset --soft` + recommitter-identity
commit + `push --force-with-lease`. Repair drill is standing-approved for exactly this case.

## KEY LESSONS (compact)
1. Bruno `.bru` files: no secrets, runtime `--env-var` only; `--insecure` for local self-signed cert.
2. bru CLI 4.x needs Node ≥ 19 (owner workaround: `--experimental-global-webcrypto`); no `--filter` flag — positional folders.
3. Every collection ENDS with logout requests (5-session cap per account).
4. Enum census tests exist — adding enum values requires updating them (e.g. `CaregiverStatusTests`).
5. Squash merges can bot-author commits — post-merge author check is standard; owner repairs with
   soft-reset + recommit when it happens.
6. `.git/config` gets stripped between sandbox sessions → `git remote set-url origin https://github.com/Mossad-55/Sanad_API.git` then fetch.
7. Controller code lives at `src/API/Sanad.API/Controllers/` (nested project dirs — always `git ls-tree` before `git show`).
8. Post-anonymization login = 401 InvalidCredentials (email scrubbed), not 403.
9. **Never use `00000000-0000-0000-0000-000000000000` in negative tests** — validators
   (`NotEqual Empty`) return 400 `Api.Validation.Failed` BEFORE the intended 401/404 paths.
   Use a fixed non-empty guid (we use `0f000000-0000-4000-8000-000000000001`). (SET-8e-B gate miss.)
10. Invitation trap: create-check counts ALL `Pending` rows; the list endpoint hides expired ones →
    "empty list + 409 PendingInvitationExists" = stale pending row → SQL `status = 5` fix.
11. Owner's local API sends REAL email (SMTP configured) — dev-console `[DevEmail]` never appears;
    tokens arrive in Gmail. Docs/handoffs assuming console OTPs must say this.
12. Never hand-copy JWTs into commands (401s from truncated pastes) — always one-block
    `TOKEN=$(curl … | sed -E 's/.*"accessToken":"([^"]+)".*/\1/')` extraction.
13. Login response `deviceSessionId` = `{"value":"…"}` object (record struct, no converters);
    docs sample showing a plain string is a KNOWN DOCS BUG (queued for the docs pass).
14. Give the owner ONE path, numbered steps, no options — no handoff-section references.
15. **EF model change = migration required:** the worker never creates migrations; the owner generates the named migration, commits it as Mossad, pushes it, then runs build/test/Bruno gates before any deploy verdict.

## SLICE B PROCESS RETROSPECTIVE — OWNER-LOCKED 2026-09-14

### What caused the delay and repeated rework
1. **Worker feedback was too late by design.** The worker contract prohibited build, test, EF,
   and Bruno execution. The first implementation therefore reached the owner before its EF
   builder mismatch was compiled. The worker was not necessarily slow; the process had a slow
   feedback loop.
2. **The banned GitHub merge path was used twice.** PR #13 and then PR #15 were merged through
   GitHub, producing bot-authored `main` commits. Each merge required a soft-reset, identical
   tree repair, force-with-lease push, and another audit. PR #14 then became stale after the
   first history rewrite.
3. **Mastermind state drift caused invalid instructions.** Commands were generated from older
   session records instead of a fresh owner-checkout snapshot; the shared Linux workspace was
   mistaken for the owner's Windows checkout; Bash-style paths were given to PowerShell; and
   commands were sometimes issued before the previous remote result had been reconciled.
4. **The implementation audit was not deep enough before the first merge.** The owned EF
   collection callback type mismatch should have been caught by inspecting the actual
   `OwnsMany` call and helper signature before approval.
5. **The gate inventory was stale.** The expected count of 55 was not derived from the live
   repository. The actual current inventory is 53: 7 + 3 + 9 + 11 + 10 + 5 + 8. The first
   gate command omitted the 3-request `account-delete-family` folder, so it reported 50/50.
6. **Small corrective edits were over-escalated.** The expression-tree null checks and EF
   metadata assertion were handled through extra worker/PR discussion instead of one bounded
   owner correction after the live tree was checked.

### New efficient process — exactly two active files

- `Sanad_Master_Context.md` is the only product-truth file: owner rulings, product scope,
  non-negotiable laws, and current released status.
- `Sanad_Operations.md` is the only operational file: the current state snapshot, active worker
  contract, PR audit, owner command block, gate results, deployment verdict, and retrospective.
- No new standalone prompt, audit, handoff, or verdict files are created for future slices.
  Existing files remain historical only and are not active instructions. If a worker needs
  instructions, they are delivered in chat and recorded in this File 2; no prompt file is
  generated.

### Mandatory state machine for every future slice

1. **SNAPSHOT:** inspect actual owner shell/path, local status/HEAD, `origin/main` SHA/tree,
   all branches, open PRs, authors/committers, changed files, implementation wiring, migration
   state, and live Bruno file inventory. Record the snapshot in Operations.
2. **CONTRACT:** write one bounded worker contract in Operations with an exact manifest and
   stop rules. The worker pushes one PR and stops; it never merges, migrates, or deploys.
3. **PRE-MERGE BUILD:** after implementation audit and before owner merge, the owner compiles
   the exact PR head. This is a diagnostic pre-merge build, not the final gate; it catches the
   failure that delayed Slice B while preserving the worker law.
4. **MERGE:** owner explicitly says `merge approved`; use only the owner-controlled merge/
   recommit flow. Never use GitHub's Merge button. Immediately re-audit main author, committer,
   parent, tree, and branch cleanup.
5. **MIGRATION:** only after the repaired implementation is owner-authored on main and the
   compile preflight passes, generate the named owner migration. Verify the migration diff
   contains only the expected migration files and snapshot, then commit/push as Mossad.
6. **FINAL GATES:** run build, tests, and one Bruno command generated from the live `.bru`
   inventory. Record the actual request count and all results; never reuse a stale number.
7. **DEPLOY:** re-audit main and gate evidence, issue `DEPLOY: YES` or a blocking verdict,
   then wait for the owner confirmation `deployed`.

### Command discipline

- One next action at a time; no speculative command blocks.
- Commands match the verified OS, shell, path, branch, and SHA.
- Every mutating block has exact SHA/clean-tree guards and a stop-on-failure condition.
- Small owner corrections stay small: inspect the diff, build, commit, and re-audit; do not
  create a new worker PR unless behavior/scope actually changed.
- No deployment verdict is based on a partial gate.

## ARTIFACT INDEX
| File | Role |
|---|---|
| `Sanad_Master_Context.md` | **File 1** — product truth (owner-ruled) |
| `Sanad_Operations.md` | **File 2** — this living doc |
| `Sanad_SET-8e-B_Worker_Prompt.txt` | closed — style reference |
| `Sanad_Slice-A2_Worker_Prompt.txt` | closed — A2 contract |
| `Sanad_Slice-B_Worker_Prompt.txt` | historical — absorbed into this File 2 retrospective; no longer active |
| `Sanad_SET-8d_Worker_Prompt.txt` | closed — style reference |
| `Sanad_Acceptance_Session_Plan.md` | session script (superseded by screen-review mode where they overlap) |
| `Sanad_Roadmap.md` | superseded by File 1 §5 (kept for history) |
| `uploads/Sanad_Care_Master_Project_Context (1).md` | owner's original 292KB master doc — source of §21 settings spec + phase roadmap |
| `uploads/Sanad_Mastermind_Handoff.md` + `/home/user/Sanad_Mastermind_Handoff.md` | original handoff v4.x — historical; Files 1–2 now carry the load |

## SYNC RULE
File 1 changes only on owner ruling. File 2 changes every session (state, queue, lessons).
Every slice closure updates both in the same turn.

## SLICE C IMPLEMENTATION CONTRACT — 2026-09-15
Baseline 76e4d946037ac44ccf6b14007a6b3d627bd8b090; branch set-c-account-switch. Owner approved original order and dashboard-only switching.
API: GET /api/v1/account/accounts lists owned sides; POST same adds Family/Medical/Companion to the authenticated active regular identity; POST /api/v1/account/switch validates an owned side and returns selection without persistence/token/session mutation. Selection is client-local, not authorization. Add returns refreshRequired=true; existing refresh then normal onboarding. No unrelated identity linking.
Safety: domain mutual exclusion plus unique filtered PostgreSQL index on user_id WHERE account_type IN (2,3); existing per-type unique index retained. Known index races map to 409, unrelated persistence failures propagate. Admin/Elderly/unsupported additions forbidden; inactive users blocked. Retained caregiver profile prevents self-service caregiver re-add, not reactivation. No automatic family creation, membership, caregiver approval or profile bootstrap.
Exact manifest (relative to repo):
- src/Modules/Identity/Domain/Sanad.Modules.Identity.Domain/Users/{User.cs,UserErrors.cs}
- src/Modules/Identity/Application/Sanad.Modules.Identity.Application/Users/{AccountChoices.cs,AccountErrors.cs}
- src/Modules/Identity/Application/Sanad.Modules.Identity.Application/Abstractions/Data/AccountWriteConflictException.cs
- src/Modules/Identity/Infrastructure/Sanad.Modules.Identity.Infrastructure/Persistence/{IdentityDbContext.cs,Configurations/UserConfiguration.cs}
- src/API/Sanad.API/Controllers/AccountController.cs
- src/API/Sanad.API/Controllers/Requests/AccountChoiceRequest.cs
- src/API/Sanad.API/ProblemDetail/ResultProblemDetailsMapper.cs
- tests/Sanad.UnitTests/Identity/Account/AccountChoicesTests.cs
- tests/Sanad.UnitTests/Identity/Infrastructure/AccountExclusivityModelTests.cs
- tests/Bruno/collections/Sanad/account-switch/{01-login-owner.bru,02-list-accounts.bru,03-add-family-duplicate.bru,04-add-admin-forbidden.bru,05-switch-elderly-forbidden.bru,06-add-anonymous.bru,07-logout-owner.bru}
No migration files written by worker. Owner migration name: EnforceCaregiverAccountExclusivity. Before migration, owner checks existing duplicate caregiver sides; conflicts block rather than auto-delete retained records. Pre-merge compile, explicit merge approved, author-proof owner merge, migration and final gates remain mandatory.
Tests: domain exclusivity both directions, allowed family hybrids, duplicate/invalid/privileged/inactive requests, missing user, unowned selection, no writes/session creation on switch, account persistence, gateway re-add guard, filtered unique index metadata. Bruno negative-only business mutations plus read and auth lifecycle; all collections end logout.
Stop rules: no dotnet/ef/bru/npm execution, migration generation, merge, branch deletion or deploy. Do not claim compile/runtime/concurrency success without owner evidence. If push/PR credentials are unavailable, report precisely and retain changes for owner transfer; do not invent a PR.

## SLICE C IMPLEMENTATION HANDOFF — 2026-09-15
Supersedes earlier pre-contract notes. Implementation is staged on local sandbox branch set-c-account-switch, 19 files / 715 added lines; no commit, remote branch or PR was created. No main changes, build/test/Bruno/EF execution, migration files, merge or deployment.
- GET /account/accounts, POST /account/accounts, POST /account/switch implemented under NormalAccess. Add requires active regular identity; switch verifies current ownership and returns client-local selection only. No token/session change on switch. Add returns refreshRequired=true; use existing refresh then existing caregiver profile bootstrap/onboarding or family create/join flow. Account addition does not approve a caregiver or grant family membership.
- Domain exclusivity and filtered unique index configuration added. Known PostgreSQL unique violations map to coded conflicts; unrelated persistence errors propagate. Duplicate, unsupported, inactive, unowned and retained-profile paths covered in authored tests.
- New test source includes handler/domain, error mapping, endpoint policy metadata and design-time EF index assertions. Existing JWT test already covers both Family + Medical claims. These tests have NOT run. PostgreSQL concurrent-request behavior and existing-data validity remain unverified.
- Seven Bruno request files added; live standing inventory is now 60 (53 existing + 7 new). Business mutations are negative-only; login/logout retained, logout last. JSON bodies parsed statically; no HTTP calls sent.
- Static checks passed: git diff --cached --check; patch applies against a temporary Git index loaded from exact baseline 76e4d946037ac44ccf6b14007a6b3d627bd8b090. These are not compile/runtime proof.
- Remote delivery blocked: GIT_TERMINAL_PROMPT=0 git push --dry-run failed with `fatal: unable to get password from user`. No credentials requested or exposed, no remote mutation. Owner-assisted patch transfer is the proposed bounded workaround, not a replacement for merge approval or release gates.
- Patch: /home/user/Sanad_Slice-C.patch, 39949 bytes, SHA256 f82f3ca2324f5f0829624f84bb4a953986b73b6e5f10608b394b8dcb3d94ebde.
- Migration EnforceCaregiverAccountExclusivity is owner-generated only, after implementation preflight and owner-controlled merge. Before generating/applying, inspect duplicate caregiver sides per user; stop on existing conflicts, never delete/anonymize records automatically to satisfy the index. Migration should be limited to the expected Identity index/snapshot changes.
- Known inherited edge for follow-up: caregiver self-delete returns CaregiverProfileNotFound before caregiver profile bootstrap. Slice C follows existing separate profile bootstrap and does not silently broaden deletion rules. Retained caregiver profiles block caregiver re-add rather than permitting reactivation/type conversion without a policy.
- Lessons: verify remote write access before promising a PR; do not conflate a successful public clone with push permission. JSON snippets containing {{variables}} must be parsed with a JSON decoder, not naive brace regex. Use EF design-time model for index metadata checks. Keep compile and PostgreSQL evidence explicitly separate from static checks.
- Next checkpoint: owner saves patch under Downloads, guarded PowerShell creates local implementation branch, applies exact hash-verified patch, makes owner-authored commit and compiles. Paste commit identity and full build result. No migration, merge, final gate or deployment authorization yet.

### Current owner command — patch transfer and diagnostic build only
Save Sanad_Slice-C.patch to $HOME\Downloads, then run in PowerShell:
```powershell
$ErrorActionPreference = 'Stop'
function gitok {
    & git @args
    if ($LASTEXITCODE -ne 0) { throw "Git failed. Stop here." }
}
Set-Location 'D:\Sanad_API'
$base = '76e4d946037ac44ccf6b14007a6b3d627bd8b090'
$patch = Join-Path $HOME 'Downloads\Sanad_Slice-C.patch'
$hash = 'f82f3ca2324f5f0829624f84bb4a953986b73b6e5f10608b394b8dcb3d94ebde'
if (!(Test-Path -LiteralPath $patch)) { throw "Save the patch in Downloads first." }
if ((Get-FileHash -LiteralPath $patch -Algorithm SHA256).Hash -ne $hash) { throw "Patch hash mismatch. Stop." }
gitok fetch origin --prune
if ((gitok branch --show-current) -ne 'main') { throw "Expected main. Stop." }
if (gitok status --porcelain) { throw "Working tree is not clean. Stop." }
if ((gitok rev-parse HEAD) -ne $base) { throw "Local baseline changed. Stop." }
if ((gitok rev-parse origin/main) -ne $base) { throw "Remote baseline changed. Stop." }
gitok apply --check --index $patch
gitok switch -c set-c-account-switch
gitok apply --index $patch
gitok -c user.name=Mossad -c user.email=mosad55522@gmail.com commit -m "feat(identity): account choices, dashboard switching and caregiver exclusivity"
$identity = gitok log -1 --format='%an <%ae>|%cn <%ce>'
if ($identity -ne 'Mossad <mosad55522@gmail.com>|Mossad <mosad55522@gmail.com>') { throw "Commit identity mismatch. Stop." }
gitok log -1 --format='%H%nAuthor: %an <%ae>%nCommitter: %cn <%ce>%nSubject: %s'
dotnet build Sanad.slnx
if ($LASTEXITCODE -ne 0) { throw "Diagnostic build failed. Paste output; do not merge or migrate." }
gitok status --short --branch
```

## SLICE C OWNER PREFLIGHT FAILURE / BOUNDED REPAIR
Owner checkout: clean set-c-account-switch at 87a00ad2f56292d6518bf9ba9523b115e9a8ecac, author and committer Mossad <mosad55522@gmail.com>. Patch applied exactly; owner commit contains 19 files / 715 additions. Application and API projects compiled, but solution build FAILED: 5 CS0121 errors in AccountChoicesTests.Validate calls and 2 xUnit2031 warnings in AccountExclusivityModelTests. No tests ran; no merge/migration/deploy approval.
Root cause (assistant-authored test defect): target-typed new(...) is ambiguous between FluentValidation.Validate(T) and Validate(ValidationContext<T>). Fix: explicitly name GetMyAccountsQuery/AddMyAccountCommand/SwitchMyAccountCommand at all five calls. Replace Assert.Single(sequence.Where(predicate)) with Assert.Single(sequence, predicate) at both model assertions.
Sandbox source corrected: two test files only, 7 lines replaced; git diff --check passed. Owner must build and test corrected tree; static correction is not compile evidence. Original downloadable Slice-C patch remains the original artifact and MUST NOT be reapplied to the owner branch.
Prevention: use explicitly typed messages in overloaded validator calls; audit analyzer-recommended overloads before handoff. Run diagnostic unit/architecture tests immediately after repaired pre-merge build, before migration or merge. Keep small compile repairs in the owner branch rather than introducing a new worker PR. Wrap all future mutating PowerShell steps in one invoked scriptblock so a throw stops the entire submitted block, not merely one pasted top-level statement.
Next: SHA/branch/clean-tree guarded two-file correction on owner's existing branch, inspect diff, build + diagnostic tests, then owner-authored repair commit only if both pass. No push/merge/migration/deploy in this checkpoint.

### Current owner repair command (supersedes patch-transfer block)
```powershell
& {
    $ErrorActionPreference = 'Stop'
    function gitok {
        & git @args
        if ($LASTEXITCODE -ne 0) { throw "Git failed. Stop." }
    }
    Set-Location 'D:\Sanad_API'
    if ((gitok branch --show-current) -ne 'set-c-account-switch') { throw "Wrong branch." }
    if ((gitok rev-parse HEAD) -ne '87a00ad2f56292d6518bf9ba9523b115e9a8ecac') { throw "HEAD changed." }
    if (gitok status --porcelain) { throw "Working tree is not clean." }

    $a = 'tests/Sanad.UnitTests/Identity/Account/AccountChoicesTests.cs'
    $b = 'tests/Sanad.UnitTests/Identity/Infrastructure/AccountExclusivityModelTests.cs'
    $text = [IO.File]::ReadAllText((Join-Path $PWD $a))
    foreach ($type in @('GetMyAccountsQuery', 'AddMyAccountCommand', 'SwitchMyAccountCommand')) {
        $old = "new ${type}Validator().Validate(new("
        $expected = if ($type -eq 'GetMyAccountsQuery') { 1 } else { 2 }
        if ([regex]::Matches($text, [regex]::Escape($old)).Count -ne $expected) {
            throw "Unexpected validator source: $type"
        }
        $text = $text.Replace($old, "new ${type}Validator().Validate(new ${type}(")
    }
    $model = [IO.File]::ReadAllText((Join-Path $PWD $b))
    $fixes = @{
        '.GetEntityTypes().Where(e => e.GetTableName() == "user_accounts"))' = '.GetEntityTypes(), e => e.GetTableName() == "user_accounts")'
        'entity.GetIndexes().Where(i => i.GetDatabaseName() == "ux_user_accounts_one_caregiver"))' = 'entity.GetIndexes(), i => i.GetDatabaseName() == "ux_user_accounts_one_caregiver")'
    }
    foreach ($old in $fixes.Keys) {
        if (!$model.Contains($old)) { throw "Unexpected model-test source." }
        $model = $model.Replace($old, $fixes[$old])
    }
    [IO.File]::WriteAllText((Join-Path $PWD $a), $text)
    [IO.File]::WriteAllText((Join-Path $PWD $b), $model)
    gitok diff --check
    gitok diff --stat

    dotnet build Sanad.slnx
    if ($LASTEXITCODE -ne 0) { throw "Build failed. Paste output; do not continue." }
    dotnet test Sanad.slnx --no-build
    if ($LASTEXITCODE -ne 0) { throw "Tests failed. Paste output; do not continue." }

    gitok add -- $a $b
    gitok -c user.name=Mossad -c user.email=mosad55522@gmail.com commit -m "fix(tests): disambiguate account validators and clean index assertions"
    gitok log -1 --format='%H%nAuthor: %an <%ae>%nCommitter: %cn <%ce>%nSubject: %s'
    gitok status --short --branch
}
```

## OWNER RULING AND TEST FAILURE RECONCILIATION — 2026-09-15
Owner prohibits scripts/commands for source-code editing going forward. Assistant edits directly and supplies complete source files for GUI replacement when authorized GitHub delivery is unavailable. Old scripted-edit blocks are historical and must not be reissued. Operational build/test/migration/merge/deploy gates remain separate.
Owner evidence: repaired solution build succeeded (39.8s); architecture tests passed; overall diagnostic run 1377 tests, 1367 succeeded, 10 failed, 0 skipped. Script stopped at test failure BEFORE add/commit. Inferred from supplied block: owner HEAD remains 87a00ad2f56292d6518bf9ba9523b115e9a8ecac with the two test edits uncommitted; do not claim a repair commit exists.
Failure audit and bounded manifest expansion:
1. Four hybrid persistence tests: IdentityTestDbContext explicitly ignores User.Accounts. Assistant selected the wrong test fixture. Correct AccountChoicesTests persistence theory to use production IdentityDbContext mapping with InMemory, retaining reload and exact account-type assertions. This tests mapping/persistence, not PostgreSQL race enforcement. Leave shared fixture unchanged to avoid collateral regressions.
2. One CMS metadata test: existing CmsDbContextModelTests queries check constraints from dbContext.Model. Correct only SupportContact singleton assertion to use GetService<IDesignTimeModel>().Model. Add Infrastructure using. This baseline test defect contradicts older recorded full-green claims; preserve old records as historical owner reports, but current evidence is failing.
3. Five AuthApiHostTests fail on Identity pending model changes during boot migration, before auth assertions. Full-suite diagnostic request before owner migration was a sequencing error. Do not suppress PendingModelChangesWarning, remove constraints, hand-write a migration, or declare auth tests passed. Factory config requests Development and startup calls Database.Migrate; logs show an actual PostgreSQL connection. Audit test-host DB isolation before future full-suite execution; no further full run on an unverified DB target.
Warnings tracked: UiLanguage default/sentinel configuration; Fluent Assertions commercial-license notice requires dependency/license review (not legal conclusion, not the cause of these test failures).
Delivery: Sanad_Slice-C_Corrected_Tests.zip contains exactly three complete source files at repository-relative paths: AccountChoicesTests.cs (includes previous explicit-type compile repair), AccountExclusivityModelTests.cs (includes previous warning repair), CmsDbContextModelTests.cs (new metadata fix). No scripts, migrations, binaries, or credentials. Replacing these three known files in the reported owner checkout preserves the rest of Slice C. No build/tests executed in sandbox; git diff --check passed. Do not rerun old patch or scripted repair.
Lessons: inspect test fixture mappings before asserting persisted navigations; use design-time EF metadata for constraints; distinguish pure tests from host tests with startup migration side effects. Code fixes must be delivered as code, not owner-run text-replacement scripts.
Next checkpoint: owner GUI-copies three files into D:\Sanad_API and reviews diff. Build and targeted DB-independent diagnostics precede further release work. Migration/host-isolation and full final gates are still pending. No PR, merge or deployment approval.

## OWNER RELEASE BUILD PASSED — CORRECTED TEST FILES
Owner reports replacing AccountChoicesTests.cs and CmsDbContextModelTests.cs on set-c-account-switch. The third archive file AccountExclusivityModelTests.cs already received its correction in the preceding successful scripted edit; no need to request redundant replacement absent contrary evidence.
Owner `dotnet-build` output: solution Release build succeeded in 77.1s, including Sanad.UnitTests. Alias/function definition is unknown; Release configuration is verified from bin/Release/net10.0 output. This is build evidence only, not new test/commit evidence. No repair commit SHA, push, PR, migration, final gates or deployment reported.
Next action is narrowly filtered, DB-independent diagnostics against the Release binaries just built; do not use default Debug --no-build and accidentally test older assemblies. Inspected selected test fixtures: account persistence uses production mapping over InMemory; CMS model uses InMemory/design-time metadata; exclusivity index uses Npgsql model metadata without a connection. Exclude AuthApiHostTests entirely at this checkpoint; their migration/isolation issue remains open, not waived.
Owner next command (test execution only; no source edits):
```powershell
dotnet test tests/Sanad.UnitTests/Sanad.UnitTests.csproj -c Release --no-build --filter "FullyQualifiedName~Sanad.UnitTests.Identity.Account.AccountChoicesTests|FullyQualifiedName~Sanad.UnitTests.Identity.Infrastructure.AccountExclusivityModelTests|FullyQualifiedName~Sanad.UnitTests.Cms.CmsDbContextModelTests"
```
Paste test summary or failures. Never treat this filtered diagnostic as the final full-suite gate. Lesson: match test configuration to the actual successful build, including when owner uses a custom build alias.

## REPEATED TEST OUTPUT — VERIFY SOURCE AND INVOCATION BEFORE RETRY
Owner latest run: 1377 total, 1367 passed, 10 failed, with architecture and AuthApiHostTests executed and restore/build output. This does not match the requested single-project --no-build filtered run. Exact executed command absent; do not assume user error or alias definition.
Rechecked delivered ZIP: corrected account persistence assertion at line 59, CMS design-time model access at line 278 and check-constraint assertion at line 299. Latest failure stack still references account assertion at line 52 and CMS check constraints at line 296 (old layout). Strong indication of source/artifact mismatch, not proof of its cause. Do not issue another speculative repair, full-suite run, clean/reset, migration or suppression.
Next request: upload the actual two files from D:\Sanad_API\tests\Sanad.UnitTests (Identity\Account\AccountChoicesTests.cs and Cms\CmsDbContextModelTests.cs) and paste the exact command that produced this output; if a custom shortcut, provide its definition via editor/UI or read-only inspection later. No source-edit scripts. Verify local source contents and test invocation before rerunning. Owner release build success alone did not verify transferred contents; acknowledge this verification gap.

## TWO-FILE REDELIVERY
Owner requested immediate regenerated files rather than further verification questions. Reissued current corrected AccountChoicesTests.cs and CmsDbContextModelTests.cs as individually downloadable files under Corrected_Files and a flat ZIP Sanad_Corrected_Two_Files.zip. Verified explicit validator types, production IdentityDbContext in persistence theory, and CMS design-time metadata access in delivered contents. No further code behavior changes or build/test execution. Prior runtime status remains unverified/failing; redelivery is not a passing gate.

## OWNER TEST PROGRESS — FIVE SOURCE-TEST FAILURES RESOLVED
Latest owner Release run: 1377 total, 1372 passed, 5 failed, 0 skipped. Prior four AccountChoices persistence failures and CMS singleton metadata failure no longer appear; remaining failures are exclusively AuthApiHostTests blocked at startup by Identity PendingModelChangesWarning. Compilation succeeded; overall test command failed. Do not call this final gate green or claim the five auth assertions passed.
Fresh sandbox origin fetch: main remains 76e4d946037ac44ccf6b14007a6b3d627bd8b090; only main advertised. No remote Slice C branch. Owner latest repair commit and working-tree state still unverified. No new implementation changes required by this output. Existing exclusivity index configuration still needs owner migration after approved owner merge; do not suppress model warning or handwrite migration. Test-host DB target/isolation still must be verified before any database-changing run.
Next one action: obtain read-only owner status/last commits and exact combined staged+unstaged diff of three repaired test files, then prepare bounded owner commit/publication step. Do not rerun the unchanged full suite. Sequence remains reviewed repair -> branch publication/PR -> explicit merge approval -> owner-authored merge -> migration/data preflight -> owner-generated migration -> complete final gates -> deploy verdict.
Read-only owner commands (PowerShell at D:\Sanad_API):
```powershell
git status --short --branch
git log -2 --format='%H %an <%ae> | %s'
git diff HEAD -- tests/Sanad.UnitTests/Identity/Account/AccountChoicesTests.cs tests/Sanad.UnitTests/Identity/Infrastructure/AccountExclusivityModelTests.cs tests/Sanad.UnitTests/Cms/CmsDbContextModelTests.cs
```
No source-edit commands. Lesson: distinguish five startup failures from five independent auth defects; acknowledge repaired tests and advance migration workflow rather than repeatedly replacing correct files.

## OWNER REPAIR DIFF VERIFIED — READY TO COMMIT/PUBLISH BRANCH
Owner HEAD verified 87a00ad2f56292d6518bf9ba9523b115e9a8ecac on set-c-account-switch. Exactly three unstaged test files; no other changes shown. Supplied diff matches sandbox corrected files: CMS blob ea9a3f5bf51aa38f6ef47018f8f514b5e75cbb8b, AccountChoices blob db847e498890483d81bb2b00d50a05d86bf7dcfc, exclusivity model blob ffdf6dad452cfa4ea897d3841e18f0454e49e323. Fresh remote main still 76e4d946037ac44ccf6b14007a6b3d627bd8b090. Static whitespace check passes.
Verdict: bounded test repair approved for owner commit and publication of implementation branch only. No merge approval inferred. Latest owner diagnostic evidence remains 1372/1377 passing, five startup failures from missing Identity migration. No source edits requested. Publish branch then remote audit/PR review artifact; owner migration only after explicit approved merge workflow. Test DB target still needs verification before migration/host full gate.
Current owner command — Git operations only:
```powershell
& {
    $ErrorActionPreference = 'Stop'
    function gitok {
        & git @args
        if ($LASTEXITCODE -ne 0) { throw "Git failed. Stop and paste the output." }
    }
    Set-Location 'D:\Sanad_API'
    if ((gitok branch --show-current) -ne 'set-c-account-switch') { throw "Wrong branch." }
    if ((gitok rev-parse HEAD) -ne '87a00ad2f56292d6518bf9ba9523b115e9a8ecac') { throw "HEAD changed." }

    $files = @(
        'tests/Sanad.UnitTests/Cms/CmsDbContextModelTests.cs'
        'tests/Sanad.UnitTests/Identity/Account/AccountChoicesTests.cs'
        'tests/Sanad.UnitTests/Identity/Infrastructure/AccountExclusivityModelTests.cs'
    )
    if (Compare-Object ($files | Sort-Object) (@(gitok diff HEAD --name-only) | Sort-Object)) {
        throw "Changed-file list differs from the reviewed repair."
    }
    if (gitok ls-files --others --exclude-standard) { throw "Untracked files found. Stop." }
    gitok diff --check
    gitok fetch origin --prune
    if ((gitok rev-parse origin/main) -ne '76e4d946037ac44ccf6b14007a6b3d627bd8b090') {
        throw "Remote main changed. Stop."
    }
    if (gitok ls-remote --heads origin refs/heads/set-c-account-switch) {
        throw "Remote branch already exists. Stop for reconciliation."
    }

    gitok add -- @files
    gitok -c user.name=Mossad -c user.email=mosad55522@gmail.com commit -m "fix(tests): correct account persistence fixture and EF metadata assertions"
    $identity = gitok log -1 --format='%an <%ae>|%cn <%ce>'
    if ($identity -ne 'Mossad <mosad55522@gmail.com>|Mossad <mosad55522@gmail.com>') {
        throw "Commit identity mismatch. Do not push."
    }
    if (gitok status --porcelain) { throw "Working tree is not clean. Do not push." }
    gitok push -u origin HEAD:refs/heads/set-c-account-switch
    gitok log -1 --format='%H%nAuthor: %an <%ae>%nCommitter: %cn <%ce>%nTree: %T%nParent: %P%nSubject: %s'
    gitok status --short --branch
}
```

## B1-B2 SCOPE LOCKED AND IMPLEMENTATION PROMPT ISSUED (FAMILY CANCELLATION ACTIVATION)
Post-B1-B1 cleanup verified live: remote advertises ONLY main @ 747946fc3abc; PR19 merged-by-ff, PR20 closed unmerged (superseded); working state pristine. Current-code audit against the pin completed before scoping: CancelBookingCommand/Handler in BookingCommands.cs gates on family membership only (Viewer can cancel today), calls booking.CancelByFamily then attempts an unconditional full refund on the first succeeded payment tx, gateway failure swallowed, no category/policy/fact. IFamiliesDbContext deliberately carries NO BookingCancellationFacts DbSet (B1-B1 decision); concrete FamiliesDbContext maps it; DI forwards the interface from the same scoped concrete instance, so a recorder on the concrete context shares one change tracker and one SaveChangesAsync. Conventions verified for reuse: family.GetRole(userId) role gate + Bookings.UnauthorizedRole -> 403 (checkout handler), Bookings.FamilyNotFound/NotFound/BookingNotInFamily -> 404, Bookings.Domain.InvalidOperation -> 409, unknown codes default to 400; controller DTO CancelBookingRequest lives inside FamilyBookingsController.cs; cancel action at HttpPost("{bookingId:guid}/cancel"); domain CancelByFamily status-guards P/PCA/Confirmed.
Deliverable issued: /home/user/B1-booking-cancellation/prompt-b1b-implementation.txt (worker session B1-B2, code-only, branch codex/b1-b2-family-cancel-activation off main @747946fc). Scope: DTO/command gain int? ReasonCategory + nullable Reason; role gate to Owner/Editor; mandatory category+note for Confirmed, optional note pre-acceptance and category rejected 400 pre-acceptance; BookingCancellationPolicyInput.FromBooking(Family, Cancel) + Decide BEFORE any mutation; refund attempted ONLY on FullCapturedRefund (existing adapter block verbatim), NoRefundDue performs no provider call; fact recorded exactly once via new IBookingCancellationFactRecorder (+ concrete-context impl + DI registration) inside the single SaveChangesAsync; unique-index race (ux_booking_cancellation_facts_booking) mapped to Bookings.Cancel.AlreadyProcessed -> 409 (one new mapper key, the only mapper change); NO EF migration; legacy refund-import paths untouched; handler harness fixes mechanical-only if compile breaks. Excluded: caregiver cancel/decline (B1-B3), admin/read-model/refund-state changes (B1-C), notifications/expiry/reconciliation/claim persistence (later hardening slices), tests + Bruno (gate work after production pin).
Hygiene: deleted dead files snapshot-before.cs, Sanad.API.csproj, BookingCancellationFactMappingTests.cs (all superseded by repo @747946fc); phase folder now holds only the three prompt artifacts (two historical B1-B1 prompts retained as history).
Known accepted interim state recorded in the prompt: until B1-C closes read-model/admin alignment, a policy-denied (NoRefundDue) cancellation still shows legacy FAILED-style refund derivation and admin retry is ungated — acceptable ONLY because nothing deploys mid-chain. STANDING: no deployment until B1-C closure; deployed VPS baseline remains f787a5c2. Next: owner hands the prompt to the implementation worker; on return, coordinator runs pre-merge review, then B1-B2 test worker, then negative-first Bruno gate with live-derived family count.

## B1-B2 MERGED TO MAIN — NEW STANDING RULE ON OWNER MECHANICAL FIXES
PR21 (family cancellation activation, B1-B2) merged ff-only to main @ c651e33bdc5ddf834dce0fdbf3534dfd20ae1a24 (merge commit == branch head; PR21 closed, merged=True by reachability); bot branch deleted remotely + locally; remote again advertises ONLY main. Chain = a214ec7f (feature, canonical identity incl. bot co-author trailer) + c651e33b (coordinator-owned fix commit, canonical). Review verdict was APPROVED before gate: DTO/command ReasonCategory int? + nullable Reason; Owner/Editor role gate reusing Bookings.UnauthorizedRole (403); mandatory category+note for Confirmed (three 400 codes), pre-acceptance category rejected 400; policy Decide strictly before any mutation; refund only on FullCapturedRefund entitlement (NoRefundDue = no provider call); fact recorded exactly once via new IBookingCancellationFactRecorder seam (concrete FamiliesDbContext, tracked-only); single SaveChangesAsync atomic commit; exactly one mapper key (Bookings.Cancel.AlreadyProcessed=409); mechanical harness stub in BookingRemediationTests; zero domain/migration/admin changes.
REVIEW LESSON: the bot PR claimed a clean build but carried TWO latent compile errors caught only by the owner gate — UnreachableException missing System.Diagnostics qualification and DependencyInjection missing the recorder-namespace using. Owner gates remain the authoritative build evidence; never accept a bot's green claim. The Workspace-delivered complete file went through a VS save that reflowed switch-case indentation (15-line whitespace churn) — sanctioned as zero-semantic; a whitespace-collapsing NetDiff guard replaced byte-hash pinning thereafter.
NEW STANDING RULE (owner-locked, update worker prompts accordingly): owner may fix mechanical build-blockers in flight WITHOUT waiting for coordinator — missing usings/namespace qualifications, pre-approved nullable tokens, ctor-arg alignment after signature changes — provided zero logic/policy/threshold/error-code/query choices are involved and the fix is reported in the same reply. Logic-adjacent changes still stop-and-report. Editor whitespace reflows on save are tolerated; review unit is the net semantic diff.
Next: B1-B2 independent test worker (prompt-b1b2-tests.txt delivered; pins main @ c651e33b; N1-N10 negative-first incl. race-to-409 mapping, P1-P7 refund-window/fact atomicity), then owner suite gate (must be 57/57 cancellation suites + BookingRemediationTests + new handler tests green), then negative-first Bruno API gate with live-derived family count, then B1-B2 closure + B1-B3 scoping. No-deploy rule stands until B1-C closes.

## B1-B2 TESTS MERGED — PRODUCTION+TEST CHAIN COMPLETE ON MAIN @2a690063; BRUNO GATE STAGED
PR22 (independent CancelBookingCommandHandler contract tests) reviewed and merged ff-only: main @ 2a6900638a7800d2e22b388877d6139c51054238, PR22 closed merged=True, branches deleted, single-branch remote. Identity course-corrected before merge: bot authored with the GitHub noreply email; owner applied an identity-only amend (tree-verified bit-identical) + force-with-lease, then a second mechanical amend in-script replaced 9 xUnit2013 analyzer sites (Assert.Equal(1,x.Count) -> Assert.Single/Linq forms) with hard count guards (6/1/1/1). Owner gate evidence: build + filtered suite 187/187 green (all BookingCancellation suites + BookingRemediationTests + new handler tests), zero xUnit2013 warnings post-sweep. Test-worker deviations adjudicated CORRECT-vs-prompt-prose (logged): grace-window axis is ConfirmedOnUtc (B1-A locked; P3/P4 prompt prose wrongly implied scheduled slot), paid PendingCaregiverApproval cancel legitimately refunds full (P1 prompt prose wrong), FamiliesDbContext is sealed so N10 uses IFamiliesDbContext wrapper with production recorder, P1b no-provider-reference latent gap logged unreachable-from-webhooks, N10b control proves filtered-catch precision (FK message on same table propagates).
Bruno negative-first gate authored and staged in workspace (B1-booking-cancellation/bruno/family-cancel-booking/, 12 requests + runbook): 401 unauthenticated, 404 unknown booking, 403 viewer role (data-independent: gate precedes booking lookup), 400 pre-acceptance category, 200 happy unpaid-pending cancel with fact-row DB verification, 409 re-cancel, optional 400 confirmed-no-category, logouts. Live happy-path constrained to UNPAID PendingPayment bookings to avoid sandbox gateway refunds (paid window paths are unit-proven). Next: owner runs gate -> paste Bru summary + 07 console + fact SQL before/after -> guarded direct commit of the collection to main (migration precedent) -> B1-B2 closure -> B1-B3 scoping. No-deploy rule stands until B1-C.

## B1-B2 BRUNO GATE GREEN — COLLECTED LIVE EVIDENCE
Gate execution history (3 runs, union adjudicated GREEN): first run hit a Bruno environment TLS block (self-signed dev cert; resolved via runner flag), then produced: pure negatives all green (401 unauth / 404 unknown+code / 403 viewer+code Bookings.UnauthorizedRole), step 08 400 Bookings.Cancel.ReasonCategoryNotAllowedPreAcceptance (live), step 09 semantic SUCCESS with HTTP 204 (coordinator assert guessed 200; ApiControllerBase maps bare Result.Success to NoContent — file corrected), step 10 409 Bookings.Domain.InvalidOperation proving persistence+idempotency. Re-runs: pending seed consumed (expected; gate is one-shot by design — 07 now prefers unpaid PendingPayment targets and prints a loud NOTE on empty seeds), steps 08-10 framework-404 on empty id (documented as no-seed signal, not contract failure), step 11 proven live against a real Confirmed booking: 400 Bookings.Cancel.ReasonCategoryRequired with booking untouched. DB evidence: exactly one new families.booking_cancellation_facts row with actor_side=1 (Family), action=1 (Cancel), reason_category NULL — matches the unpaid-pending happy cancel. Coordinator errors logged: 200-vs-204 assert, pending-target auto-pick needed unpaid preference; both fixed in-file. Next: guarded direct commit of the 12-file collection to main (batch issued), then B1-B2 formal closure and B1-B3 scoping (caregiver confirmed-cancel endpoint + reject-path facts). No-deploy rule stands until B1-C.

## B1-B2 FORMALLY CLOSED; B1-B3 SCOPED AND PROMPT ISSUED
Bruno gate collection committed direct to main @ 4c4fd09a434b1d7ff310532fb8563c66349a117a (12 files, canonical identity, exact-set guard; owner filename variant 03-cancel-unknown-bookings.bru accepted and mirrored). B1-B2 is fully closed: production c651e33b + independent tests 2a690063 (187/187 gate) + live negative-first gate adjudicated green (union of runs; fact-row SQL evidence actor_side=1/action=1/category NULL) + tooling landed. Standing understanding logged: steps 08-10 report 404 on re-runs when no unpaid pending seed exists = no-seed signal, NOT failures; gate is one-consumable by design.
B1-B3 scoped from a completed audit @4c4fd09a and prompt issued (B1-booking-cancellation/prompt-b1b3-implementation.txt): new POST api/v1/caregiver/bookings/{id}/cancel endpoint (CaregiverCancelBookingRequest reason+int? category; 204 via ToActionResult), caregiver-confirmed cancel = category + note mandatory + policy-first + FullCapturedRefund adapter attempt + one incident-carrying fact via existing recorder seam + 409 race mapping; shared CancellationPersistenceGuard.IsFactUniqueViolation extracted (family handler pure-move swap — the only edit to B1-B2 code); decline/REJECT path gains fact recording with MINIMAL surgery preserving legacy TransitionFailed semantics (ActorUserId added to decline command + controller pass; race catch ordered before the generic inner catch). No mapper/EF/DI/migration changes; no notifications/incident aggregation/admin work; tests + caregiver Bruno gate follow the same rhythm after the production pin. No-deploy rule stands until B1-C.
