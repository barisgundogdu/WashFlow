import '../features/scheduling/domain/scheduling.dart';
import '../features/service_catalog/domain/service_catalog.dart';
import '../features/customer_vehicles/domain/customer_vehicles.dart';

import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';

import '../features/auth/domain/auth_service.dart';
import '../features/auth/domain/auth_submission.dart';
import '../features/auth/domain/context_service.dart';
import '../features/auth/domain/session_service.dart';
import '../features/business_settings/domain/business_settings.dart';
import '../features/invitations/domain/invitation_redemption_service.dart';
import '../features/invitations/domain/owner_invitation_service.dart';
import 'routing/app_router.dart';
import 'theme/washflow_theme.dart';

class WashFlowApp extends StatelessWidget {
  const WashFlowApp({
    super.key,
    this.initialRoute = AppRouter.welcome,
    this.startupMessage,
    this.contextService,
    this.invitationService,
    this.settingsService,
    this.customerVehicleService,
    this.catalogService,
    this.schedulingService,
    this.redemptionService,
    this.authService = const UnavailableAuthService(),
  });

  final CatalogService? catalogService;
  final SchedulingService? schedulingService;
  final AuthService authService;
  final ContextService? contextService;
  final OwnerInvitationService? invitationService;
  final BusinessSettingsService? settingsService;
  final CustomerVehicleService? customerVehicleService;
  final InvitationRedemptionService? redemptionService;
  final String initialRoute;
  final String? startupMessage;

  @override
  Widget build(BuildContext context) {
    return MaterialApp(
      // Replacing an account/service boundary destroys the old route stack.
      key: ValueKey((
        authService,
        contextService,
        invitationService,
        settingsService,
        customerVehicleService,
        catalogService,
        schedulingService,
        redemptionService,
      )),
      title: 'WashFlow',
      initialRoute: initialRoute,
      builder: (context, child) => Column(
        children: [
          if (startupMessage != null)
            SafeArea(
              bottom: false,
              child: Material(
                child: Padding(
                  padding: const EdgeInsets.all(WashFlowSpacing.md),
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
      theme: WashFlowTheme.light,
      onGenerateRoute: (settings) => AppRouter.onGenerateRoute(
        settings,
        submission: ServiceAuthSubmission(authService),
        authService: authService,
        redemptionService: redemptionService,
        contextService: contextService,
        invitationService: invitationService,
        settingsService: settingsService,
        customerVehicleService: customerVehicleService,
        catalogService: catalogService,
        schedulingService: schedulingService,
        sessionService: authService is SessionService
            ? authService as SessionService
            : null,
      ),
    );
  }
}
