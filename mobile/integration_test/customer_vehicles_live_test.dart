import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';
import 'package:washflow/app/app.dart';
import 'package:washflow/app/routing/app_router.dart';
import 'package:washflow/features/auth/data/api_auth_service.dart';
import 'package:washflow/features/auth/data/api_context_service.dart';
import 'package:washflow/features/auth/domain/auth_service.dart';
import 'package:washflow/features/auth/domain/user_contexts.dart';
import 'package:washflow/features/customer_vehicles/data/api_customer_vehicle_service.dart';
import 'package:washflow/features/customer_vehicles/domain/customer_vehicles.dart';

import 'invitation_live_test.dart' as live;

Future<void> mount(WidgetTester t, ApiAuthService auth) async {
  await t.pumpWidget(
    WashFlowApp(
      authService: auth,
      contextService: ApiContextService(
        apiClient: auth.apiClient,
        session: auth,
      ),
      customerVehicleService: ApiCustomerVehicleService(
        apiClient: auth.apiClient,
        session: auth,
      ),
      initialRoute: AppRouter.session,
    ),
  );
  await t.pumpAndSettle();
}

Future<void> edit(WidgetTester t, String key, String value) async {
  final finder = find.byKey(ValueKey(key));
  await live.visible(t, finder);
  await t.ensureVisible(finder);
  await t.tap(finder);
  await t.pumpAndSettle();
  await t.enterText(finder, value);
  await t.pumpAndSettle();
}

Future<void> tapText(WidgetTester t, String value) async {
  final f = find.text(value).first;
  await live.visible(t, f);
  await t.ensureVisible(f);
  await t.tap(f);
  await t.pumpAndSettle();
}

Future<void> vehicleFields(WidgetTester t, String plate) async {
  await edit(t, 'plate', plate);
  await edit(t, 'brand', 'Test brand');
  await edit(t, 'model', 'Test model');
  final category = find.byKey(const ValueKey('category'));
  await t.ensureVisible(category);
  await t.tap(category);
  await t.pumpAndSettle();
  await tapText(t, 'Otomobil');
  await edit(t, 'modelYear', '2020');
}

void main() {
  IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  setUp(() async {
    expect(
      live.base == 'http://127.0.0.1:5189',
      isTrue,
      reason: 'Use only the isolated test backend.',
    );
    await live.store.clear();
  });
  tearDown(() => live.store.clear());
  testWidgets(
    'live Owner creates searches edits customer and manages local vehicle',
    (t) async {
      final auth = await live.owner();
      await mount(t, auth);
      await live.tap(t, 'Müşteriler ve araçlar');
      await live.visible(t, find.text('Kayıt bulunamadı.'));
      await live.tap(t, 'Müşteri ekle');
      await edit(t, 'fullName', 'Phase 7 local person');
      await edit(t, 'phone', '0555 888 77 66');
      await live.tap(t, 'Kaydet');
      await live.visible(t, find.text('Müşteriler ve araçlar'));
      await edit(t, 'record-search', '555888');
      await live.visible(t, find.text('Phase 7 local person'));
      await tapText(t, 'Phase 7 local person');
      await live.visible(t, find.text('Müşteri ayrıntıları'));
      await live.tap(t, 'Müşteriyi düzenle');
      await edit(t, 'email', 'local-phase7@example.test');
      await live.tap(t, 'Kaydet');
      await live.visible(t, find.text('local-phase7@example.test'));
      await live.tap(t, 'Araç ekle');
      await vehicleFields(t, '34abc123');
      await live.tap(t, 'Kaydet');
      await live.visible(t, find.text('34 ABC 123'));
      await tapText(t, '34 ABC 123');
      await edit(t, 'color', 'Mavi');
      await live.tap(t, 'Kaydet');
      await live.visible(t, find.text('Müşteri ayrıntıları'));
      final service = ApiCustomerVehicleService(
        apiClient: auth.apiClient,
        session: auth,
      );
      final contexts = await ApiContextService(
        apiClient: auth.apiClient,
        session: auth,
      ).getContexts();
      live.success(contexts);
      final business = (contexts as AuthSuccess<UserContexts>)
          .value
          .businessContexts
          .single
          .businessId;
      final people = await service.customers(business, query: '34abc123');
      live.success(people);
      final customer = (people as AuthSuccess<RecordPage<LocalCustomerRecord>>)
          .value
          .items
          .single;
      final vehicles = await service.vehicles(
        business: business,
        customer: customer.id,
      );
      live.success(vehicles);
      final v = (vehicles as AuthSuccess<RecordPage<VehicleRecord>>)
          .value
          .items
          .single;
      expect(v.color, 'Mavi');
      expect(v.businessId, business);
      expect(v.localCustomerId, customer.id);
      await tapText(t, '34 ABC 123');
      await live.tap(t, 'Aracı arşivle');
      await live.tap(t, 'Onayla');
      await live.visible(t, find.text('Kayıt bulunamadı.'));
      await live.tap(t, 'Çıkış yap');
      await live.visible(t, find.byKey(const ValueKey('email')));
      await t.pumpWidget(const SizedBox.shrink());
      await t.pumpAndSettle();
    },
  );
  testWidgets(
    'live platform Customer adds edits and archives only personal vehicle',
    (t) async {
      final auth = live.auth();
      final email = live.email();
      live.success(
        await auth.registerCustomer(
          CustomerRegistrationRequest(
            fullName: 'Phase 7 customer',
            email: email,
            password: live.password,
          ),
        ),
      );
      live.success(
        await auth.verifyEmail(await live.mail(email, 'verification')),
      );
      live.success(
        await auth.login(LoginRequest(email: email, password: live.password)),
      );
      await mount(t, auth);
      await live.tap(t, 'Araçlarım');
      await live.visible(t, find.text('Kayıt bulunamadı.'));
      await live.tap(t, 'Araç ekle');
      await vehicleFields(t, 'ab-123-cd');
      await live.tap(t, 'Kaydet');
      await live.visible(t, find.text('ab-123-cd'));
      await tapText(t, 'ab-123-cd');
      await edit(t, 'notes', 'Synthetic vehicle note');
      await live.tap(t, 'Kaydet');
      await live.visible(t, find.text('Araçlarım'));
      final service = ApiCustomerVehicleService(
        apiClient: auth.apiClient,
        session: auth,
      );
      final r = await service.vehicles();
      live.success(r);
      final v =
          (r as AuthSuccess<RecordPage<VehicleRecord>>).value.items.single;
      expect(v.businessId, isNull);
      expect(v.localCustomerId, isNull);
      expect(v.notes, 'Synthetic vehicle note');
      await tapText(t, 'ab-123-cd');
      await live.tap(t, 'Aracı arşivle');
      await live.tap(t, 'Onayla');
      await live.visible(t, find.text('Kayıt bulunamadı.'));
      await live.tap(t, 'Çıkış yap');
      await live.visible(t, find.byKey(const ValueKey('email')));
      await t.pumpWidget(const SizedBox.shrink());
      await t.pumpAndSettle();
    },
  );
}
