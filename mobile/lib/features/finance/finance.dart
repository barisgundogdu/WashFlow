import 'dart:convert';

import 'package:flutter_secure_storage/flutter_secure_storage.dart';

import '../../core/network/api_client.dart';
import '../auth/domain/auth_service.dart';
import '../auth/domain/session_service.dart';
import '../invitations/domain/invitation.dart';
import '../scheduling/domain/scheduling.dart';

/// Exact kuruş arithmetic, including aggregate values above a single-entry limit.
class FinanceAmount {
  FinanceAmount(String raw) {
    if (!RegExp(r'^-?(0|[1-9][0-9]*)(\.[0-9]{2})$').hasMatch(raw)) {
      throw const FormatException();
    }
    kurus = BigInt.parse(raw.replaceAll('.', ''));
  }
  late final BigInt kurus;
  String get wire {
    final a = kurus.abs();
    return '${kurus.isNegative ? '-' : ''}${a ~/ BigInt.from(100)}.${(a % BigInt.from(100)).toString().padLeft(2, '0')}';
  }

  String get display {
    final parts = wire.split('.');
    final whole = parts[0].replaceAllMapped(
      RegExp(r'\B(?=(\d{3})+(?!\d))'),
      (_) => '.',
    );
    return '$whole,${parts[1]} TL';
  }
}

class FinanceMethod {
  FinanceMethod(Map<String, dynamic> j)
    : id = invitationId(j['id']),
      name = j['name'] as String,
      archived = j['archived'] as bool,
      version = j['version'] as int {
    if (name.isEmpty || version < 1) throw const FormatException();
  }
  final String id, name;
  final bool archived;
  final int version;
}

class FinanceSettings {
  FinanceSettings(Map<String, dynamic> j)
    : methods = List.unmodifiable(
        (j['methods'] as List).map(
          (e) => FinanceMethod(e as Map<String, dynamic>),
        ),
      ),
      employees = List.unmodifiable(
        (j['employees'] as List).map(
          (e) => (id: invitationId(e['id']), name: e['name'] as String),
        ),
      );
  final List<FinanceMethod> methods;
  final List<({String id, String name})> employees;
}

class Receivable {
  Receivable(Map<String, dynamic> j)
    : id = invitationId(j['workOrderId']),
      plate = j['vehiclePlate'] as String,
      service = j['serviceName'] as String,
      completed = schedulingInstant(j['completedAtUtc']),
      price = FinanceAmount(j['price'] as String),
      collected = FinanceAmount(j['collected'] as String),
      remaining = FinanceAmount(j['remaining'] as String),
      status = j['status'] as String {
    if (!{'Unpaid', 'Partial', 'Paid'}.contains(status)) {
      throw const FormatException();
    }
  }
  final String id, plate, service, status;
  final DateTime completed;
  final FinanceAmount price, collected, remaining;
}

class FinanceEntry {
  FinanceEntry(Map<String, dynamic> j)
    : id = invitationId(j['id']),
      business = invitationId(j['businessId']),
      kind = j['kind'] as String,
      amount = FinanceAmount(j['amount'] as String),
      at = schedulingInstant(j['occurredAtUtc']),
      category = j['category'] as String?,
      description = j['description'] as String?,
      reference = j['reference'] as String?,
      work = j['workOrderId'] == null ? null : invitationId(j['workOrderId']),
      method = j['paymentMethodId'] == null
          ? null
          : invitationId(j['paymentMethodId']),
      methodName = j['paymentMethodName'] as String?,
      employee = j['employeeMembershipId'] == null
          ? null
          : invitationId(j['employeeMembershipId']),
      reverses = j['reversesId'] == null ? null : invitationId(j['reversesId']),
      corrects = j['correctsId'] == null ? null : invitationId(j['correctsId']),
      reason = j['correctionReason'] as String? {
    if (j['currency'] != 'TRY' ||
        !financeKinds.containsKey(kind) ||
        amount.kurus <= BigInt.zero) {
      throw const FormatException();
    }
  }
  final String id, business, kind;
  final String? category,
      description,
      reference,
      work,
      method,
      methodName,
      employee,
      reverses,
      corrects,
      reason;
  final FinanceAmount amount;
  final DateTime at;
}

