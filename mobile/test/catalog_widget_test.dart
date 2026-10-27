import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:washflow/app/app.dart';
import 'package:washflow/app/routing/app_router.dart';
import 'package:washflow/features/auth/domain/auth_service.dart';
import 'package:washflow/features/customer_vehicles/domain/customer_vehicles.dart';
import 'package:washflow/features/service_catalog/domain/service_catalog.dart';
import 'package:washflow/features/service_catalog/presentation/catalog_screen.dart';

import 'support/catalog_fakes.dart';
import 'support/session_fakes.dart';

void main() {
  Future<void> show(
    WidgetTester t,
    FakeCatalog service, {
    bool Function()? current,
  }) async {
    await t.pumpWidget(
      MaterialApp(
        home: CatalogScreen(
          service: service,
          business: businessA,
          businessName: 'İşletme A',
          isCurrent: current ?? () => true,
          onAuthorizationFailure: (_) async {},
          onSwitch: () async {},
          onLogout: () async {},
        ),
      ),
    );
    await t.pumpAndSettle();
  }

  Future<void> tap(WidgetTester t, String text) async {
    FocusManager.instance.primaryFocus?.unfocus();
    await t.pumpAndSettle();
    final f = find.text(text).last;
    await t.ensureVisible(f);
    await t.pumpAndSettle();
    await t.tap(f);
    await t.pumpAndSettle();
  }

  testWidgets('unsupported text shows Turkish field feedback without saving', (
    t,
  ) async {
    final s = FakeCatalog();
    await show(t, s);
    await tap(t, 'Hizmet ekle');
    await t.enterText(
      find.widgetWithText(TextFormField, 'Hizmet adı'),
      'Yıkama 🚗',
    );
    await t.enterText(
      find.widgetWithText(TextFormField, 'Açıklama'),
      'Not\u0085',
    );
    await tap(t, 'Kaydet');
    expect(
      find.text('Hizmet adında emoji veya kontrol karakteri kullanmayın.'),
      findsOneWidget,
    );
    expect(
      find.text(
        'Açıklamada emoji veya desteklenmeyen kontrol karakteri kullanmayın.',
      ),
      findsOneWidget,
    );
    expect(s.saves, 0);
  });

  testWidgets(
    'normalization-expanded lengths show field errors before submission',
    (t) async {
      final s = FakeCatalog();
      await show(t, s);
      await tap(t, 'Hizmet ekle');
      await t.enterText(
        find.widgetWithText(TextFormField, 'Hizmet adı'),
        '\u0344' * 101,
      );
      await t.enterText(
        find.widgetWithText(TextFormField, 'Açıklama'),
        '\u0344' * 1001,
      );
      await tap(t, 'Kaydet');
      expect(
        find.text(
          'Hizmet adı normalleştirme sonrasında en fazla 200 karakter olabilir.',
        ),
        findsOneWidget,
      );
      expect(
        find.text(
          'Açıklama normalleştirme sonrasında en fazla 2000 karakter olabilir.',
        ),
        findsOneWidget,
      );
      expect(s.saves, 0);
    },
  );

  testWidgets('list details edit confirmation and archive workflow', (t) async {
    final s = FakeCatalog();
    await show(t, s);
    expect(find.text('İşletme A'), findsOneWidget);
    expect(find.textContaining('450,10 TL'), findsOneWidget);
    await tap(t, 'Dış yıkama');
    expect(find.textContaining('120 dk bekleme'), findsOneWidget);
    await tap(t, 'Düzenle');
    await t.enterText(
      find.widgetWithText(TextFormField, 'Hizmet adı'),
      'Yeni hizmet',
    );
    await tap(t, 'Kaydet');
    expect(s.saves, 1);
    expect(s.reads, 2);
    await tap(t, 'Arşivle');
    expect(s.archives, 0);
    await tap(t, 'Vazgeç');
    expect(s.archives, 0);
    await tap(t, 'Arşivle');
    await tap(t, 'Onayla');
    expect(s.archives, 1);
    expect(s.lists, 2);
  });
  testWidgets('new editor category controls validate price and estimates', (
    t,
  ) async {
    final s = FakeCatalog();
    await show(t, s);
    await tap(t, 'Hizmet ekle');
    await tap(t, 'Kaydet');
    expect(find.text('Hizmet adı girin.'), findsOneWidget);
    await t.enterText(find.widgetWithText(TextFormField, 'Hizmet adı'), 'Yeni');
    final toggle = find.widgetWithText(SwitchListTile, 'Otomobil');
    await t.ensureVisible(toggle);
    await t.pumpAndSettle();
    await t.tap(toggle);
    await t.pumpAndSettle();
    await tap(t, 'Kaydet');
    expect(s.saves, 0);
    await t.enterText(
      find.widgetWithText(TextFormField, 'Otomobil fiyatı (TL)'),
      '450,10',
    );
    await t.enterText(
      find.widgetWithText(TextFormField, 'Otomobil etkin süre (dk)'),
      '20',
    );
    await t.enterText(
      find.widgetWithText(TextFormField, 'Otomobil çalışan sayısı'),
      '1',
    );
    await tap(t, 'Kaydet');
    expect(s.saves, 1);
    expect(s.savedDraft!.offerings.single.price.wire, '450.10');
  });
  testWidgets(
    'discard requires confirmation and duplicate creates unsaved disabled draft',
    (t) async {
      final s = FakeCatalog();
      await show(t, s);
      await tap(t, 'Dış yıkama');
      await tap(t, 'Kopyadan yeni taslak');
      expect(s.saves, 0);
      expect(find.text('Yeni hizmet taslağı'), findsOneWidget);
      await t.tap(find.byTooltip('Yenile'));
      await t.pumpAndSettle();
      expect(find.text('Taslak bırakılsın mı?'), findsOneWidget);
      await tap(t, 'Vazgeç');
      expect(find.text('Yeni hizmet taslağı'), findsOneWidget);
      await t.tap(find.byTooltip('Yenile'));
      await t.pumpAndSettle();
      await tap(t, 'Onayla');
      expect(s.lists, 2);
    },
  );
  testWidgets('loading empty error retry search and filter', (t) async {
    final s = FakeCatalog();
    s.listHandler = () async =>
        AuthSuccess(RecordPage<CatalogServiceRecord>([], 1, false));
    await show(t, s);
    expect(find.text('Gösterilecek hizmet bulunamadı.'), findsOneWidget);
    await t.enterText(
      find.widgetWithText(TextFormField, 'Hizmet ara'),
      'Köpük',
    );
    await t.pump(const Duration(milliseconds: 100));
    expect(find.byKey(const ValueKey('catalog-search')), findsOneWidget);
    await t.enterText(
      find.byKey(const ValueKey('catalog-search')),
      'Köpük yıkama',
    );
    await t.pump(const Duration(milliseconds: 400));
    await t.pumpAndSettle();
    expect(s.lastQuery, 'Köpük yıkama');
    await t.tap(find.byType(DropdownButtonFormField<String>));
    await t.pumpAndSettle();
    await t.tap(find.text('Devre dışı').last);
    await t.pumpAndSettle();
    expect(s.lastStatus, 'disabled');
    s.listHandler = () async =>
        AuthFailure(AuthError(code: AuthErrorCode.network));
    await tap(t, 'Yeniden dene');
    expect(find.textContaining('Sunucuya ulaşılamadı'), findsWidgets);
  });
  testWidgets('uncertain save disabled until deliberate draft discard', (
    t,
  ) async {
    final s = FakeCatalog();
    await show(t, s);
    await tap(t, 'Dış yıkama');
    await tap(t, 'Düzenle');
    s.saveHandler = () async =>
        AuthFailure(AuthError(code: AuthErrorCode.timeout));
    await tap(t, 'Kaydet');
    expect(s.saves, 1);
    expect(find.textContaining('Otomatik tekrar yapılmadı'), findsOneWidget);
    final button = t.widget<FilledButton>(
      find.widgetWithText(FilledButton, 'Kaydet'),
    );
    expect(button.onPressed, isNull);
    await tap(t, 'Güncel listeyi yükle');
    await tap(t, 'Onayla');
    expect(s.saves, 1);
  });
  testWidgets('disposed pending tenant read cannot show stale data', (t) async {
    final s = FakeCatalog();
    final pending = Completer<AuthResult<RecordPage<CatalogServiceRecord>>>();
    s.listHandler = () => pending.future;
    await t.pumpWidget(
      MaterialApp(
        home: CatalogScreen(
          service: s,
          business: businessA,
          businessName: 'İşletme A',
          isCurrent: () => true,
          onAuthorizationFailure: (_) async {},
          onSwitch: () async {},
          onLogout: () async {},
        ),
      ),
    );
    await t.pump();
    await t.pumpWidget(const MaterialApp(home: Text('Yeni hesap')));
    pending.complete(AuthSuccess(RecordPage([catalogRecord()], 1, false)));
    await t.pumpAndSettle();
    expect(find.text('Dış yıkama'), findsNothing);
    expect(t.takeException(), isNull);
  });
  testWidgets(
    'only server-authorized Owner navigation opens catalog and logout clears it',
    (t) async {
      final s = FakeCatalog();
      final session = FakeSession();
      final contexts = FakeContexts(grants(businesses: [businessContext()]));
      await t.pumpWidget(
        WashFlowApp(
          initialRoute: AppRouter.session,
          authService: session,
          contextService: contexts,
          catalogService: s,
        ),
      );
      await t.pumpAndSettle();
      await tap(t, 'Hizmetler ve Fiyatlar');
      expect(s.lists, 1);
      expect(contexts.calls, greaterThanOrEqualTo(2));
      await tap(t, 'Çıkış yap');
      expect(find.text('Dış yıkama'), findsNothing);
      expect(session.logouts, 1);
    },
  );
  testWidgets(
    'Employee context and forged root route have no catalog destination',
    (t) async {
      final s = FakeCatalog();
      await t.pumpWidget(
        WashFlowApp(
          initialRoute: AppRouter.session,
          authService: FakeSession(),
          contextService: FakeContexts(
            grants(
              businesses: [
                businessContext(roles: ['Employee']),
              ],
            ),
          ),
          catalogService: s,
        ),
      );
      await t.pumpAndSettle();
      expect(find.text('Hizmetler ve Fiyatlar'), findsNothing);
      await t.pumpWidget(
        WashFlowApp(
          initialRoute: '/service-catalog',
          catalogService: FakeCatalog(),
        ),
      );
      await t.pumpAndSettle();
      expect(find.text('Sayfa bulunamadı.'), findsOneWidget);
      expect(s.lists, 0);
    },
  );

  testWidgets(
    'revoked catalog grant refreshes contexts and removes private screen',
    (t) async {
      final service = FakeCatalog();
      final contexts = FakeContexts(grants(businesses: [businessContext()]));
      await t.pumpWidget(
        WashFlowApp(
          initialRoute: AppRouter.session,
          authService: FakeSession(),
          contextService: contexts,
          catalogService: service,
        ),
      );
      await t.pumpAndSettle();
      await tap(t, 'Hizmetler ve Fiyatlar');
      contexts.value = grants();
      service.listHandler = () async =>
          AuthFailure(AuthError(code: AuthErrorCode.forbidden));
      await t.tap(find.byTooltip('Yenile'));
      await t.pumpAndSettle();
      expect(
        find.textContaining('Kullanılabilir hesap bağlamı bulunamadı'),
        findsOneWidget,
      );
      expect(find.text('Dış yıkama'), findsNothing);
    },
  );
  testWidgets(
    'suspension destroys draft dialog and business switch uses fresh scoped list',
    (t) async {
      final service = FakeCatalog();
      final contexts = FakeContexts(grants(businesses: [businessContext()]));
      await t.pumpWidget(
        WashFlowApp(
          initialRoute: AppRouter.session,
          authService: FakeSession(),
          contextService: contexts,
          catalogService: service,
        ),
      );
      await t.pumpAndSettle();
      await tap(t, 'Hizmetler ve Fiyatlar');
      await tap(t, 'Hizmet ekle');
      await t.enterText(
        find.widgetWithText(TextFormField, 'Hizmet adı'),
        'Private draft',
      );
      await t.tap(find.byTooltip('Yenile'));
      await t.pumpAndSettle();
      expect(find.text('Onayla'), findsOneWidget);
      contexts.value = grants(
        businesses: [businessContext(id: businessB, name: 'İşletme B')],
      );
      t.binding.handleAppLifecycleStateChanged(AppLifecycleState.inactive);
      await t.pump();
      t.binding.handleAppLifecycleStateChanged(AppLifecycleState.resumed);
      await t.pumpAndSettle();
      expect(find.text('Private draft'), findsNothing);
      expect(find.text('Onayla'), findsNothing);
      await tap(t, 'Hizmetler ve Fiyatlar');
      expect(service.lastBusiness, businessB);
      expect(find.text('İşletme A'), findsNothing);
    },
  );
  testWidgets('compact large text editor remains scrollable with keyboard', (
    t,
  ) async {
    t.view.physicalSize = const Size(360, 640);
    t.view.devicePixelRatio = 1;
    addTearDown(t.view.resetPhysicalSize);
    addTearDown(t.view.resetDevicePixelRatio);
    t.platformDispatcher.textScaleFactorTestValue = 1.7;
    addTearDown(t.platformDispatcher.clearTextScaleFactorTestValue);
    final s = FakeCatalog();
    await show(t, s);
    await tap(t, 'Hizmet ekle');
    await t.enterText(
      find.widgetWithText(TextFormField, 'Hizmet adı'),
      'Compact',
    );
    t.view.viewInsets = const FakeViewPadding(bottom: 250);
    addTearDown(t.view.resetViewInsets);
    await t.pumpAndSettle();
    await tap(t, 'Kaydet');
    expect(s.saves, 1);
    expect(t.takeException(), isNull);
  });
}
