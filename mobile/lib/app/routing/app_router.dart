import 'package:flutter/material.dart';

import '../../features/auth/domain/auth_submission.dart';
import '../../features/auth/presentation/auth_form_screen.dart';
import '../../features/auth/presentation/registration_choice_screen.dart';
import '../../features/welcome/presentation/welcome_screen.dart';

abstract final class AppRouter {
  static const welcome = '/';
  static const login = '/login';
  static const register = '/register';
  static const customerRegister = '/register/customer';
  static const businessRegister = '/register/business';

  static Route<void> onGenerateRoute(
    RouteSettings settings, {
    AuthSubmission submission = const UnavailableAuthSubmission(),
  }) {
    return MaterialPageRoute<void>(
      settings: settings,
      builder: (context) => switch (settings.name) {
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
