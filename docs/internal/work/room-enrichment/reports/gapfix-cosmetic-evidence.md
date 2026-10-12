# Gapfix report 1 — cosmetic evidence (docs-only)

- Branch: `opencode/room-enrichment-gapfix`
- Base: **`4e7f956`** (origin/main)
- Branch base: `88a4777` (room-enrichment tip)
- Work commit: **`a6374c8`** — *Correct Phase 1 report: Bruno baseline 135, refresh-path evidence clarified*
- Final SHA for this item: **`a6374c8`** (no further changes to owned files)

## Changes (reports/*.md only, no code diff)

`docs/internal/work/room-enrichment/reports/phase-1-types.md`:

1. **Bruno baseline corrected:** "missing Bruno = 136 = baseline 134" →
   "missing Bruno = 137 = baseline **135** at `4e7f956`", with an inline note
   that the original 134/136 figures were a miscount and the Phase 2 report
   already documents the re-measurement (measured runs: origin/main = 135,
   `e253716` = 137 = 135 + 2, `90ca26f` = 139 = 135 + 4).
2. **Refresh evidence clarified as two distinct paths:** the Phase 1 gate's
   refresh-after-**logout** result (generic **400** validation failure — the
   session row is already deleted) is now explicitly distinguished from the
   Phase 2 gate's refresh-after-**revoke** result (**401
   `Identity.Refresh.SessionRevoked`** — the session exists but is revoked).
   Both block refresh; the status/code differ by path. Phase 2's report was
   already accurate and was left unchanged.
3. Per scope item 1c: the legacy HC-023 Postman requests without descriptions
   (`Read my inventory`, `Create bed`, `Rename bed`) were **left untouched**
   (out of manifest scope) — noted here and in `gapfix-manifest-hygiene.md`.

## Gates

| Gate | Command | Result |
|---|---|---|
| Whitespace | `git diff --check` | exit 0 |
| Diff scope | `git status --porcelain` | only `reports/phase-1-types.md` (no code diff) |

## Limitations

- Docs-only change; no build/test re-run was required beyond the always-on
  gates recorded in the slice-final commit (build + contract-map run there).
