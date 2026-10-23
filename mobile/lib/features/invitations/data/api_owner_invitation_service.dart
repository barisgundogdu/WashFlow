import '../../../core/network/api_client.dart';
import '../../auth/domain/auth_service.dart';
import '../../auth/domain/session_service.dart';
import '../domain/invitation.dart';
import '../domain/owner_invitation_service.dart';

/// Stateless: scope is captured for each request, never a mutable global tenant.
class ApiOwnerInvitationService implements OwnerInvitationService {
  ApiOwnerInvitationService({required this.apiClient, required this.session});
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
      return AuthSuccess(await action(credential));
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

  String _path(String business) =>
      '/api/v1/businesses/${invitationId(business)}/employee-invitations';
  EmployeeInvitation _parse(Object? value) {
    if (value is! Map<String, dynamic>) throw const FormatException();
    return EmployeeInvitation.fromJson(value);
  }

  @override
  Future<AuthResult<List<EmployeeInvitation>>> list(String businessId) =>
      _run((access) async {
        final json = await apiClient.get(_path(businessId), access: access);
        if (json is! List) throw const FormatException();
        final items = json.map(_parse).toList();
        if (items.map((item) => item.id).toSet().length != items.length) {
          throw const FormatException();
        }
        return List.unmodifiable(items);
      });
  @override
  Future<AuthResult<EmployeeInvitation>> create(
    String businessId,
    String email,
  ) => _run(
    (access) async => _parse(
      await apiClient.post(
        _path(businessId),
        {'email': email.trim()},
        access: access,
        expected: 201,
      ),
    ),
  );
  @override
  Future<AuthResult<void>> revoke(String businessId, String id) =>
      _run((access) async {
        await apiClient.post(
          '${_path(businessId)}/${invitationId(id)}/revoke',
          null,
          access: access,
          expected: 204,
        );
      });
  @override
  Future<AuthResult<EmployeeInvitation>> resend(String businessId, String id) =>
      _run(
        (access) async => _parse(
          await apiClient.post(
            '${_path(businessId)}/${invitationId(id)}/resend',
            null,
            access: access,
          ),
        ),
      );
}
