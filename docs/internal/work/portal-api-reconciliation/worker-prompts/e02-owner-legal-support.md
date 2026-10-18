# E02 whole-phase Worker brief — Care Home Owner legal support

## Pinned baseline and local delta

- Repository: `D:\Sanad_API`, branch `main`.
- Pinned `HEAD`: `cae46032fc5448592a2a324122bedd41503e7e1f`.
- The working tree already contains unrelated Owner changes. Preserve them;
  do not stage, alter, clean, or overwrite them:
  - modified `.codex/agents/sanad_documenter.toml`,
    `.codex/agents/sanad_implementer.toml`, `.codex/agents/sanad_reviewer.toml`,
    `.codex/agents/sanad_scout.toml`, `.codex/agents/sanad_test_author.toml`,
    `.codex/config.toml`, `README.md`,
    `docs/internal/Project_Main_Goal.md`,
    `docs/internal/governance/AGENTS.md`,
    `docs/internal/operations/codex-workflow.md`,
    `tools/opencode_bridge/opencode_bridge.py`;
  - untracked `.opencode-audit-tmp/` and `.opencode/`.
- The existing `.opencode/agents/sanad-worker.md` profile is configured as
  `opencode/beam-free#high`. Preserve all profile content and unrelated files.
- Do not commit, push, package, publish, deploy, start a database/API target, or
  begin any phase other than E02.

## Required reading

Read and follow the complete repository `AGENTS.md`,
`docs/internal/governance/AGENTS.md`, current
`docs/internal/Mastermind_Handoff.md`, this approved contract, and this entire
brief. Read only E02's linked active-slice materials and the source files needed
to execute the approved manifest. The E02 contract is
`docs/internal/work/portal-api-reconciliation/contracts/e02-owner-legal-support.md`.

The Owner approved that exact contract on 2026-10-18. Do not reopen settled
decisions, infer policy, or expand scope. Stop and report `BLOCKED` if a
contract ambiguity or required file outside the manifest prevents safe work.

## Objective

Implement the approved E02 contract end-to-end: allow CareHomeOwner claim-based
reads of separately authored/published Owner Privacy Policy and Terms content
through the existing legal routes and CMS, while preserving all existing
audiences, authorization, publication semantics, and Help Center boundaries.

## Non-negotiable contract behavior

- Add numeric `LegalAudience.CareHomeOwner = 5` for legal content only.
- Legal audience selection comes from the authenticated active account-type
  claim. No query/body/header audience override.
- Owner gets only the current Published document for the matching type and Owner
  audience. No sharing, cross-audience or cross-document fallback, or invented
  static text. Missing Owner publication returns `404 Cms.Legal.NotPublished`.
- Preserve current routes, response DTO/shape, numeric enum serialization,
  bilingual section rules, Admin CMS permissions, and Draft → Published →
  Archived history/version behavior.
- The shared LegalAudience type is also used by Help Center/FAQ. Keep Owner
  Help Center reads unsupported and do not permit Owner FAQ authoring. Preserve
  all four current Help audiences and add regression coverage.
- No personal ID, Identity document, account deletion, facility document,
  frontend, database/schema, seed, provider, Production request, or other-phase
  work. No migration is expected; stop for an Owner contract amendment if one
  proves necessary.
- Do not author, invent, seed, or copy legal content. CMS examples must not
  contain realistic legal wording.

## Owned files

Change only the following approved manifest:

- `src/Modules/Cms/Domain/Sanad.Modules.Cms.Domain/Legal/LegalAudience.cs`
- `src/Modules/Cms/Domain/Sanad.Modules.Cms.Domain/Legal/LegalDocument.cs`
- `src/Modules/Cms/Domain/Sanad.Modules.Cms.Domain/Help/HelpFaq.cs`
- `src/Modules/Cms/Application/Sanad.Modules.Cms.Application/Legal/CreateLegalDocumentCommandHandler.cs`
- `src/Modules/Cms/Application/Sanad.Modules.Cms.Application/Legal/GetPublishedLegalDocumentQueryHandler.cs`
- `src/Modules/Cms/Application/Sanad.Modules.Cms.Application/Help/GetHelpCenterQueryHandler.cs`
- `src/API/Sanad.API/Controllers/ApiControllerBase.cs`
- `src/API/Sanad.API/Controllers/LegalController.cs`
- `tests/Sanad.UnitTests/Cms/LegalDocumentTests.cs`
- `tests/Sanad.UnitTests/Cms/HelpCenterTests.cs`
- `tests/Sanad.UnitTests/API/PortalRemainingResponseContractTests.cs`
- `tests/Sanad.UnitTests/API/OwnerLegalAudienceApiTests.cs` (new, if needed)
- `docs/admin/legal-content.md`
- `docs/app/settings/legal.md`
- `docs/postman/admins/Sanad.Admin.postman_collection.json`
- `docs/postman/care-homes/Sanad.CareHomes.Owner.postman_collection.json`
- `docs/internal/work/portal-api-reconciliation/reports/e02-owner-legal-support-delivery.md` (new)

The Mastermind owns the contract, this brief, `plan.md`, and
`Mastermind_Handoff.md`. If another source/test/doc file must change, stop and
report its exact path/reason; do not edit it without a bounded Mastermind
correction instruction.

## Required coverage and delivery

Cover both Owner legal routes, claim-derived audience, absence of client
audience selection, independently published Owner versions, no fallback when
other audiences have publications, expected `404 Cms.Legal.NotPublished`, all
existing legal audiences, CMS roles, and unchanged Help Center restrictions.
Verify the generated OpenAPI enum/response surface. Update both legal guides and
the two listed Postman collections with accurate numeric mappings and
permissions; validate JSON and route mapping.

Run one strict Release solution build after the final code change and focused
Legal/Help/API/OpenAPI tests. Also run Postman JSON/route validation and
`git diff --check`. Full suite and Bruno are not required. Use only isolated
synthetic/in-memory tests; no shared target, persistent fixture, migration,
Production request, or API startup.

Record exact commands and results, test counts/skips, build warnings/errors,
Postman/diff results, changed files, and limitations in the delivery report.
Do not claim unrun checks passed. Preserve all unrelated edits. Do not commit,
push, publish, deploy, or continue to another phase. Return one concise final
report with status (`COMPLETE` or `BLOCKED`), evidence, findings, and any
decision needed, then stop.
