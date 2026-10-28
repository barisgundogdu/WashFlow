import 'dart:async';
import 'dart:convert';

import 'package:flutter_test/flutter_test.dart';
import 'package:washflow/features/auth/domain/auth_service.dart';
import 'package:washflow/features/scheduling/domain/scheduling.dart';
import 'package:washflow/features/scheduling/presentation/scheduling_controller.dart';

import 'support/scheduling_fakes.dart';
import 'support/catalog_fakes.dart';
import 'support/customer_vehicle_fakes.dart';
import 'support/settings_fakes.dart';
import 'support/session_fakes.dart';

SchedulingController controller(
  FakeScheduling s, {
  bool Function()? current,
  Future<void> Function(AuthError)? failure,
  BookingAttemptStore? store,
}) => SchedulingController(
  service: s,
  attemptStore: store,
  settings: FakeSettings()
    ..value = settings().change(
      fields: {...settings().fields, 'timeZoneId': 'Europe/Istanbul'},
    ),
  catalog: FakeCatalog(),
  customers: FakeCustomerVehicles(),
  business: businessA,
  isCurrent: current ?? () => true,
  onAuthorizationFailure: failure ?? (_) async {},
);

class MemoryAttemptStore implements BookingAttemptStore {
  String? value;
  bool fail = false;
  @override
  Future<String?> read() async {
    if (fail) throw StateError('storage');
    return value;
  }

  @override
  Future<void> write(String v) async {
    if (fail) throw StateError('storage');
    value = v;
  }

  @override
  Future<void> clear() async {
    if (fail) throw StateError('storage');
    value = null;
  }
}

