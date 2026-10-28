import 'package:flutter_test/flutter_test.dart';
import 'package:washflow/features/scheduling/domain/scheduling.dart';

import 'support/scheduling_fakes.dart';

void main() {
  test(
    'Istanbul input and previous UTC calendar date never use device zone',
    () {
      final clock = BusinessClock('Europe/Istanbul');
      expect(
        clock.resolve(DateTime.utc(2030, 1, 2, 0, 30)),
        DateTime.utc(2030, 1, 1, 21, 30),
      );
      expect(
        localWire(clock.local(DateTime.utc(2030, 1, 1, 21, 30))),
        '2030-01-02T00:30',
      );
    },
  );
  test('DST gap is rejected; fold requires explicit valid offset', () {
    final clock = BusinessClock('Europe/London');
    expect(clock.candidates(DateTime.utc(2030, 3, 31, 1, 30)), isEmpty);
    expect(
      () => clock.resolve(DateTime.utc(2030, 3, 31, 1, 30)),
      throwsFormatException,
    );
    final fold = DateTime.utc(2030, 10, 27, 1, 30);
    expect(clock.candidates(fold), hasLength(2));
    expect(() => clock.resolve(fold), throwsFormatException);
    expect(
      clock.resolve(fold, offsetMinutes: 60),
      DateTime.utc(2030, 10, 27, 0, 30),
    );
    expect(
      clock.resolve(fold, offsetMinutes: 0),
      DateTime.utc(2030, 10, 27, 1, 30),
    );
    expect(
      () => clock.resolve(fold, offsetMinutes: 180),
      throwsFormatException,
    );
  });
  test('unknown business zone cannot fall back to local machine', () {
    expect(() => BusinessClock('Invalid/Example'), throwsA(anything));
  });
  test(
    'typed snapshots preserve exact price, version and retained history',
    () {
      final a = Appointment.fromJson(appointmentJson());
      expect(a.price.display, '450,10 TL');
      expect(a.version, 10);
      expect(a.employeeIds, {employeeId});
      expect(a.events.single.kind, 'Created');
      expect(
        Appointment.fromJson(appointmentJson(status: 'Cancelled')).employeeIds,
        {employeeId},
      );
    },
  );
  test('invalid enums, unsafe numbers and offsetless instants fail closed', () {
    for (final patch in [
      {'status': 'Working'},
      {'source': 'Customer'},
      {'price': 450.10},
      {'version': 0},
      {'startsAtUtc': '2030-01-02T10:00:00'},
      {'category': 'Truck'},
      {'currency': 'EUR'},
    ]) {
      expect(
        () => Appointment.fromJson({...appointmentJson(), ...patch}),
        throwsA(anything),
      );
    }
  });
  test('configuration and availability validation', () {
    expect(const WorkAreaInput('', 2).validate(), isNotNull);
    expect(const WorkAreaInput('Alan', 0).validate(), isNotNull);
    expect(const WorkAreaInput('Alan', 2).validate(), isNull);
    expect(
      AvailabilityInput(DateTime.utc(2030), DateTime.utc(2029)).validate(),
      isNotNull,
    );
    expect(
      AvailabilityInput(
        DateTime.utc(2030),
        DateTime.utc(2030, 1, 1, 1),
      ).toJson()['startsAtUtc'],
      '2030-01-01T00:00:00.000Z',
    );
  });
  test('attempt is immutable and UUID v4; employee ordering is canonical', () {
    final a = BookingAttempt(bookingPayload(employees: [employeeId, areaId]));
    final b = BookingAttempt(bookingPayload(employees: [areaId, employeeId]));
    expect(a.fingerprint, b.fingerprint);
    expect(
      a.clientRequestId,
      matches(
        RegExp(
          r'^[a-f0-9]{8}-[a-f0-9]{4}-4[a-f0-9]{3}-[89ab][a-f0-9]{3}-[a-f0-9]{12}$',
        ),
      ),
    );
    expect(a.clientRequestId, isNot(b.clientRequestId));
    expect(
      a.toJson().keys,
      containsAll([
        'clientRequestId',
        'expectedServiceVersion',
        'expectedVehicleVersion',
        'startLocal',
        'source',
      ]),
    );
    expect(a.toJson().containsKey('price'), isFalse);
    expect(a.toJson().containsKey('workAreaId'), isFalse);
    expect(
      () => a.payload.schedule.employeeMembershipIds.add(areaId),
      throwsUnsupportedError,
    );
  });
}
