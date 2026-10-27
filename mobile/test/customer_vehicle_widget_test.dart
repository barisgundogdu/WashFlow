import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:washflow/app/app.dart';
import 'package:washflow/app/routing/app_router.dart';
import 'package:washflow/features/auth/domain/auth_service.dart';
import 'package:washflow/features/customer_vehicles/domain/customer_vehicles.dart';

import 'support/customer_vehicle_fakes.dart';
import 'support/session_fakes.dart';

Future<void> tap(WidgetTester t, String text) async {
  await t.pump();
  await t.ensureVisible(find.text(text).last);
  await t.tap(find.text(text).last);
  await t.pumpAndSettle();
}

Future<void> edit(WidgetTester t, String key, String value) async {
  final f = find.byKey(ValueKey(key));
  await t.ensureVisible(f);
  await t.enterText(f, value);
  await t.pump();
}

Future<void> open(
  WidgetTester t,
  FakeCustomerVehicles service, {
  bool mine = false,
  FakeSession? session,
  FakeContexts? contexts,
  bool enter = true,
}) async {
  final s = session ?? FakeSession();
  addTearDown(s.dispose);
  addTearDown(() => t.pumpWidget(const SizedBox.shrink()));
  await t.pumpWidget(
    WashFlowApp(
      authService: s,
      customerVehicleService: service,
      contextService:
          contexts ??
          FakeContexts(
            mine
                ? grants(customer: true)
                : grants(businesses: [businessContext()]),
          ),
      initialRoute: AppRouter.session,
    ),
  );
  await t.pumpAndSettle();
  if (enter) await tap(t, mine ? 'Araçlarım' : 'Müşteriler ve araçlar');
}

Future<void> vehicleFields(WidgetTester t) async {
  await edit(t, 'plate', '34 ABC 123');
  await edit(t, 'brand', 'Test brand');
  await edit(t, 'model', 'Test model');
  await t.ensureVisible(find.byKey(const ValueKey('category')));
  await t.tap(find.byKey(const ValueKey('category')));
  await t.pumpAndSettle();
  await tap(t, 'Otomobil');
}

