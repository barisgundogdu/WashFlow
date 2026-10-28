import 'package:washflow/features/auth/domain/auth_service.dart';
import 'package:washflow/features/scheduling/domain/scheduling.dart';

import 'session_fakes.dart';
import 'catalog_fakes.dart' as catalog;
import 'customer_vehicle_fakes.dart' as cv;

const areaId = '88888888-8888-4888-8888-888888888888';
const employeeId = '99999999-9999-4999-8999-999999999999';
const appointmentId = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa';
const requirementId = 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb';
const availabilityId = 'cccccccc-cccc-4ccc-8ccc-cccccccccccc';
const reservationId = 'dddddddd-dddd-4ddd-8ddd-dddddddddddd';
Map<String, dynamic> areaJson({int version = 7}) => {
  'id': areaId,
  'businessId': businessA,
  'name': 'Yıkama alanı',
  'capacity': 2,
  'archivedAt': null,
  'version': version,
};
Map<String, dynamic> employeeJson() => {
  'membershipId': employeeId,
  'businessId': businessA,
  'name': 'Deniz Çalışan',
};
Map<String, dynamic> requirementJson() => {
  'id': requirementId,
  'businessId': businessA,
  'serviceId': catalog.serviceId,
  'category': 'Car',
  'workAreaId': areaId,
  'singleStageConfirmed': true,
  'version': 8,
};
Map<String, dynamic> availabilityJson() => {
  'id': availabilityId,
  'businessId': businessA,
  'membershipId': employeeId,
  'startsAtUtc': '2030-01-02T06:00:00Z',
  'endsAtUtc': '2030-01-02T15:00:00Z',
  'archivedAt': null,
  'version': 9,
};
Map<String, dynamic> appointmentJson({
  int version = 10,
  String status = 'Confirmed',
}) => {
  'id': appointmentId,
  'businessId': businessA,
  'version': version,
  'source': 'OwnerPhone',
  'status': status,
  'localCustomerId': cv.customerId,
  'localVehicleId': cv.vehicleId,
  'serviceId': catalog.serviceId,
  'workAreaId': areaId,
  'serviceName': 'Dış yıkama',
  'customerName': 'Ayşe Yılmaz',
  'vehiclePlate': '34 ABC 123',
  'category': 'Car',
  'price': '450.10',
  'currency': 'TRY',
  'activeMinutes': 20,
  'waitingMinutes': 120,
  'employees': 1,
  'timeZoneId': 'Europe/Istanbul',
  'startsAtUtc': '2030-01-02T07:00:00Z',
  'activeEndsAtUtc': '2030-01-02T07:20:00Z',
  'reservations': [
    {
      'id': reservationId,
      'workAreaId': null,
      'employeeMembershipId': employeeId,
      'startsAtUtc': '2030-01-02T07:00:00Z',
      'endsAtUtc': '2030-01-02T07:20:00Z',
      'releasedAt': status == 'Cancelled' ? '2026-10-27T12:00:00Z' : null,
    },
  ],
  'events': [
    {
      'sequence': 1,
      'kind': status == 'Cancelled' ? 'Cancelled' : 'Created',
      'status': status,
      'previousStart': null,
      'previousEnd': null,
      'newStart': '2030-01-02T07:00:00Z',
      'newEnd': '2030-01-02T07:20:00Z',
      'occurredAt': '2026-10-27T12:00:00Z',
    },
  ],
};
BookingPayload bookingPayload({
  String time = '2030-01-02T10:00',
  Iterable<String> employees = const [employeeId],
}) => BookingPayload(
  customerId: cv.customerId,
  vehicleId: cv.vehicleId,
  serviceId: catalog.serviceId,
  serviceVersion: 1,
  vehicleVersion: 1,
  schedule: ScheduleInput(
    startLocal: time,
    utcOffsetMinutes: 180,
    employees: employees,
  ),
);

class FakeScheduling implements SchedulingService {
  List<Appointment> appointments = [];
  List<WorkArea> workAreas = [WorkArea.fromJson(areaJson())];
  List<EmployeeAvailability> windows = [
    EmployeeAvailability.fromJson(availabilityJson()),
  ];
  List<SchedulingRequirement> requirementValues = [
    SchedulingRequirement.fromJson(requirementJson()),
  ];
  final creates = <BookingAttempt>[];
  final schedules = <ScheduleInput>[];
  final versions = <int>[];
  final areaInputs = <WorkAreaInput>[];
  final availabilityInputs = <AvailabilityInput>[];
  int calendars = 0,
      details = 0,
      cancellations = 0,
      previews = 0,
      areaArchives = 0,
      availabilityArchives = 0,
      disables = 0;
  Future<AuthResult<List<Appointment>>> Function()? calendarHandler;
  Future<AuthResult<Appointment>> Function()? createHandler,
      detailHandler,
      rescheduleHandler;
  AuthError? error;
  @override
  Future<AuthResult<List<Appointment>>> calendar(
    String b,
    DateTime from,
    DateTime to, {
    int page = 1,
  }) async {
    calendars++;
    return calendarHandler == null
        ? AuthSuccess(appointments)
        : calendarHandler!();
  }

