# Families (mobile app)

Family routes live under `/api/v1/family/...`. They drive the family-side app: family bootstrap, elderly dependents (whose logins are provisioned server-side), family-member invitations, caregiver discovery, and the booking lifecycle (checkout → pay → caregiver response → care → completion).

## Access

All routes require a **Normal JWT** for a **Family** account:

- Authenticated user
- Claim `access_type` = `Normal`
- Claim `account_type` = `Family` (`1`)

Policy: `FamilyAccess`.

A Restricted-verification token (before email/phone verification) and every non-Family account (Medical/Companion caregiver, Elderly, admin) receive **403** on these routes.

## Concepts

### Family

A family is bootstrapped once per user. The bootstrapping user is the **Owner** and becomes the first `FamilyMember`. Family names default to `"My Family"` when none is supplied.

### Family roles

Every member has one role, resolved from the family's member list (the owner is always `Owner`):

| Value | Role | Can do |
|---|---|---|
| `1` | Owner | Everything: rename, manage dependents, view, invite, revoke invitations |
| `2` | Editor | Manage elderly dependents (add/update/remove/photo), view the family and dependents, create invitations |
| `3` | Viewer | Read-only: view the family, dependents, and dependent photos |

The owner can never be removed or created by invitation; invited members are only ever Editors or Viewers. Role enforcement errors:

- `403 Families.Family.NotOwner` — rename, revoke invitation (owner-only actions).
- `403 Families.Family.AccessDenied` / `Families.Elderly.AccessDenied` / `Families.Invitation.AccessDenied` — the acting user's role does not permit the action (e.g. a Viewer trying to add a dependent, a Viewer inviting).
- `403 Bookings.UnauthorizedRole` — a Viewer attempting booking checkout (see `docs/app/families/bookings.md`).

### Elderly dependents vs. family members

- **Dependent** = an elderly person the family cares for. Stored in the Families module (`families.elderlies`) and linked to an **Elderly Identity account** created on the family's behalf.
- **Family member** = another **Family-account user** invited by email to share management of the family.
- The two never mix: elderly accounts are phone-only (no email, no password) and log in with SMS OTP (see `docs/auth/elderly-sms-login.md`); members log in with email/password and accept invitations from the app.

## One elderly → one family

An elderly Identity account (identified by phone) can be linked to **at most one family**:

- Adding a dependent whose phone already has an **elderly** identity linked to another family → `409 Families.Elderly.PhoneLinkedToAnotherFamily`.
- Adding a dependent whose phone belongs to a **non-elderly** account (family/caregiver/admin) → `409 Families.Elderly.PhoneBelongsToNonElderly`.
- An elderly identity that exists but is not currently linked (e.g. the dependent was previously removed) is **re-linked** — no new identity is created.

Removing a dependent **hard-deletes** the Families row and the photo file. The Elderly Identity user **remains** (it can still log in by SMS OTP and can be re-added by this or another family via phone). There is no explicit transfer endpoint in v1; remove + re-add performs a transfer.

## Document map

| Screen / flow | Doc |
|---|---|
| Bootstrap, get family, rename | `docs/app/families/family.md` |
| Care-needs assessment quiz (Step 1) | `docs/app/families/assessment.md` |
| Add/list/get/update/remove dependents, photos | `docs/app/families/dependents.md` |
| Dependent medical profile (chronic, allergies, history) | `docs/app/families/medical-profile.md` |
| Elderly medications, stock inventory & daily dose schedule | `docs/app/families/medications.md` |
| Care notes, observations & activity access timeline | `docs/app/families/notes-and-activities.md` |
| Invitations by email, deep link, accept/decline/revoke | `docs/app/families/invitations.md` |
| Caregiver discovery: search, public profile, price quote | `docs/app/families/discovery.md` |
| Bookings: checkout, tabs, detail, cancel | `docs/app/families/bookings.md` |
| Postman | `docs/postman/app/Sanad.App.Family.postman_collection.json` |

The Elderly SMS OTP login used by dependents is documented in `docs/auth/elderly-sms-login.md`.

## Conventions

- JSON bodies except dependent creation and photo uploads (`multipart/form-data`).
- Enum fields are sent as numbers:

  | Field | Values |
  |---|---|
  | `gender` | `1` Male, `2` Female |
  | family member `role` (invitations) | `1` Owner, `2` Editor, `3` Viewer (only 2/3 allowed on invite) |
  | `relationshipType` | `1` Father, `2` Mother, `3` Son, `4` Daughter, `5` Brother, `6` Sister, `7` Grandfather, `8` Grandmother, `9` Grandson, `10` Granddaughter, `11` Uncle, `12` Aunt, `13` Nephew, `14` Niece, `15` Spouse, `99` Other |
  | invitation `status` (responses) | `1` Pending, `2` Accepted, `3` Declined, `4` Revoked, `5` Expired |

- Dates are `YYYY-MM-DD`; date of birth cannot be in the future.
- Phone numbers are **E.164** (e.g. `+201001234567`).
- The user id always comes from the JWT `sub`, never from the request body.
- Errors are `application/problem+json` with a `code`; see `docs/auth/errors.md` and the error catalog in each doc.
