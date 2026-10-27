import 'dart:async';

import 'package:flutter_test/flutter_test.dart';
import 'package:washflow/features/auth/domain/auth_service.dart';
import 'package:washflow/features/customer_vehicles/domain/customer_vehicles.dart';
import 'package:washflow/features/service_catalog/domain/service_catalog.dart';
import 'package:washflow/features/service_catalog/presentation/catalog_controller.dart';

import 'support/catalog_fakes.dart';
import 'support/session_fakes.dart';

void main() {
  late FakeCatalog service;
  late CatalogController c;
  late bool current;
  late int failures;
  setUp(() {
    service = FakeCatalog();
    current = true;
    failures = 0;
    c = CatalogController(
      service: service,
      business: businessA,
      isCurrent: () => current,
      onAuthorizationFailure: (_) async {
        failures++;
      },
    );
  });
  tearDown(() => c.dispose());
  test('list filter pagination and fresh detail', () async {
    await c.load(nextPage: 2);
    expect(service.lastPage, 2);
    expect(c.items.single.businessId, businessA);
    c.filter('disabled');
    await Future<void>.delayed(Duration.zero);
    expect(service.lastStatus, 'disabled');
    await c.open(serviceId);
    expect(service.reads, 1);
    expect(c.selected!.version, 2);
  });
  test('save duplicate prevention followed by authoritative GET', () async {
    await c.open(serviceId);
    c.edit();
    final pending = Completer<AuthResult<CatalogServiceRecord>>();
    service.saveHandler = () => pending.future;
    final first = c.save(c.draft!);
    await c.save(c.draft!);
    expect(service.saves, 1);
    pending.complete(AuthSuccess(catalogRecord(version: 3)));
    await first;
    expect(service.reads, 2);
    expect(c.selected!.version, 2);
    expect(c.draft, isNull);
    expect(c.requiresReload, isFalse);
  });
  for (final code in [
    AuthErrorCode.conflict,
    AuthErrorCode.network,
    AuthErrorCode.timeout,
    AuthErrorCode.unavailable,
  ]) {
    test('write $code locks until explicit reconciliation', () async {
      await c.open(serviceId);
      c.edit();
      service.saveHandler = () async => AuthFailure(AuthError(code: code));
      await c.save(c.draft!);
      expect(c.requiresReload, isTrue);
      await c.save(c.draft!);
      expect(service.saves, 1);
      await c.load();
      expect(c.requiresReload, isFalse);
      expect(c.draft, isNull);
    });
  }
  test('confirmed write failed GET permits recovery only', () async {
    await c.open(serviceId);
    c.edit();
    service.readHandler = () async =>
        AuthFailure(AuthError(code: AuthErrorCode.network));
    await c.save(c.draft!);
    expect(c.requiresReload, isTrue);
    expect(c.message, contains('Kaydetme onaylandı'));
    expect(service.saves, 1);
  });
  test('revoked grant clears tenant data before context refresh', () async {
    await c.open(serviceId);
    c.edit();
    service.saveHandler = () async =>
        AuthFailure(AuthError(code: AuthErrorCode.forbidden));
    await c.save(c.draft!);
    expect(failures, 1);
    expect(c.blocked, isTrue);
    expect(c.items, isEmpty);
    expect(c.draft, isNull);
    expect(c.selected, isNull);
  });
  test('cross tenant response never displayed', () async {
    service.listHandler = () async =>
        AuthSuccess(RecordPage([catalogRecord(business: businessB)], 1, false));
    await c.load();
    expect(c.items, isEmpty);
    expect(c.message, isNotNull);
  });
  test('stale reads and stale saves after context switch ignored', () async {
    final pending = Completer<AuthResult<RecordPage<CatalogServiceRecord>>>();
    service.listHandler = () => pending.future;
    final loading = c.load();
    current = false;
    pending.complete(AuthSuccess(RecordPage([catalogRecord()], 1, false)));
    await loading;
    expect(c.items, isEmpty);
    current = true;
    await c.open(serviceId);
    c.edit();
    final save = Completer<AuthResult<CatalogServiceRecord>>();
    service.saveHandler = () => save.future;
    final saving = c.save(c.draft!);
    current = false;
    save.complete(AuthSuccess(catalogRecord()));
    await saving;
    expect(service.reads, 1);
  });
  test('newer search supersedes earlier list', () async {
    final pending = Completer<AuthResult<RecordPage<CatalogServiceRecord>>>();
    service.listHandler = () => pending.future;
    final loading = c.load();
    c.search('Yeni');
    pending.complete(AuthSuccess(RecordPage([catalogRecord()], 1, false)));
    await loading;
    expect(c.items, isEmpty);
  });
  test('archive duplicate blocked and uncertainty locks', () async {
    await c.open(serviceId);
    final pending = Completer<AuthResult<void>>();
    service.archiveHandler = () => pending.future;
    final first = c.archive();
    await c.archive();
    expect(service.archives, 1);
    pending.complete(AuthFailure(AuthError(code: AuthErrorCode.timeout)));
    await first;
    expect(c.requiresReload, isTrue);
  });
}
