import 'dart:convert';
import 'dart:math';

import 'package:timezone/data/latest.dart' as tzdata;
import 'package:timezone/timezone.dart' as tz;

import '../../auth/domain/auth_service.dart';
import '../../invitations/domain/invitation.dart';
import '../../service_catalog/domain/service_catalog.dart';
import '../../customer_vehicles/domain/customer_vehicles.dart';

String _s(Map<String, dynamic> j, String k) {
  if (j[k] is! String || (j[k] as String).isEmpty) {
    throw const FormatException();
  }
  return j[k];
}

int _n(Map<String, dynamic> j, String k) {
  if (j[k] is! int || j[k] < 1 || j[k] > 4294967295) {
    throw const FormatException();
  }
  return j[k];
}

DateTime schedulingInstant(Object? value) {
  if (value is! String || !RegExp(r'(Z|[+-]\d\d:\d\d)$').hasMatch(value)) {
    throw const FormatException();
  }
  return DateTime.parse(value).toUtc();
}

DateTime? _archived(Map<String, dynamic> j) =>
    j['archivedAt'] == null ? null : schedulingInstant(j['archivedAt']);
String dateWire(DateTime d) =>
    '${d.year.toString().padLeft(4, '0')}-${d.month.toString().padLeft(2, '0')}-${d.day.toString().padLeft(2, '0')}';
String timeWire(DateTime d) =>
    '${d.hour.toString().padLeft(2, '0')}:${d.minute.toString().padLeft(2, '0')}';
String localWire(DateTime d) => '${dateWire(d)}T${timeWire(d)}';
String localDisplay(DateTime d) =>
    '${d.day.toString().padLeft(2, '0')}.${d.month.toString().padLeft(2, '0')}.${d.year} ${timeWire(d)}';

/// Uses IANA data exclusively. Wall values are UTC-shaped containers, never device-local instants.
class BusinessClock {
  BusinessClock(this.zoneId) {
    if (!_initialized) {
      tzdata.initializeTimeZones();
      _initialized = true;
    }
    location = tz.getLocation(zoneId); // Unknown zone fails closed.
  }
  static bool _initialized = false;
  final String zoneId;
  late final tz.Location location;
  DateTime local(DateTime instant) =>
      tz.TZDateTime.from(instant.toUtc(), location);
  DateTime get today {
    final n = local(DateTime.now().toUtc());
    return DateTime.utc(n.year, n.month, n.day);
  }

  List<DateTime> candidates(DateTime wall) {
    final shaped = DateTime.utc(
      wall.year,
      wall.month,
      wall.day,
      wall.hour,
      wall.minute,
    );
    final offsets = location.zones.map((z) => z.offset).toSet();
    final values = <DateTime>[];
    for (final offset in offsets) {
      final instant = shaped.subtract(offset);
      final actual = local(instant);
      if (localWire(actual) == localWire(shaped) && actual.second == 0) {
        values.add(instant);
      }
    }
    values.sort();
    return values;
  }

  DateTime resolve(DateTime wall, {int? offsetMinutes}) {
    final values = candidates(wall)
        .where(
          (v) =>
              offsetMinutes == null ||
              local(v).timeZoneOffset.inMinutes == offsetMinutes,
        )
        .toList();
    if (values.length != 1) {
      throw const FormatException(
        'Saat mevcut değil veya UTC farkı seçilmelidir.',
      );
    }
    return values.single;
  }
}

class WorkArea {
  WorkArea.fromJson(Map<String, dynamic> j)
    : id = invitationId(j['id']),
      businessId = invitationId(j['businessId']),
      name = _s(j, 'name'),
      capacity = _n(j, 'capacity'),
      version = _n(j, 'version'),
      archivedAt = _archived(j);
  final String id, businessId, name;
  final int capacity, version;
  final DateTime? archivedAt;
}

