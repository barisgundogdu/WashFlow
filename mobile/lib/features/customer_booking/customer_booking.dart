import 'dart:convert';

import 'package:flutter_secure_storage/flutter_secure_storage.dart';

import '../../core/network/api_client.dart';
import '../auth/domain/auth_service.dart';
import '../auth/domain/session_service.dart';
import '../invitations/domain/invitation.dart';
import '../scheduling/domain/scheduling.dart';
import '../service_catalog/domain/service_catalog.dart';

Map<String, dynamic> bookingMap(dynamic j) => j as Map<String, dynamic>;
String bookingText(dynamic j, String key, int max) {
  final v = j[key];
  if (v is! String || v.trim().isEmpty || v.length > max) {
    throw const FormatException();
  }
  return v;
}

int bookingInt(dynamic j, String key, {int min = 1, int max = 4294967295}) {
  final v = j[key];
  if (v is! int || v < min || v > max) throw const FormatException();
  return v;
}

class PublicBusiness {
  PublicBusiness(Map<String, dynamic> j)
    : id = invitationId(j['id']),
      name = bookingText(j, 'name', 200),
      city = j['city'] as String?,
      district = j['district'] as String?,
      address = j['address'] as String?,
      phone = j['phone'] as String?,
      description = j['description'] as String?,
      zone = j['timeZoneId'] as String?;
  final String id, name;
  final String? city, district, address, phone, description, zone;
}

class PublicOffering {
  PublicOffering(Map<String, dynamic> j)
    : category = j['category'] as String,
      price = Lira.parse(bookingText(j, 'price', 20)),
      minutes = bookingInt(j, 'activeMinutes', max: 10080),
      configured = j['schedulingConfigured'] as bool {
    if (j['currency'] != 'TRY') throw const FormatException();
  }
  final String category;
  final Lira price;
  final int minutes;
  final bool configured;
}

class PublicService {
  PublicService(Map<String, dynamic> j)
    : id = invitationId(j['id']),
      name = bookingText(j, 'name', 200),
      offerings = List.unmodifiable(
        (j['offerings'] as List).map((e) => PublicOffering(bookingMap(e))),
      );
  final String id, name;
  final List<PublicOffering> offerings;
}

class PublicBusinessDetail {
  PublicBusinessDetail(Map<String, dynamic> j)
    : business = PublicBusiness(bookingMap(j['business'])),
      services = List.unmodifiable(
        (j['services'] as List).map((e) => PublicService(bookingMap(e))),
      ),
      cutoff = bookingInt(j, 'cancellationRescheduleCutoffMinutes', min: 0),
      page = bookingInt(j, 'page'),
      hasMore = j['hasMore'] as bool,
      hours = (j['hours'] as List?)?.map((e) => bookingMap(e)).toList();
  final PublicBusiness business;
  final List<PublicService> services;
  final int cutoff, page;
  final bool hasMore;
  final List<Map<String, dynamic>>? hours;
}

class CustomerSlot {
  CustomerSlot(Map<String, dynamic> j)
    : start = schedulingInstant(j['startsAtUtc']),
      end = schedulingInstant(j['endsAtUtc']),
      label = bookingText(j, 'localLabel', 40) {
    if (!end.isAfter(start)) throw const FormatException();
  }
  final DateTime start, end;
  final String label;
}

class CustomerSlots {
  CustomerSlots(Map<String, dynamic> j)
    : business = invitationId(j['businessId']),
      service = invitationId(j['serviceId']),
      vehicle = invitationId(j['ownedVehicleId']),
      plate = bookingText(j, 'vehiclePlate', 32),
      serviceName = bookingText(j, 'serviceName', 200),
      price = Lira.parse(bookingText(j, 'price', 20)),
      minutes = bookingInt(j, 'activeMinutes', max: 10080),
      serviceVersion = bookingInt(j, 'serviceVersion'),
      vehicleVersion = bookingInt(j, 'vehicleVersion'),
      page = bookingInt(j, 'page'),
      hasMore = j['hasMore'] as bool,
      items = List.unmodifiable(
        (j['items'] as List).map((e) => CustomerSlot(bookingMap(e))),
      ) {
    if (j['currency'] != 'TRY' || items.length > 12) {
      throw const FormatException();
    }
  }
  final String business, service, vehicle, plate, serviceName;
  final Lira price;
  final int minutes, serviceVersion, vehicleVersion, page;
  final bool hasMore;
  final List<CustomerSlot> items;
}

