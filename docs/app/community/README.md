# Community

Community routes are authenticated and live under `/api/v1/community`. Read
routes expose published posts; new posts enter moderation as `PendingReview`.
Write identities come from the JWT and cannot be supplied by the client.

- [Posts, interactions, ratings, check-ins, recommendations, uploads and moderation](writes.md)
- [Postman Community collection](../../postman/app/Sanad.App.Community.postman_collection.json)
- [Postman moderation collection](../../postman/admins/Sanad.Admin.Community.postman_collection.json)

Comments and replies enforce author ownership for edits/deletes. Run stateful
requests only with a disposable fixture and a content moderator token where
noted.
