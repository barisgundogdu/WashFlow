# Employee invitation redemption — Flutter Phase 4

Issue #2; SRS FR-01/02/03/20/27 and NFR-02/03/04/05/07/14.
This is employee account onboarding, not operational employee/work-order functionality.
No backend/schema/migration changes or production email delivery were introduced.

## Existing accounts

1. Choose **Davet koduyla katıl** on Welcome or the authenticated shell, including zero-context accounts.
2. Manually enter/paste the credential from the invitation email. It stays obscured and in memory.
   Shape checks do not prove validity. No business preview or token decoding is performed.
3. A current local session can proceed to confirmation; the backend still validates that session
   and requires verified email matching the invitation. Otherwise, use the shared login form.
4. Explicitly choose **Daveti kabul et** and confirm the dialog. Nothing accepts automatically.
5. A confirmed 204 clears the credential and reloads `/api/v1/auth/contexts`. Continue to the normal
   authenticated shell, which loads grants afresh and applies its existing role-selection rules.

The frontend cannot confirm the signed-in email or target business name from the available
contracts. The UI explains this and asks the user to use the invited verified account. Wrong-account
acceptance fails on the backend. Switching accounts through logout clears the credential; re-enter
it to start again. No name/plate/email matching in Flutter can create a membership.

## New accounts

1. Enter the invitation credential and choose **Davet için hesap oluştur**.
2. Use the shared name/email/password form with invitation-specific submission and labels.
   Only the recipient registration endpoint is called; customer/business registration is not called.
3. Registration 202 is a non-disclosing acknowledgement. It does not prove account creation,
   invitation validity, email delivery, verification, membership or authentication.
4. Complete the existing verification screen, including resend if needed. Verification credentials
   and the pending invitation are separate values. Verification never logs in or accepts a membership.
5. Return to the flow's shared login form. After successful login, explicitly confirm acceptance.
6. Only acceptance 204 confirms the invitation. Reloaded server grants determine all destinations.

An existing email submitted to invitation registration is not overwritten or linked automatically;
use its original credentials to log in. Generic acknowledgement means invalid/wrong-email invitations
may produce the same response as eligible registration. The UI does not promise account creation.

## Actual API contracts

| Endpoint | Request | Success |
| --- | --- | --- |
| POST `/api/v1/employee-invitations/register` | `{token, fullName, email, password}`; anonymous | 202 `{message}` acknowledgement |
| POST `/api/v1/auth/email/verify` | `{token}` verification credential | 200 acknowledgement |
| POST `/api/v1/auth/email/resend` | `{email}` | 202 generic acknowledgement |
| POST `/api/v1/auth/login` | Existing email/password contract | Existing secure session pair |
| POST `/api/v1/employee-invitations/accept` | `{token}`; authenticated | 204, no membership payload |
| GET `/api/v1/auth/contexts` | Authenticated; no role/business override | Existing server-authorized snapshot |

No preview, recipient status, receipt, business-name lookup, account creation shortcut or alternate
acceptance endpoint was invented. Passwords preserve their exact submitted value. Mutations use
existing bounded transport without redirects or automatic retries. Token acquisition stays inside
the existing serialized authentication boundary; an identity change during acquisition prevents
acceptance dispatch under a different account.

## Memory, navigation and stale responses

`InvitationRedemptionController` is owned by one redemption route. Its credential is private and
has no diagnostic/toJson getter. Requests also avoid record-generated credential diagnostics.
Pending credentials are not written to secure storage, preferences, analytics, URLs, route arguments
or logs. Entry text is cleared after moving forward; the controller clears pending credentials on
cancellation, disposal, confirmed acceptance, logout and unrelated identity changes. Restarting the
process loses the flow; the user must re-enter the email credential. Paste uses the normal text input;
the app does not copy the credential to the clipboard.

Welcome/shell entry removes prior navigation. The shell releases its context state and identity
listener immediately, before Flutter's outgoing route animation finishes. A rapid expected in-flow
login therefore cannot be interrupted by the old shell. One expected identity revision from that
login is permitted; further/unrelated changes invalidate the flow and its pending results. Ordinary
session rotation does not change identity. Existing secure-store format and refresh replay protection
are unchanged. Returning to the shell always triggers a new backend context load.

One flow generation guards registration, login, acceptance and context reads. Busy guards reject
rapid duplicate submissions; dialogs are dismissed on unrelated account changes. Late responses
cannot restore a cancelled flow, expose old context state or claim acceptance for another account.
Cancellation clears local state but cannot reverse an already-dispatched backend write. Such a
registration/acceptance may still commit. The UI states this and never automatically repeats it.

