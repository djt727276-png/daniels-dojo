# Personal data lifecycle

How Daniel's Dojo implements the member's rights over their own data: export and
deletion, both self-service on the account page, both audited.

## Export — `GET /api/v1/me/privacy/export`

A JSON download containing only what the requesting member owns:

| Section              | Contents                                                        |
| -------------------- | --------------------------------------------------------------- |
| `account`            | Display name, email, verification state, created date           |
| `communityProfile`   | Handle, bio, avatar flag, privacy settings                      |
| `friends`            | Handles and acceptance dates                                    |
| `messagesSent`       | Bodies and timestamps of messages **they wrote**                |
| `forumPosts`         | Their posts with the thread title                               |
| `reviews`            | Their course reviews                                            |
| `enrollments`        | Courses and completed-lesson counts                             |
| `certificates`       | Verification codes and issuance dates                           |
| `orders`             | Order summaries (status, total, currency, date)                 |

Deliberately absent: messages the member received, other members' posts, and
moderation records — those belong to other people or to the operator.

Every export writes a `privacy.export` audit row.

## Deletion — `POST /api/v1/me/privacy/delete-account`

Requires the typed phrase `delete my account`. Immediate, transactional,
irreversible. Writes a `privacy.account_deleted` audit row.

| Data                                              | Outcome                                                             |
| ------------------------------------------------- | ------------------------------------------------------------------- |
| Community profile, handle, bio                    | Deleted                                                             |
| Avatar                                            | Deleted (bytes removed from the database)                           |
| Friendships, friend requests, blocks              | Deleted (both directions)                                           |
| Notifications received, read positions            | Deleted                                                             |
| Direct messages **sent**                          | Tombstoned: body erased, "deleted" placeholder keeps the shape      |
| Forum threads and posts authored                  | Retained, rendered as "Former member" (profile gone)                |
| Course reviews                                    | Tombstoned (withdrawn from the public aggregate)                    |
| Roles (including Admin)                           | Removed                                                             |
| Account row: name, email                          | Scrubbed to "Deleted member" / empty                                |
| Sign-in binding (issuer, subject)                 | Subject overwritten with `deleted:{id}` — the row is unreachable    |
| Orders, refunds, disputes, Stripe references      | **Retained** — financial record-keeping (typically 7 years)         |
| Subscriptions, entitlements                       | Retained against the scrubbed row                                   |
| Enrollments, lesson progress                      | Retained against the scrubbed row (pseudonymous)                    |
| Certificates                                      | Retained so issued certificates stay verifiable; holder name was    |
|                                                   | captured at issuance and remains on the certificate record          |
| Audit logs                                        | Retained — they are the operator's record that rules were followed  |

### Why the subject is scrubbed rather than the row deleted

Orders, entitlements, certificates, and audit rows must keep a stable foreign
key. Deleting the `Users` row would either cascade through records the law
requires us to keep or orphan them. Overwriting `ExternalSubjectId` with
`deleted:{userId}` achieves the privacy outcome — the identity provider's
subject no longer maps to anything, so sign-in cannot reach the history — while
the retained records stay internally consistent and carry no name or email.

A returning customer is provisioned as a brand-new account: nothing reattaches.

### Operator notes

- There is no grace period in v1. The dialog says so; the phrase requirement is
  the safeguard.
- Deletion executes in one database transaction; a failure rolls back to a
  fully intact account.
- The public privacy policy page mirrors this table in plain language; keep the
  two in sync when either changes.