class SchedulingEmployee {
  SchedulingEmployee.fromJson(Map<String, dynamic> j)
    : membershipId = invitationId(j['membershipId']),
      businessId = invitationId(j['businessId']),
      name = _s(j, 'name');
  final String membershipId, businessId, name;
}

class SchedulingRequirement {
  SchedulingRequirement.fromJson(Map<String, dynamic> j)
    : id = invitationId(j['id']),
      businessId = invitationId(j['businessId']),
      serviceId = invitationId(j['serviceId']),
      category = _s(j, 'category'),
      workAreaId = invitationId(j['workAreaId']),
      version = _n(j, 'version'),
      singleStageConfirmed = j['singleStageConfirmed'] as bool {
    if (!vehicleCategories.containsKey(category)) throw const FormatException();
  }
  final String id, businessId, serviceId, category, workAreaId;
  final int version;
  final bool singleStageConfirmed;
}

class EmployeeAvailability {
  EmployeeAvailability.fromJson(Map<String, dynamic> j)
    : id = invitationId(j['id']),
      businessId = invitationId(j['businessId']),
      membershipId = invitationId(j['membershipId']),
      startsAtUtc = schedulingInstant(j['startsAtUtc']),
      endsAtUtc = schedulingInstant(j['endsAtUtc']),
      archivedAt = _archived(j),
      version = _n(j, 'version') {
    if (!endsAtUtc.isAfter(startsAtUtc)) throw const FormatException();
  }
  final String id, businessId, membershipId;
  final DateTime startsAtUtc, endsAtUtc;
  final DateTime? archivedAt;
  final int version;
}

class AppointmentReservation {
  AppointmentReservation.fromJson(Map<String, dynamic> j)
    : id = invitationId(j['id']),
      workAreaId = j['workAreaId'] == null
          ? null
          : invitationId(j['workAreaId']),
      employeeMembershipId = j['employeeMembershipId'] == null
          ? null
          : invitationId(j['employeeMembershipId']),
      startsAtUtc = schedulingInstant(j['startsAtUtc']),
      endsAtUtc = schedulingInstant(j['endsAtUtc']),
      releasedAt = j['releasedAt'] == null
          ? null
          : schedulingInstant(j['releasedAt']) {
    if ((workAreaId == null) == (employeeMembershipId == null) ||
        !endsAtUtc.isAfter(startsAtUtc)) {
      throw const FormatException();
    }
  }
  final String id;
  final String? workAreaId, employeeMembershipId;
  final DateTime startsAtUtc, endsAtUtc;
  final DateTime? releasedAt;
}

class AppointmentEvent {
  AppointmentEvent.fromJson(Map<String, dynamic> j)
    : sequence = _n(j, 'sequence'),
      kind = _s(j, 'kind'),
      status = _s(j, 'status'),
      previousStart = j['previousStart'] == null
          ? null
          : schedulingInstant(j['previousStart']),
      previousEnd = j['previousEnd'] == null
          ? null
          : schedulingInstant(j['previousEnd']),
      newStart = schedulingInstant(j['newStart']),
      newEnd = schedulingInstant(j['newEnd']),
      occurredAt = schedulingInstant(j['occurredAt']) {
    if (!{'Created', 'Rescheduled', 'Cancelled'}.contains(kind) ||
        !{'Confirmed', 'Cancelled'}.contains(status)) {
      throw const FormatException();
    }
  }
  final int sequence;
  final String kind, status;
  final DateTime? previousStart, previousEnd;
  final DateTime newStart, newEnd, occurredAt;
}

