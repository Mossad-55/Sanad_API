# Docs separation & hardening — task checklist

Statuses: Not started, In progress, Blocked, Done. Plan §0 (the constitution)
is frozen; changing it needs a new explicit owner decision.

- [ ] Not started — **Phase 0.** Inventory confirmed (consumer map + internal map in plan §0). Owns: nothing (scout only).
- [ ] Not started — **Phase 1. Moves.** Execute plan §1 table via `git mv` + reference updates + link check. Owns: moved files + referencing lines only.
- [ ] Not started — **Phase 2. Conflict resolutions.** Plan §2 table: banners, cross-links, verbatim-dupe merges only. Owns: listed files only.
- [ ] Not started — **Phase 3. Detailing pass.** Bring every `app/`+`admin/`+`auth/` file to the plan §3 bar, priority order auth → money → care-homes → chat → rest. Ambiguity stops and returns to owner. Owns: guide files + their Postman examples only.
- [ ] Not started — **Phase 4. README indexes.** `docs/app/README.md` + `docs/admin/README.md` (new), verify `docs/README.md` + `docs/postman/README.md` links. Owns: index files only.
- [ ] Not started — **Phase 5. Gates + closeout.** Contract-map 0/0, Postman parse, link check, full unit + arch once, handoff note. Owns: evidence only.

Done criteria per phase: `git diff --check` clean, links resolve, no consumer file loses documented behavior, branch + SHA reported, commit per phase locally, no push/merge/deploy.
