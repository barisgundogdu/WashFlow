import '../../../core/network/api_client.dart';
import '../../auth/domain/auth_service.dart';
import '../../auth/domain/session_service.dart';
import '../../invitations/domain/invitation.dart';
import '../domain/customer_vehicles.dart';

class ApiCustomerVehicleService implements CustomerVehicleService {
  ApiCustomerVehicleService({required this.apiClient, required this.session});
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
        AuthError(
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

  String _base(String business) =>
      '/api/v1/businesses/${invitationId(business)}';
  String _vehicles(String? b, String? c) {
    if (b == null) {
      if (c != null) throw const FormatException();
      return '/api/v1/me/vehicles';
    }
    return c == null
        ? '${_base(b)}/vehicles'
        : '${_base(b)}/customers/${invitationId(c)}/vehicles';
  }

  Map<String, dynamic> _map(Object? j) {
    if (j is! Map<String, dynamic>) throw const FormatException();
    return j;
  }

  RecordPage<T> _page<T>(
    Object? raw,
    T Function(Map<String, dynamic>) parse,
    int page,
  ) {
    final j = _map(raw);
    if (j['items'] is! List ||
        j['page'] != page ||
        j['pageSize'] != 20 ||
        j['hasMore'] is! bool ||
        (j['items'] as List).length > 20) {
      throw const FormatException();
    }
    return RecordPage(
      List.unmodifiable((j['items'] as List).map((e) => parse(_map(e)))),
      page,
      j['hasMore'],
    );
  }

  LocalCustomerRecord _customer(Object? raw, String b) {
    final c = LocalCustomerRecord.fromJson(_map(raw));
    if (c.businessId != b) throw const FormatException();
    return c;
  }

  VehicleRecord _vehicle(Object? raw, String? b, String? c) {
    final v = VehicleRecord.fromJson(_map(raw));
    if (v.businessId != b || (c != null && v.localCustomerId != c)) {
      throw const FormatException();
    }
    return v;
  }

  Map<String, String> _query(String q, int page) {
    if (q.length > 100 || page < 1 || page > 10000) {
      throw const FormatException();
    }
    return {'q': q, 'page': '$page', 'pageSize': '20'};
  }

  @override
  Future<AuthResult<RecordPage<LocalCustomerRecord>>> customers(
    String business, {
    String query = '',
    int page = 1,
  }) => _run(
    (token) async => _page(
      await apiClient.get(
        '${_base(business)}/customers',
        access: token,
        query: _query(query, page),
      ),
      (j) => _customer(j, business),
      page,
    ),
  );
  @override
  Future<AuthResult<LocalCustomerRecord>> customer(
    String business,
    String id,
  ) => _run(
    (token) async => _customer(
      await apiClient.get(
        '${_base(business)}/customers/${invitationId(id)}',
        access: token,
      ),
      business,
    ),
  );
  @override
  Future<AuthResult<LocalCustomerRecord>> saveCustomer(
    String business,
    CustomerDraft draft,
  ) => _run((token) async {
    if (draft.validate() != null) {
      throw const ApiException(ApiErrorCode.validation);
    }
    final path = '${_base(business)}/customers';
    final raw = draft.id == null
        ? await apiClient.post(
            path,
            draft.toJson(),
            access: token,
            expected: 201,
          )
        : await apiClient.put(
            '$path/${invitationId(draft.id!)}',
            draft.toJson(),
            access: token,
          );
    final c = _customer(raw, business);
    if (draft.id != null && c.id != draft.id) throw const FormatException();
    return c;
  });
  @override
  Future<AuthResult<RecordPage<VehicleRecord>>> vehicles({
    String? business,
    String? customer,
    String query = '',
    int page = 1,
  }) => _run(
    (token) async => _page(
      await apiClient.get(
        _vehicles(business, customer),
        access: token,
        query: _query(query, page),
      ),
      (j) => _vehicle(j, business, customer),
      page,
    ),
  );
  @override
  Future<AuthResult<VehicleRecord>> saveVehicle(
    VehicleDraft draft, {
    String? business,
    String? customer,
  }) => _run((token) async {
    if (draft.validate() != null) {
      throw const ApiException(ApiErrorCode.validation);
    }
    if (business != null && customer == null) throw const FormatException();
    final path = _vehicles(business, customer);
    final raw = draft.id == null
        ? await apiClient.post(
            path,
            draft.toJson(),
            access: token,
            expected: 201,
          )
        : await apiClient.put(
            '$path/${invitationId(draft.id!)}',
            draft.toJson(),
            access: token,
          );
    final v = _vehicle(raw, business, customer);
    if (draft.id != null && v.id != draft.id) throw const FormatException();
    return v;
  });
  @override
  Future<AuthResult<void>> archiveVehicle(VehicleRecord v) => _run((
    token,
  ) async {
    await apiClient.post(
      '${_vehicles(v.businessId, v.localCustomerId)}/${invitationId(v.id)}/archive',
      {'version': v.version},
      access: token,
      expected: 204,
    );
  });
}