class CustomerAppointment {
  CustomerAppointment(Map<String, dynamic> j)
    : id = invitationId(j['id']),
      business = invitationId(j['businessId']),
      vehicle = invitationId(j['ownedVehicleId']),
      service = invitationId(j['serviceId']),
      businessName = bookingText(j, 'businessName', 200),
      plate = bookingText(j, 'vehiclePlate', 32),
      serviceName = bookingText(j, 'serviceName', 200),
      local = bookingText(j, 'localStart', 100),
      zone = bookingText(j, 'timeZoneId', 100),
      status = j['status'] as String,
      workStatus = j['workStatus'] as String,
      price = Lira.parse(bookingText(j, 'price', 20)),
      version = bookingInt(j, 'version'),
      minutes = bookingInt(j, 'activeMinutes', max: 10080),
      cutoff = bookingInt(j, 'cancellationRescheduleCutoffMinutes', min: 0),
      canChange = j['canChange'] as bool,
      start = schedulingInstant(j['startsAtUtc']) {
    if (j['currency'] != 'TRY' ||
        !{'Confirmed', 'Cancelled'}.contains(status) ||
        !{
          'NotStarted',
          'Scheduled',
          'InProgress',
          'Completed',
        }.contains(workStatus)) {
      throw const FormatException();
    }
  }
  final String id,
      business,
      vehicle,
      service,
      businessName,
      plate,
      serviceName,
      local,
      zone,
      status,
      workStatus;
  final Lira price;
  final int version, minutes, cutoff;
  final bool canChange;
  final DateTime start;
}

class CustomerAttempt {
  CustomerAttempt(this.business, this.body) {
    invitationId(business);
    for (final k in ['clientRequestId', 'ownedVehicleId', 'serviceId']) {
      invitationId(body[k]);
    }
    bookingInt(body, 'expectedServiceVersion');
    bookingInt(body, 'expectedVehicleVersion');
    schedulingInstant(body['startsAtUtc']);
  }
  final String business;
  final Map<String, dynamic> body;
  String encode() => jsonEncode({'business': business, 'body': body});
  factory CustomerAttempt.decode(String raw) {
    final j = bookingMap(jsonDecode(raw));
    return CustomerAttempt(invitationId(j['business']), bookingMap(j['body']));
  }
}

abstract interface class CustomerAttemptStore {
  Future<String?> read();
  Future<void> write(String value);
  Future<void> clear();
}

class SecureCustomerAttemptStore implements CustomerAttemptStore {
  SecureCustomerAttemptStore(String profile)
    : key = 'washflow.customer.pending.v1.${invitationId(profile)}';
  final String key;
  static const storage = FlutterSecureStorage(
    iOptions: IOSOptions(
      accessibility: KeychainAccessibility.unlocked_this_device,
    ),
  );
  @override
  Future<String?> read() => storage.read(key: key);
  @override
  Future<void> write(String value) => storage.write(key: key, value: value);
  @override
  Future<void> clear() => storage.delete(key: key);
}

abstract interface class CustomerBookingService {
  Future<AuthResult<List<PublicBusiness>>> discover(String query, int page);
  Future<AuthResult<PublicBusinessDetail>> business(String id, int page);
  Future<AuthResult<CustomerSlots>> slots(
    String business,
    String service,
    String vehicle,
    DateTime date,
    int page, {
    String? appointment,
  });
  Future<AuthResult<CustomerAppointment>> book(CustomerAttempt attempt);
  Future<AuthResult<List<CustomerAppointment>>> mine(String status, int page);
  Future<AuthResult<CustomerAppointment>> detail(String id);
  Future<AuthResult<CustomerAppointment>> change(
    CustomerAppointment row,
    String request, {
    DateTime? start,
  });
}

class ApiCustomerBookingService implements CustomerBookingService {
  ApiCustomerBookingService({required this.apiClient, required this.session});
  final ApiClient apiClient;
  final AccessTokenProvider session;
  Future<AuthResult<T>> _run<T>(Future<T> Function(String) action) async {
    try {
      final identity = session is SessionIdentity
          ? session as SessionIdentity
          : null;
      final revision = identity?.sessionRevision;
      final token = await session.accessToken();
      if (token is AuthFailure<String>) {
        return AuthFailure(
          token.error.code == AuthErrorCode.invalidCredentials
              ? AuthError(code: AuthErrorCode.unauthenticated)
              : token.error,
        );
      }
      if (token is! AuthSuccess<String> ||
          token.value.isEmpty ||
          identity != null && identity.sessionRevision != revision) {
        return AuthFailure(AuthError(code: AuthErrorCode.unauthenticated));
      }
      final result = await action(token.value);
      if (identity != null && identity.sessionRevision != revision) {
        return AuthFailure(AuthError(code: AuthErrorCode.unauthenticated));
      }
      return AuthSuccess(result);
    } on ApiException catch (e) {
      return AuthFailure(
        SchedulingError(
          reason: e.problemCode,
          code: switch (e.code) {
            ApiErrorCode.unauthenticated => AuthErrorCode.unauthenticated,
            ApiErrorCode.forbidden => AuthErrorCode.forbidden,
            ApiErrorCode.notFound => AuthErrorCode.notFound,
            ApiErrorCode.conflict => AuthErrorCode.conflict,
            ApiErrorCode.validation => AuthErrorCode.validation,
            ApiErrorCode.timeout => AuthErrorCode.timeout,
            ApiErrorCode.network => AuthErrorCode.network,
            ApiErrorCode.rateLimited => AuthErrorCode.rateLimited,
            ApiErrorCode.unavailable => AuthErrorCode.unavailable,
            _ => AuthErrorCode.unexpected,
          },
        ),
      );
    } catch (_) {
      return AuthFailure(AuthError(code: AuthErrorCode.unexpected));
    }
  }

