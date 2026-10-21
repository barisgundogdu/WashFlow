# WashFlow mobile

Flutter foundation for one Turkish Android/iOS application, with iOS as the initial development target. This is the foundation portion of [Issue #9](https://github.com/barisgundogdu/WashFlow/issues/9), using [SRS v0.2](../docs/requirements/WashFlow_SRS_v0.2.md) sections 3.1 and 10 and NFR-05. Authentication screens and the remaining issue criteria are pending; Issue #9 is not complete.

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
- `lib/app/routing/`: Flutter Navigator route factory; `/` displays the welcome placeholder, and unknown routes display a Turkish fallback. No role routes or authorization behavior yet.
- `lib/features/welcome/presentation/`: responsive placeholder UI.
- `lib/features/owner/`, `employee/`, `customer/`: documented homes for future role features; folders do not imply permissions or implemented behavior.
- `lib/core/network/`: documented boundary for future ASP.NET Core transport integration.

Use feature-local presentation, domain, and data layers when behavior is introduced. Shared features can sit alongside role folders to avoid duplicating login or shared components. Dependencies flow from presentation toward feature contracts; future data adapters implement those contracts and are wired at the app root. No HTTP package, state-management framework, or additional routing package is needed for this single-screen foundation. Flutter SDK localization supplies Turkish framework messages; screen strings are currently Turkish literals.

No authentication, role selection, API calls, persistence, or business features are implemented. Backend authorization and tenant/customer ownership checks remain mandatory in later work. Unresolved SRS policies remain undecided.