class Appointment {
  Appointment.fromJson(Map<String, dynamic> j)
    : id = invitationId(j['id']),
      businessId = invitationId(j['businessId']),
      version = _n(j, 'version'),
      source = _s(j, 'source'),
      status = _s(j, 'status'),
      localCustomerId = invitationId(j['localCustomerId']),
      localVehicleId = invitationId(j['localVehicleId']),
      serviceId = invitationId(j['serviceId']),
      workAreaId = invitationId(j['workAreaId']),
      serviceName = _s(j, 'serviceName'),
      customerName = _s(j, 'customerName'),
      vehiclePlate = _s(j, 'vehiclePlate'),
      category = _s(j, 'category'),
      price = Lira.parse(_s(j, 'price')),
      currency = _s(j, 'currency'),
      activeMinutes = _n(j, 'activeMinutes'),
      waitingMinutes = j['waitingMinutes'] as int?,
      employees = _n(j, 'employees'),
      timeZoneId = _s(j, 'timeZoneId'),
      startsAtUtc = schedulingInstant(j['startsAtUtc']),
      activeEndsAtUtc = schedulingInstant(j['activeEndsAtUtc']),
      reservations = List.unmodifiable(
        (j['reservations'] as List).map(
          (v) => AppointmentReservation.fromJson(v as Map<String, dynamic>),
        ),
      ),
      events = List.unmodifiable(
        (j['events'] as List).map(
          (v) => AppointmentEvent.fromJson(v as Map<String, dynamic>),
        ),
      ) {
    if (!{'Confirmed', 'Cancelled'}.contains(status) ||
        source != 'OwnerPhone' ||
        currency != 'TRY' ||
        !vehicleCategories.containsKey(category) ||
        !activeEndsAtUtc.isAfter(startsAtUtc)) {
      throw const FormatException();
    }
  }
  final String id,
      businessId,
      source,
      status,
      localCustomerId,
      localVehicleId,
      serviceId,
      workAreaId;
  final String serviceName,
      customerName,
      vehiclePlate,
      category,
      currency,
      timeZoneId;
  final int version, activeMinutes, employees;
  final int? waitingMinutes;
  final Lira price;
  final DateTime startsAtUtc, activeEndsAtUtc;
  final List<AppointmentReservation> reservations;
  final List<AppointmentEvent> events;
  bool get cancelled => status == 'Cancelled';
  Set<String> get employeeIds => reservations
      .where(
        (v) =>
            (v.releasedAt == null ||
                cancelled &&
                    events.isNotEmpty &&
                    v.releasedAt == events.last.occurredAt) &&
            v.employeeMembershipId != null,
      )
      .map((v) => v.employeeMembershipId!)
      .toSet();
}

class AvailabilityPreview {
  AvailabilityPreview.fromJson(Map<String, dynamic> j)
    : available = j['available'] as bool,
      startsAtUtc = schedulingInstant(j['startsAtUtc']),
      activeEndsAtUtc = schedulingInstant(j['activeEndsAtUtc']);
  final bool available;
  final DateTime startsAtUtc, activeEndsAtUtc;
}

class WorkAreaInput {
  const WorkAreaInput(this.name, this.capacity, {this.version});
  final String name;
  final int capacity;
  final int? version;
  String? validate() =>
      name.trim().isEmpty ||
          name.trim().length > 200 ||
          capacity < 1 ||
          capacity > 2147483647
      ? 'Alan adı (1–200 karakter) ve pozitif kapasite girin.'
      : null;
  Map<String, dynamic> toJson() => {
    'name': name.trim(),
    'capacity': capacity,
    'version': version,
  };
}

class RequirementInput {
  const RequirementInput(
    this.workAreaId,
    this.singleStageConfirmed, {
    this.version,
  });
  final String workAreaId;
  final bool singleStageConfirmed;
  final int? version;
  Map<String, dynamic> toJson() => {
    'workAreaId': workAreaId,
    'singleStageConfirmed': singleStageConfirmed,
    'version': version,
  };
}