class FinanceReport {
  FinanceReport(Map<String, dynamic> j)
    : from = j['fromDate'] as String,
      to = j['toDateExclusive'] as String,
      today = j['businessDate'] as String,
      zone = j['timeZoneId'] as String,
      totals = {
        for (final k in [
          'earnedRevenue',
          'collectedPayments',
          'manualIncome',
          'expenses',
          'serviceOperatingResult',
          'cashOperatingBalance',
          'unpaidBalance',
        ])
          k: FinanceAmount(j[k] as String),
      },
      services = buckets(j['serviceRevenue']),
      expenses = buckets(j['expenseCategories']),
      methods = buckets(j['paymentMethods']),
      trends = List.unmodifiable(
        (j['trends'] as List).map(
          (e) => (
            date: e['date'] as String,
            earned: FinanceAmount(e['earnedRevenue'] as String),
            collected: FinanceAmount(e['collectedPayments'] as String),
            manual: FinanceAmount(e['manualIncome'] as String),
            expenses: FinanceAmount(e['expenses'] as String),
          ),
        ),
      ) {
    if (j['currency'] != 'TRY') throw const FormatException();
    BusinessClock(zone);
    DateTime.parse(from);
    DateTime.parse(to);
    DateTime.parse(today);
  }
  static List<({String label, FinanceAmount amount})> buckets(dynamic raw) =>
      List.unmodifiable(
        (raw as List).map(
          (e) => (
            label: e['label'] as String,
            amount: FinanceAmount(e['amount'] as String),
          ),
        ),
      );
  final String from, to, today, zone;
  final Map<String, FinanceAmount> totals;
  final List<({String label, FinanceAmount amount})> services,
      expenses,
      methods;
  final List<
    ({
      String date,
      FinanceAmount earned,
      FinanceAmount collected,
      FinanceAmount manual,
      FinanceAmount expenses,
    })
  >
  trends;
}

const financeKinds = {
  'payments': 'Tahsilatlar',
  'manual-income': 'Ek gelir',
  'expenses': 'Giderler',
};

class FinanceAttempt {
  FinanceAttempt(this.kind, this.body, {this.reverse}) {
    if (!financeKinds.containsKey(kind)) throw const FormatException();
    invitationId(body['requestId']);
    if (reverse != null) invitationId(reverse);
  }
  final String kind;
  final Map<String, dynamic> body;
  final String? reverse;
  String encode() =>
      jsonEncode({'kind': kind, 'body': body, 'reverse': reverse});
  factory FinanceAttempt.decode(String raw) {
    final j = jsonDecode(raw) as Map<String, dynamic>;
    return FinanceAttempt(
      j['kind'] as String,
      j['body'] as Map<String, dynamic>,
      reverse: j['reverse'] as String?,
    );
  }
}

class SecureFinanceAttemptStore implements BookingAttemptStore {
  SecureFinanceAttemptStore(String business, String member)
    : key =
          'washflow.finance.pending.v1.${invitationId(business)}.${invitationId(member)}';
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

abstract interface class FinanceService {
  Future<AuthResult<FinanceSettings>> settings(String business);
  Future<AuthResult<FinanceReport>> report(
    String business, {
    String? from,
    String? to,
  });
  Future<AuthResult<List<Receivable>>> receivables(
    String business,
    int page, {
    bool all = false,
  });
  Future<AuthResult<List<FinanceEntry>>> entries(
    String business,
    String kind,
    int page,
  );
  Future<AuthResult<FinanceEntry>> submit(
    String business,
    FinanceAttempt attempt,
  );
  Future<AuthResult<FinanceMethod>> addMethod(String business, String name);
  Future<AuthResult<FinanceMethod>> archiveMethod(
    String business,
    FinanceMethod method,
  );
}

class ApiFinanceService implements FinanceService {
  ApiFinanceService({required this.apiClient, required this.session});
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

