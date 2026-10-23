# Employee invitations — Phase 5 readiness (23 October 2026)

Scope: Issue #2, SRS FR-01/02/03/27 and NFR-02/03/04/07/14. Validation of Phases 1–4;
this does not complete all Issue #2 criteria or the operational MVP.
Baseline: `main`, `a5d5d658f600f9c0c5f4aa390ef6bcc646a0c8c6`, initially clean.

## Environment and isolation

- .NET SDK 10.0.402; Flutter 3.47.7 stable / Dart 3.13.5.
- Docker available, PostgreSQL 17.11 image available. Existing `washflow-postgres` was inspected
  only for name/image/port; its databases and configuration were not modified.
- A separate `washflow-phase5-test` container used tmpfs database storage and a separate
  loopback-only port (55439), with a disposable `washflow_phase5_test` database.
  Local trust authentication was confined to this temporary loopback test service.
- Backend PostgreSQL tests create/migrate/drop unique schemas inside that disposable database.
  For simulator HTTP tests, existing migrations were applied to its otherwise empty public schema.
  No development/production database or schema was migrated, reset, truncated or deleted.
- A separate Development backend ran on loopback port 5189 with a fresh in-memory signing key,
  Memory email provider and suppressed application logs. No production credentials were loaded.
  Test credentials and mailbox contents were handled only in memory, never included in reports.
- Xcode 27.1, iOS 27.0 booted iPhone 18 Pro simulator available. Android SDK 36/build-tools 36,
  emulator 37.2.12 and an Android API 37 AVD available; Android was not launched/tested.
  No physical-device testing occurred. Flutter doctor reported no issues.

## Executed checks

| Category | Result | What it establishes |
| --- | --- | --- |
| `dotnet build` | Passed, 0 warnings/errors | Backend and tests compile |
| Backend unit tests | 48 passed, 0 skipped | Domain/service policy and token validation |
| Non-PostgreSQL HTTP integration tests | 37 passed | ASP.NET test-host auth/validation/error/mailbox boundaries |
| PostgreSQL integration tests | 44 passed, 0 skipped | Real PostgreSQL constraints, transactions, concurrency, auth and HTTP flows |
| `flutter analyze` | Passed, no issues | App and live integration harness analysis |
| `flutter test` | 140 passed | Unit/controller/widget and mocked HTTP adapter regressions |
| iOS simulator real-HTTP tests | 2 passed together | Real UI/adapters/Keychain + Kestrel + PostgreSQL, separate from widget tests |
| `git diff --check` | Passed | Whitespace validation |

The backend total is 48 unit + 81 integration (37 non-PostgreSQL + 44 PostgreSQL).
The new three PostgreSQL lifecycle tests use `WebApplicationFactory`/HttpClient, the actual endpoint
pipeline, actual password/JWT/session services and EF stores; this is in-process HTTP transport,
not an external socket or mocked persistence. Existing PostgreSQL tests additionally exercise
concurrent create/accept, refresh replay, resend/revoke races and expiry after lock waits.
The simulator harness uses the actual socket listener and the existing Flutter API adapters,
route tree and screens, plus the real secure-storage plugin. It constructs `WashFlowApp` directly;
it does not test the production `main()` configuration bootstrap or an app-store release build.

## Verified workflows and contracts

Owner registration is `/api/v1/auth/businesses/register`: this existing flow creates the business
and Owner membership together. There is no invented separate business-creation endpoint.
Verification and login establish a verified session; GET `/api/v1/auth/contexts` supplies business
IDs, display names and role grants. Owner create/list/resend/revoke use the route's business ID.
List metadata has `id`, `email`, `expiresAt`, `acceptedAt`, `revokedAt`; it has no status string,
credential or delivery confirmation. Flutter derives Turkish status labels from those timestamps.
Creation is 201, resend 200, revoke 204, recipient registration 202 and acceptance 204.
Bodies, bearer headers and DTO parsing were checked against the actual backend contracts.

Existing recipient: register/verify a normal customer account, login, explicitly accept a matching
email invitation, then fetch contexts. Existing Customer access is preserved; exactly Employee is
added for the target business, and invitation management is denied. Refresh rotation retains the
same authorized contexts; logout invalidates the session.

New recipient: invitation-specific registration, verify email, login, explicitly confirm acceptance,
then reload contexts. Before acceptance there is no customer profile or business membership;
afterward there is only the Employee membership. Registration/verification do not consume the
invitation. Backend inspection confirms no separate business/customer profile is created.

Real database-backed HTTP checks cover revoked credentials, resend invalidating the old credential,
duplicate acceptance, email mismatch, expired invitations, active-member conflict, cross-business
mutation attempts, and users who are Owner in one business but Employee in another. Removing the
original inviter's Owner grant denies list/create/resend/revoke and prevents recipient acceptance.
Test-only database writes simulate expiry/grant removal because no membership-management endpoint
exists. These are controlled fixture operations, not production feature endpoints.

## Security evidence and limits

- Backend rechecks membership, Owner grant, invited email, verification and invitation state.
  Grants are transactional and Employee-only. Historical Owner grants cannot reactivate.
- The full Flutter suite covers stale responses across logout/account/role/business switches,
  revoked grants, forged routes, pending dialogs, duplicate submits, cancellation and rapid login.
  Such adversarial timing cases use deterministic fakes; they were not all reproduced on a device.
- Existing transport tests cover 401/403/404/409/429, validation, timeouts, network failures,
  redirects and malformed responses. Real HTTP tests exercise applicable backend 400/401/403/409
  behavior. Network faults and rate limits were not deliberately injected into the simulator.