void main() {
  testWidgets(
    'Owner creates customer opens details and adds edits archives local vehicle',
    (t) async {
      final service = FakeCustomerVehicles();
      final ctx = FakeContexts(grants(businesses: [businessContext()]));
      await open(t, service, contexts: ctx);
      expect(ctx.calls, 2);
      expect(find.text('Kayıt bulunamadı.'), findsOneWidget);
      await tap(t, 'Müşteri ekle');
      await tap(t, 'Kaydet');
      expect(service.calls.where((c) => c.startsWith('save')), isEmpty);
      await edit(t, 'fullName', 'Ayşe Yılmaz');
      await edit(t, 'phone', '0555 123 45 67');
      await tap(t, 'Kaydet');
      await tap(t, 'Ayşe Yılmaz');
      expect(find.text('Müşteri ayrıntıları'), findsOneWidget);
      await tap(t, 'Araç ekle');
      await vehicleFields(t);
      await tap(t, 'Kaydet');
      expect(service.cars.single.localCustomerId, customerId);
      await tap(t, '34 ABC 123');
      await edit(t, 'color', 'Mavi');
      await tap(t, 'Kaydet');
      expect(service.cars.single.color, 'Mavi');
      await tap(t, '34 ABC 123');
      await tap(t, 'Aracı arşivle');
      await tap(t, 'Onayla');
      expect(service.cars, isEmpty);
    },
  );
  testWidgets('Customer Araçlarım add edit archive never selects business', (
    t,
  ) async {
    final service = FakeCustomerVehicles();
    await open(t, service, mine: true);
    await tap(t, 'Araç ekle');
    await vehicleFields(t);
    await tap(t, 'Kaydet');
    expect(service.cars.single.businessId, isNull);
    await tap(t, '34 ABC 123');
    await edit(t, 'notes', 'Test note');
    await tap(t, 'Kaydet');
    expect(service.cars.single.notes, 'Test note');
    await tap(t, '34 ABC 123');
    await tap(t, 'Aracı arşivle');
    await tap(t, 'Onayla');
    expect(service.cars, isEmpty);
    expect(
      service.calls
          .where((c) => c.startsWith('saveVehicle'))
          .every((c) => c == 'saveVehicle:null:null'),
      isTrue,
    );
  });
  testWidgets(
    'Owner global vehicle list is searchable and requires parent selection for creation',
    (t) async {
      final s = FakeCustomerVehicles()
        ..cars = [VehicleRecord.fromJson(vehicleJson())];
      await open(t, s);
      await tap(t, 'Araçlar');
      expect(find.text('34 ABC 123'), findsOneWidget);
      expect(find.text('Araç eklemek için müşteri seç'), findsOneWidget);
      await tap(t, 'Araç eklemek için müşteri seç');
      expect(find.text('Müşteri ekle'), findsOneWidget);
    },
  );
  testWidgets(
    'unsaved editor changes require confirmation and cancellation creates no record',
    (t) async {
      final s = FakeCustomerVehicles();
      await open(t, s);
      await tap(t, 'Müşteri ekle');
      await edit(t, 'fullName', 'Unsaved');
      await tap(t, 'İptal');
      expect(find.text('Kaydedilmemiş değişiklikler'), findsOneWidget);
      await tap(t, 'Vazgeç');
      expect(find.byKey(const ValueKey('fullName')), findsOneWidget);
      await tap(t, 'İptal');
      await tap(t, 'Onayla');
      expect(s.people, isEmpty);
    },
  );
  testWidgets(
    'uncertain mutation cannot claim success or retry and list reconciliation is GET only',
    (t) async {
      final s = FakeCustomerVehicles()
        ..onSaveCustomer = (b, d) async =>
            AuthFailure(AuthError(code: AuthErrorCode.timeout));
      await open(t, s);
      await tap(t, 'Müşteri ekle');
      await edit(t, 'fullName', 'Pending person');
      await tap(t, 'Kaydet');
      expect(find.textContaining('Sonuç doğrulanamadı'), findsOneWidget);
      expect(
        t
            .widget<FilledButton>(find.byKey(const ValueKey('record-save')))
            .onPressed,
        isNull,
      );
      await tap(t, 'Listeye dön ve kontrol et');
      await tap(t, 'Onayla');
      expect(s.calls.where((c) => c.startsWith('saveCustomer')).length, 1);
    },
  );
  testWidgets('search is debounced, pagination bounded, stale result ignored', (
    t,
  ) async {
    final s = FakeCustomerVehicles();
    s.onCustomers = (b, q, p) async => AuthSuccess(
      RecordPage(
        [
          LocalCustomerRecord.fromJson(
            customerJson(name: q.isEmpty ? 'Initial' : 'Found'),
          ),
        ],
        p,
        p == 1,
      ),
    );
    await open(t, s);
    await edit(t, 'record-search', 'a');
    await edit(t, 'record-search', 'ay');
    await t.pump(const Duration(milliseconds: 400));
    await t.pumpAndSettle();
    expect(s.calls.where((c) => c.startsWith('customers')).length, 2);
    expect(find.text('Found'), findsOneWidget);
    await tap(t, 'Sonraki sayfa');
    expect(s.calls.last, contains(':2'));
  });
  testWidgets('403 revalidates contexts and removes revoked tenant', (t) async {
    final s = FakeCustomerVehicles();
    final ctx = FakeContexts(grants(businesses: [businessContext()]));
    await open(t, s, contexts: ctx);
    ctx.value = grants();
    s.onCustomers = (b, q, p) async =>
        AuthFailure(AuthError(code: AuthErrorCode.forbidden));
    await tap(t, 'Listeyi yenile');
    expect(
      find.textContaining('Kullanılabilir hesap bağlamı bulunamadı'),
      findsOneWidget,
    );
    expect(find.text('Müşteriler ve araçlar'), findsNothing);
  });
  testWidgets('Employee and forged root routes never reach directory', (
    t,
  ) async {
    final s = FakeCustomerVehicles();
    await open(
      t,
      s,
      contexts: FakeContexts(
        grants(
          businesses: [
            businessContext(roles: ['Employee']),
          ],
        ),
      ),
      enter: false,
    );
    expect(find.text('Müşteriler ve araçlar'), findsNothing);
    expect(find.text('Araçlarım'), findsNothing);
    final context = t.element(find.byType(Scaffold).first);
    Navigator.of(context, rootNavigator: true).pushNamed(
      '/local-customers',
      arguments: {'businessId': businessA, 'role': 'Owner'},
    );
    await t.pumpAndSettle();
    expect(find.text('Sayfa bulunamadı.'), findsOneWidget);
    expect(s.calls, isEmpty);
  });
  testWidgets('pending write and personal fields vanish on logout', (t) async {
    final s = FakeCustomerVehicles();
    final session = FakeSession();
    await open(t, s, session: session);
    await tap(t, 'Müşteri ekle');
    await edit(t, 'fullName', 'Private person');
    final p = Completer<AuthResult<LocalCustomerRecord>>();
    s.onSaveCustomer = (b, d) => p.future;
    await t.ensureVisible(find.text('Kaydet'));
    await t.tap(find.text('Kaydet'));
    await t.pump();
    session.changeIdentity();
    await t.pumpAndSettle();
    p.complete(
      AuthSuccess(
        LocalCustomerRecord.fromJson(customerJson(name: 'Late person')),
      ),
    );
    await t.pumpAndSettle();
    expect(find.text('Private person'), findsNothing);
    expect(find.text('Late person'), findsNothing);
  });
  testWidgets('business switch invalidates pending list and editing dialogs', (
    t,
  ) async {
    final s = FakeCustomerVehicles();
    final ctx = FakeContexts(
      grants(
        businesses: [
          businessContext(),
          businessContext(id: businessB, name: 'Business B'),
        ],
      ),
    );
    await open(t, s, contexts: ctx, enter: false);
    await tap(t, 'İşletme A · İşletme sahibi');
    await tap(t, 'Müşteriler ve araçlar');
    await tap(t, 'Hesap değiştir');
    await tap(t, 'Business B · İşletme sahibi');
    final p = Completer<AuthResult<RecordPage<LocalCustomerRecord>>>();
    s.onCustomers = (b, q, page) => p.future;
    await t.ensureVisible(find.text('Müşteriler ve araçlar'));
    await t.tap(find.text('Müşteriler ve araçlar'));
    await t.pump();
    await t.pump(const Duration(milliseconds: 400));
    ctx.value = grants();
    final session =
        t.widget<WashFlowApp>(find.byType(WashFlowApp)).authService
            as FakeSession;
    session.changeIdentity();
    await t.pumpAndSettle();
    p.complete(
      AuthSuccess(
        RecordPage(
          [
            LocalCustomerRecord.fromJson(
              customerJson(business: businessB, name: 'Late B'),
            ),
          ],
          1,
          false,
        ),
      ),
    );
    await t.pumpAndSettle();
    expect(find.text('Late B'), findsNothing);
  });
  testWidgets('loading errors retry and empty lists are visible', (t) async {
    final s = FakeCustomerVehicles()
      ..onCustomers = (b, q, p) async =>
          AuthFailure(AuthError(code: AuthErrorCode.network));
    await open(t, s);
    expect(find.textContaining('Sunucuya ulaşılamadı'), findsOneWidget);
    s.onCustomers = null;
    await tap(t, 'Listeyi yenile');
    expect(find.text('Kayıt bulunamadı.'), findsOneWidget);
  });

  testWidgets(
    'Owner to Customer role switch drops pending personal search results',
    (t) async {
      final s = FakeCustomerVehicles();
      final ctx = FakeContexts(
        grants(customer: true, businesses: [businessContext()]),
      );
      await open(t, s, contexts: ctx, enter: false);
      await tap(t, 'İşletme A · İşletme sahibi');
      await tap(t, 'Müşteriler ve araçlar');
      final p = Completer<AuthResult<RecordPage<LocalCustomerRecord>>>();
      s.onCustomers = (b, q, page) => p.future;
      await edit(t, 'record-search', 'pending');
      await t.pump(const Duration(milliseconds: 400));
      await tap(t, 'Hesap değiştir');
      await tap(t, 'Müşteri');
      await tap(t, 'Araçlarım');
      p.complete(
        AuthSuccess(
          RecordPage(
            [
              LocalCustomerRecord.fromJson(
                customerJson(name: 'Private Owner result'),
              ),
            ],
            1,
            false,
          ),
        ),
      );
      await t.pumpAndSettle();
      expect(find.text('Private Owner result'), findsNothing);
      expect(find.text('Araçlarım'), findsOneWidget);
    },
  );
  testWidgets('lifecycle suspension clears form and scoped dialogs', (t) async {
    final s = FakeCustomerVehicles();
    await open(t, s);
    await tap(t, 'Müşteri ekle');
    await edit(t, 'fullName', 'Private draft');
    await tap(t, 'İptal');
    t.binding.handleAppLifecycleStateChanged(AppLifecycleState.inactive);
    await t.pump();
    t.binding.handleAppLifecycleStateChanged(AppLifecycleState.resumed);
    await t.pumpAndSettle();
    expect(find.text('Private draft'), findsNothing);
    expect(find.text('Onayla'), findsNothing);
    expect(find.text('İşletme sahibi alanı'), findsOneWidget);
  });
  testWidgets('compact large-text vehicle form handles keyboard and saves', (
    t,
  ) async {
    t.view.physicalSize = const Size(360, 640);
    t.view.devicePixelRatio = 1;
    addTearDown(t.view.resetPhysicalSize);
    addTearDown(t.view.resetDevicePixelRatio);
    t.platformDispatcher.textScaleFactorTestValue = 1.7;
    addTearDown(t.platformDispatcher.clearTextScaleFactorTestValue);
    final s = FakeCustomerVehicles();
    await open(t, s, mine: true);
    await tap(t, 'Araç ekle');
    await vehicleFields(t);
    t.view.viewInsets = const FakeViewPadding(bottom: 250);
    addTearDown(t.view.resetViewInsets);
    await t.pump();
    await tap(t, 'Kaydet');
    expect(s.cars.length, 1);
    expect(t.takeException(), isNull);
  });

  testWidgets('failed detail refresh clears previous associated vehicles', (
    t,
  ) async {
    final service = FakeCustomerVehicles()
      ..people = [LocalCustomerRecord.fromJson(customerJson())]
      ..cars = [VehicleRecord.fromJson(vehicleJson())];
    await open(t, service);
    await tap(t, 'Ayşe Yılmaz');
    expect(find.text('34 ABC 123'), findsOneWidget);
    service.onCustomer = (b, id) async =>
        AuthFailure(AuthError(code: AuthErrorCode.notFound));
    await tap(t, 'Listeyi yenile');
    expect(find.text('34 ABC 123'), findsNothing);
    expect(find.textContaining('artık erişilemiyor'), findsOneWidget);
  });
}