  String root(String business) =>
      '/api/v1/businesses/${invitationId(business)}/finance';
  @override
  Future<AuthResult<FinanceSettings>> settings(String business) => _run(
    (t) async => FinanceSettings(
      await apiClient.get('${root(business)}/settings', access: t)
          as Map<String, dynamic>,
    ),
  );
  @override
  Future<AuthResult<FinanceReport>> report(
    String business, {
    String? from,
    String? to,
  }) => _run(
    (t) async => FinanceReport(
      await apiClient.get(
        '${root(business)}/report',
        access: t,
        query: {'fromDate': ?from, 'toDateExclusive': ?to},
      ) as Map<String, dynamic>,
    ),
  );
  @override
  Future<AuthResult<List<Receivable>>> receivables(
    String business,
    int page, {
    bool all = false,
  }) => _run(
    (t) async => List.unmodifiable(
      (await apiClient.get(
        '${root(business)}/receivables',
        access: t,
        query: {'page': '$page', 'status': all ? 'all' : 'unpaid'},
      ) as List).map((e) => Receivable(e as Map<String, dynamic>)),
    ),
  );
  FinanceEntry scoped(dynamic raw, String business, String kind, {String? id}) {
    final e = FinanceEntry(raw as Map<String, dynamic>);
    if (e.business != business ||
        e.kind != kind ||
        id != null && e.reverses != id) {
      throw const FormatException();
    }
    return e;
  }

  @override
  Future<AuthResult<List<FinanceEntry>>> entries(
    String business,
    String kind,
    int page,
  ) => _run((t) async {
    if (!financeKinds.containsKey(kind)) throw const FormatException();
    return List.unmodifiable(
      (await apiClient.get(
        '${root(business)}/entries/$kind',
        access: t,
        query: {'page': '$page'},
      ) as List).map((e) => scoped(e, business, kind)),
    );
  });
  @override
  Future<AuthResult<FinanceEntry>> submit(
    String business,
    FinanceAttempt a,
  ) => _run(
    (t) async => scoped(
      await apiClient.post(
        '${root(business)}/entries/${a.kind}${a.reverse == null ? '' : '/${invitationId(a.reverse)}/reverse'}',
        a.body,
        access: t,
      ),
      business,
      a.kind,
      id: a.reverse,
    ),
  );
  @override
  Future<AuthResult<FinanceMethod>> addMethod(String business, String name) =>
      _run(
        (t) async => FinanceMethod(
          await apiClient.post('${root(business)}/methods', {
            'name': name,
          }, access: t) as Map<String, dynamic>,
        ),
      );
  @override
  Future<AuthResult<FinanceMethod>> archiveMethod(
    String business,
    FinanceMethod m,
  ) => _run((t) async {
    final row = FinanceMethod(
      await apiClient.post('${root(business)}/methods/${m.id}/archive', {
        'version': m.version,
      }, access: t) as Map<String, dynamic>,
    );
    if (row.id != m.id) throw const FormatException();
    return row;
  });
}

String financeError(AuthError e) {
  if (e is SchedulingError) {
    switch (e.reason) {
      case 'overpayment':
        return 'Ödeme kalan borcu aşamaz. Bakiyeyi yenileyin.';
      case 'entryReversed':
        return 'Bu kayıt zaten ters çevrilmiş. Güncel kayıtları yükleyin.';
      case 'workNotCompleted':
        return 'Ödeme yalnızca tamamlanan iş emrine kaydedilebilir.';
      case 'methodArchived':
        return 'Ödeme yöntemi arşivlenmiş. Etkin bir yöntem seçin.';
    }
  }
  return schedulingError(e);
}