## Errors and uncertain outcomes

- 400 `invalidInvitation` covers invalid, expired, revoked, reused/already-accepted invitations,
  wrong email, and loss of the inviter's Owner grant. Unverified accounts normally fail earlier at login/session validation. Display a safe combined message;
  do not guess the precise reason or label an already-used credential as newly accepted.
- 409 `invitationConflict` covers membership conflicts, including historical memberships rejected
  by current backend policy. Reload current access or consult the business owner; never restore Owner.
- 401 asks for login again in the active flow, preserving the credential for that explicit login.
  An unrelated login/logout signal still clears it. 403/404/429 and validation failures have safe
  Turkish messages; arbitrary server/detail/provider text is never rendered.
- Registration network/timeout/unknown results are uncertain. The UI offers verification/login
  first, or a deliberate fresh registration submission. There is no hidden retry.
- Acceptance network/timeout/unknown results remain unconfirmed. Refresh current contexts or choose
  a deliberate retry through another confirmation dialog. A contexts snapshot is not proof that this
  specific invitation was accepted, even if an Employee context is now visible.
- Confirmed acceptance with failed context refresh stays confirmed, with its credential already
  cleared. Retry only the GET; acceptance cannot be replayed from this completed flow.
- Logout clears pending memory immediately. Storage-deletion failure exposes cleanup retry;
  network failure returns to login while reporting unconfirmed server revocation.

All roles come from `/auth/contexts`. The flow never adds Customer, Employee or Owner locally.
An employee-only response cannot create an Owner destination. Existing legitimate server Owner or
Customer contexts remain available through the normal shell; acceptance does not invent them.

## Limitations and backend gaps

There is no preview or verified current-account email endpoint, and acceptance returns no business
or invitation receipt. Errors intentionally combine several failure reasons. Consequently, the app
cannot confirm a destination name before acceptance, reliably diagnose account mismatch versus
expiry, or reconcile a lost acceptance response to a particular invitation. There are no mutation
idempotency keys; deliberate retries can return `invalidInvitation` after an earlier success.

No customer profile/business is created by this registration endpoint. The UI cannot turn an
employee into a Customer without a separate future backend capability. Delivery remains the existing
non-durable backend email mechanism; this phase adds no SMTP credentials or delivery guarantee.
Role homes are still temporary and other operational features remain unimplemented.

Automated tests use synthetic credentials, mock HTTP and mock storage. They compare credential
matches as booleans and avoid raw credential diagnostics. They do not prove live API/PostgreSQL,
email delivery or physical-device behavior. No database or migration commands are part of testing.

## Phase 4 validation and changed files

Validation: `flutter analyze` reported no issues; `flutter test` passed all 140 tests;
`git diff --check` passed. No live backend, database, email-delivery or device tests were run.

Modified files:

- `mobile/README.md`
- `mobile/lib/main.dart`
- `mobile/lib/app/app.dart`
- `mobile/lib/app/routing/app_router.dart`
- `mobile/lib/core/network/README.md`
- `mobile/lib/features/auth/presentation/auth_form_screen.dart`
- `mobile/lib/features/auth/presentation/session_controller.dart`
- `mobile/lib/features/auth/presentation/session_screen.dart`
- `mobile/lib/features/auth/presentation/verification_screen.dart`
- `mobile/lib/features/employee/README.md`
- `mobile/lib/features/welcome/presentation/welcome_screen.dart`
- `mobile/test/session_controller_test.dart`

New files:

- `mobile/docs/employee-invitation-redemption.md`
- `mobile/lib/features/invitations/domain/invitation_redemption_service.dart`
- `mobile/lib/features/invitations/data/api_invitation_redemption_service.dart`
- `mobile/lib/features/invitations/presentation/invitation_redemption_controller.dart`
- `mobile/lib/features/invitations/presentation/invitation_redemption_screen.dart`
- `mobile/test/invitation_redemption_service_test.dart`
- `mobile/test/invitation_redemption_controller_test.dart`
- `mobile/test/invitation_redemption_widget_test.dart`
- `mobile/test/invitation_redemption_api_flow_test.dart`
- `mobile/test/support/redemption_fakes.dart`

## Phase 5 follow-up

The mock-only limitation above describes Phase 4's original validation. Current real HTTP,
PostgreSQL and iOS simulator evidence, with remaining device/email limitations, is recorded in
[Phase 5 readiness](../../docs/validation/employee-invitations-phase5.md).
