import 'dart:async';

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
}) => SchedulingController(
  service: s,
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
void main() {
  test('calendar empty, failure, retry and valid local clock', () async {
    final s = FakeScheduling(), c = controller(FakeScheduling());
    c.dispose();
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
