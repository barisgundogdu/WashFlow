/// Transport-independent input. Passwords must never be logged or persisted here.
class LoginRequest {
  const LoginRequest({required this.email, required this.password});
  final String email;
  final String password;
}

class CustomerRegistrationRequest {
  const CustomerRegistrationRequest({
    required this.fullName,
    required this.email,
    required this.password,
  });
  final String fullName;
  final String email;
  final String password;
}

class BusinessRegistrationRequest {
  const BusinessRegistrationRequest({
    required this.businessName,
    required this.ownerFullName,
    required this.email,
    required this.password,
  });
  final String businessName;
  final String ownerFullName;
  final String email;
  final String password;
}

/// A session was established by the backend; contains no inferred identity.
/// This identifier alone grants no permission or tenant membership.
class LoginResult {
  const LoginResult();
}

/// Acknowledgement only: does not imply activation or an authenticated session.
class CustomerRegistrationResult {
  const CustomerRegistrationResult({this.referenceId});
  final String? referenceId;
}

/// Does not decide whether business approval is required or already granted.
class BusinessRegistrationResult {
  const BusinessRegistrationResult({this.referenceId});
  final String? referenceId;
}

enum AuthErrorCode {
  unavailable,
  invalidCredentials,
  validation,
  conflict,
  unexpected,
  network,
  timeout,
  rateLimited,
  invalidVerificationToken,
  storage,
}

enum AuthInputField { email, password, fullName, businessName }

enum AuthFieldErrorCode { required, invalid, alreadyUsed }

/// Safe semantic errors. Adapters must not expose raw server/transport messages.
class AuthError {
  AuthError({
    required this.code,
    Map<AuthInputField, AuthFieldErrorCode> fieldErrors = const {},
  }) : fieldErrors = Map.unmodifiable(fieldErrors);
  final AuthErrorCode code;
  final Map<AuthInputField, AuthFieldErrorCode> fieldErrors;
}

sealed class AuthResult<T> {
  const AuthResult();
}

final class AuthSuccess<T> extends AuthResult<T> {
  const AuthSuccess(this.value);
  final T value;
}

final class AuthFailure<T> extends AuthResult<T> {
  const AuthFailure(this.error);
  final AuthError error;
}

/// Transport-independent authentication boundary. The server is authoritative.
abstract interface class AuthService {
  Future<AuthResult<LoginResult>> login(LoginRequest request);
  Future<AuthResult<CustomerRegistrationResult>> registerCustomer(
    CustomerRegistrationRequest request,
  );
  Future<AuthResult<BusinessRegistrationResult>> registerBusiness(
    BusinessRegistrationRequest request,
  );
}

/// Preview/test fallback. main.dart injects the real API adapter.
class UnavailableAuthService implements AuthService {
  const UnavailableAuthService();

  AuthFailure<T> _unavailable<T>() =>
      AuthFailure(AuthError(code: AuthErrorCode.unavailable));

  @override
  Future<AuthResult<LoginResult>> login(LoginRequest request) async =>
      _unavailable();

  @override
  Future<AuthResult<CustomerRegistrationResult>> registerCustomer(
    CustomerRegistrationRequest request,
  ) async => _unavailable();

  @override
  Future<AuthResult<BusinessRegistrationResult>> registerBusiness(
    BusinessRegistrationRequest request,
  ) async => _unavailable();
}
