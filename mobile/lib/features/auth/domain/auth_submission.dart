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
