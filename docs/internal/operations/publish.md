# Publish trigger (`publish`)

## Trigger and scope

`publish` means create a package and report its provenance. It does not authorize
deployment, production access, migration application, or secret exposure. An
explicit owner order to deploy an accepted phase includes preparing its fresh
package; do not ask the owner to repeat `publish`. Deployment follows the
separate [deploy runbook](deploy-runbook.md).

## Preconditions

1. The phase is accepted and its required verification evidence is recorded.
   Evidence must cover the final source tree and relevant dependencies,
   toolchain, command/configuration, and stateful target/fixtures. If a source or
   other relevant input changed, rerun only the checks that change invalidated.
   Do not repeat successful tests because packaging follows them.
2. The working tree is clean on `main`, and `main` equals `origin/main`.
3. The package revision is the pushed `main` HEAD. Do not package a dirty or
   unpushed revision.

## Package

Run the publisher once:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\docs\internal\tools\Publish-AhlenaApi.ps1 -Configuration Release
```

The publisher compiles the API as part of `dotnet publish`; do not run a second
solution build just for packaging. It derives the latest migration source heads
for all nine contexts and writes `ahlena-deployment-manifest.json`. It also
refuses to publish if `appsettings.Development.json` appears in the output.
The output directory is `deploy/publish-out/` (git-ignored and cleared for each
run so files from an older package cannot leak into the new one). Packaging
does not run tests.

Confirm once that:

- the manifest `revision` equals pushed `main` HEAD;
- the manifest contains all nine contexts (Identity, Cms, Caregivers, Families,
  CareHomes, Notifications, Community, Finance, Chat);
- the output has no `.env`, local settings, keys, certificates, or other secrets.

The manifest migration heads are source-derived by the publisher. Do not repeat
an independent source-folder comparison in the deployment preflight. Deployment
must still compare these required heads with the *actually applied remote*
heads; that is runtime evidence, not a duplicate source check.

## Report and stop

Report the revision, package path, manifest revision match, and links to the
already-recorded gate evidence. A package-only request stops here. It does not
authorize transfer, service restart, or deployment.

## Hard boundaries

- Never copy `appsettings.Development.json`, seed settings, `.env`, or secrets
  into or alongside the package.
- Do not run test suites, route mapping, or `git diff --check` as part of
  packaging. Those checks belong to source acceptance and are conditional on the
  phase contract; reuse their results while inputs remain unchanged.
- Never launch the package against production from the build machine. Never
  push, tag, or deploy under a package-only request.
