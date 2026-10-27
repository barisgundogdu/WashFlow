import '../../../core/network/api_client.dart';
import '../../auth/domain/auth_service.dart';
import '../../auth/domain/session_service.dart';
import '../../invitations/domain/invitation.dart';
import '../domain/business_settings.dart';

class ApiBusinessSettingsService implements BusinessSettingsService {
  ApiBusinessSettingsService({required this.apiClient, required this.session});
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

  String _path(String id) => '/api/v1/businesses/${invitationId(id)}/settings';
  BusinessSettings _parse(Object? json, String id) {
    if (json is! Map<String, dynamic>) {
      throw const FormatException();
    }
    final result = BusinessSettings.fromJson(json);
    if (result.businessId != id.toLowerCase()) {
      throw const FormatException();
    }
    return result;
  }

  @override
  Future<AuthResult<BusinessSettings>> read(String businessId) => _run(
    (access) async => _parse(
      await apiClient.get(_path(businessId), access: access),
      businessId,
    ),
  );
  @override
  Future<AuthResult<BusinessSettings>> save(BusinessSettings settings) => _run(
    (access) async => _parse(
      await apiClient.post(
        _path(settings.businessId),
        settings.toJson(),
        access: access,
      ),
      settings.businessId,
    ),
  );
}
