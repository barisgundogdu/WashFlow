import 'auth_service.dart';

/// Form intent; server responses determine success.
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

/// Completes only on successful server acknowledgement.
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

/// Bridges the form to the typed service; the data adapter owns the session.
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
  }
}

class AuthSubmissionFailure implements Exception {
  const AuthSubmissionFailure(this.error);
  final AuthError error;
}
