import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:washflow/app/app.dart';
import 'package:washflow/features/auth/domain/auth_service.dart';
import 'package:washflow/features/auth/domain/auth_submission.dart';
import 'package:washflow/features/auth/presentation/auth_form_screen.dart';

class RecordingService implements AuthService {
  LoginRequest? loginRequest;
  CustomerRegistrationRequest? customerRequest;
  BusinessRegistrationRequest? businessRequest;
  AuthErrorCode code = AuthErrorCode.unavailable;
  AuthFailure<T> failure<T>() => AuthFailure(AuthError(code: code));

  @override
  Future<AuthResult<LoginResult>> login(LoginRequest request) async {
    loginRequest = request;
    return failure();
  }

  @override
  Future<AuthResult<CustomerRegistrationResult>> registerCustomer(
    CustomerRegistrationRequest request,
  ) async {
    customerRequest = request;
    return failure();
  }

  @override
  Future<AuthResult<BusinessRegistrationResult>> registerBusiness(
    BusinessRegistrationRequest request,
  ) async {
    businessRequest = request;
    return failure();
  }
}

void main() {
  test(
    'Default service reports typed unavailability for every operation',
    () async {
      const service = UnavailableAuthService();
      final results = <AuthResult<Object>>[
        await service.login(
          const LoginRequest(email: 'a@b.com', password: ' p '),
        ),
        await service.registerCustomer(
          const CustomerRegistrationRequest(
            fullName: 'Ayşe',
            email: 'a@b.com',
            password: ' p ',
          ),
        ),
        await service.registerBusiness(
          const BusinessRegistrationRequest(
            businessName: 'Yıkama',
            ownerFullName: 'Ayşe',
            email: 'a@b.com',
            password: ' p ',
          ),
        ),
      ];
      for (final result in results) {
        expect(result, isA<AuthFailure<Object>>());
        expect(
          (result as AuthFailure<Object>).error.code,
          AuthErrorCode.unavailable,
        );
      }
    },
  );
  test('Adapter maps each operation and preserves passwords exactly', () async {
    final service = RecordingService();
    final submission = ServiceAuthSubmission(service);
    for (final kind in AuthFormKind.values) {
      await expectLater(
        submission.submit(
          AuthFormInput(
            kind: kind,
            email: 'ayse@example.com',
            password: ' p ',
            fullName: 'Ayşe Yılmaz',
            businessName: 'Örnek Yıkama',
          ),
        ),
        throwsA(isA<AuthServiceUnavailable>()),
      );
    }
    expect(service.loginRequest!.email, 'ayse@example.com');
    expect(service.loginRequest!.password, ' p ');
    expect(service.customerRequest!.fullName, 'Ayşe Yılmaz');
    expect(service.customerRequest!.email, 'ayse@example.com');
    expect(service.customerRequest!.password, ' p ');
    expect(service.businessRequest!.ownerFullName, 'Ayşe Yılmaz');
    expect(service.businessRequest!.businessName, 'Örnek Yıkama');
    expect(service.businessRequest!.email, 'ayse@example.com');
    expect(service.businessRequest!.password, ' p ');
  });
  test('Typed failures survive adapter', () async {
    final service = RecordingService()..code = AuthErrorCode.invalidCredentials;
    await expectLater(
      ServiceAuthSubmission(service).submit(
        const AuthFormInput(
          kind: AuthFormKind.login,
          email: 'a@b.com',
          password: 'x',
        ),
      ),
      throwsA(
        isA<AuthSubmissionFailure>().having(
          (e) => e.error.code,
          'code',
          AuthErrorCode.invalidCredentials,
        ),
      ),
    );
  });
  test('Field errors are immutable snapshots', () {
    final fields = {AuthInputField.email: AuthFieldErrorCode.invalid};
    final error = AuthError(
      code: AuthErrorCode.validation,
      fieldErrors: fields,
    );
    fields.clear();
    expect(error.fieldErrors[AuthInputField.email], AuthFieldErrorCode.invalid);
    expect(() => error.fieldErrors.clear(), throwsUnsupportedError);
  });
  testWidgets(
    'App injection reaches form without changing Turkish unavailable behavior',
    (tester) async {
      final service = RecordingService();
      await tester.pumpWidget(WashFlowApp(authService: service));
      await tester.tap(find.text('Giriş yap'));
      await tester.pumpAndSettle();
      await tester.enterText(
        find.byKey(const ValueKey('email')),
        ' ayse@example.com ',
      );
      await tester.enterText(find.byKey(const ValueKey('password')), ' p ');
      await tester.ensureVisible(find.byKey(const ValueKey('submit')));
      await tester.tap(find.byKey(const ValueKey('submit')));
      await tester.pumpAndSettle();
      expect(service.loginRequest!.email, 'ayse@example.com');
      expect(service.loginRequest!.password, ' p ');
      expect(
        find.text(
          'Giriş hizmeti henüz kullanıma açık değil. Şu anda giriş yapılamıyor.',
        ),
        findsOneWidget,
      );
      expect(find.byType(AuthFormScreen), findsOneWidget);
    },
  );
}
