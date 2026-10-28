import '../../../core/network/api_client.dart';
import '../../auth/domain/auth_service.dart';
import '../../auth/domain/session_service.dart';
import '../../invitations/domain/invitation.dart';
import '../domain/scheduling.dart';

class ApiSchedulingService implements SchedulingService {
  ApiSchedulingService({required this.apiClient, required this.session});
  final ApiClient apiClient;
  final AccessTokenProvider session;
  Future<AuthResult<T>> _run<T>(Future<T> Function(String) action) async {
    try {
      final identity = session is SessionIdentity
          ? session as SessionIdentity
          : null;
      final revision = identity?.sessionRevision;
      final access = await session.accessToken();
      if (identity != null && identity.sessionRevision != revision) {
        return AuthFailure(AuthError(code: AuthErrorCode.unauthenticated));
      }
      if (access case AuthFailure<String>(:final error)) {
        return AuthFailure(
          error.code == AuthErrorCode.invalidCredentials
              ? AuthError(code: AuthErrorCode.unauthenticated)
              : error,
        );
      }
      final credential = (access as AuthSuccess<String>).value;
      if (credential.isEmpty) {
        return AuthFailure(AuthError(code: AuthErrorCode.unauthenticated));
      }
      final value = await action(credential);
      if (identity != null && identity.sessionRevision != revision) {
        return AuthFailure(AuthError(code: AuthErrorCode.unauthenticated));
      }
      return AuthSuccess(value);
    } on ApiException catch (error) {
      return AuthFailure(
        SchedulingError(
          reason: error.problemCode,
          code: switch (error.code) {
            ApiErrorCode.unauthenticated => AuthErrorCode.unauthenticated,
            ApiErrorCode.forbidden => AuthErrorCode.forbidden,
            ApiErrorCode.notFound => AuthErrorCode.notFound,
            ApiErrorCode.invalidInvitation => AuthErrorCode.invalidInvitation,
            ApiErrorCode.conflict => AuthErrorCode.conflict,
            ApiErrorCode.validation => AuthErrorCode.validation,
            ApiErrorCode.rateLimited => AuthErrorCode.rateLimited,
            ApiErrorCode.unavailable => AuthErrorCode.unavailable,
            ApiErrorCode.network => AuthErrorCode.network,
            ApiErrorCode.timeout => AuthErrorCode.timeout,
            _ => AuthErrorCode.unexpected,
          },
        ),
      );
    } catch (_) {
      return AuthFailure(AuthError(code: AuthErrorCode.unexpected));
    }
  }

  String _base(String b) => '/api/v1/businesses/${invitationId(b)}';
  String _id(String id) => invitationId(id);
  Map<String, dynamic> _map(Object? j, String b, [String? id]) {
    if (j is! Map<String, dynamic> ||
        j['businessId'] != b.toLowerCase() ||
        id != null && j['id'] != id.toLowerCase()) {
      throw const FormatException();
    }
    return j;
  }

  List<T> _list<T>(
    Object? j,
    String b,
    T Function(Map<String, dynamic>) parse,
  ) {
    if (j is! List) throw const FormatException();
    return List.unmodifiable(j.map((v) => parse(_map(v, b))));
  }

  void _range(DateTime from, DateTime to, int page) {
    if (!to.isAfter(from) ||
        to.difference(from) > const Duration(days: 31) ||
        page < 1 ||
        page > 10000) {
      throw const ApiException(ApiErrorCode.validation);
    }
  }

  @override
  Future<AuthResult<List<WorkArea>>> areas(String business) => _run(
    (t) async => _list(
      await apiClient.get('${_base(business)}/work-areas', access: t),
      business,
      WorkArea.fromJson,
    ),
  );
  @override
  Future<AuthResult<WorkArea>> saveArea(
    String business,
    WorkAreaInput input, {
    String? id,
  }) => _run((t) async {
    if (input.validate() != null) {
      throw const ApiException(ApiErrorCode.validation);
    }
    final path = '${_base(business)}/work-areas';
    final raw = id == null
        ? await apiClient.post(path, input.toJson(), access: t, expected: 201)
        : await apiClient.put('$path/${_id(id)}', input.toJson(), access: t);
    return WorkArea.fromJson(_map(raw, business, id));
  });
  @override
  Future<AuthResult<void>> archiveArea(String business, WorkArea area) =>
      _run((t) async {
        if (area.businessId != business) throw const FormatException();
        await apiClient.post(
          '${_base(business)}/work-areas/${_id(area.id)}/archive',
          {'version': area.version},
          access: t,
          expected: 204,
        );
      });
  @override
  Future<AuthResult<List<SchedulingEmployee>>> employees(String business) =>
      _run(
        (t) async => _list(
          await apiClient.get(
            '${_base(business)}/scheduling-employees',
            access: t,
          ),
          business,
          SchedulingEmployee.fromJson,
        ),
      );
  @override
  Future<AuthResult<List<SchedulingRequirement>>> requirements(
    String business,
    String service,
  ) => _run((t) async {
    final values = _list(
      await apiClient.get(
        '${_base(business)}/services/${_id(service)}/scheduling-requirements',
        access: t,
      ),
      business,
      SchedulingRequirement.fromJson,
    );
    if (values.any((v) => v.serviceId != service)) {
      throw const FormatException();
    }
    return values;
  });
  String _requirement(String b, String s, String c) {
    if (!const {
      'Car',
      'Suv',
      'Van',
      'Pickup',
      'Motorcycle',
      'Other',
    }.contains(c)) {
      throw const ApiException(ApiErrorCode.validation);
    }
    return '${_base(b)}/services/${_id(s)}/offerings/$c/scheduling-requirement';
  }

