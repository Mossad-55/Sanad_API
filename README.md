# Sanad Care API

Sanad Care (سند) is a bilingual Arabic/English healthcare and caregiving platform. This repository is the .NET 10 backend.

The active development branch is `develop`.

## Current status

The Caregivers Domain and the non-social Authentication vertical slice are implemented.

Implemented Auth HTTP surface:

- Family / Medical Caregiver / Companion Caregiver registration
- Dual-channel email and SMS OTP verification and resend
- Email/password login with normal or restricted access
- Elderly phone + SMS OTP login
- Refresh-token rotation and reuse detection
- Session list, current logout, logout-all, and owned-session revoke
- Password reset and authenticated password change

Email and SMS delivery:

- Provider-neutral SMTP adapter (MailKit)
- SMS Misr adapter
- If SMTP or SMS Misr is not configured, the host keeps the development no-op senders
- SMS Misr with username, password, and sender but no template uses `POST /api/SMS/`
- SMS Misr with a template token uses `POST /api/OTP/`

Not in this repository yet:

- Caregivers Application / Infrastructure / HTTP endpoints
- Families Application / Infrastructure / HTTP endpoints
- Social / Google / Apple authentication (cancelled and removed)

## Solution layout

```text
src/
├── API/Sanad.API                         HTTP host
├── BuildingBlocks/                       Shared Domain, Application, Infrastructure
└── Modules/
    ├── Identity/                         Auth Domain, Application, Infrastructure
    ├── Caregivers/                       Domain complete; other layers are shells
    └── Families/                         Domain foundation; other layers are shells
tests/
├── Sanad.ArchitectureTests
└── Sanad.UnitTests