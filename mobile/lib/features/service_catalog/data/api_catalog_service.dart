import '../../../core/network/api_client.dart';
import '../../auth/domain/auth_service.dart';
import '../../auth/domain/session_service.dart';
import '../../invitations/domain/invitation.dart';
import '../../customer_vehicles/domain/customer_vehicles.dart';
import '../domain/service_catalog.dart';

class ApiCatalogService implements CatalogService {
  ApiCatalogService({required this.apiClient, required this.session});
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

  String _base(String b) => '/api/v1/businesses/${invitationId(b)}/services';
  CatalogServiceRecord _record(Object? raw, String b, [String? id]) {
    if (raw is! Map<String, dynamic>) throw const FormatException();
    final value = CatalogServiceRecord.fromJson(raw);
    if (value.businessId != b || id != null && value.id != id) {
      throw const FormatException();
    }
    return value;
  }

  @override
  Future<AuthResult<RecordPage<CatalogServiceRecord>>> list(
    String business, {
    String query = '',
    String status = 'all',
    int page = 1,
  }) => _run((token) async {
    if (query.length > 100 ||
        page < 1 ||
        page > 10000 ||
        !{'all', 'enabled', 'disabled'}.contains(status)) {
      throw const ApiException(ApiErrorCode.validation);
    }
    final raw = await apiClient.get(
      _base(business),
      access: token,
      query: {'q': query, 'status': status, 'page': '$page', 'pageSize': '20'},
    );
    if (raw is! Map<String, dynamic> ||
        raw['items'] is! List ||
        (raw['items'] as List).length > 20 ||
        raw['page'] != page ||
        raw['pageSize'] != 20 ||
        raw['hasMore'] is! bool) {
      throw const FormatException();
    }
    return RecordPage(
      List.unmodifiable(
        (raw['items'] as List).map((j) => _record(j, business)),
      ),
      page,
      raw['hasMore'],
    );
  });
  @override
  Future<AuthResult<CatalogServiceRecord>> read(String business, String id) =>
      _run(
        (token) async => _record(
          await apiClient.get(
            '${_base(business)}/${invitationId(id)}',
            access: token,
          ),
          business,
          id,
        ),
      );
  @override
  Future<AuthResult<CatalogServiceRecord>> save(
    String business,
    CatalogDraft draft,
  ) => _run((token) async {
    if (draft.validate() != null) {
      throw const ApiException(ApiErrorCode.validation);
    }
    final raw = draft.id == null
        ? await apiClient.post(
            _base(business),
            draft.toJson(),
            access: token,
            expected: 201,
          )
        : await apiClient.put(
            '${_base(business)}/${invitationId(draft.id!)}',
            draft.toJson(),
            access: token,
          );
    return _record(raw, business, draft.id);
  });
  @override
  Future<AuthResult<void>> archive(
    String business,
    CatalogServiceRecord record,
  ) => _run((token) async {
    if (record.businessId != business) throw const FormatException();
    await apiClient.post(
      '${_base(business)}/${invitationId(record.id)}/archive',
      {'version': record.version},
      access: token,
      expected: 204,
    );
  });
}
