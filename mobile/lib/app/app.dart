import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';

import 'routing/app_router.dart';
import '../features/auth/domain/context_service.dart';
import '../features/auth/domain/session_service.dart';
import '../features/auth/domain/auth_service.dart';
import '../features/auth/domain/auth_submission.dart';

class WashFlowApp extends StatelessWidget {
  const WashFlowApp({
    super.key,
    this.initialRoute = AppRouter.welcome,
    this.startupMessage,
    this.contextService,
    this.authService = const UnavailableAuthService(),
  });

  final AuthService authService;
  final ContextService? contextService;
  final String initialRoute;
  final String? startupMessage;

  @override
  Widget build(BuildContext context) {
    return MaterialApp(
      // Replacing an account/service boundary destroys the old route stack.
      key: ValueKey((authService, contextService)),
      title: 'WashFlow',
      initialRoute: initialRoute,
      builder: (context, child) => Column(
        children: [
          if (startupMessage != null)
            SafeArea(
              bottom: false,
              child: Material(
                child: Padding(
                  padding: const EdgeInsets.all(12),
                  child: Text(startupMessage!),
                ),
              ),
            ),
          Expanded(child: child!),
        ],
      ),
      debugShowCheckedModeBanner: false,
      locale: const Locale('tr'),
      supportedLocales: const [Locale('tr')],
      localizationsDelegates: GlobalMaterialLocalizations.delegates,
      theme: ThemeData(
        colorScheme: ColorScheme.fromSeed(seedColor: const Color(0xFF006B75)),
        useMaterial3: true,
        scaffoldBackgroundColor: const Color(0xFFF6F9F9),
        appBarTheme: const AppBarTheme(
          backgroundColor: Color(0xFFF6F9F9),
          surfaceTintColor: Colors.transparent,
          centerTitle: true,
        ),
        inputDecorationTheme: InputDecorationTheme(
          filled: true,
          fillColor: Colors.white,
          contentPadding: const EdgeInsets.symmetric(
            horizontal: 16,
            vertical: 18,
          ),
          border: OutlineInputBorder(borderRadius: BorderRadius.circular(16)),
          enabledBorder: OutlineInputBorder(
            borderRadius: BorderRadius.circular(16),
            borderSide: const BorderSide(color: Color(0xFFCFDDDF)),
          ),
          errorMaxLines: 3,
        ),
        filledButtonTheme: FilledButtonThemeData(
          style: FilledButton.styleFrom(
            minimumSize: const Size(0, 56),
            padding: const EdgeInsets.symmetric(horizontal: 24, vertical: 16),
            shape: RoundedRectangleBorder(
              borderRadius: BorderRadius.circular(16),
            ),
          ),
        ),
        textButtonTheme: TextButtonThemeData(
          style: TextButton.styleFrom(minimumSize: const Size(48, 48)),
        ),
      ),
      onGenerateRoute: (settings) => AppRouter.onGenerateRoute(
        settings,
        submission: ServiceAuthSubmission(authService),
        contextService: contextService,
        sessionService: authService is SessionService
            ? authService as SessionService
            : null,
      ),
    );
  }
}
