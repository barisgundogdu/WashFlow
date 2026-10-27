import 'dart:async';

import 'package:flutter_test/flutter_test.dart';
import 'package:washflow/features/auth/domain/auth_service.dart';
import 'package:washflow/features/business_settings/domain/business_settings.dart';
import 'package:washflow/features/business_settings/presentation/business_settings_controller.dart';

import 'support/settings_fakes.dart';
import 'support/session_fakes.dart';

void main() {
  late FakeSettings service;
  late BusinessSettingsController c;
  bool current = true;
  int failures = 0;
  setUp(() {
    service = FakeSettings();
    current = true;
    failures = 0;
    c = BusinessSettingsController(
      businessId: businessA,
      service: service,
      isCurrent: () => current,
      onAuthorizationFailure: (_) async {
        failures++;
      },
    );
  });
  tearDown(() => c.dispose());
  test('load edit save reload authoritative version and hours', () async {
    await c.load();
    expect(c.dirty, isFalse);
    c.field('name', 'Changed');
    c.field('timeZoneId', 'Europe/London');
    c.hours([for (var i = 1; i <= 7; i++) OpeningDay(i, true, null, null)]);
    await c.save();
    expect(c.draft!.version, 2);
    expect(c.dirty, isFalse);
    expect(service.calls, [
      'read:$businessA',
      'save:$businessA',
      'read:$businessA',
    ]);
  });
  test('invalid fields and hours never dispatch writes', () async {
    await c.load();
    c.field('name', '');
    await c.save();
    expect(service.calls.length, 1);
    c.field('name', 'Name');
    c.hours([const OpeningDay(1, false, 1000, 500)]);
    await c.save();
    expect(service.calls.length, 1);
  });
  test('duplicate saves and edits blocked during pending write', () async {
    await c.load();
    c.field('name', 'Changed');
    final pending = Completer<AuthResult<BusinessSettings>>();
    service.onSave = (_) => pending.future;
    final f = c.save();
    await c.save();
    c.field('name', 'Unexpected');
    expect(c.draft!.fields['name'], 'Changed');
    expect(service.calls.where((s) => s.startsWith('save')).length, 1);
    pending.complete(AuthSuccess(settings(version: 2)));
    await f;
  });
  for (final code in [
    AuthErrorCode.conflict,
    AuthErrorCode.network,
    AuthErrorCode.timeout,
    AuthErrorCode.unexpected,
  ]) {
    test('$code requires deliberate reload and never replays', () async {
      await c.load();
      c.field('name', 'Changed');
      service.onSave = (_) async => AuthFailure(AuthError(code: code));
      await c.save();
      expect(c.requiresReload, isTrue);
      expect(c.message, isNot(contains('kaydedildi')));
      await c.save();
      expect(service.calls.where((s) => s.startsWith('save')).length, 1);
      await c.load();
      expect(c.requiresReload, isFalse);
    });
  }
  test('confirmed save with failed GET permits GET recovery only', () async {
    await c.load();
    c.field('name', 'Changed');
    service.onSave = (input) async {
      service.onRead = (_) async =>
          AuthFailure(AuthError(code: AuthErrorCode.network));
      return AuthSuccess(input);
    };
    await c.save();
    expect(c.requiresReload, isTrue);
    await c.save();
    expect(service.calls.where((s) => s.startsWith('save')).length, 1);
  });
  for (final code in [AuthErrorCode.unauthenticated, AuthErrorCode.forbidden]) {
    test('$code clears data and delegates revalidation', () async {
      await c.load();
      c.field('name', 'Changed');
      service.onSave = (_) async => AuthFailure(AuthError(code: code));
      await c.save();
      expect(c.draft, isNull);
      expect(c.blocked, isTrue);
      expect(failures, 1);
    });
  }
  test('late account or tenant read cannot populate controller', () async {
    final p = Completer<AuthResult<BusinessSettings>>();
    service.onRead = (_) => p.future;
    final f = c.load();
    current = false;
    p.complete(AuthSuccess(settings()));
    await f;
    expect(c.draft, isNull);
  });
  test('late mutation cannot display success after scope changed', () async {
    await c.load();
    c.field('name', 'Changed');
    final p = Completer<AuthResult<BusinessSettings>>();
    service.onSave = (_) => p.future;
    final f = c.save();
    current = false;
    p.complete(AuthSuccess(settings()));
    await f;
    expect(service.calls.length, 2);
    expect(c.message, isNull);
  });
  test('foreign response is rejected', () async {
    service.onRead = (_) async => AuthSuccess(settings(id: businessB));
    await c.load();
    expect(c.draft, isNull);
    expect(c.requiresReload, isTrue);
  });
}