class AvailabilityInput {
  const AvailabilityInput(this.startsAtUtc, this.endsAtUtc, {this.version});
  final DateTime startsAtUtc, endsAtUtc;
  final int? version;
  String? validate() =>
      !endsAtUtc.isAfter(startsAtUtc) ||
          startsAtUtc.second != 0 ||
          endsAtUtc.second != 0 ||
          startsAtUtc.millisecond != 0 ||
          startsAtUtc.microsecond != 0 ||
          endsAtUtc.millisecond != 0 ||
          endsAtUtc.microsecond != 0
      ? 'Bitiş başlangıçtan sonra, saatler dakika hassasiyetinde olmalıdır.'
      : null;
  Map<String, dynamic> toJson() => {
    'startsAtUtc': startsAtUtc.toUtc().toIso8601String(),
    'endsAtUtc': endsAtUtc.toUtc().toIso8601String(),
    'version': version,
  };
}

class ScheduleInput {
  ScheduleInput({
    required this.startLocal,
    required this.utcOffsetMinutes,
    required Iterable<String> employees,
  }) : employeeMembershipIds = List.unmodifiable(employees.toList()..sort());
  final String startLocal;
  final int utcOffsetMinutes;
  final List<String> employeeMembershipIds;
  Map<String, dynamic> toJson() => {
    'startLocal': startLocal,
    'utcOffsetMinutes': utcOffsetMinutes,
    'employeeMembershipIds': employeeMembershipIds,
  };
}

class BookingPayload {
  const BookingPayload({
    required this.customerId,
    required this.vehicleId,
    required this.serviceId,
    required this.serviceVersion,
    required this.vehicleVersion,
    required this.schedule,
  });
  final String customerId, vehicleId, serviceId;
  final int serviceVersion, vehicleVersion;
  final ScheduleInput schedule;
  Map<String, dynamic> toJson() => {
    'localCustomerId': customerId,
    'localVehicleId': vehicleId,
    'serviceId': serviceId,
    'expectedServiceVersion': serviceVersion,
    'expectedVehicleVersion': vehicleVersion,
    ...schedule.toJson(),
    'source': 'OwnerPhone',
  };
}

/// Local recovery storage; production scopes it to an authorized owner membership.
abstract interface class BookingAttemptStore {
  Future<String?> read();
  Future<void> write(String value);
  Future<void> clear();
}

/// One logical create attempt. Immutable payload is reused byte-for-byte across retries.
class BookingAttempt {
  factory BookingAttempt.fromJson(Map<String, dynamic> j) {
    if (j['source'] != 'OwnerPhone' ||
        j['utcOffsetMinutes'] is! int ||
        j['employeeMembershipIds'] is! List ||
        !RegExp(r'^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}$')
            .hasMatch(_s(j, 'startLocal'))) {
      throw const FormatException();
    }
    return BookingAttempt(
      BookingPayload(
        customerId: invitationId(j['localCustomerId']),
        vehicleId: invitationId(j['localVehicleId']),
        serviceId: invitationId(j['serviceId']),
        serviceVersion: _n(j, 'expectedServiceVersion'),
        vehicleVersion: _n(j, 'expectedVehicleVersion'),
        schedule: ScheduleInput(
          startLocal: j['startLocal'] as String,
          utcOffsetMinutes: j['utcOffsetMinutes'] as int,
          employees: (j['employeeMembershipIds'] as List).map(invitationId),
        ),
      ),
      requestId: invitationId(j['clientRequestId']),
    );
  }
  BookingAttempt(this.payload, {String? requestId})
    : clientRequestId = requestId ?? _uuid();
  final BookingPayload payload;
  final String clientRequestId;
  static String _uuid() {
    final r = Random.secure();
    final bytes = List.generate(16, (_) => r.nextInt(256));
    bytes[6] = (bytes[6] & 15) | 64;
    bytes[8] = (bytes[8] & 63) | 128;
    final h = bytes.map((v) => v.toRadixString(16).padLeft(2, '0')).join();
    return '${h.substring(0, 8)}-${h.substring(8, 12)}-${h.substring(12, 16)}-${h.substring(16, 20)}-${h.substring(20)}';
  }

