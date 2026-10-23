import '../../../core/network/api_client.dart';
import '../../auth/domain/auth_service.dart';
import '../../auth/domain/session_service.dart';
import '../domain/invitation_redemption_service.dart';

/// Uses only existing recipient endpoints; never retries a mutation.
class ApiInvitationRedemptionService implements InvitationRedemptionService {
  ApiInvitationRedemptionService({
    required this.apiClient,
    required this.session,
  });
  final ApiClient apiClient;
  final AccessTokenProvider session;

  Future<AuthResult<void>> _safe(Future<void> Function() action) async {
    try {
      await action();
      return const AuthSuccess(null);
    } on ApiException catch (e) {
      return AuthFailure(
        AuthError(
          code: switch (e.code) {
            ApiErrorCode.unauthenticated => AuthErrorCode.unauthenticated,
            ApiErrorCode.forbidden => AuthErrorCode.forbidden,
            ApiErrorCode.notFound => AuthErrorCode.notFound,
            ApiErrorCode.invalidInvitation => AuthErrorCode.invalidInvitation,
            ApiErrorCode.conflict => AuthErrorCode.conflict,
            ApiErrorCode.validation => AuthErrorCode.validation,
            ApiErrorCode.rateLimited => AuthErrorCode.rateLimited,
            ApiErrorCode.network => AuthErrorCode.network,
            ApiErrorCode.timeout => AuthErrorCode.timeout,
            ApiErrorCode.unavailable => AuthErrorCode.unavailable,
            _ => AuthErrorCode.unexpected,
          },
        ),
      );
    } catch (_) {
      return AuthFailure(AuthError(code: AuthErrorCode.unexpected));
    }
  }

  @override
  Future<AuthResult<void>> register(InvitationRegistrationRequest request) =>
      _safe(() async {
        final response = await apiClient.post(
          '/api/v1/employee-invitations/register',
          {
            'token': request.token,
            'fullName': request.fullName.trim(),
            'email': request.email.trim(),
            'password': request.password,
          },
          expected: 202,
        );
        if (response is! Map<String, dynamic> ||
            response['message'] is! String) {
          throw const FormatException();
        }
        // Acknowledgement only: no account/session/membership is inferred.
      });
  @override
  Future<AuthResult<void>> accept(String credential) async {
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
      final bearer = (access as AuthSuccess<String>).value;
      if (bearer.isEmpty) {
        return AuthFailure(AuthError(code: AuthErrorCode.unauthenticated));
      }
      return await _safe(() async {
        await apiClient.post(
          '/api/v1/employee-invitations/accept',
          {'token': credential},
          access: bearer,
          expected: 204,
        );
      });
    } catch (_) {
      return AuthFailure(AuthError(code: AuthErrorCode.unexpected));
    }
  }
}
