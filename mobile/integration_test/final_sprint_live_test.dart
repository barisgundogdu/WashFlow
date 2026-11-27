import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:integration_test/integration_test.dart';
import 'package:washflow/app/app.dart';
import 'package:washflow/app/routing/app_router.dart';
import 'package:washflow/features/auth/data/api_auth_service.dart';
import 'package:washflow/features/auth/data/api_context_service.dart';
import 'package:washflow/features/auth/data/token_store.dart';
import 'package:washflow/features/auth/domain/auth_service.dart';
import 'package:washflow/features/business_settings/data/api_business_settings_service.dart';
import 'package:washflow/features/business_settings/domain/business_settings.dart';
import 'package:washflow/features/customer_booking/customer_booking.dart';
import 'package:washflow/features/customer_vehicles/data/api_customer_vehicle_service.dart';
import 'package:washflow/features/customer_vehicles/domain/customer_vehicles.dart';
import 'package:washflow/features/finance/finance.dart';
import 'package:washflow/features/invitations/data/api_owner_invitation_service.dart';
import 'package:washflow/features/invitations/data/api_invitation_redemption_service.dart';
import 'package:washflow/features/scheduling/data/api_scheduling_service.dart';
import 'package:washflow/features/scheduling/domain/scheduling.dart';
import 'package:washflow/features/service_catalog/data/api_catalog_service.dart';
import 'package:washflow/features/service_catalog/domain/service_catalog.dart';
import 'package:washflow/features/work_orders/work_orders.dart';
import 'package:washflow/features/work_orders/work_order_screen.dart';

// Opt-in real socket test. A NEW database on the disposable container must back this URL.
// No normal app token-store access, database reset, response-body or credential diagnostics.
const base = String.fromEnvironment('FINAL_SPRINT_API_URL');

class MemorySession implements TokenStore {
  String? value;
  @override
  Future<String?> read() async => value;
  @override
  Future<void> write(String text) async {
    value = text;
  }

  @override
  Future<void> clear() async {
    value = null;
  }
}

T ok<T>(AuthResult<T> result) {
  expect(
    result is AuthSuccess<T>,
    isTrue,
    reason: result is AuthFailure<T>
        ? 'API semantic error: ${result.error.code.name}'
        : null,
  );
  return (result as AuthSuccess<T>).value;
}

ApiAuthService actor() =>
    ApiAuthService(baseUrl: Uri.parse(base), store: MemorySession());
Future<String> mail(String address, String purpose) async {
  for (var i = 0; i < 100; i++) {
    final r = await http.post(
      Uri.parse('$base/api/dev/email/$purpose/take'),
      headers: {'Content-Type': 'application/json'},
      body: jsonEncode({'email': address}),
    );
    if (r.statusCode == 200) {
      return (jsonDecode(r.body) as Map)['token'] as String;
    }
    expect(r.statusCode, 404);
    await Future<void>.delayed(const Duration(milliseconds: 100));
  }
  throw StateError('Isolated memory mailbox not ready');
}

Future<void> click(WidgetTester t, String label) async {
  final f = find.text(label);
  if (f.evaluate().isEmpty) {
    await t.scrollUntilVisible(
      f,
      250,
      scrollable: find.byType(Scrollable).first,
      maxScrolls: 60,
    );
  }
  await t.ensureVisible(f.last);
  await t.pumpAndSettle();
  await t.tap(f.last);
  await t.pumpAndSettle();
}

Future<void> text(WidgetTester t, String label, String value) async {
  final f = find.widgetWithText(TextField, label);
  if (f.evaluate().isEmpty) {
    await t.scrollUntilVisible(
      f,
      250,
      scrollable: find.byType(Scrollable).first,
      maxScrolls: 60,
    );
  }
  await t.ensureVisible(f);
  await t.pumpAndSettle();
  await t.enterText(f, value);
  await t.pumpAndSettle();
}

Future<void> capture(WidgetTester t, String name) async {
  await t.pumpAndSettle();
  debugPrint('LIVE_CAPTURE: $name');
  await t.runAsync(() => Future<void>.delayed(const Duration(seconds: 3)));
}

Future<void> settleApi(WidgetTester t) async {
  await t.pumpAndSettle();
  await t.pump(const Duration(milliseconds: 300));
  await t.pumpAndSettle();
}

