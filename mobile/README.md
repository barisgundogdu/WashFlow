# WashFlow mobile — Phase 4C

One Turkish Flutter Android/iOS application. Phase 4C connects the existing authentication
design to the Phase 4A/4B backend under Issue #2, which remains open. Issue #9 supplied the
completed UI foundation. Requirements: FR-01, FR-20, NFR-01/02/04/05/07.
Context discovery and role-aware navigation (FR-27) now use backend grants. Role destinations
are clearly labeled temporary shells, with Owner employee invitation management now implemented.
Recipient invitation registration/redemption is now implemented in Phase 4. Other business
operations and dashboards remain subsequent work. A session alone does not grant tenant access.

## Dependencies and verification

Flutter 3.47.7 / Dart 3.13.5. Added direct dependencies:
- `http ^1.6.0`: JSON transport, injected client for deterministic tests.
- `flutter_secure_storage ^11.2.0`: iOS Keychain and Android encrypted storage backed by
  platform keys. No plain SharedPreferences credentials, logging interceptors or JWT role decoder.

From `mobile/`:
```sh
flutter pub get
flutter analyze
flutter test
```

Tests use HTTP and storage doubles; secure-storage adapter tests mock the platform channel.
These do not prove live API/SMTP delivery or physical-device Keychain/Keystore behavior.

## API configuration and local devices

Set an origin (optionally with a deployment prefix) without `/api/v1/auth`, query, fragment
or embedded credentials. No secret is needed in Flutter configuration.

```sh
# iOS simulator, API running on the same Mac:
flutter run --dart-define=API_BASE_URL=http://localhost:5080
# Android emulator:
flutter run --dart-define=API_BASE_URL=http://10.0.2.2:5080
# Deployment:
flutter run --release --dart-define=API_BASE_URL=https://api.example.com
```

Select the intended device with `-d <device-id>`. Missing/invalid configuration displays a safe
Turkish configuration message. HTTP is accepted only in debug Dart builds. Android debug
manifest permits local cleartext traffic; release uses platform HTTPS rules. iOS ATS permits
local networking, without arbitrary-load exceptions or TLS certificate bypasses. Prefer a trusted
HTTPS development endpoint if the simulator/OS blocks local HTTP.

Backend setup, PostgreSQL migrations, JWT configuration and SMTP:
[core authentication](../backend/docs/core-authentication.md). Use the real migration/setup
instructions; the mobile app never creates databases or verifies users directly.
The backend currently permits localhost/127.0.0.1 hosts by default. For the Android emulator,
include `10.0.2.2` in your local `AllowedHosts` configuration. For physical devices bind the API
to an appropriate LAN interface and explicitly allow its hostname; use trusted HTTPS and
normal certificate validation. Do not globally disable host filtering or certificate checks.

