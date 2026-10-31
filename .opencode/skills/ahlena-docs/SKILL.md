---
name: Ahlena Documentation
description: Keep approved Ahlena App, Admin, and Care Homes documentation clear, accurate, and useful to developers when documentation is in the task's approved file scope.
---

# Ahlena documentation guidance

Use this skill only when documentation is included in an approved task manifest
or the Owner explicitly requests a documentation review. It supports the
assigned work; it does not create a separate review role or additional mandatory
gate.

## Write for the intended developer

- Label the audience and feature clearly: App (Family/Caregiver/Elderly), Admin,
  or Care Home Owner. Do not conflate Admin with App roles.
- Start with what the feature does and when a developer needs the guide. Prefer
  short sections, direct language, exact route names, and minimal examples.
- Cover the essentials for the changed feature: authorization/permissions,
  lifecycle and important state transitions, request/response shape, relevant
  errors, and safe integration notes. Omit unrelated endpoint inventories.
- Link to the approved contract and authoritative task/evidence record rather
  than copying their history or maintaining a second status list.

## Keep claims grounded

- Treat the approved contract as scope and source behavior as implementation
  evidence. Do not infer or invent product policy, permissions, payloads, error
  codes, or deployment state. Report contradictions or missing decisions.
- Examples must match behavior verified for the current change. Mark illustrative
  examples clearly; never include credentials, personal data, or private target
  details.
- Distinguish authored checks from executed checks. Record unrun checks as
  `NOT RUN` with a reason when known; never imply they passed.
- Keep deployment and production claims tied to the applicable release receipt.
  Passing tests is not deployment approval.

## Keep synchronization small

- Update only documentation and Postman files in the approved manifest. Preserve
  existing language, collection conventions, and unrelated edits.
- Search the affected docs for stale route, permission, error, migration, and
  roadmap claims; fix only findings within scope and report the rest.
- Validate changed links/examples as appropriate. Do not duplicate a full audit,
  add documentation solely to satisfy this skill, or rewrite historical receipts.