Widget app(ApiAuthService auth) => WashFlowApp(
  authService: auth,
  contextService: ApiContextService(apiClient: auth.apiClient, session: auth),
  initialRoute: AppRouter.session,
  customerBookingService: ApiCustomerBookingService(
    apiClient: auth.apiClient,
    session: auth,
  ),
  customerVehicleService: ApiCustomerVehicleService(
    apiClient: auth.apiClient,
    session: auth,
  ),
  schedulingService: ApiSchedulingService(
    apiClient: auth.apiClient,
    session: auth,
  ),
  settingsService: ApiBusinessSettingsService(
    apiClient: auth.apiClient,
    session: auth,
  ),
  catalogService: ApiCatalogService(apiClient: auth.apiClient, session: auth),
  workOrderService: ApiWorkOrderService(
    apiClient: auth.apiClient,
    session: auth,
  ),
  financeService: ApiFinanceService(apiClient: auth.apiClient, session: auth),
);
void main() {
  IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'live customer booking employee ordered execution overdue and owner finance remain integrated',
    (t) async {
      expect(
        base,
        'http://127.0.0.1:5199',
        reason: 'Only the explicitly isolated final-sprint loopback backend is permitted.',
      );
      final owner = actor(), customer = actor(), employee = actor();
      final suffix = DateTime.now().microsecondsSinceEpoch.toString();
      final password = 'Ephemeral-${workRequestId()}-A1!';
      final ownerEmail = 'owner-$suffix@example.test',
          customerEmail = 'customer-$suffix@example.test',
          employeeEmail = 'employee-$suffix@example.test',
          businessName = 'Sprint Prova $suffix';
      ok(
        await owner.registerBusiness(
          BusinessRegistrationRequest(
            businessName: businessName,
            ownerFullName: 'Prova sahibi',
            email: ownerEmail,
            password: password,
          ),
        ),
      );
      ok(await owner.verifyEmail(await mail(ownerEmail, 'verification')));
      ok(
        await owner.login(LoginRequest(email: ownerEmail, password: password)),
      );
      for (final pair in [
        (auth: customer, email: customerEmail, name: 'Prova müşterisi'),
        (auth: employee, email: employeeEmail, name: 'Prova çalışanı'),
      ]) {
        ok(
          await pair.auth.registerCustomer(
            CustomerRegistrationRequest(
              fullName: pair.name,
              email: pair.email,
              password: password,
            ),
          ),
        );
        ok(await pair.auth.verifyEmail(await mail(pair.email, 'verification')));
        ok(
          await pair.auth.login(
            LoginRequest(email: pair.email, password: password),
          ),
        );
      }
      final ownerContexts = ok(
        await ApiContextService(
          apiClient: owner.apiClient,
          session: owner,
        ).getContexts(),
      );
      final business = ownerContexts.businessContexts.single;
      ok(
        await ApiOwnerInvitationService(
          apiClient: owner.apiClient,
          session: owner,
        ).create(business.businessId, employeeEmail),
      );
      ok(
        await ApiInvitationRedemptionService(
          apiClient: employee.apiClient,
          session: employee,
        ).accept(await mail(employeeEmail, 'invitation')),
      );
      final scheduling = ApiSchedulingService(
        apiClient: owner.apiClient,
        session: owner,
      );
      final settingsApi = ApiBusinessSettingsService(
        apiClient: owner.apiClient,
        session: owner,
      );
      final catalog = ApiCatalogService(
        apiClient: owner.apiClient,
        session: owner,
      );
      final workApi = ApiWorkOrderService(
        apiClient: owner.apiClient,
        session: owner,
      );
      var settings = ok(await settingsApi.read(business.businessId));
      settings = ok(
        await settingsApi.save(
          settings.change(
            fields: {
              ...settings.fields,
              'timeZoneId': 'Europe/Istanbul',
              'city': 'İstanbul',
            },
            hours: [
              for (var day = 1; day <= 7; day++)
                OpeningDay(day, false, 0, 1440),
            ],
          ),
        ),
      );
      final area = ok(
        await scheduling.saveArea(
          business.businessId,
          const WorkAreaInput('Prova dış alan', 2),
        ),
      );
      final secondArea = ok(
        await scheduling.saveArea(
          business.businessId,
          const WorkAreaInput('Prova iç alan', 1),
        ),
      );
      final crew = ok(await scheduling.employees(business.businessId)).single;
      final now = DateTime.now().toUtc();
      ok(
        await scheduling.saveAvailability(
          business.businessId,
          crew.membershipId,
          AvailabilityInput(
            DateTime.utc(
              now.year,
              now.month,
              now.day,
            ).subtract(const Duration(days: 1)),
            DateTime.utc(
              now.year,
              now.month,
              now.day,
            ).add(const Duration(days: 2)),
          ),
        ),
      );
      final service = ok(
        await catalog.save(
          business.businessId,
          CatalogDraft(
            name: 'Prova iç dış yıkama',
            enabled: true,
            published: true,
            offerings: [
              const Offering(
                category: 'Car',
                price: Lira(45000),
                activeMinutes: 2,
                employees: 1,
              ),
            ],
          ),
        ),
      );
      final requirement = ok(
        await scheduling.saveRequirement(
          business.businessId,
          service.id,
          'Car',
          RequirementInput(area.id, true),
        ),
      );
      ok(
        await workApi.saveStagePlan(
          business.businessId,
          service.id,
          'Car',
          requirement.version,
          [
            ServiceStagePlan('Dış yıkama', 1, area.id, 1),
            ServiceStagePlan('İç temizlik', 1, secondArea.id, 1),
          ],
        ),
      );
      final vehicleApi = ApiCustomerVehicleService(
        apiClient: customer.apiClient,
        session: customer,
      );
      final vehicle = ok(
        await vehicleApi.saveVehicle(
          VehicleDraft({
            'plate': '34 LIVE 25',
            'brand': 'Prova',
            'model': 'Araç',
            'category': 'Car',
          }),
        ),
      );
      debugPrint(
        'LIVE: actual accounts, published service, ordered resources and owned vehicle configured.',
      );
      await t.pumpWidget(app(customer));
      await settleApi(t);
      await click(t, 'Randevularım ve işletmeler');
      await click(t, businessName);
      await click(t, '34 LIVE 25 • Otomobil');
      await click(t, 'Prova iç dış yıkama • 450,00 TL');
      // Align the configured opening minute so the UI's 15-minute grid offers a near-future slot.
      final local = BusinessClock('Europe/Istanbul')
          .local(DateTime.now().toUtc());
      final opening = local.hour * 60 + local.minute + 1;
      expect(opening, lessThan(1430));
      settings = ok(
        await settingsApi.save(
          settings.change(
            hours: [
              for (var day = 1; day <= 7; day++)
                OpeningDay(day, false, opening, 1440),
            ],
          ),
        ),
      );
      await click(t, 'Uygun saatleri göster');
      final customerApi = ApiCustomerBookingService(
        apiClient: customer.apiClient,
        session: customer,
      );
      final date = DateTime(local.year, local.month, local.day);
      final quote = ok(
        await customerApi.slots(
          business.businessId,
          service.id,
          vehicle.id,
          date,
          1,
        ),
      );
      expect(quote.items, isNotEmpty);
      await click(t, quote.items.first.label);
      expect(find.text('Randevuyu onayla'), findsOneWidget);
      await click(t, 'Onayla');
      expect(find.text('Randevunuz onaylandı.'), findsOneWidget);
      await capture(t, 'customer-confirmed');
      final booking = ok(await customerApi.mine('upcoming', 1)).single;
      expect(
        booking.start.difference(DateTime.now().toUtc()).inSeconds,
        lessThan(70),
      );
      final ownerAppointment = ok(
        await scheduling.detail(business.businessId, booking.id),
      );
      expect(ownerAppointment.source, 'Customer');
      expect(ownerAppointment.localCustomerId, isNull);
      expect(ownerAppointment.stages.length, 2);
      final next = ok(
        await customerApi.book(
          CustomerAttempt(business.businessId, {
            'clientRequestId': workRequestId(),
            'ownedVehicleId': vehicle.id,
            'serviceId': service.id,
            'expectedServiceVersion': quote.serviceVersion,
            'expectedVehicleVersion': quote.vehicleVersion,
            'startsAtUtc': booking.start
                .add(const Duration(minutes: 5))
                .toIso8601String(),
          }),
        ),
      );
      // A contradictory hours edit must not invalidate either confirmed booking.
      final rejected = await settingsApi.save(
        settings.change(
          hours: [
            for (var day = 1; day <= 7; day++)
              OpeningDay(day, true, null, null),
          ],
        ),
      );
      expect(rejected, isA<AuthFailure<BusinessSettings>>());
      expect(
        ok(await scheduling.detail(business.businessId, booking.id))
            .startsAtUtc,
        booking.start,
      );
      final order = ok(
        await workApi.create(business.businessId, ownerAppointment, [
          (name: 'Dış yıkama', minutes: 1),
          (name: 'İç temizlik', minutes: 1),
        ], workRequestId()),
      );
      await t.pumpWidget(app(owner));
      await settleApi(t);
      await click(t, 'İş emirleri');
      expect(find.textContaining('34 LIVE 25'), findsWidgets);
      final wait = booking.start.difference(DateTime.now().toUtc());
      if (wait > Duration.zero) {
        debugPrint('LIVE: waiting for the real planned start.');
        await t.runAsync(
          () => Future<void>.delayed(wait + const Duration(seconds: 1)),
        );
      }
      final employeeApi = ApiWorkOrderService(
        apiClient: employee.apiClient,
        session: employee,
      );
      Widget detail(bool ownerMode) => MaterialApp(
        debugShowCheckedModeBanner: false,
        home: WorkOrderDetailScreen(
          service: ownerMode ? workApi : employeeApi,
          business: business.businessId,
          id: order.id,
          owner: ownerMode,
          isCurrent: () => true,
          onAuthorizationFailure: (_) async {},
        ),
      );
      await t.pumpWidget(detail(false));
      await settleApi(t);
      await click(t, 'Başlat');
      await click(t, 'Onayla');
      expect(find.text('Aşamayı tamamla'), findsOneWidget);
      expect(find.textContaining('kaldı'), findsWidgets);
      await t.pumpWidget(const SizedBox.shrink());
      await t.pumpWidget(detail(false));
      await settleApi(t);
      expect(find.text('Aşamayı tamamla'), findsOneWidget);
      debugPrint(
        'LIVE: real employee countdown running; waiting for overdue without automatic completion.',
      );
      await t.runAsync(() => Future<void>.delayed(const Duration(seconds: 62)));
      await settleApi(t);
      expect(find.textContaining('gecikti'), findsWidgets);
      await capture(t, 'employee-overdue');
      expect(find.text('Aşamayı tamamla'), findsOneWidget);
      await t.pumpWidget(detail(true));
      await settleApi(t);
      expect(
        find.textContaining('otomatik kaydırma yapılmadı'),
        findsOneWidget,
      );
      await capture(t, 'owner-warning');
      expect(ok(await customerApi.detail(next.id)).start, next.start);
      await t.pumpWidget(detail(false));
      await settleApi(t);
      await click(t, 'Aşamayı tamamla');
      await click(t, 'Onayla');
      await click(t, 'Başlat');
      await click(t, 'Onayla');
      await click(t, 'Aşamayı tamamla');
      await click(t, 'Onayla');
      expect(find.text('Tamamlanma sunucuya kaydedildi.'), findsNWidgets(2));
      final completed = ok(
        await employeeApi.detail(business.businessId, order.id, false),
      );
      expect(completed.status, 'Completed');
      expect(
        completed.stages.every((stage) => stage.status == 'Completed'),
        isTrue,
      );
      final finance = ApiFinanceService(
        apiClient: owner.apiClient,
        session: owner,
      );
      await t.pumpWidget(app(owner));
      await settleApi(t);
      await click(t, 'Finans yönetimi');
      await click(t, 'Ödemeler');
      expect(find.textContaining('450,00 TL'), findsWidgets);
      await click(t, 'Ödeme kaydet');
      await text(t, 'Tutar (TRY)', '150,00');
      await click(t, 'Kaydı onayla');
      await click(t, 'Onayla');
      expect(find.text('Finans kaydı sunucuya kaydedildi.'), findsOneWidget);
      await click(t, 'Özet');
      await click(t, 'Gider ekle');
      await text(t, 'Tutar (TRY)', '100,00');
      await text(t, 'Kategori', 'Temizlik malzemesi');
      await text(t, 'Belge / referans (isteğe bağlı)', 'Prova referansı');
      await click(t, 'Kaydı onayla');
      await click(t, 'Onayla');
      await click(t, 'Özet');
      await capture(t, 'finance-report');
      final report = ok(await finance.report(business.businessId));
      expect(report.totals['earnedRevenue']!.wire, '450.00');
      expect(report.totals['collectedPayments']!.wire, '150.00');
      expect(report.totals['expenses']!.wire, '100.00');
      expect(report.totals['cashOperatingBalance']!.wire, '50.00');
      expect(report.totals['unpaidBalance']!.wire, '300.00');
      expect(ok(await customerApi.detail(next.id)).start, next.start);
      await t.pumpWidget(app(customer));
      await settleApi(t);
      await click(t, 'Randevularım ve işletmeler');
      await click(t, 'Randevularım');
      await click(t, 'Geçmiş randevular');
      expect(find.textContaining('Tamamlandı'), findsWidgets);
      expect(t.takeException(), isNull);
      debugPrint(
        'LIVE PASSED: customer → owner calendar → ordered employee countdown/overdue/completion → partial payment + expense → exact finance → own history. Existing demo untouched.',
      );
      await t.pumpWidget(const SizedBox.shrink());
      await owner.logout();
      await customer.logout();
      await employee.logout();
    },
    timeout: const Timeout(Duration(minutes: 12)),
  );
}
