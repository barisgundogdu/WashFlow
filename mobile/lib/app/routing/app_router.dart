import 'package:flutter/material.dart';

import '../../features/auth/domain/auth_submission.dart';
import '../../features/auth/domain/session_service.dart';
import '../../features/auth/presentation/session_screen.dart';
import '../../features/auth/presentation/verification_screen.dart';
import '../../features/auth/presentation/auth_form_screen.dart';
import '../../features/auth/presentation/registration_choice_screen.dart';
import '../../features/welcome/presentation/welcome_screen.dart';

abstract final class AppRouter {
  static const verify = '/email/verify';
  static const session = '/session';
  static const welcome = '/';
  static const login = '/login';
  static const register = '/register';
  static const customerRegister = '/register/customer';
  static const businessRegister = '/register/business';

  static Route<void> onGenerateRoute(
    RouteSettings settings, {
    SessionService? sessionService,
    AuthSubmission submission = const UnavailableAuthSubmission(),
  }) {
    return MaterialPageRoute<void>(
      settings: settings,
      builder: (context) => switch (settings.name) {
        verify => VerificationScreen(
          service: sessionService,
          email: settings.arguments is String
              ? settings.arguments as String
              : '',
        ),
        session => SessionScreen(service: sessionService),
        welcome => const WelcomeScreen(),
        login => AuthFormScreen(
          kind: AuthFormKind.login,
          submission: submission,
        ),
        register => const RegistrationChoiceScreen(),
        customerRegister => AuthFormScreen(
          submission: submission,
          kind: AuthFormKind.customerRegistration,
        ),
        businessRegister => AuthFormScreen(
          submission: submission,
          kind: AuthFormKind.businessRegistration,
        ),
        _ => const Scaffold(
          body: SafeArea(child: Center(child: Text('Sayfa bulunamadı.'))),
        ),
      },
    );
  }
}
