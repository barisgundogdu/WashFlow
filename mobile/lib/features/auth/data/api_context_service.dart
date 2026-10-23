import '../../../core/network/api_client.dart';
import '../domain/auth_service.dart';
import '../domain/context_service.dart';
import '../domain/session_service.dart';
import '../domain/user_contexts.dart';

class ApiContextService implements ContextService {
  ApiContextService({required this.apiClient, required this.session});

  final ApiClient apiClient;
  final AccessTokenProvider session;

  @override
  Future<AuthResult<UserContexts>> getContexts() async {
    try {
      final access = await session.accessToken();
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
      final json = await apiClient.get(
        '/api/v1/auth/contexts',
        access: credential,
      );
      if (json is! Map<String, dynamic>) {
        return AuthFailure(AuthError(code: AuthErrorCode.unexpected));
      }
      return AuthSuccess(UserContexts.fromJson(json));
    } on ApiException catch (e) {
      return AuthFailure(
        AuthError(
          code: switch (e.code) {
            ApiErrorCode.unauthenticated => AuthErrorCode.unauthenticated,
            ApiErrorCode.forbidden => AuthErrorCode.forbidden,
            ApiErrorCode.conflict => AuthErrorCode.conflict,
            ApiErrorCode.validation => AuthErrorCode.validation,
            ApiErrorCode.invalidVerificationToken => AuthErrorCode.unexpected,
            ApiErrorCode.rateLimited => AuthErrorCode.rateLimited,
            ApiErrorCode.unavailable => AuthErrorCode.unavailable,
            ApiErrorCode.network => AuthErrorCode.network,
            ApiErrorCode.timeout => AuthErrorCode.timeout,
            ApiErrorCode.unexpected => AuthErrorCode.unexpected,
          },
        ),
      );
    } catch (_) {
      return AuthFailure(AuthError(code: AuthErrorCode.unexpected));
    }
  }
}
