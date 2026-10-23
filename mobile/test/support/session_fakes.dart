import 'package:flutter/foundation.dart';
import 'package:washflow/features/auth/domain/auth_service.dart';
import 'package:washflow/features/auth/domain/context_service.dart';
import 'package:washflow/features/auth/domain/session_service.dart';
import 'package:washflow/features/auth/domain/user_contexts.dart';

const businessA = '22222222-2222-2222-2222-222222222222';
const businessB = '44444444-4444-4444-4444-444444444444';
Map<String, dynamic> businessContext({
  String id = businessA,
  String name = 'İşletme A',
  List<String> roles = const ['Owner'],
}) => {
  'businessId': id,
  'businessName': name,
  'membershipId': id == businessA
      ? '33333333-3333-3333-3333-333333333333'
      : '55555555-5555-5555-5555-555555555555',
  'roles': roles,
};
UserContexts grants({
  bool customer = false,
  List<Map<String, dynamic>> businesses = const [],
}) => UserContexts.fromJson({
  'customerContext': customer
      ? {'customerProfileId': '11111111-1111-1111-1111-111111111111'}
      : null,
  'businessContexts': businesses,
});

class FakeContexts implements ContextService {
  FakeContexts(this.value);
  UserContexts value;
  int calls = 0;
  Future<AuthResult<UserContexts>> Function()? handler;
  @override
  Future<AuthResult<UserContexts>> getContexts() async {
    calls++;
    return handler == null ? AuthSuccess(value) : handler!();
  }
}

class FakeSession extends ChangeNotifier
    implements AuthService, SessionService, SessionIdentity {
  int revision = 0;
  int logouts = 0;
  int checks = 0;
  AuthResult<void> logoutResult = const AuthSuccess(null);
  AuthResult<void> refreshResult = const AuthSuccess(null);
  Future<AuthResult<void>> Function()? logoutHandler;
  @override
  int get sessionRevision => revision;
  void changeIdentity() {
    revision++;
    notifyListeners();
  }

  @override
  Future<AuthResult<LoginResult>> login(LoginRequest request) async {
    changeIdentity();
    return const AuthSuccess(LoginResult());
  }

  @override
  Future<AuthResult<bool>> restoreSession() async {
    changeIdentity();
    return const AuthSuccess(true);
  }

  @override
  Future<AuthResult<void>> refreshSession() async {
    checks++;
    return refreshResult;
  }

  @override
  Future<AuthResult<void>> logout() async {
    logouts++;
    changeIdentity();
    return logoutHandler == null ? logoutResult : logoutHandler!();
  }

  @override
  Future<AuthResult<void>> verifyEmail(String token) async =>
      const AuthSuccess(null);
  @override
  Future<AuthResult<void>> resendVerification(String email) async =>
      const AuthSuccess(null);
  @override
  Future<AuthResult<CustomerRegistrationResult>> registerCustomer(
    CustomerRegistrationRequest request,
  ) async => const AuthSuccess(CustomerRegistrationResult());
  @override
  Future<AuthResult<BusinessRegistrationResult>> registerBusiness(
    BusinessRegistrationRequest request,
  ) async => const AuthSuccess(BusinessRegistrationResult());
}
