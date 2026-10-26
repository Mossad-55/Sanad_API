# D1 work order — demo corpus tooling and disposable dry run

Owner-authorized phase; Mastermind dispatch note. Scope authority is
[full-demo-corpus-2026-10-25.md](full-demo-corpus-2026-10-25.md) (inventory,
consistency rules, boundaries, D1–D4). This file only pins how D1 is executed.

## Objective

Extend `tools/Sanad.PortalProvisioner` so the approved demo corpus can be
seeded additively into a verified target, and prove it end to end on a
throwaway nonce database. D1 writes **no** real target.

## Required tooling

1. **Corpus groups** — implement all 22 inventory groups with the counts in the
   brief (hybrid depth: several rows per list/state, one per rare state) under
   run tag `demo1` and marker prefix `demo`:
   `identity-accounts`, `families-people`, `families-subscriptions`,
   `caregivers-profiles`, `caregiver-ratings`, `care-home-facilities`,
   `care-home-inventory`, `care-home-visits`, `care-home-bookings`,
   `caregiver-bookings`, `finance-platform`, `finance-claims`,
   `finance-complaints`, `chat`, `community`, `cms-library`, `cms-wellness`,
   `cms-faq`, `cms-landing`, `cms-legal`, `cms-support`, `notifications`.
2. **Additive and idempotent** — marker-keyed upsert on rerun; never update or
   delete a row the corpus did not create; reuse existing singletons (plan
   versions, site settings, support contact, legal documents, landing sections)
   and report a skip instead of overwriting; one current subscription per
   family; monotonic booking chronology; every seeded list row owned by a
   seeded account; notifications addressed only to demo accounts.
3. **Demo markers** — every generated row carries the demo marker/run tag where
   the schema allows; legal documents carry `DEMO — not legal text` in both
   title and body and are `Public`; media fields point at the single demo pair.
4. **Media generator** — generate (never stock) one JPEG with a visible
   `SANAD DEMO` mark and one short demo MP4, then reuse both everywhere.
   Owner rule: media follows the same workflow as a user upload. The provisioner
   writes the bytes through the API's own storage implementation
   (`LocalDiskFileStorage` / `IFileStorage`) into the target's configured upload
   root using the same folder conventions as the real upload endpoints, and the
   seeded rows store the returned key. Public image fields use `SaveAsync` and
   resolve at `/files/<key>`; `video/mp4` is accepted only by
   `SavePrivateAsync`, so the MP4 is referenced only by private/video-capable
   fields and resolved through the authorized media route. No fabricated keys,
   no database blobs, no new route, no bypass. A seeded reference whose file is
   missing or unopenable fails the run, and the plan prints each resolved key.
   No encoder is installed (`ffmpeg` absent). Preferred approach: a separate
   `docs/internal/tools/New-DemoMedia.ps1` that draws the image locally and
   renders the MP4 from that still through Windows' built-in media pipeline
   (WinRT `MediaClip.CreateFromImageFile` + `MediaComposition`), keeping the
   provisioner itself cross-platform. If the platform path genuinely cannot
   produce a playable MP4, stop and report that single blocker with evidence;
   do not substitute an invalid placeholder or third-party asset. Without
   burned-in video text, the documented labelling (filename, title,
   description, DEMO poster) stands.
5. **Target guard** — add exactly two narrowly scoped kinds on top of the
   existing `rehearsal` guard:
   - `demo-nonce`: loopback accepted only when the database name matches a
     generated `SanadDemoNonce_<timestamp>` pattern and the local cluster
     identity is verified; used only by D1.
   - `local-demo` + `--allow-local-sanad-db`: the single recorded override for
     the local `SanadDb`, accepted only with tag `demo1`, cluster-identity
     check, and a printed warning in the plan; never applies to the report
     fixture groups.
   Production stays refused in every path; `--tunnel-declared` rules for the
   rehearsal target are unchanged.
6. **Report-item coverage** — the corpus must produce the states the mobile
   report needs: named and anonymous posts, at least one post with recorded
   deduplicated views, comments owned by the caller / by others / liked, plus
   caregiver availability that yields open, booked-out and unavailable slots
   for the seeded caregivers.

## D1 execution

1. Create a throwaway `SanadDemoNonce_<timestamp>` database on the local
   server; apply the current migrations to it; record pre-run counts.
2. Run `plan`, review the printed plan, `provision`, then `provision` again.
3. Prove: second run changes zero row counts/identities; the run-wide invariant
   is clean; pre-existing rows are untouched; media resolves; the demo marker
   and legal marker are present; the new UI states are queryable.
4. Drop only the nonce database afterwards (it is the disposable target), and
   record that no real target was written.

## Checks

Strict Release build `-warnaserror` (0/0), provisioner test suite green with new
facts for the guard kinds, idempotency, singleton reuse and the media
generator's output presence, `git diff --check` clean, and a written D1 result
in the brief. No commit, push, deploy, remote/VPS access, provider call, clock
change, or write to `SanadDb`, the rehearsal database, or any shared target.

## Handback

Changed files, commands and exact results, group/count matrix actually seeded
on the nonce database, media file paths and byte sizes, invariant output,
limitations (including the media-encoder outcome), and what D2/D3 will need.