  @override
  Future<AuthResult<SchedulingRequirement>> saveRequirement(
    String business,
    String service,
    String category,
    RequirementInput input,
  ) => _run(
    (t) async => SchedulingRequirement.fromJson(
      _map(
        await apiClient.put(
          _requirement(business, service, category),
          input.toJson(),
          access: t,
        ),
        business,
      ),
    ),
  );
  @override
  Future<AuthResult<SchedulingRequirement>> disableRequirement(
    String business,
    SchedulingRequirement requirement,
  ) => _run((t) async {
    if (requirement.businessId != business) throw const FormatException();
    return SchedulingRequirement.fromJson(
      _map(
        await apiClient.post(
          '${_requirement(business, requirement.serviceId, requirement.category)}/disable',
          {'version': requirement.version},
          access: t,
        ),
        business,
        requirement.id,
      ),
    );
  });
  String _availability(String b, String e) =>
      '${_base(b)}/employees/${_id(e)}/availability';
  @override
  Future<AuthResult<List<EmployeeAvailability>>> availability(
    String business,
    String employee,
    DateTime fromUtc,
    DateTime toUtcExclusive, {
    int page = 1,
  }) => _run((t) async {
    _range(fromUtc, toUtcExclusive, page);
    final values = _list(
      await apiClient.get(
        _availability(business, employee),
        access: t,
        query: {
          'fromUtc': fromUtc.toUtc().toIso8601String(),
          'toUtcExclusive': toUtcExclusive.toUtc().toIso8601String(),
          'page': '$page',
          'pageSize': '50',
        },
      ),
      business,
      EmployeeAvailability.fromJson,
    );
    if (values.any((v) => v.membershipId != employee)) {
      throw const FormatException();
    }
    return values;
  });
  @override
  Future<AuthResult<EmployeeAvailability>> saveAvailability(
    String business,
    String employee,
    AvailabilityInput input, {
    String? id,
  }) => _run((t) async {
    if (input.validate() != null) {
      throw const ApiException(ApiErrorCode.validation);
    }
    final path = _availability(business, employee);
    final raw = id == null
        ? await apiClient.post(path, input.toJson(), access: t, expected: 201)
        : await apiClient.put('$path/${_id(id)}', input.toJson(), access: t);
    final value = EmployeeAvailability.fromJson(_map(raw, business, id));
    if (value.membershipId != employee) throw const FormatException();
    return value;
  });
  @override
  Future<AuthResult<void>> archiveAvailability(
    String business,
    EmployeeAvailability availability,
  ) => _run((t) async {
    if (availability.businessId != business) throw const FormatException();
    await apiClient.post(
      '${_availability(business, availability.membershipId)}/${_id(availability.id)}/archive',
      {'version': availability.version},
      access: t,
      expected: 204,
    );
  });
  @override
  Future<AuthResult<List<Appointment>>> calendar(
    String business,
    DateTime fromDate,
    DateTime toDateExclusive, {
    int page = 1,
  }) => _run((t) async {
    _range(fromDate, toDateExclusive, page);
    return _list(
      await apiClient.get(
        '${_base(business)}/appointments',
        access: t,
        query: {
          'fromDate': dateWire(fromDate),
          'toDateExclusive': dateWire(toDateExclusive),
          'page': '$page',
          'pageSize': '50',
        },
      ),
      business,
      Appointment.fromJson,
    );
  });
  @override
  Future<AuthResult<Appointment>> detail(String business, String id) => _run(
    (t) async => Appointment.fromJson(
      _map(
        await apiClient.get(
          '${_base(business)}/appointments/${_id(id)}',
          access: t,
        ),
        business,
        id,
      ),
    ),
  );
  @override
  Future<AuthResult<AvailabilityPreview>> preview(
    String business,
    String service,
    String category,
    ScheduleInput input,
  ) => _run(
    (t) async => AvailabilityPreview.fromJson(
      await apiClient.post(
        '${_base(business)}/appointments/availability-preview',
        {'serviceId': service, 'category': category, ...input.toJson()},
        access: t,
      ) as Map<String, dynamic>,
    ),
  );
  @override
  Future<AuthResult<Appointment>> create(
    String business,
    BookingAttempt attempt,
  ) => _run(
    (t) async => Appointment.fromJson(
      _map(
        await apiClient.post(
          '${_base(business)}/appointments',
          attempt.toJson(),
          access: t,
          expected: 201,
          alsoAccept: const {200},
        ),
        business,
      ),
    ),
  );
  @override
  Future<AuthResult<Appointment>> reschedule(
    String business,
    Appointment appointment,
    ScheduleInput input,
  ) => _run((t) async {
    if (appointment.businessId != business) throw const FormatException();
    return Appointment.fromJson(
      _map(
        await apiClient.post(
          '${_base(business)}/appointments/${_id(appointment.id)}/reschedule',
          {'version': appointment.version, ...input.toJson()},
          access: t,
        ),
        business,
        appointment.id,
      ),
    );
  });
  @override
  Future<AuthResult<Appointment>> cancel(
    String business,
    Appointment appointment,
  ) => _run((t) async {
    if (appointment.businessId != business) throw const FormatException();
    return Appointment.fromJson(
      _map(
        await apiClient.post(
          '${_base(business)}/appointments/${_id(appointment.id)}/cancel',
          {'version': appointment.version},
          access: t,
        ),
        business,
        appointment.id,
      ),
    );
  });
}
