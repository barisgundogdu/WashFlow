import 'dart:async';

import '../../core/network/api_client.dart';
import '../auth/domain/auth_service.dart';
import '../auth/domain/session_service.dart';
import '../invitations/domain/invitation.dart';
import '../scheduling/domain/scheduling.dart';

class WorkStage {
  WorkStage(Map<String, dynamic> j)
    : id = invitationId(j['id']),
      sequence = j['sequence'] as int,
      name = j['name'] as String,
      minutes = j['estimatedMinutes'] as int,
      status = j['status'] as String,
      started = _time(j['startedAtUtc']),
      completed = _time(j['completedAtUtc']),
      paused = _time(j['pausedAtUtc']),
      pausedMs = j['pausedMilliseconds'] as int,
      areaName = j['workAreaName'] as String? {
    if (sequence < 1 ||
        minutes < 1 ||
        pausedMs < 0 ||
        !workStatus.containsKey(status)) {
      throw const FormatException();
    }
  }
  final String id, name, status;
  final String? areaName;
  final int sequence, minutes, pausedMs;
  final DateTime? started, completed, paused;
  static DateTime? _time(dynamic value) =>
      value == null ? null : schedulingInstant(value);
  Duration remaining(DateTime serverNow) {
    if (started == null) return Duration(minutes: minutes);
    final until = completed ?? paused ?? serverNow;
    return Duration(minutes: minutes) -
        until.difference(started!) +
        Duration(milliseconds: pausedMs);
  }
}

const workStatus = {
  'Waiting': 'Sırasını bekliyor',
  'Ready': 'Hazır',
  'Scheduled': 'Planlandı',
  'InProgress': 'Devam ediyor',
  'Paused': 'Duraklatıldı',
  'Completed': 'Tamamlandı',
  'Cancelled': 'İptal edildi',
};

class WorkOrder {
  WorkOrder(Map<String, dynamic> j)
    : id = invitationId(j['id']),
      business = invitationId(j['businessId']),
      appointment = invitationId(j['appointmentId']),
      version = j['version'] as int,
      plate = j['vehiclePlate'] as String,
      service = j['serviceName'] as String,
      status = j['status'] as String,
      planned = schedulingInstant(j['plannedStartUtc']),
      serverNow = schedulingInstant(j['serverNowUtc']),
      stages = List.unmodifiable(
        (j['stages'] as List).map((s) => WorkStage(s as Map<String, dynamic>)),
      ),
      potentialConflict = j['potentialConflict'] as bool,
      plannedLocal = j['plannedStartLocal'] as String,
      todayLocal = j['businessDateLocal'] as String {
    if (version <= 0 || stages.isEmpty || !workStatus.containsKey(status)) {
      throw const FormatException();
    }
  }
  final String id, business, appointment, plate, service, status;
  final int version;
  final DateTime serverNow, planned;
  final List<WorkStage> stages;
  final bool potentialConflict;
  final String plannedLocal, todayLocal;
}

abstract interface class WorkOrderService {
  Future<AuthResult<List<WorkOrder>>> list(
    String business,
    bool owner, {
    int page = 1,
  });
  Future<AuthResult<WorkOrder>> detail(String business, String id, bool owner);
  Future<AuthResult<WorkOrder>> create(
    String business,
    Appointment appointment,
    List<({String name, int minutes})> stages,
    String requestId,
  );
  Future<AuthResult<WorkOrder>> act(
    WorkOrder order,
    WorkStage? stage,
    String action,
    String requestId,
  );
}

class ApiWorkOrderService implements WorkOrderService, StageTemplateService {
  ApiWorkOrderService({required this.apiClient, required this.session});
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

  String _root(String b) => '/api/v1/businesses/${invitationId(b)}';
  String _role(bool owner) => owner ? 'owner' : 'employee';
  WorkOrder _parse(dynamic j, String b, [String? id]) {
    final order = WorkOrder(j as Map<String, dynamic>);
    if (order.business != b.toLowerCase() ||
        id != null && order.id != id.toLowerCase()) {
      throw const FormatException();
    }
    return order;
  }