  String _business(String b) =>
      '/api/v1/customer/businesses/${invitationId(b)}';
  CustomerAppointment _appointment(
    dynamic raw, {
    String? id,
    String? business,
    String? vehicle,
    String? service,
  }) {
    final a = CustomerAppointment(bookingMap(raw));
    if (id != null && a.id != id ||
        business != null && a.business != business ||
        vehicle != null && a.vehicle != vehicle ||
        service != null && a.service != service) {
      throw const FormatException();
    }
    return a;
  }

  @override
  Future<AuthResult<List<PublicBusiness>>> discover(String query, int page) =>
      _run((t) async {
        final raw = await apiClient.get(
          '/api/v1/customer/businesses',
          access: t,
          query: {'q': query, 'page': '$page'},
        );
        return List.unmodifiable(
          (raw as List).map((e) => PublicBusiness(bookingMap(e))),
        );
      });
  @override
  Future<AuthResult<PublicBusinessDetail>> business(
    String id,
    int page,
  ) => _run((t) async {
    final d = PublicBusinessDetail(
      bookingMap(
        await apiClient.get(_business(id), access: t, query: {'page': '$page'}),
      ),
    );
    if (d.business.id != id || d.page != page) throw const FormatException();
    return d;
  });
  @override
  Future<AuthResult<CustomerSlots>> slots(
    String business,
    String service,
    String vehicle,
    DateTime date,
    int page, {
    String? appointment,
  }) => _run((t) async {
    final d = CustomerSlots(
      bookingMap(
        await apiClient.get(
          '${_business(business)}/services/${invitationId(service)}/vehicles/${invitationId(vehicle)}/slots',
          access: t,
          query: {
            'date': dateWire(date),
            'page': '$page',
            if (appointment != null) 'appointmentId': invitationId(appointment),
          },
        ),
      ),
    );
    if (d.business != business ||
        d.service != service ||
        d.vehicle != vehicle ||
        d.page != page) {
      throw const FormatException();
    }
    return d;
  });
  @override
  Future<AuthResult<CustomerAppointment>> book(CustomerAttempt a) => _run(
    (t) async => _appointment(
      await apiClient.post(
        '${_business(a.business)}/appointments',
        a.body,
        access: t,
        expected: 201,
        alsoAccept: {200},
      ),
      business: a.business,
      vehicle: a.body['ownedVehicleId'],
      service: a.body['serviceId'],
    ),
  );
  @override
  Future<AuthResult<List<CustomerAppointment>>> mine(String status, int page) =>
      _run((t) async {
        final raw = await apiClient.get(
          '/api/v1/customer/appointments',
          access: t,
          query: {'status': status, 'page': '$page'},
        );
        return List.unmodifiable((raw as List).map((e) => _appointment(e)));
      });
  @override
  Future<AuthResult<CustomerAppointment>> detail(String id) => _run(
    (t) async => _appointment(
      await apiClient.get(
        '/api/v1/customer/appointments/${invitationId(id)}',
        access: t,
      ),
      id: id,
    ),
  );
  @override
  Future<AuthResult<CustomerAppointment>> change(
    CustomerAppointment a,
    String request, {
    DateTime? start,
  }) => _run(
    (t) async => _appointment(
      await apiClient.post(
        '/api/v1/customer/appointments/${a.id}/${start == null ? 'cancel' : 'reschedule'}',
        {
          'requestId': invitationId(request),
          'version': a.version,
          if (start != null) 'startsAtUtc': start.toUtc().toIso8601String(),
        },
        access: t,
      ),
      id: a.id,
      business: a.business,
      vehicle: a.vehicle,
      service: a.service,
    ),
  );
}

String customerBookingError(AuthError e) {
  if (e is SchedulingError) {
    switch (e.reason) {
      case 'bookingChangeCutoff':
        return 'İptal ve erteleme süresi dolmuş. İşletmeyle görüşün.';
      case 'vehicleChanged':
        return 'Araç kaydı değişmiş. İşletmeyle görüşün.';
      case 'resourceConflict':
      case 'slotUnavailable':
        return 'Seçilen saat artık uygun değil. Uygun saatleri yenileyin.';
    }
  }
  return schedulingError(e);
}
