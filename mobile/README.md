# WashFlow mobile

Flutter foundation for one Turkish Android/iOS application, with iOS as the initial development target. This is the foundation portion of [Issue #9](https://github.com/barisgundogdu/WashFlow/issues/9), using [SRS v0.2](../docs/requirements/WashFlow_SRS_v0.2.md) sections 3.1 and 10 and NFR-05. Authentication UI is implemented; real authentication is pending. Issue #9 is not marked complete: no commit is created automatically.

## Run and verify

Developed with Flutter 3.47.7 / Dart 3.13.5. From `mobile/`:

```sh
flutter pub get
flutter devices
flutter run -d <ios-simulator-id>
flutter analyze
flutter test
flutter build ios --simulator
```

For Android, select an Android emulator/device with `flutter run -d <device-id>`. Only Android and iOS platform projects are generated. Generated `com.example` identifiers are development placeholders; confirm organization identifiers and signing before distribution.

## Structure and decisions

- `lib/main.dart`: starts the app; `lib/app/app.dart`: composition root, theme, and Turkish SDK localization.
- `lib/app/routing/`: Flutter Navigator route factory; `/` displays welcome; `/login`, `/register`, `/register/customer`, and `/register/business` provide authentication UI. Unknown routes display a Turkish fallback.
- `lib/features/welcome/presentation/`: responsive branded entry screen with login and registration actions.
- `lib/features/owner/`, `employee/`, `customer/`: documented homes for future role features; folders do not imply permissions or implemented behavior.
- `lib/core/network/`: documented boundary for future ASP.NET Core transport integration.

Use feature-local presentation, domain, and data layers when behavior is introduced. Shared features can sit alongside role folders to avoid duplicating login or shared components. Dependencies flow from presentation toward feature contracts; future data adapters implement those contracts and are wired at the app root. No HTTP package, state-management framework, or additional routing package is needed for this single-screen foundation. Flutter SDK localization supplies Turkish framework messages; screen strings are currently Turkish literals.

No real authentication, API calls, persistence, or business features are implemented. Backend authorization and tenant/customer ownership checks remain mandatory in later work. Unresolved SRS policies remain undecided.

## Authentication UI (Issue #9)

Shared forms live in `lib/features/auth/presentation/`; `domain/auth_submission.dart` is an injectable UI submission boundary, not a finalized backend API contract. Its default implementation always reports service unavailability. There is no success/session result and credentials are neither persisted nor logged. Autofill context is cancelled when the form is disposed so preview credentials are not offered for saving.

Customer registration collects full name, email, password, and confirmation. Business registration adds business name and labels the name field for the owner. Validation checks required fields, basic email syntax, and matching passwords; no password complexity policy, employee onboarding process, or business approval policy is invented. Related requirements: FR-01, FR-20, NFR-05. Server security and registration policies remain pending.

`AuthorizedContextPicker` accepts only supplied options and emits a selected identifier. It is tested with fixtures and intentionally has no public route or sample memberships. Future server-authorized context integration relates to FR-27 and must validate selection on the server.

Forms use scrollable constrained layouts, safe areas, keyboard actions, autofill hints, password visibility, and pending/error states. Pending states can be tested using an injected completer; no artificial delay or fake account is used in the running app. Password recovery is an explicitly unavailable placeholder.

Verification: `flutter analyze` passed; `flutter test` passed all 9 tests (navigation, validators, password visibility, unavailable submissions, pending/error handling, compact keyboard layout, authorized-option selection, Turkish welcome, unknown route). `flutter build ios --simulator` passed; `flutter run --no-resident` successfully launched on the iPhone 18 Pro simulator. Android native build and full device usability evaluation have not been performed.