  @override
  Future<AuthResult<List<WorkOrder>>> list(
    String business,
    bool owner, {
    int page = 1,
  }) => _run((token) async {
    final data = await apiClient.get(
      '${_root(business)}/${_role(owner)}/work-orders',
      access: token,
      query: {'page': '$page'},
    );
    return List.unmodifiable((data as List).map((j) => _parse(j, business)));
  });
  @override
  Future<AuthResult<WorkOrder>> detail(
    String business,
    String id,
    bool owner,
  ) => _run(
    (token) async => _parse(
      await apiClient.get(
        '${_root(business)}/${_role(owner)}/work-orders/${invitationId(id)}',
        access: token,
      ),
      business,
      id,
    ),
  );
  @override
  Future<AuthResult<WorkOrder>> create(
    String business,
    Appointment appointment,
    List<({String name, int minutes})> stages,
    String requestId,
  ) => _run(
    (token) async => _parse(
      await apiClient.post(
        '${_root(business)}/appointments/${invitationId(appointment.id)}/work-order',
        {
          'requestId': invitationId(requestId),
          'appointmentVersion': appointment.version,
          'stages': stages
              .map((s) => {'name': s.name, 'minutes': s.minutes})
              .toList(),
        },
        access: token,
      ),
      business,
    ),
  );
  @override
  Future<AuthResult<WorkOrder>> act(
    WorkOrder order,
    WorkStage? stage,
    String action,
    String requestId,
  ) => _run((token) async {
    if (!{'start', 'pause', 'resume', 'complete', 'cancel'}.contains(action)) {
      throw const FormatException();
    }
    final path = action == 'cancel'
        ? 'owner/work-orders/${order.id}/cancel'
        : 'employee/work-orders/${order.id}/stages/${invitationId(stage!.id)}/$action';
    return _parse(
      await apiClient.post('${_root(order.business)}/$path', {
        'requestId': invitationId(requestId),
        'version': order.version,
      }, access: token),
      order.business,
      order.id,
    );
  });
  ServiceStageConfig _stageConfig(
    dynamic json,
    String business,
    String service,
    String category,
  ) {
    final j = json as Map<String, dynamic>;
    final r = j['requirement'];
    int? version;
    if (r != null) {
      final requirement = SchedulingRequirement.fromJson(
        r as Map<String, dynamic>,
      );
      if (requirement.businessId != business ||
          requirement.serviceId != service ||
          requirement.category != category) {
        throw const FormatException();
      }
      version = requirement.version;
    }
    final rows = <ServiceStagePlan>[];
    for (final raw in j['stages'] as List) {
      final row = raw as Map<String, dynamic>;
      if (row['businessId'] != business || row['sequence'] != rows.length + 1) {
        throw const FormatException();
      }
      rows.add(
        ServiceStagePlan(
          row['name'] as String,
          row['minutes'] as int,
          invitationId(row['workAreaId']),
          row['employees'] as int,
        ),
      );
    }
    return ServiceStageConfig(version, List.unmodifiable(rows));
  }

  String _stagePath(String b, String s, String c) {
    if (!{'Car', 'Suv', 'Van', 'Pickup', 'Motorcycle', 'Other'}.contains(c)) {
      throw const FormatException();
    }
    return '${_root(b)}/services/${invitationId(s)}/offerings/$c/stages';
  }

  @override
  Future<AuthResult<ServiceStageConfig>> stagePlan(
    String business,
    String service,
    String category,
  ) => _run(
    (t) async => _stageConfig(
      await apiClient.get(_stagePath(business, service, category), access: t),
      business,
      service,
      category,
    ),
  );
  @override
  Future<AuthResult<ServiceStageConfig>> saveStagePlan(
    String business,
    String service,
    String category,
    int? version,
    List<ServiceStagePlan> plans,
  ) => _run(
    (t) async => _stageConfig(
      await apiClient.put(_stagePath(business, service, category), {
        'version': version,
        'stages': plans.map((p) => p.toJson()).toList(),
      }, access: t),
      business,
      service,
      category,
    ),
  );
}

/// Reanchors to server time on every fetch; elapsed Stopwatch ignores device clock changes.
class WorkClock {
  WorkClock(this.serverNow) {
    elapsed.start();
  }
  final DateTime serverNow;
  final Stopwatch elapsed = Stopwatch();
  DateTime get now => serverNow.add(elapsed.elapsed);
  void dispose() => elapsed.stop();
}

String workRemaining(WorkStage stage, DateTime now) {
  final duration = stage.remaining(now);
  final seconds = duration.isNegative
      ? duration.inMilliseconds.abs() ~/ 1000
      : (duration.inMilliseconds / 1000).ceil();
  final clock = '${seconds ~/ 60}:${(seconds % 60).toString().padLeft(2, '0')}';
  return duration.isNegative ? '$clock gecikti' : '$clock kaldı';
}

class ServiceStagePlan {
  const ServiceStagePlan(this.name, this.minutes, this.area, this.employees);
  final String name, area;
  final int minutes, employees;
  Map<String, dynamic> toJson() => {
    'name': name,
    'minutes': minutes,
    'workAreaId': area,
    'employees': employees,
  };
}

class ServiceStageConfig {
  const ServiceStageConfig(this.version, this.stages);
  final int? version;
  final List<ServiceStagePlan> stages;
}

abstract interface class StageTemplateService {
  Future<AuthResult<ServiceStageConfig>> stagePlan(
    String business,
    String service,
    String category,
  );
  Future<AuthResult<ServiceStageConfig>> saveStagePlan(
    String business,
    String service,
    String category,
    int? version,
    List<ServiceStagePlan> plans,
  );
}
