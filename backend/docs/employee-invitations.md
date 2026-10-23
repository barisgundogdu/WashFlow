# Employee invitations — Issue #2

Backend implementation for SRS FR-01/02/03/27 and NFR-02/03/04/07/14.
Flutter owner management and recipient redemption are implemented in Phases 3–4. This does not complete all Issue #2 criteria.

For current real PostgreSQL/HTTP and simulator evidence, see the
[Phase 5 readiness report](../../docs/validation/employee-invitations-phase5.md).
The implementation-session results at the end of this document are historical, not the current validation totals.

## API

All endpoints use the existing authentication IP rate limit and `Cache-Control: no-store`.
Owner operations require a currently active Owner membership in the route business.

| Method | Path | Input / result |
| --- | --- | --- |
| POST | `/api/v1/businesses/{businessId}/employee-invitations` | `{email}`; 201 metadata |
| GET | `/api/v1/businesses/{businessId}/employee-invitations` | 200 metadata list |
| POST | `/api/v1/businesses/{businessId}/employee-invitations/{id}/resend` | Replacement invitation metadata; previous credential invalidated |
| POST | `/api/v1/businesses/{businessId}/employee-invitations/{id}/revoke` | 204; repeat revocation is safe |
| POST | `/api/v1/employee-invitations/register` | `{token,email,fullName,password}`; anonymous generic 202 acknowledgement |
| POST | `/api/v1/employee-invitations/accept` | `{token}`; authenticated verified recipient; 204 |

Validation errors are 400; missing/invalid authentication is 401; owner authorization failure is 403;
state/membership conflicts are 409. Unusable credentials return a common 400 `invalidInvitation`.
Registration acknowledgement does not disclose account existence or token eligibility.
Owner listings expose invitation metadata, never credentials, hashes, or platform account details.

Configure `Invitations__LifetimeHours` (default 24, supported range 1–720).
Only one unaccepted, unrevoked invitation exists for each business/normalized email.
Expired records are revoked before replacement. Resend creates a new record and retains the original
inviter membership; the original inviter must still be an active Owner at acceptance.
An active membership (including Owner or grantless membership) prevents issuance.
Acceptance rejects all existing memberships, including revoked historical memberships, until a
separate rehire policy is approved. Historical Owner grants are never reactivated.

New users register through the invitation endpoint, verify through `/api/v1/auth/email/verify`,
log in normally, and explicitly accept. Registration creates only User and EmailVerificationToken.
It neither consumes the invitation nor creates a customer profile, business, membership, or session.
Existing users sign in with their own verified account matching the invitation email.
Other business memberships and customer profiles are preserved.

## Security and transactions

Credentials contain 32 random bytes, base64url encoded. Only purpose-separated SHA-256 hashes are
stored. Tokens belong in JSON bodies; do not log requests, emails, or credentials.
Business-row locks serialize invitation mutations. Writes then lock the inviter membership and
Owner grant, and acceptance locks the recipient User. Expiry is re-evaluated after waits.
Membership/grant creation and token consumption commit together. Database uniqueness, tenant-bound
issuer foreign keys, final-state constraints, and xmin concurrency checks provide additional protection.
Future membership-management writes must acquire business locks first and follow this protocol;
otherwise complex cross-business changes can introduce deadlocks. Keep transactions short.

## Email and operational limitations

Uses the existing in-memory dispatch queue and development/test mailbox. Verification and invitation
mail have distinct purposes so registration verification cannot overwrite an invitation.
Development-only loopback POST `/api/dev/email/invitation/take` with `{email}` retrieves the latest
invitation once; it has the same proxy restrictions as `/api/dev/email/verification/take`.
Neither route exists outside Development or with the SMTP provider.
The existing SMTP adapter understands invitation messages; no production credentials were added.

The bounded queue is **not durable**: queue saturation, process restart or exhausted retries can lose
an email after the database commit. API success confirms persistence, not delivery. Resend is the
recovery mechanism. Deployment requires a durability/delivery decision; no outbox, guaranteed retry,
or production delivery verification is claimed. Tokens must never be returned by owner APIs to
work around delivery failures. Owner lists are currently unpaginated; suitable only for pilot volume.

## Migration and verification

`20261023112302_EmployeeInvitationLifecycle` adds a partial unique index and mutually exclusive
accepted/revoked state constraint. Older duplicate outstanding rows are revoked while keeping the
newest credential. Rollback cannot restore those revoked credentials. Inconsistent pre-existing
accepted-and-revoked rows cause migration failure and require explicit data review.
No development or non-test database migration was applied during implementation.

Run `dotnet test WashFlow.sln` in backend. PostgreSQL tests require an explicitly configured
`WASHFLOW_TEST_POSTGRES` dedicated test connection. The existing fixture creates, migrates and drops
only uniquely named test schemas. Without it, PostgreSQL tests are skipped, not passed.
Tests cover token security, mailbox separation, HTTP authentication/validation, tenant authorization,
new-account verification/login/acceptance, single-use concurrency, replacement/revocation races,
expiry, removed inviter grants and historical Owner membership rejection.

Historical backend implementation-session verification (23 October 2026): build completed with zero warnings/errors;
48 unit tests and 37 HTTP integration tests passed. All 41 PostgreSQL tests, including eight new
invitation cases, were explicitly skipped because `WASHFLOW_TEST_POSTGRES` was unset.
Offline migration SQL generation and model consistency checks passed; SQL was not applied.
PostgreSQL concurrency, constraint behavior and the database-backed HTTP flow remain unverified
in this session. No Flutter changes, commits or pushes were made.