void main() {
  test('pending create survives controller restart and replays original versions and UUID', () async {
    final store = MemoryAttemptStore();
    final s = FakeScheduling()
      ..createHandler = () async =>
          AuthFailure(AuthError(code: AuthErrorCode.timeout));
    final first = controller(s, store: store);
    await first.book(bookingPayload());
    final original = s.creates.single.toJson();
    expect(jsonDecode(store.value!), original);
    first.dispose();
    final recovered = controller(s, store: store);
    addTearDown(recovered.dispose);
    await recovered.loadCalendar();
    expect(recovered.uncertainBooking, isTrue);
    expect(recovered.attempt!.toJson(), original);
    s.createHandler = null;
    await recovered.book(recovered.attempt!.payload);
    expect(s.creates.last.toJson(), original);
    expect(store.value, isNull);
  });
  test(
    'storage failure prevents an unjournaled network create and supports retry',
    () async {
      final store = MemoryAttemptStore()..fail = true;
      final s = FakeScheduling(), c = controller(FakeScheduling());
      c.dispose();
      final actual = controller(s, store: store);
      addTearDown(actual.dispose);
      await actual.book(bookingPayload());
      expect(s.creates, isEmpty);
      expect(actual.uncertainBooking, isTrue);
      expect(actual.lastError!.code, AuthErrorCode.storage);
      store.fail = false;
      await actual.book(bookingPayload());
      expect(s.creates, hasLength(1));
    },
  );
  test(
    'corrupt journal fails closed; explicit abandonment must clear storage',
    () async {
      final store = MemoryAttemptStore()..value = 'invalid json';
      final s = FakeScheduling();
      final actual = controller(s, store: store);
      addTearDown(actual.dispose);
      await actual.loadCalendar();
      expect(actual.loaded, isTrue);
      expect(s.calendars, 1);
      expect(actual.recoveryBlocked, isTrue);
      await actual.book(bookingPayload());
      expect(s.creates, isEmpty);
      expect(actual.lastError!.code, AuthErrorCode.storage);
      expect(await actual.abandonAttempt(), isTrue);
      expect(store.value, isNull);
      await actual.loadCalendar();
      expect(actual.loaded, isTrue);
    },
  );
  test(
    'calendar rechecks business timezone and rejects invalid page locally',
    () async {
      final s = FakeScheduling();
      final c = controller(s);
      addTearDown(c.dispose);
      await c.loadCalendar();
      final date = c.date;
      final settingsService = c.settings as FakeSettings;
      settingsService.value = settings().change(
        fields: {...settings().fields, 'timeZoneId': 'Europe/London'},
      );
      await c.loadCalendar();
      expect(c.clock!.zoneId, 'Europe/London');
      expect(c.date, date);
      final count = s.calendars;
      await c.loadCalendar(nextPage: 10001);
      expect(s.calendars, count);
    },
  );
  test('calendar empty, failure, retry and valid local clock', () async {
    final s = FakeScheduling();
    final actual = controller(s);
    addTearDown(actual.dispose);
    await actual.loadCalendar();
    expect(actual.loaded, isTrue);
    expect(actual.appointments, isEmpty);
    expect(actual.clock!.zoneId, 'Europe/Istanbul');
    s.calendarHandler = () async =>
        AuthFailure(AuthError(code: AuthErrorCode.network));
    await actual.loadCalendar();
    expect(actual.loaded, isFalse);
    expect(actual.message, isNotNull);
    s.calendarHandler = null;
    s.appointments = [Appointment.fromJson(appointmentJson())];
    await actual.loadCalendar();
    expect(actual.appointments, hasLength(1));
  });
  test('timeout retries preserve exact payload/key; changed payload gets a new key', () async {
    final s = FakeScheduling();
    final c = controller(s);
    addTearDown(c.dispose);
    s.createHandler = () async =>
        AuthFailure(AuthError(code: AuthErrorCode.timeout));
    await c.book(bookingPayload());
    expect(c.uncertainBooking, isTrue);
    await c.book(bookingPayload());
    expect(s.creates, hasLength(2));
    expect(s.creates[0].toJson(), s.creates[1].toJson());
    await c.book(bookingPayload(time: '2030-01-02T11:00'));
    expect(s.creates, hasLength(2));
    s.createHandler = null;
    expect(await c.book(bookingPayload()), isA<Appointment>());
    expect(c.attempt, isNull);
    await c.book(bookingPayload(time: '2030-01-02T11:00'));
    expect(
      s.creates.last.clientRequestId,
      isNot(s.creates.first.clientRequestId),
    );
  });
  test(
    'known conflict retains key for retry, permits changed corrected payload',
    () async {
      final s = FakeScheduling();
      final c = controller(s);
      addTearDown(c.dispose);
      s.createHandler = () async => AuthFailure(
        SchedulingError(
          code: AuthErrorCode.conflict,
          reason: 'resourceConflict',
        ),
      );
      await c.book(bookingPayload());
      expect(c.uncertainBooking, isFalse);
      expect(c.message, contains('kapasitesi'));
      await c.book(bookingPayload());
      expect(s.creates.first.clientRequestId, s.creates.last.clientRequestId);
      await c.book(bookingPayload(time: '2030-01-02T11:00'));
      expect(
        s.creates.first.clientRequestId,
        isNot(s.creates.last.clientRequestId),
      );
    },
  );
  for (final code in [AuthErrorCode.forbidden, AuthErrorCode.unauthenticated]) {
    test('$code blocks mutations and delegates session revalidation', () async {
      final s = FakeScheduling();
      AuthError? received;
      final c = controller(
        s,
        failure: (e) async {
          received = e;
        },
      );
      addTearDown(c.dispose);
      s.calendarHandler = () async => AuthFailure(AuthError(code: code));
      await c.loadCalendar();
      expect(c.blocked, isTrue);
      expect(received!.code, code);
      await c.book(bookingPayload());
      expect(s.creates, isEmpty);
    });
  }
  test('late read after disposal or business switch cannot publish', () async {
    for (final dispose in [true, false]) {
      final s = FakeScheduling();
      var current = true;
      final c = controller(s, current: () => current);
      var notices = 0;
      c.addListener(() => notices++);
      final pending = Completer<AuthResult<List<Appointment>>>();
      s.calendarHandler = () => pending.future;
      final future = c.loadCalendar();
      await Future<void>.delayed(Duration.zero);
      if (dispose) {
        c.dispose();
      } else {
        current = false;
      }
      final before = notices;
      pending.complete(AuthSuccess([Appointment.fromJson(appointmentJson())]));
      await future;
      expect(c.appointments, isEmpty);
      expect(notices, before);
      if (!dispose) c.dispose();
    }
  });
  test('pending create disables duplicate submissions', () async {
    final s = FakeScheduling();
    final c = controller(s);
    addTearDown(c.dispose);
    final pending = Completer<AuthResult<Appointment>>();
    s.createHandler = () => pending.future;
    final first = c.book(bookingPayload());
    await c.book(bookingPayload());
    expect(s.creates, hasLength(1));
    pending.complete(AuthSuccess(Appointment.fromJson(appointmentJson())));
    await first;
  });
}