- Credentials belong only in JSON bodies and route-owned memory, never URLs or persistent grants.
  Source review found no credential logging/persistence in invitation code. Tests cover memory
  cleanup and stale completions. Simulator checks verify stored session data excludes the invitation.
- Writes have no automatic retry. Uncertain results remain unconfirmed; a contexts refresh is not
  proof of acceptance of a particular invitation. Confirmed 204 clears the credential before reads.
- Email reads use the development/test mailbox only. This proves message availability in process,
  not internet delivery. Production durability and delivery remain unimplemented/unverified.

## Findings, fixes and remaining gaps

No production-code integration defect has been confirmed in this validation. No schema change or
migration was added. New tests initially exposed issues in their own fixtures: an expired timestamp
violated the existing database constraint, and the live UI test advanced before server reconciliation.
Fixtures were corrected to preserve constraints and await actual UI readiness, without weakening checks.

Known backend gaps remain: no invitation preview, verified current-account email lookup, acceptance
receipt/status reconciliation or mutation idempotency keys. The UI cannot verify the destination
business name before acceptance or distinguish every invalid/expired/revoked/used/wrong-email case.
A lost acceptance response cannot be reconciled to one invitation; a deliberate retry may return
`invalidInvitation` after the earlier request committed. No role is inferred from that result.

The bounded email dispatch queue and mailbox are not durable. A committed invitation can outlive
lost mail after restart or queue exhaustion. Owner resend is recovery, not a delivery guarantee.
Owner listing is unpaginated and role operational homes remain explicitly temporary.

## Manual verification before a demonstration/release

1. Start an explicitly isolated test backend/database, configure the app's API address, and use
   only disposable accounts. Retrieve development mailbox messages privately; never screen-share codes.
2. On a physical iOS and Android device, repeat Owner create/list/resend/revoke and both recipient
   flows; confirm keyboard/paste behavior, back/cancel and business-name explanation.
3. Background/foreground during verification, terminate/restart the app (pending invite must be lost),
   restore a normal session, switch businesses/roles/accounts, and verify no previous tenant data appears.
4. Disconnect the network during create/accept, then reconnect. Confirm no hidden mutation retry or
   false success. Refresh access/list deliberately; demonstrate revoked access removal.
5. Demonstrate expiry/revoke/reuse/mismatch with disposable data, including multi-business Owner/Employee
   separation. Never use production permission changes as a test fixture.
6. Verify secure-store behavior and HTTPS configuration in signed release builds. Review accessibility
   and Turkish layout on actual screen sizes. No production email-delivery claim is appropriate yet.

## Reproduction and cleanup

Use a fresh disposable PostgreSQL instance, not `washflow_db` or an existing shared test database.
Set `WASHFLOW_TEST_POSTGRES` privately for `dotnet test backend/WashFlow.sln`; never echo it.
Run `dotnet build backend/WashFlow.sln`, then Flutter analyze/tests from `mobile`.
For live tests, start a separate Development backend on `http://127.0.0.1:5189` pointing exclusively
to that disposable database; apply only existing migrations there. Use Memory email and a temporary
signing key, suppress request/body/database-value logging, and never put secrets in command arguments.
Run from `mobile`:

```sh
flutter test integration_test/invitation_live_test.dart -d <ios-simulator-id> \
  --dart-define=PHASE5_API_URL=http://127.0.0.1:5189
```

The live harness is opt-in and rejects other API URLs; this is a safety reminder, not proof of database
identity. Verify server/database isolation before execution. It creates unique test accounts and clears
the simulator app's test session from Keychain. Do not run alongside a personal signed-in app session.
Stop the test backend and remove only the explicitly created disposable container after validation.

## Readiness decision and changed files

**Ready for a controlled graduation-project invitation demo**, using the existing development mailbox
and an isolated test backend. Both live simulator tests passed together in the final run (38 seconds,
excluding Xcode build/install). They cover Owner create/list/resend/revoke with confirmation,
existing-user explicit acceptance and Customer/Employee switching, denial of Owner management to an
Employee, new-user registration/verification/login/acceptance, Employee-only contexts, real Keychain
session restoration, explicit refresh and logout. This is iOS simulator evidence, not physical-device,
Android, signed-release, production-email or full MVP readiness.

No production-code fix was necessary. Confirmed test-harness corrections were constraint-valid expiry
fixtures, waiting for replacement/revoke reconciliation and explicit focus of the re-enabled iOS input.
All gates passed without skipping PostgreSQL tests. The disposable backend was stopped and its tmpfs
container removed after testing; existing WashFlow services/data were left untouched.

Changed/new files (9):

- `README.md` — current readiness-report link.
- `backend/docs/employee-invitations.md` — current Flutter status and historical/current evidence distinction.
- `backend/tests/WashFlow.IntegrationTests/InvitationLifecycleHttpPostgresTests.cs` — three additional real endpoint/PostgreSQL lifecycle/security tests.
- `docs/validation/employee-invitations-phase5.md` — this evidence, limitations, reproduction and manual checklist.
- `mobile/README.md` — opt-in real backend/device validation guidance.
- `mobile/docs/employee-invitation-redemption.md` — link to current Phase 5 evidence.
- `mobile/pubspec.yaml` — Flutter SDK integration-test development dependency.
- `mobile/pubspec.lock` — resolved integration-test tooling dependencies.
- `mobile/integration_test/invitation_live_test.dart` — two live iOS UI/HTTP/Keychain scenarios.

No backend application, schema, migration or operational feature changes; no commit or push.
Suggested commit: `test: validate employee invitation lifecycle and demo readiness (#2)`.