Android requires API 23+ (Flutter's current default satisfies this). INTERNET permission is
declared; automatic application backup is disabled to avoid restoring credentials without
their platform encryption keys. iOS Runner configurations use `Runner.entitlements` for
Keychain access. Configure your own bundle/application IDs, signing and provisioning before
distribution. Device storage must be available/unlocked; storage failures never count as login
success. iOS Keychain can survive uninstall; logout clears the app's session key. Reinstallation
alone must not be relied upon as server logout.

## Authentication and session lifecycle

`main.dart` composes `ApiAuthService`, `SecureTokenStore` and the existing `WashFlowApp` injection.
The feature retains domain, data and presentation boundaries. `AuthService` supplies typed
registration/login outcomes; the additional `SessionService` supplies verification and lifecycle
operations without forcing old UI/test adapters to implement unrelated methods.

- Registration sends endpoint-specific name fields and exact passwords; HTTP 202 is only a
  generic acknowledgement. It neither stores tokens nor authenticates or asserts account creation.
- Login sends email/password. Only a successful backend Bearer token-pair response persisted
  in secure storage establishes the session. No user ID/role is inferred.
- Both credentials and their UTC deadlines occupy one secure JSON value. Passwords and
  verification credentials are never persisted. Tokens are never put into URLs or logs.
- Restart reads secure storage and rotates via the server even if the stored access token
  appears unexpired. This validates current server session status before restoring the UI.
- Access expiry uses a 30-second margin. The session screen checks every 30 seconds and on
  foreground resume. Future protected API adapters must use `accessToken()` before each request;
  endpoints must independently validate server authorization. No protected business API exists yet.
- Session mutations are serialized. Concurrent consumers reuse the first rotated result.
  Refresh retains the server's absolute session deadline.
- Before sending a refresh, the predecessor is cleared from memory/storage. Invalid/revoked,
  expired, failed or uncertain refresh leaves the user signed out. This intentionally requires
  login after an offline restart/rotation failure rather than replaying a potentially consumed
  token. Crashes during rotation can also require login.
- Logout refreshes an expired access token when possible, POSTs with its Bearer header and
  no body, and clears local credentials in a finally block. An offline/server failure displays
  that server revocation was not confirmed. Server sessions can remain active until expiry;
  there is no all-device logout. A storage deletion failure remains visible and can be retried.
- HTTP redirects and automatic POST retries are disabled. Network, timeout, 400, 401, 429,
  unavailable and malformed responses map to safe Turkish messages; arbitrary server text,
  database errors and validation strings are not rendered.

The authenticated screen now loads server-authorized contexts before opening a temporary role
shell; see Phase 2 below. Password recovery remains explicitly unavailable.

## Email verification

The backend sends a Turkish plain-text email containing a base64url credential and UTC expiry.
The verification screen accepts that credential and POSTs `{"token":"…"}` to `/email/verify`.
It does not invent a short code, GET link or app-link scheme. Verification never logs the user
in; the screen directs them back to login. Resend uses `/email/resend` and always displays the
generic eligibility acknowledgement. Used/expired tokens require a new email.

Development defaults to a bounded in-memory mailbox accessible only through backend DI or an
IDE debugger, not an HTTP inbox or application log. For manual end-to-end testing configure
the backend's STARTTLS SMTP sender. Registration/resend responses never contain the credential.
Deep links are not required for this manual-token flow; automatic link handling would require
a future agreed backend/mobile contract. Email delivery is queued in process and is not durable.

## Manual end-to-end checks

1. Configure PostgreSQL/migrations, JWT and a real test SMTP provider using backend instructions.
   Start the API and confirm readiness. Launch mobile with the appropriate base URL above.
2. Register a new customer. Confirm generic acknowledgement and verification screen, no session.
   Attempt login before verification: expect a generic credential/verification failure.
3. Copy the credential from the delivered test email into the verification field. Verify,
   return to login and sign in. Confirm the session screen; no dashboard is expected.
4. Repeat with a new owner email and business name. Verify backend persistence with authorized
   development tools if needed; the client does not assert or select a tenant membership.
5. Resend an eligible unverified account after cooldown; confirm generic acknowledgement.
   Confirm the older token fails, the replacement succeeds and used tokens fail on repeat.
   Unknown/verified email resend and duplicate registration must not disclose account existence.
6. Restart the app: session restoration rotates credentials. Wait beyond access expiry while
   foregrounded or resume afterward; confirm rotation succeeds. Logout and restart: no session.
7. Revoke the test session on the server using an authorized test procedure; restart/refresh
   must return to login. Test airplane mode, unavailable API, wrong password and rate limits.
   Logout offline must clear local credentials and report unconfirmed server revocation.
8. On actual iOS/Android devices verify storage persistence, locked-device failure, local cleanup,
   keyboard/large text layouts and signing. Never copy real credentials into test reports or logs.

Automated evidence: `test/api_auth_service_test.dart` covers both registrations, login, exact JSON,
verification/resend, restart, rotation/concurrency, corrupt/revoked/expired credentials, network,
timeout, safe errors, secure-write failure, logout and successful routed UI flows.
`test/token_store_test.dart` covers the platform adapter with a mock channel.
Existing authentication, layout, routing and authorized-option fixture tests remain.
No live database/SMTP/real-device verification is claimed by these automated checks.

## Phase 4C delivery evidence

Checks performed on 2026-10-23: `flutter pub get` passed; `flutter analyze` passed
without findings; `flutter test` passed 27 tests; `flutter build ios --simulator
--dart-define=API_BASE_URL=http://localhost:5080` passed. This is a build, not an
app launch or live backend test. Android native build, physical-device storage and live
SMTP/API checks were not performed.

Changed/new files (relative to `mobile/`):
- `lib/main.dart`, `lib/app/app.dart`, `lib/app/routing/app_router.dart`
- `lib/features/auth/domain/auth_service.dart`, `auth_submission.dart`, `session_service.dart`
- `lib/features/auth/data/api_auth_service.dart`, `token_store.dart`
- `lib/features/auth/presentation/auth_form_screen.dart`, `verification_screen.dart`, `session_screen.dart`
- `lib/core/network/README.md`, `README.md`
- `pubspec.yaml`, `pubspec.lock`
- `android/app/src/main/AndroidManifest.xml`, `android/app/src/debug/AndroidManifest.xml`
- `ios/Runner/Info.plist`, `ios/Runner/Runner.entitlements`,
  `ios/Runner.xcodeproj/project.pbxproj`
- `test/api_auth_service_test.dart`, `token_store_test.dart`,
  `auth_service_test.dart`, `auth_ui_test.dart`

No backend behavior was changed. Issue #2 remains open; no commit/push was created.
Suggested commit: `feat(mobile): integrate secure authentication and email verification (#2)`.


## Flutter integration Phase 1 — Transport and context contracts

The shared `lib/core/network/api_client.dart` now supplies authentication and protected context
transport. Existing `ApiAuthService` constructor injection remains compatible. Session mutation
serialization, pre-refresh predecessor removal, secure storage, and no-retry behavior remain in
that adapter. No session-storage schema or UI routing changes were made.

`AccessTokenProvider` exposes the existing serialized access boundary without requiring protected
adapters to depend on concrete authentication implementation or storage. `ContextService` and
`ApiContextService` load the authenticated `/api/v1/auth/contexts` response using the same
`auth.apiClient` transport and `auth` session. They are not wired into the UI in this phase.

`UserContexts` contains an optional `CustomerContext` and immutable `BusinessContext` snapshots
with business ID/name, membership ID and an immutable set of `BusinessRole.owner/employee` grants.
Parsing requires the backend response shape and nonzero UUID identifiers; duplicate business or
membership entries fail closed. Both supported grants can coexist. Unknown string role names
are ignored; unknown-only roles create no supported grant. Malformed shapes fail safely, and an
explicit null customer plus empty business list is a valid authenticated account with no contexts.
No customer context, membership or role is inferred from account registration or token contents.

Transport 401 maps to `unauthenticated` for context reads; login retains its existing
`invalidCredentials` message. 403 maps separately to `forbidden`, with safe Turkish messages.
Raw server validation/detail strings are never displayed. Context failures never trigger automatic
logout, credential deletion, forced refresh or request retries; navigation/session coordination is
Phase 2 work. In particular, a late 401 must not clear a newer login. Failed/uncertain refresh still
clears its predecessor under the existing authentication policy.

Phase 1 tests include transport prefix/header/JSON/204 behavior, unsafe URL rejection, redirects,
HTTP error classification, network and whole-response timeouts; immutable context parsing, unknown
roles, malformed payloads, distinct authorization failures and no-cache behavior; and real session
boundary concurrency with one refresh for simultaneous context consumers. Existing authentication,
verification, secure-storage and UI regression tests remain in the full Flutter test suite.
No live API, database, email delivery or device secure-storage behavior is exercised by these tests.

Phase 1 verification (23 October 2026): `flutter analyze` passed with no issues; `flutter test`
passed all 46 tests, including 14 new transport/context tests and the existing authentication
regressions. `git diff --check` passed. No backend, database, migration, navigation or invitation UI
changes were made; no commit or push was created. Context loading is available for Phase 2 wiring.


## Flutter integration Phase 2 — Authenticated contexts and navigation

Requirements: Issue #2, SRS FR-02/03/27 and NFR-02/03/05/14. `main.dart` injects the Phase 1
`ApiContextService` alongside the existing authentication/session adapter. Normal login and restored
sessions both enter `/session`, which fetches `/api/v1/auth/contexts` before rendering role content.
The same secure-store format and serialized refresh-token safeguards remain in place.

`SessionController` maps each server customer profile to Customer and each supported business
grant to Owner/Employee experiences. Both grants in the same membership remain separate options.
A single option opens automatically. Multiple options use `AuthorizedContextPicker` with business
names and Turkish role labels. Unknown roles cannot become destinations. Zero supported grants
show a signed-in empty state with refresh/logout, without an invitation action.

All roles currently show explicitly temporary home screens. No customer, owner, employee operation,
invitation management, registration, or acceptance was added in Phase 2. Selection grants no API
privileges: each future business operation must still validate membership/role/ownership on the server.

Refresh hides and clears old role content while fetching. Switching first refreshes the picker;
selection revalidates again before opening the requested experience. Removed selections cannot be
restored from stale picker callbacks. Role content lives inside a navigator keyed to its current
request generation and experience: switching or refreshing disposes its routes and local state.
Role-specific named routes are not registered; route arguments cannot select a business or role.
The authenticated root blocks back navigation to an earlier account state.

App inactivity clears the protected subtree and invalidates pending responses. Resume fetches fresh
contexts; if there are multiple grants, selection may be requested again. The existing 30-second
session check remains and uses the original refresh boundary. Network/service errors show retry/logout
without retaining role content. Context 401 exits to login; context 403 shows a safe error without
an automatic retry loop. The controller's generation-bound authorization-failure hook refreshes
contexts after future business 403 responses and ignores errors originating from a former context.
There are no business API calls to attach this hook to in the temporary role screens.

A monotonic, in-memory `SessionIdentity` revision signals login, restoration and logout boundaries.
Token rotation does not change identity. Controllers discard late results on identity changes,
suspension, superseding reads, switching, logout or disposal. Replacing app authentication/context
services rebuilds the root navigator. Test/alternate session adapters should implement this identity
signal or be replaced with their app dependencies whenever accounts change. No grants or selection
are persisted, and invitation credentials are not collected.

Logout clears context state immediately, including while reads are pending. Confirmed logout and
network failures return to login; uncertain server revocation is reported. Secure-storage deletion
failures leave role content cleared and expose retryable logout cleanup. The controller cannot use
a late old-account response to restore a role or overwrite a new account's state.

Limitations: these are navigation shells, not implemented operational dashboards. Grants are
revalidated on entry, refresh, resume, switch and relevant authorization failures; there is no push
subscription for immediate grant changes while the app stays idle in the foreground. Backend ACLs
remain necessary on every request. Mobile tests use HTTP/storage doubles, not a live API, database,
email provider or physical device. No backend or database changes are part of this phase.

Phase 2 verification (23 October 2026): `flutter analyze` passed with no issues;
`flutter test` passed all 76 tests (30 additional controller/widget/session-boundary cases over
Phase 1, plus existing authentication/verification/storage regressions). `git diff --check` passed.
Coverage includes zero/one/multiple contexts, combined roles, restored-session entry, unknown roles,
selection revalidation, forged routes, revoked grants, foreground resume, tenant navigator disposal,
late success/failure, logout/account changes, dependency replacement, storage cleanup failures,
offline logout, periodic session invalidation and compact/large-text rendering.
No live backend/device test, backend change, migration, database modification, commit or push occurred.

Phase 2 changed/new files relative to `mobile/`:

- Composition/routing: `lib/main.dart`, `lib/app/app.dart`, `lib/app/routing/app_router.dart`.
- Authentication identity: `lib/features/auth/data/api_auth_service.dart`,
  `lib/features/auth/domain/session_service.dart`.
- Shell and reusable UI: `lib/features/auth/presentation/session_controller.dart`,
  `session_screen.dart`, `widgets/auth_layout.dart`, `widgets/authorized_context_picker.dart`.
- Temporary role destinations: `lib/features/customer/presentation/customer_home_screen.dart`,
  `lib/features/owner/presentation/owner_home_screen.dart`,
  `lib/features/employee/presentation/employee_home_screen.dart`.
- Tests: `test/api_auth_service_test.dart`, `test/session_controller_test.dart`,
  `test/session_navigation_test.dart`, `test/support/session_fakes.dart`.
- Documentation: this README, `lib/core/network/README.md`, and customer/owner/employee READMEs.

Suggested commit: `feat(mobile): add authorized context selection and secure navigation (#2)`.


## Flutter integration Phase 3 — Owner employee invitation management

Requirements: Issue #2, SRS FR-02/03/27 and NFR-02/03/04/05/07/14. `main.dart` injects a stateless
`ApiOwnerInvitationService` through app/router/session composition, sharing the existing API origin,
HTTP transport and serialized token provider. Secure storage and token rotation behavior are unchanged.

Only the selected Owner experience exposes **Çalışan davetleri**. Entry revalidates current server
contexts before opening a guarded nested route. Customer and Employee experiences have no entry;
role/business arguments cannot select an unauthorized tenant. The backend independently authorizes
all invitation requests. A guessed route under a stale Owner snapshot still cannot bypass the API.
The screen displays the server-provided business name, metadata list, email form and status labels.
Dates are displayed in local time through Turkish Material localization; expiry is only a UI hint.

### Actual backend contracts

| Method | Business-scoped route | Input / success |
| --- | --- | --- |
| GET | `/api/v1/businesses/{businessId}/employee-invitations` | Array of invitation metadata, 200 |
| POST | same route | `{email}`, metadata 201 |
| POST | `.../employee-invitations/{id}/revoke` | No body, 204 |
| POST | `.../employee-invitations/{id}/resend` | No body, replacement metadata 200 |

The DTO contains only `id`, `email`, `expiresAt`, `acceptedAt`, `revokedAt`.
There is no invitation credential, business ID, capability flag or delivery receipt in this DTO.
Unknown extra fields are not retained or displayed. Nonzero UUIDs, timezone-bearing timestamps,
metadata shapes and mutually exclusive accepted/revoked states are validated by the adapter.

Labels are **Kabul bekliyor**, **Süresi doldu**, **Kabul edildi**, **İptal edildi**.
Accepted/revoked rows disable resend/revoke. Expired rows can still be resent or revoked per the
backend. Repeating an already-revoked operation is backend-idempotent but is not offered by the UI.
Resend creates a replacement record/ID and invalidates the previous credential. The client reloads
the list instead of optimistically editing an old row. A confirmation dialog is required for revoke.

### Errors, isolation and races

400 validation shows safe email guidance; 400 `invalidInvitation` reports changed/unusable state;
404 reports a missing resource; 409 reports a state/membership conflict; 429 asks the user to wait.
Neither backend account existence nor exact conflict reason is inferred. 401 returns to login;
403 clears the invitation subtree and refreshes server contexts. Removed grants cannot continue
using the former screen; if grants still exist, entry must be explicitly attempted again.

Each controller is bound to one immutable business ID and the originating shell generation.
Switching businesses/roles/accounts clears the nested navigator, email input, records and confirmation
dialogs. Late reads, mutation results and authorization failures cannot populate or invalidate the
new tenant. All actions capture their business before awaiting; a switch never retargets a request.

Mutations serialize and disable competing actions synchronously. They are never automatically
retried, even after network timeout, redirects, 400, 404, 409 or 429. A single GET reconciles success,
conflicts and uncertain outcomes. Failed reconciliation disables writes until a successful manual
list refresh. If a write's response is lost/malformed, the UI reports that it may already have
completed; it does not treat absence of a success response as proof of failure. An already-sent
write may still commit in its original business after the user leaves. This cannot be cancelled
or inferred from local navigation. Success clears the create form only after confirmed recording.

Creation/resend success explicitly says that delivery is unconfirmed. The bounded backend email
queue is non-durable and the API supplies no delivery status. No credential is displayed as a
workaround. No email-delivery or live SMTP guarantee is made.

### Backend gaps and scope

Basic owner management needs no backend/schema change. The list is currently unpaginated; action
capabilities and member-conflict reasons are not exposed, so UI actions remain hints and backend
responses decide eligibility. A resend can still fail due to state changes or existing membership.
Missing/terminal invitations commonly return 400 `invalidInvitation`, rather than a distinct 404.
There is no mutation idempotency key or delivery/status receipt; uncertain writes need list review,
not an automatic replay. The DTO omits business ID, so tenant ownership is established by the
scoped endpoint/backend authorization rather than inferred from the payload.

Recipient registration, verification continuation, acceptance, credential entry and recipient UI
remain Phase 4 work. Other business operations remain unimplemented. No backend, schema, migration,
or database change is part of Phase 3. Automated checks use mock HTTP/services/storage and do not
prove live API, email delivery or physical-device behavior.

Phase 3 verification (23 October 2026): `flutter analyze` passed with no issues;
`flutter test` passed all 105 tests (29 additional invitation cases over Phase 2, with existing
transport/authentication/session/context regressions preserved). `git diff --check` passed.
Tests cover exact backend contracts and error codes, metadata validation, confirmation/cancellation,
terminal/expired rows, serialized mutations, resend/revoke races, uncertain results and reconciliation,
Owner-only entry, revocation, forged routes, business switching, late writes/reads/errors across
business/account boundaries and compact/large-text UI. No live backend/device/email test was run.
No recipient flow, backend change, database operation, migration, commit or push was performed.

Complete Phase 3 changed/new file list, relative to `mobile/` (21 files):

- `README.md`
- `lib/main.dart`
- `lib/app/app.dart`
- `lib/app/routing/app_router.dart`
- `lib/core/network/README.md`
- `lib/core/network/api_client.dart`
- `lib/features/auth/data/api_auth_service.dart`
- `lib/features/auth/data/api_context_service.dart`
- `lib/features/auth/domain/auth_service.dart`
- `lib/features/auth/presentation/session_screen.dart`
- `lib/features/invitations/domain/invitation.dart` (new)
- `lib/features/invitations/domain/owner_invitation_service.dart` (new)
- `lib/features/invitations/data/api_owner_invitation_service.dart` (new)
- `lib/features/owner/README.md`
- `lib/features/owner/presentation/owner_home_screen.dart`
- `lib/features/owner/presentation/employee_invitations_controller.dart` (new)
- `lib/features/owner/presentation/employee_invitations_screen.dart` (new)
- `test/owner_invitation_service_test.dart` (new)
- `test/employee_invitations_controller_test.dart` (new)
- `test/employee_invitations_widget_test.dart` (new)
- `test/support/invitation_fakes.dart` (new)

Suggested commit: `feat(mobile): add owner employee invitation management (#2)`.


## Flutter integration Phase 4 — Recipient invitation redemption

Manual invitation entry is now available on Welcome and the authenticated shell. Existing users
sign in if necessary, then explicitly confirm acceptance. New users register through the invitation
endpoint, use the existing verification screen, sign in, then explicitly accept. Neither registration
nor verification creates a membership or customer/business profile in Flutter.

The pending credential exists only in the route-owned in-memory controller. Cancellation, disposal,
confirmed acceptance, logout and unrelated account changes clear it. Business names are not inferred
before acceptance. A confirmed acceptance reloads server contexts; failed/uncertain mutations never
add a local grant or automatically retry. The shell's outgoing identity listener is detached before
route animation completion to preserve a rapid legitimate in-flow login.

See [recipient invitation flow, contracts, security and backend gaps](docs/employee-invitation-redemption.md)
for the full behavior. Existing owner/customer authentication, session rotation and Owner invitation
management remain covered by the regression suite. This phase adds no backend/schema/database changes
or production email delivery, and does not implement operational role features.

## Phase 5 — Invitation integration and readiness

The opt-in `integration_test/invitation_live_test.dart` harness runs the actual Flutter UI,
HTTP adapters and iOS secure storage against an isolated Kestrel/PostgreSQL backend. SDK
`integration_test` is a development dependency only. Ordinary `flutter test` remains mock-based.
See [Phase 5 evidence and manual checklist](../docs/validation/employee-invitations-phase5.md)
for precise test categories, environment setup, results and release limitations.