  @override
  Future<AuthResult<Appointment>> detail(String b, String id) async {
    details++;
    return detailHandler == null
        ? AuthSuccess(Appointment.fromJson(appointmentJson()))
        : detailHandler!();
  }

  @override
  Future<AuthResult<Appointment>> create(
    String b,
    BookingAttempt attempt,
  ) async {
    creates.add(attempt);
    return createHandler == null
        ? AuthSuccess(Appointment.fromJson(appointmentJson()))
        : createHandler!();
  }

  @override
  Future<AuthResult<List<WorkArea>>> areas(String b) async =>
      AuthSuccess(workAreas);
  @override
  Future<AuthResult<WorkArea>> saveArea(
    String b,
    WorkAreaInput input, {
    String? id,
  }) async {
    areaInputs.add(input);
    return error == null
        ? AuthSuccess(
            WorkArea.fromJson({
              ...areaJson(version: 11),
              'name': input.name,
              'capacity': input.capacity,
            }),
          )
        : AuthFailure(error!);
  }

  @override
  Future<AuthResult<void>> archiveArea(String b, WorkArea area) async {
    areaArchives++;
    return const AuthSuccess(null);
  }

  @override
  Future<AuthResult<List<SchedulingEmployee>>> employees(String b) async =>
      AuthSuccess([SchedulingEmployee.fromJson(employeeJson())]);
  @override
  Future<AuthResult<List<SchedulingRequirement>>> requirements(
    String b,
    String s,
  ) async => AuthSuccess(requirementValues);
  @override
  Future<AuthResult<SchedulingRequirement>> saveRequirement(
    String b,
    String s,
    String category,
    RequirementInput input,
  ) async => AuthSuccess(
    SchedulingRequirement.fromJson({
      ...requirementJson(),
      ...input.toJson(),
      'version': 12,
    }),
  );
  @override
  Future<AuthResult<SchedulingRequirement>> disableRequirement(
    String b,
    SchedulingRequirement r,
  ) async {
    disables++;
    return AuthSuccess(
      SchedulingRequirement.fromJson({
        ...requirementJson(),
        'singleStageConfirmed': false,
        'version': 13,
      }),
    );
  }

  @override
  Future<AuthResult<List<EmployeeAvailability>>> availability(
    String b,
    String e,
    DateTime from,
    DateTime to, {
    int page = 1,
  }) async => AuthSuccess(windows);
  @override
  Future<AuthResult<EmployeeAvailability>> saveAvailability(
    String b,
    String e,
    AvailabilityInput input, {
    String? id,
  }) async {
    availabilityInputs.add(input);
    return AuthSuccess(
      EmployeeAvailability.fromJson({
        ...availabilityJson(),
        ...input.toJson(),
        'version': 14,
      }),
    );
  }

  @override
  Future<AuthResult<void>> archiveAvailability(
    String b,
    EmployeeAvailability w,
  ) async {
    availabilityArchives++;
    return const AuthSuccess(null);
  }

  @override
  Future<AuthResult<AvailabilityPreview>> preview(
    String b,
    String s,
    String category,
    ScheduleInput input,
  ) async {
    previews++;
    return AuthSuccess(
      AvailabilityPreview.fromJson({
        'available': true,
        'startsAtUtc': '2030-01-02T07:00:00Z',
        'activeEndsAtUtc': '2030-01-02T07:20:00Z',
      }),
    );
  }

  @override
  Future<AuthResult<Appointment>> reschedule(
    String b,
    Appointment a,
    ScheduleInput input,
  ) async {
    schedules.add(input);
    versions.add(a.version);
    return rescheduleHandler == null
        ? AuthSuccess(Appointment.fromJson(appointmentJson(version: 11)))
        : rescheduleHandler!();
  }

  @override
  Future<AuthResult<Appointment>> cancel(String b, Appointment a) async {
    cancellations++;
    versions.add(a.version);
    return error == null
        ? AuthSuccess(
            Appointment.fromJson(
              appointmentJson(version: 11, status: 'Cancelled'),
            ),
          )
        : AuthFailure(error!);
  }
}