  String get fingerprint => jsonEncode(payload.toJson());
  Map<String, dynamic> toJson() => {
    'clientRequestId': clientRequestId,
    ...payload.toJson(),
  };
}

class SchedulingError extends AuthError {
  SchedulingError({required super.code, this.reason});
  final String? reason;
}

String schedulingError(AuthError e) {
  if (e.code == AuthErrorCode.storage) {
    return 'Randevu işlem kaydı güvenle saklanamadı veya okunamadı. Aynı isteği tekrar deneyin; sorun sürerse uygulamayı kapatmadan takvimi kontrol edin.';
  }
  final reason = e is SchedulingError ? e.reason : null;
  return switch (reason) {
    'resourceConflict' => 'Alan kapasitesi veya çalışan uygunluğu değişti. Saat ve çalışan seçimini kontrol edin.',
    'scheduleConflict' || 'requirementChanged' =>
      'Kayıt değişti. Güncel verileri yükleyip seçiminizi yeniden onaylayın.',
    'idempotencyConflict' => 'Bu işlem anahtarı farklı bir kayıt için kullanılmış. Takvimi kontrol edin.',
    'employeeUnavailable' => 'Seçilen çalışan artık uygun veya yetkili değil. Çalışan listesini yenileyin.',
    'outsideHours' ||
    'hoursUnconfigured' => 'İşletmenin çalışma saatlerini kontrol edin.',
    'invalidLocalTime' || 'ambiguousLocalTime' => 'Saat dilimi geçişi nedeniyle saat geçersiz veya belirsiz. Saat ve UTC farkını kontrol edin.',
    'availabilityInUse' ||
    'requirementInUse' ||
    'areaInUse' ||
    'capacityInUse' => 'Mevcut randevular bu değişikliğe izin vermiyor.',
    'unsupportedStages' => 'Hizmet için tek etkin aşama onayı gereklidir.',
    'invalidStatus' =>
      'Randevunun durumu bu işleme izin vermiyor. Güncel kaydı yükleyin.',
    _ => managementError(e),
  };
}

abstract interface class SchedulingService {
  Future<AuthResult<List<WorkArea>>> areas(String business);
  Future<AuthResult<WorkArea>> saveArea(
    String business,
    WorkAreaInput input, {
    String? id,
  });
  Future<AuthResult<void>> archiveArea(String business, WorkArea area);
  Future<AuthResult<List<SchedulingEmployee>>> employees(String business);
  Future<AuthResult<List<SchedulingRequirement>>> requirements(
    String business,
    String service,
  );
  Future<AuthResult<SchedulingRequirement>> saveRequirement(
    String business,
    String service,
    String category,
    RequirementInput input,
  );
  Future<AuthResult<SchedulingRequirement>> disableRequirement(
    String business,
    SchedulingRequirement requirement,
  );
  Future<AuthResult<List<EmployeeAvailability>>> availability(
    String business,
    String employee,
    DateTime fromUtc,
    DateTime toUtcExclusive, {
    int page = 1,
  });
  Future<AuthResult<EmployeeAvailability>> saveAvailability(
    String business,
    String employee,
    AvailabilityInput input, {
    String? id,
  });
  Future<AuthResult<void>> archiveAvailability(
    String business,
    EmployeeAvailability availability,
  );
  Future<AuthResult<List<Appointment>>> calendar(
    String business,
    DateTime fromDate,
    DateTime toDateExclusive, {
    int page = 1,
  });
  Future<AuthResult<Appointment>> detail(String business, String id);
  Future<AuthResult<AvailabilityPreview>> preview(
    String business,
    String service,
    String category,
    ScheduleInput input,
  );
  Future<AuthResult<Appointment>> create(
    String business,
    BookingAttempt attempt,
  );
  Future<AuthResult<Appointment>> reschedule(
    String business,
    Appointment appointment,
    ScheduleInput input,
  );
  Future<AuthResult<Appointment>> cancel(
    String business,
    Appointment appointment,
  );
}
