import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';
import 'package:washflow/app/app.dart';
import 'package:washflow/app/routing/app_router.dart';
import 'package:washflow/features/auth/data/api_context_service.dart';
import 'package:washflow/features/auth/domain/auth_service.dart';
import 'package:washflow/features/auth/domain/user_contexts.dart';
import 'package:washflow/features/service_catalog/data/api_catalog_service.dart';
import 'package:washflow/features/service_catalog/domain/service_catalog.dart';

import 'invitation_live_test.dart' as live;

Future<void> field(WidgetTester t, String label, String value) async {
  final f = find.widgetWithText(TextFormField, label);
  await live.visible(t, f);
  await t.ensureVisible(f);
  await t.pumpAndSettle();
  await t.tap(f);
  await t.pumpAndSettle();
  await t.enterText(f, value);
  await t.pumpAndSettle();
}

Future<void> toggle(WidgetTester t, String label) async {
  final f = find.widgetWithText(SwitchListTile, label);
  await live.visible(t, f);
  await t.ensureVisible(f);
  await t.pumpAndSettle();
  await t.tap(f);
  await t.pumpAndSettle();
}

void main() {
  IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  setUp(() async {
    expect(
      live.base == 'http://127.0.0.1:5189',
      isTrue,
      reason: 'Use only the isolated disposable backend.',
    );
    await live.store.clear();
  });
  tearDown(() => live.store.clear());
  testWidgets(
    'live Owner creates publishes edits conflicts and archives exact category pricing',
    (t) async {
      final auth = await live.owner();
      final api = ApiCatalogService(apiClient: auth.apiClient, session: auth);
      final contexts = ApiContextService(
        apiClient: auth.apiClient,
        session: auth,
      );
      final grants = await contexts.getContexts();
      live.success(grants);
      final b = (grants as AuthSuccess<UserContexts>)
          .value
          .businessContexts
          .single
          .businessId;
      await t.pumpWidget(
        WashFlowApp(
          authService: auth,
          contextService: contexts,
          catalogService: api,
          initialRoute: AppRouter.session,
        ),
      );
      await t.pumpAndSettle();
      await live.tap(t, 'Hizmetler ve Fiyatlar');
      await live.visible(t, find.text('Gösterilecek hizmet bulunamadı.'));
      await live.tap(t, 'Hizmet ekle');
      await field(t, 'Hizmet adı', 'Phase 8 exterior');
      await field(t, 'Açıklama', 'Synthetic catalog validation');
      await toggle(t, 'Hizmet etkin');
      await toggle(t, 'Müşterilere yayınla');
      await toggle(t, 'Otomobil');
      await field(t, 'Otomobil fiyatı (TL)', '450,10');
      await field(t, 'Otomobil etkin süre (dk)', '20');
      await field(t, 'Otomobil bekleme / kuruma (dk, isteğe bağlı)', '120');
      await field(t, 'Otomobil çalışan sayısı', '1');
      await live.tap(t, 'Kaydet');
      await live.visible(
        t,
        find.text('Hizmet kaydedildi ve yeniden yüklendi.'),
      );
      expect(find.textContaining('450,10 TL'), findsWidgets);
      await t.tap(find.byType(BackButton));
      await live.visible(t, find.byKey(const ValueKey('catalog-search')));
      await field(t, 'Hizmet ara', 'Phase 8');
      await t.pump(const Duration(milliseconds: 400));
      await live.visible(t, find.text('Phase 8 exterior'));
      FocusManager.instance.primaryFocus?.unfocus();
      await t.pumpAndSettle();
      final row = find.text('Phase 8 exterior');
      await t.ensureVisible(row);
      await t.pumpAndSettle();
      await t.tap(row);
      await live.visible(t, find.text('Düzenle'));
      final list = await api.list(b);
      live.success(list);
      final stale =
          (list as AuthSuccess).value.items.single as CatalogServiceRecord;
      expect(stale.offerings.single.price.wire, '450.10');
      await live.tap(t, 'Düzenle');
      await field(t, 'Hizmet adı', 'Phase 8 updated');
      await live.tap(t, 'Kaydet');
      await live.visible(t, find.text('Phase 8 updated'));
      final conflict = await api.save(b, stale.draft());
      expect(
        conflict is AuthFailure<CatalogServiceRecord> &&
            conflict.error.code == AuthErrorCode.conflict,
        isTrue,
      );
      final published = await auth.apiClient.get(
        '/api/v1/businesses/$b/published-services',
        access: (await auth.accessToken() as AuthSuccess<String>).value,
      );
      expect((published as Map)['items'].length, 1);
      await live.tap(t, 'Arşivle');
      await live.tap(t, 'Onayla');
      await live.visible(t, find.text('Gösterilecek hizmet bulunamadı.'));
      final after = await auth.apiClient.get(
        '/api/v1/businesses/$b/published-services',
        access: (await auth.accessToken() as AuthSuccess<String>).value,
      );
      expect((after as Map)['items'], isEmpty);
      await live.tap(t, 'Çıkış yap');
      await live.visible(t, find.text('Giriş yap'));
    },
  );
}
