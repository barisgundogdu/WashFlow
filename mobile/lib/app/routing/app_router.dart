import 'package:flutter/material.dart';

import '../../features/welcome/presentation/welcome_screen.dart';

abstract final class AppRouter {
  static const welcome = '/';

  static Route<void> onGenerateRoute(RouteSettings settings) {
    return MaterialPageRoute<void>(
      settings: settings,
      builder: (context) => switch (settings.name) {
        welcome => const WelcomeScreen(),
        _ => const Scaffold(
          body: SafeArea(child: Center(child: Text('Sayfa bulunamadı.'))),
        ),
      },
    );
  }
}
