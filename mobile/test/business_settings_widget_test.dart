import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:washflow/app/app.dart';
import 'package:washflow/app/routing/app_router.dart';
import 'package:washflow/features/auth/domain/auth_service.dart';
import 'package:washflow/features/business_settings/domain/business_settings.dart';

import 'support/settings_fakes.dart';
import 'support/session_fakes.dart';

// TextFormField does not expose decoration; use descendant TextField instead.
Finder input(String label) => find.byWidgetPredicate(
  (w) => w is TextField && w.decoration?.labelText == label,
);
Future<void> edit(WidgetTester t, Finder f, String value) async {
  await t.ensureVisible(f);
  await t.enterText(f, value);
  await t.pump();
}

Future<void> tap(WidgetTester t, String text) async {
  await t.pump();
  await t.ensureVisible(find.text(text).last);
  await t.tap(find.text(text).last);
  await t.pumpAndSettle();
}

Future<void> open(
  WidgetTester t,
  FakeSettings settings, {
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
      settingsService: settings,
      contextService:
          contexts ?? FakeContexts(grants(businesses: [businessContext()])),
      initialRoute: AppRouter.session,
    ),
  );
  await t.pumpAndSettle();
  if (enter) await tap(t, 'İşletme ayarları');
}

void main() {
  testWidgets(
    'Owner route revalidates selected business and saves actual settings',
    (t) async {
      final service = FakeSettings();
      final contexts = FakeContexts(grants(businesses: [businessContext()]));
      await open(t, service, contexts: contexts);
      expect(contexts.calls, 2);
      expect(service.calls, ['read:$businessA']);
      await edit(t, input('İşletme adı'), 'Changed');
      expect(find.text('Kaydedilmemiş değişiklikler var.'), findsOneWidget);
      await tap(t, 'Değişiklikleri kaydet');
      expect(service.value.fields['name'], 'Changed');
      expect(
        find.text('İşletme ayarları kaydedildi ve yeniden yüklendi.'),
        findsOneWidget,
      );
      expect(service.calls, [
        'read:$businessA',
        'save:$businessA',
        'read:$businessA',
      ]);
    },
  );
  testWidgets('weekly editor shows seven days, closed flags and time input', (
    t,
  ) async {
    final s = FakeSettings();
    await open(t, s);
    await edit(t, input('IANA saat dilimi'), 'Europe/London');
    await tap(t, 'Haftalık saatleri yapılandır');
    for (final day in [
      'Pazartesi',
      'Salı',
      'Çarşamba',
      'Perşembe',
      'Cuma',
      'Cumartesi',
      'Pazar',
    ]) {
      expect(find.text(day), findsOneWidget);
    }
    await tap(t, 'Pazartesi');
    await tap(t, 'Açılış: Seçin');
    final dialog = find.byType(TimePickerDialog);
    expect(dialog, findsOneWidget);
    final inputs = find.descendant(
      of: dialog,
      matching: find.byType(TextField),
    );
    await t.enterText(inputs.at(0), '09');
    await t.enterText(inputs.at(1), '30');
    await tap(t, 'Tamam');
    await tap(t, 'Gün sonu 24:00');
    await tap(t, 'Değişiklikleri kaydet');
    expect(s.value.weeklyHours!.first.opensAtMinute, 570);
    expect(s.value.weeklyHours!.first.closesAtMinute, 1440);
    expect(s.value.weeklyHours!.skip(1).every((d) => d.closed), isTrue);
  });
  testWidgets(
    'invalid fields do not dispatch and reload requires discard confirmation',
    (t) async {
      final s = FakeSettings();
      await open(t, s);
      await edit(t, input('İşletme adı'), '');
      await tap(t, 'Değişiklikleri kaydet');
      expect(s.calls.length, 1);
      await tap(t, 'Güncel kaydı yükle');
      expect(find.text('Kaydedilmemiş değişiklikler'), findsOneWidget);
      await tap(t, 'Düzenlemeye dön');
      expect(s.calls.length, 1);
      await tap(t, 'Güncel kaydı yükle');
      await tap(t, 'Değişiklikleri sil');
      expect(s.calls.length, 2);
    },
  );
  testWidgets('load error retry and uncertain save never display success', (
    t,
  ) async {
    final s = FakeSettings()
      ..onRead = (_) async =>
          AuthFailure(AuthError(code: AuthErrorCode.network));
    await open(t, s);
    expect(find.textContaining('Güncel kayıt yüklenemedi'), findsOneWidget);
    s.onRead = null;
    await tap(t, 'Güncel kaydı yükle');
    await edit(t, input('İşletme adı'), 'Changed');
    s.onSave = (_) async => AuthFailure(AuthError(code: AuthErrorCode.timeout));
    await tap(t, 'Değişiklikleri kaydet');
    expect(
      find.textContaining('Kaydetme sonucu doğrulanamadı'),
      findsOneWidget,
    );
    expect(s.calls.where((c) => c.startsWith('save')).length, 1);
    expect(
      t
          .widget<FilledButton>(
            find.widgetWithText(FilledButton, 'Değişiklikleri kaydet'),
          )
          .onPressed,
      isNull,
    );
  });
  testWidgets('duplicate saves are blocked and logout discards late response', (
    t,
  ) async {
    final s = FakeSettings();
    final session = FakeSession();
    await open(t, s, session: session);
    await edit(t, input('İşletme adı'), 'Changed');
    final p = Completer<AuthResult<BusinessSettings>>();
    s.onSave = (_) => p.future;
    await t.ensureVisible(find.text('Değişiklikleri kaydet'));
    await t.tap(find.text('Değişiklikleri kaydet'));
    await t.pump();
    expect(find.text('Kaydediliyor…'), findsOneWidget);
    await tap(t, 'Çıkış yap');
    p.complete(AuthSuccess(settings(version: 2, name: 'Late tenant name')));
    await t.pumpAndSettle();
    expect(find.text('Late tenant name'), findsNothing);
    expect(s.calls.where((c) => c.startsWith('save')).length, 1);
    expect(session.logouts, 1);
  });
  testWidgets('403 refresh removes revoked Owner access', (t) async {
    final s = FakeSettings();
    final ctx = FakeContexts(grants(businesses: [businessContext()]));
    await open(t, s, contexts: ctx);
    ctx.value = grants();
    await edit(t, input('İşletme adı'), 'Changed');
    s.onSave = (_) async =>
        AuthFailure(AuthError(code: AuthErrorCode.forbidden));
    await tap(t, 'Değişiklikleri kaydet');
    expect(find.text('İşletme ayarları'), findsNothing);
    expect(
      find.textContaining('Kullanılabilir hesap bağlamı bulunamadı'),
      findsOneWidget,
    );
    expect(ctx.calls, 3);
  });
  for (final owner in [false, true]) {
    testWidgets(
      'employee/customer and forged root settings route stay blocked $owner',
      (t) async {
        final s = FakeSettings();
        await open(
          t,
          s,
          contexts: FakeContexts(
            owner
                ? grants(customer: true)
                : grants(
                    businesses: [
                      businessContext(roles: ['Employee']),
                    ],
                  ),
          ),
          enter: false,
        );
        expect(find.text('İşletme ayarları'), findsNothing);
        final context = t.element(find.byType(Scaffold).first);
        Navigator.of(context, rootNavigator: true).pushNamed(
          '/business-settings',
          arguments: {'businessId': businessA, 'role': 'Owner'},
        );
        await t.pumpAndSettle();
        expect(find.text('Sayfa bulunamadı.'), findsOneWidget);
        expect(s.calls, isEmpty);
      },
    );
  }
  testWidgets('account changes destroy pending settings and all dialogs', (
    t,
  ) async {
    final s = FakeSettings();
    final session = FakeSession();
    await open(t, s, session: session);
    await edit(t, input('İşletme adı'), 'Changed');
    await tap(t, 'Güncel kaydı yükle');
    session.changeIdentity();
    await t.pumpAndSettle();
    expect(find.text('Değişiklikleri sil'), findsNothing);
    expect(find.text('İşletme ayarları'), findsNothing);
  });
  testWidgets('pending business A read cannot appear after selecting B', (
    t,
  ) async {
    final s = FakeSettings();
    final p = Completer<AuthResult<BusinessSettings>>();
    s.onRead = (id) => id == businessA
        ? p.future
        : Future.value(
            AuthSuccess(settings(id: businessB, name: 'Business B')),
          );
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
    await t.ensureVisible(find.text('İşletme ayarları'));
    await t.tap(find.text('İşletme ayarları'));
    await t.pump();
    await t.pump(const Duration(milliseconds: 400));
    await tap(t, 'Hesap değiştir');
    await tap(t, 'Business B · İşletme sahibi');
    await tap(t, 'İşletme ayarları');
    p.complete(AuthSuccess(settings(name: 'Late A')));
    await t.pumpAndSettle();
    expect(find.text('Late A'), findsNothing);
    expect(find.text('Business B'), findsNWidgets(2));
  });

  testWidgets('back navigation requires discard confirmation', (t) async {
    final s = FakeSettings();
    await open(t, s);
    await edit(t, input('İşletme adı'), 'Unsaved');
    await t.tap(find.byType(BackButton));
    await t.pumpAndSettle();
    expect(find.text('Kaydedilmemiş değişiklikler'), findsOneWidget);
    await tap(t, 'Değişiklikleri sil');
    await t.pumpAndSettle();
    expect(find.text('İşletme sahibi alanı'), findsOneWidget);
    expect(s.calls.where((c) => c.startsWith('save')), isEmpty);
  });
  testWidgets('pending write cannot overwrite business B after switching', (
    t,
  ) async {
    final s = FakeSettings();
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
    await tap(t, 'İşletme ayarları');
    await edit(t, input('İşletme adı'), 'A edit');
    final p = Completer<AuthResult<BusinessSettings>>();
    s.onSave = (_) => p.future;
    await t.ensureVisible(find.text('Değişiklikleri kaydet'));
    await t.tap(find.text('Değişiklikleri kaydet'));
    await t.pump();
    await tap(t, 'Hesap değiştir');
    await tap(t, 'Değişiklikleri sil');
    await tap(t, 'Business B · İşletme sahibi');
    s.onRead = (_) async =>
        AuthSuccess(settings(id: businessB, name: 'Business B'));
    await tap(t, 'İşletme ayarları');
    p.complete(AuthSuccess(settings(name: 'Late A')));
    await t.pumpAndSettle();
    expect(find.text('Late A'), findsNothing);
    expect(
      t.widget<TextField>(input('İşletme adı')).controller!.text,
      'Business B',
    );
  });
  testWidgets('lifecycle suspension removes settings and discards late read', (
    t,
  ) async {
    final s = FakeSettings();
    await open(t, s);
    final p = Completer<AuthResult<BusinessSettings>>();
    s.onRead = (_) => p.future;
    await t.ensureVisible(find.text('Güncel kaydı yükle'));
    await t.tap(find.text('Güncel kaydı yükle'));
    await t.pump();
    t.binding.handleAppLifecycleStateChanged(AppLifecycleState.inactive);
    await t.pump();
    t.binding.handleAppLifecycleStateChanged(AppLifecycleState.resumed);
    await t.pumpAndSettle();
    p.complete(AuthSuccess(settings(name: 'Suspended response')));
    await t.pumpAndSettle();
    expect(find.text('Suspended response'), findsNothing);
    expect(find.text('İşletme sahibi alanı'), findsOneWidget);
  });
  testWidgets(
    'settings remain usable with compact screen large text and keyboard',
    (t) async {
      t.view.physicalSize = const Size(360, 640);
      t.view.devicePixelRatio = 1;
      addTearDown(t.view.resetPhysicalSize);
      addTearDown(t.view.resetDevicePixelRatio);
      t.platformDispatcher.textScaleFactorTestValue = 1.7;
      addTearDown(t.platformDispatcher.clearTextScaleFactorTestValue);
      await open(t, FakeSettings());
      await edit(t, input('İşletme adı'), 'Accessible name');
      t.view.viewInsets = const FakeViewPadding(bottom: 250);
      addTearDown(t.view.resetViewInsets);
      await t.pump();
      await tap(t, 'Değişiklikleri kaydet');
      expect(t.takeException(), isNull);
    },
  );
}
