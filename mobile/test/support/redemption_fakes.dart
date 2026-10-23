import 'package:washflow/features/auth/domain/auth_service.dart';
import 'package:washflow/features/auth/domain/session_service.dart';
import 'package:washflow/features/invitations/domain/invitation_redemption_service.dart';

import 'session_fakes.dart';

// Synthetic format-only fixture. Compare booleans, never print credential values.
String redemptionFixture() => List.filled(43, 'r').join();

class RedemptionSession extends FakeSession implements AccessTokenProvider {
  bool signedIn = false;
  int logins = 0,
      verifications = 0,
      customerRegistrations = 0,
      businessRegistrations = 0;
  Future<AuthResult<LoginResult>> Function()? onLogin;
  Future<AuthResult<String>> Function()? onAccess;
  Future<AuthResult<void>> Function()? onVerify;
  @override
  Future<AuthResult<String>> accessToken() async => onAccess != null
      ? onAccess!()
      : signedIn
      ? const AuthSuccess('synthetic-session')
      : AuthFailure(AuthError(code: AuthErrorCode.invalidCredentials));
  @override
  Future<AuthResult<LoginResult>> login(LoginRequest request) async {
    logins++;
    changeIdentity();
    final result = onLogin == null
        ? const AuthSuccess(LoginResult())
        : await onLogin!();
    signedIn = result is AuthSuccess<LoginResult>;
    return result;
  }

  @override
  Future<AuthResult<void>> verifyEmail(String token) async {
    verifications++;
    return onVerify == null ? const AuthSuccess(null) : onVerify!();
  }

  @override
  Future<AuthResult<void>> logout() async {
    signedIn = false;
    return super.logout();
  }

  @override
  Future<AuthResult<CustomerRegistrationResult>> registerCustomer(
    CustomerRegistrationRequest request,
  ) async {
    customerRegistrations++;
    return super.registerCustomer(request);
  }

  @override
  Future<AuthResult<BusinessRegistrationResult>> registerBusiness(
    BusinessRegistrationRequest request,
  ) async {
    businessRegistrations++;
    return super.registerBusiness(request);
  }
}

class FakeRedemption implements InvitationRedemptionService {
  int registrations = 0, acceptances = 0;
  Future<AuthResult<void>> Function(InvitationRegistrationRequest)? onRegister;
  Future<AuthResult<void>> Function()? onAccept;
  @override
  Future<AuthResult<void>> register(
    InvitationRegistrationRequest request,
  ) async {
    registrations++;
    return onRegister == null ? const AuthSuccess(null) : onRegister!(request);
  }

  @override
  Future<AuthResult<void>> accept(String credential) async {
    acceptances++;
    return onAccept == null ? const AuthSuccess(null) : onAccept!();
  }
}
