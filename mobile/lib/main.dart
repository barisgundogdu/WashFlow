import 'features/service_catalog/data/api_catalog_service.dart';
import 'features/customer_vehicles/data/api_customer_vehicle_service.dart';

import 'package:flutter/material.dart';

import 'app/app.dart';
import 'app/routing/app_router.dart';
import 'features/auth/data/api_auth_service.dart';
import 'features/auth/data/api_context_service.dart';
import 'features/auth/data/token_store.dart';
import 'features/auth/domain/auth_service.dart';
import 'features/auth/domain/session_service.dart';
import 'features/business_settings/data/api_business_settings_service.dart';
import 'features/invitations/data/api_invitation_redemption_service.dart';
import 'features/invitations/data/api_owner_invitation_service.dart';

Future<void> main() async {
  WidgetsFlutterBinding.ensureInitialized();
  const configured = String.fromEnvironment('API_BASE_URL');
  if (configured.isEmpty) {
    runApp(
      const WashFlowApp(
        startupMessage:
            'API adresi yapılandırılmamış. API_BASE_URL ayarını kontrol edin.',
      ),
    );
    return;
  }
  try {
    final service = ApiAuthService(
      baseUrl: Uri.parse(configured),
      store: const SecureTokenStore(),
    );
    final result = await service.restoreSession();
    runApp(
      WashFlowApp(
        authService: service,
        contextService: ApiContextService(
          apiClient: service.apiClient,
          session: service,
        ),
        catalogService: ApiCatalogService(
          apiClient: service.apiClient,
          session: service,
        ),
        customerVehicleService: ApiCustomerVehicleService(
          apiClient: service.apiClient,
          session: service,
        ),
        settingsService: ApiBusinessSettingsService(
          apiClient: service.apiClient,
          session: service,
        ),
        invitationService: ApiOwnerInvitationService(
          apiClient: service.apiClient,
          session: service,
        ),
        redemptionService: ApiInvitationRedemptionService(
          apiClient: service.apiClient,
          session: service,
        ),
        initialRoute: result is AuthSuccess<bool> && result.value
            ? AppRouter.session
            : AppRouter.welcome,
        startupMessage: result is AuthFailure<bool>
            ? authErrorMessage(result.error)
            : null,
      ),
    );
  } catch (_) {
    runApp(
      const WashFlowApp(
        startupMessage: 'API adresi geçersiz. Yapılandırmayı kontrol edin.',
      ),
    );
  }
}
