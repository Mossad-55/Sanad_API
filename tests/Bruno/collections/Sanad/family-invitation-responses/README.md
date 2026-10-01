# Family invitation response requests

These requests cover successful accept and decline responses without persisting a one-time invitation token. The Development sender prints the token only to the local guarded API console; no external email is sent.

For acceptance, first create a pending invitation for `caregiverEmail` using the existing invitation collection's owner login and create request. Copy the token from the local `[DevEmail]` output, then run the invitee family login and accept request with `--env-var familyInvitationToken=<token>`.

For decline, run the Viewer invitation creation request, copy its token from the same local console, then run the Viewer login and decline request with the same environment override. Do not save tokens in Bruno files, environment files, reports, or source control. Run only against the guarded Development `SanadBrunoTestDb`; reset that disposable database after these state-changing cases.
