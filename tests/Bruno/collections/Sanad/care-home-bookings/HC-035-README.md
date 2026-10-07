# HC-035 cancellation and refund selection

Requests 112–120 are a small stateful selection for the HC-035 cancellation/refund routes. It expects existing disposable `Family`, `Owner`, `SuperAdmin`, and `ContentAdmin` JWTs plus separate booking IDs:

- `hc035FamilyPreCheckInBookingId`: paid, before check-in.
- `hc035FamilyCheckedInBookingId`: paid/accepted, checked in, not checked out.
- `hc035OwnerBookingId`: accepted stay belonging to the Owner.
- `hc035FailedRefundBookingId`: refund state `Failed`.
- `hc035ManualRefundBookingId`: refund state pending/failed/initiated, plus `hc035ExternalRefundReference`.

Use only the authorized disposable Development database. Never reset/drop the database, create identities/inventory, or call live Paymob.

The first five requests verify Family full and 50% refund amounts, Owner full cancellation, Admin retry, and Admin manual completion. Requests 117–119 verify Family 401, Family access denied on the Owner route, and ContentAdmin denied on refund operations; request 120 verifies repeated Family cancellation returns 409. Response assertions confirm the amount and refund state.

The Development Paymob stub succeeds, so do not fabricate a failed provider result or call a live provider to satisfy request 115. If no confirmed failed-refund record exists, skip that request and report it as unavailable. Unit tests cover retry and callback behavior.

## Keep fixture setup short

Before running stateful requests, read back the Owner's bookings and inventory once. Match `roomTypeId` from the booking to an active room of that same type; for a shared room, also select one bed belonging to that room. Do not guess inventory IDs or probe assignment with mismatched resource types.

Prepare only these states, using the existing family checkout, Development payment callback, and Owner decision requests:

1. Paid/accepted Family booking before check-in (full refund boundary).
2. Paid/accepted Family booking assigned to a matching room, then checked in (50% boundary).
3. One paid/accepted booking for Owner cancellation (full refund). Use a future, nonoverlapping date and a matching available room if assignment is required by the chosen stay flow.

Reuse booking 1 for Admin manual completion and the repeated-cancellation conflict. Do not add a fourth booking for an unseedable failed-refund case. Keep the test requests focused; if a setup request fails, stop and inspect its exact response before changing fixture data.

## Latest runtime checkpoint — 2026-10-07

Against the authorized worktree API at `http://localhost:5236` and `localhost:5432/SanadBrunoTestDb`, a read-only preflight identified booking status, room type, and matching rooms before mutations. The mismatched room type caused the earlier assignment 404; the matching private-room assignment and check-in then both returned 200. The existing flow created one future paid booking for Owner cancellation and read back a full refund of 15150. Verified: Family full refund/manual completion 15150; post-check-in Family refund 7575; Owner full refund 15150; 401 unauthenticated, 403 Family-on-Owner-route, 409 repeated cancellation, and 403 ContentAdmin. The three new HC-035 booking records and refund data remain in the disposable database. Admin retry request 115 was skipped because no confirmed failed refund exists and the Development Paymob stub succeeds. No live provider call or database reset occurred.
