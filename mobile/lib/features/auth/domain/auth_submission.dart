import 'auth_service.dart';

/// UI boundary only. Implement transport after backend contracts are approved.
enum AuthFormKind { login, customerRegistration, businessRegistration }

class AuthFormInput {
  const AuthFormInput({
    required this.kind,
    required this.email,
    required this.password,
    this.fullName,
    this.businessName,
  });

  final AuthFormKind kind;
  final String email;
  final String password;
  final String? fullName;
  final String? businessName;
}

/// Deliberately has no successful authentication or session result.
abstract interface class AuthSubmission {
  Future<void> submit(AuthFormInput input);
}

class AuthServiceUnavailable implements Exception {
  const AuthServiceUnavailable();
}

class UnavailableAuthSubmission implements AuthSubmission {
  const UnavailableAuthSubmission();

  @override
  Future<void> submit(AuthFormInput input) async {
    throw const AuthServiceUnavailable();
  }
}

/// Bridges existing form behavior to the typed service without creating a session.
class ServiceAuthSubmission implements AuthSubmission {
  const ServiceAuthSubmission(this.service);
  final AuthService service;

  @override
  Future<void> submit(AuthFormInput input) async {
    switch (input.kind) {
      case AuthFormKind.login:
        _check(
          await service.login(
            LoginRequest(email: input.email, password: input.password),
          ),
        );
      case AuthFormKind.customerRegistration:
        _check(
          await service.registerCustomer(
            CustomerRegistrationRequest(
              fullName: input.fullName!,
              email: input.email,
              password: input.password,
            ),
          ),
        );
      case AuthFormKind.businessRegistration:
        _check(
          await service.registerBusiness(
            BusinessRegistrationRequest(
              businessName: input.businessName!,
              ownerFullName: input.fullName!,
              email: input.email,
              password: input.password,
            ),
          ),
        );
    }
  }

  void _check<T>(AuthResult<T> result) {
    if (result case AuthFailure<T>(:final error)) {
      if (error.code == AuthErrorCode.unavailable) {
        throw const AuthServiceUnavailable();
      }
      throw AuthSubmissionFailure(error);
    }
    // Acknowledgement is not consumed as login success by the UI.
  }
}

class AuthSubmissionFailure implements Exception {
  const AuthSubmissionFailure(this.error);
  final AuthError error;
}
