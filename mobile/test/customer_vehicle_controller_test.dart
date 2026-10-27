import 'dart:async';

import 'package:flutter_test/flutter_test.dart';
import 'package:washflow/features/auth/domain/auth_service.dart';
import 'package:washflow/features/customer_vehicles/domain/customer_vehicles.dart';
import 'package:washflow/features/customer_vehicles/presentation/management_controllers.dart';

import 'support/customer_vehicle_fakes.dart';
import 'support/session_fakes.dart';

void main() {
  late FakeCustomerVehicles service;
  bool current = true;
  int failures = 0;
  setUp(() {
    service = FakeCustomerVehicles();
    current = true;
    failures = 0;
  });
  DirectoryController directory({String? business = businessA}) =>
      DirectoryController(
        service: service,
        business: business,
        isCurrent: () => current,
        onAuthorizationFailure: (_) async {
          failures++;
        },
      );
  RecordEditorController editor({bool vehicle = false}) =>
      RecordEditorController(
        service: service,
        business: businessA,
        customer: vehicle ? customerId : null,
        vehicle: null,
        customerRecord: null,
        isCurrent: () => current,
        onAuthorizationFailure: (_) async {
          failures++;
        },
      );
  void valid(RecordEditorController c) => c.edit('fullName', 'Test person');
  test('customer create confirmed exactly once', () async {
    final c = editor();
    addTearDown(c.dispose);
    valid(c);
    await c.save(isVehicle: false);
    await c.save(isVehicle: false);
    expect(c.confirmed, isTrue);
    expect(service.calls.length, 1);
  });
  test('invalid customer and vehicle never submit', () async {
    final c = editor();
    addTearDown(c.dispose);
    await c.save(isVehicle: false);
    await c.save(isVehicle: true);
    expect(service.calls, isEmpty);
    expect(c.message, isNotNull);
  });
  test(
    'duplicate pending mutation blocked and scope change drops result',
    () async {
      final c = editor();
      addTearDown(c.dispose);
      valid(c);
      final p = Completer<AuthResult<LocalCustomerRecord>>();
      service.onSaveCustomer = (b, d) => p.future;
      final f = c.save(isVehicle: false);
      await c.save(isVehicle: false);
      current = false;
      p.complete(AuthSuccess(LocalCustomerRecord.fromJson(customerJson())));
      await f;
      expect(c.confirmed, isFalse);
      expect(service.calls.length, 1);
    },
  );
  for (final code in [
    AuthErrorCode.network,
    AuthErrorCode.timeout,
    AuthErrorCode.conflict,
    AuthErrorCode.notFound,
  ]) {
    test('$code locks editor and never replays', () async {
      final c = editor();
      addTearDown(c.dispose);
      valid(c);
      service.onSaveCustomer = (b, d) async =>
          AuthFailure(AuthError(code: code));
      await c.save(isVehicle: false);
      await c.save(isVehicle: false);
      expect(c.requiresReload, isTrue);
      expect(c.confirmed, isFalse);
      expect(service.calls.length, 1);
    });
  }
  test('validation failure preserves draft and permits correction', () async {
    final c = editor();
    addTearDown(c.dispose);
    valid(c);
    service.onSaveCustomer = (b, d) async =>
        AuthFailure(AuthError(code: AuthErrorCode.validation));
    await c.save(isVehicle: false);
    expect(c.canEdit, isTrue);
    expect(c.fields['fullName'], 'Test person');
  });
  test('forbidden delegates revalidation', () async {
    final c = editor();
    addTearDown(c.dispose);
    valid(c);
    service.onSaveCustomer = (b, d) async =>
        AuthFailure(AuthError(code: AuthErrorCode.forbidden));
    await c.save(isVehicle: false);
    expect(c.blocked, isTrue);
    expect(failures, 1);
  });
  test('stale search read does not replace new result', () async {
    final c = directory();
    addTearDown(c.dispose);
    final old = Completer<AuthResult<RecordPage<LocalCustomerRecord>>>();
    service.onCustomers = (b, q, p) => q == ''
        ? old.future
        : Future.value(
            AuthSuccess(
              RecordPage(
                [LocalCustomerRecord.fromJson(customerJson(name: 'New'))],
                1,
                false,
              ),
            ),
          );
    final f = c.load();
    c.search('new');
    await Future<void>.delayed(const Duration(milliseconds: 380));
    old.complete(
      AuthSuccess(
        RecordPage(
          [LocalCustomerRecord.fromJson(customerJson(name: 'Old'))],
          1,
          false,
        ),
      ),
    );
    await f;
    expect(c.customers.single.fullName, 'New');
  });
  test(
    'debounce coalesces typing and disposed timer cannot dispatch',
    () async {
      final c = directory();
      c.search('a');
      c.search('ay');
      c.search('ays');
      await Future<void>.delayed(const Duration(milliseconds: 380));
      expect(service.calls.length, 1);
      c.search('later');
      c.dispose();
      await Future<void>.delayed(const Duration(milliseconds: 380));
      expect(service.calls.length, 1);
    },
  );
  test(
    'tenant response is checked and account change drops late list',
    () async {
      final c = directory();
      addTearDown(c.dispose);
      service.people = [
        LocalCustomerRecord.fromJson(customerJson(business: businessB)),
      ];
      await c.load();
      expect(c.customers, isEmpty);
      final pending = Completer<AuthResult<RecordPage<LocalCustomerRecord>>>();
      service.onCustomers = (b, q, p) => pending.future;
      final f = c.load();
      current = false;
      pending.complete(
        AuthSuccess(
          RecordPage([LocalCustomerRecord.fromJson(customerJson())], 1, false),
        ),
      );
      await f;
      expect(c.customers, isEmpty);
    },
  );
  test(
    'archive uses owning record once and keeps uncertain result unconfirmed',
    () async {
      final v = VehicleRecord.fromJson(
        vehicleJson(business: null, customer: null),
      );
      final c = RecordEditorController(
        service: service,
        business: null,
        customer: null,
        vehicle: v,
        customerRecord: null,
        isCurrent: () => true,
        onAuthorizationFailure: (_) async {},
      );
      addTearDown(c.dispose);
      service.onArchive = (_) async =>
          AuthFailure(AuthError(code: AuthErrorCode.timeout));
      await c.archive();
      await c.archive();
      expect(c.requiresReload, isTrue);
      expect(c.confirmed, isFalse);
      expect(service.calls.length, 1);
    },
  );
}
