import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';
import 'package:washflow/app/app.dart';
import 'package:washflow/app/routing/app_router.dart';
import 'package:washflow/features/auth/data/api_context_service.dart';
import 'package:washflow/features/auth/domain/auth_service.dart';
import 'package:washflow/features/auth/domain/user_contexts.dart';
import 'package:washflow/features/business_settings/data/api_business_settings_service.dart';
import 'package:washflow/features/business_settings/domain/business_settings.dart';
import 'package:washflow/features/invitations/data/api_owner_invitation_service.dart';

import 'invitation_live_test.dart' as live;

Finder input(String label) => find.byWidgetPredicate(
  (w) => w is TextField && w.decoration?.labelText == label,
);
Future<void> edit(WidgetTester t, String label, String value) async {
  final f = input(label);
  await live.visible(t, f);
  await t.ensureVisible(f);
  await t.tap(f);
  await t.pumpAndSettle();
  await t.enterText(f, value);
  await t.pumpAndSettle();
}

Future<void> toggle(WidgetTester tester, String label) async {
  final finder = find.widgetWithText(SwitchListTile, label);
  await live.visible(tester, finder);
  await tester.ensureVisible(finder);
  await tester.tap(finder);
  await tester.pumpAndSettle();
}

void main() {
  IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'live Owner persists profile and weekly hours, handles concurrent edit and retains invitations',
    (t) async {
      expect(
        live.base == 'http://127.0.0.1:5189',
        isTrue,
        reason: 'Only the isolated test backend is allowed.',
      );
      await live.store.clear();
      addTearDown(() => live.store.clear());
      final auth = await live.owner();
      final service = ApiBusinessSettingsService(
        apiClient: auth.apiClient,
        session: auth,
      );
      await t.pumpWidget(
        WashFlowApp(
          authService: auth,
          contextService: ApiContextService(
            apiClient: auth.apiClient,
            session: auth,
          ),
          settingsService: service,
          invitationService: ApiOwnerInvitationService(
            apiClient: auth.apiClient,
            session: auth,
          ),
          initialRoute: AppRouter.session,
        ),
      );
      await t.pumpAndSettle();
      await live.tap(t, 'İşletme ayarları');
      await live.visible(t, input('İşletme adı'));
      await edit(t, 'İşletme adı', 'Phase 6 edited business');
      await edit(t, 'Herkese açık telefon', '+44 20 5555 0100');
      await edit(t, 'Adres satırı 1', 'Synthetic test street');
      await edit(t, 'Şehir', 'Test city');
      await edit(t, 'Ülke kodu (iki harf)', 'GB');
      await edit(t, 'IANA saat dilimi', 'Europe/London');
      await toggle(t, 'Haftalık saatleri yapılandır');
      await toggle(t, 'Pazartesi');
      await live.tap(t, 'Açılış: Seçin');
      final dialog = find.byType(TimePickerDialog);
      await live.visible(t, dialog);
      final fields = find.descendant(
        of: dialog,
        matching: find.byType(TextField),
      );
      await t.enterText(fields.at(0), '09');
      await t.enterText(fields.at(1), '30');
      await live.tap(t, 'Tamam');
      await live.tap(t, 'Gün sonu 24:00');
      await live.tap(t, 'Değişiklikleri kaydet');
      await live.visible(
        t,
        find.text('İşletme ayarları kaydedildi ve yeniden yüklendi.'),
      );
      final state = t.widget<TextField>(input('İşletme adı'));
      expect(state.controller!.text, 'Phase 6 edited business');
      final contexts = await ApiContextService(
        apiClient: auth.apiClient,
        session: auth,
      ).getContexts();
      live.success(contexts);
      // Route identity comes only from the actual backend context.
      final contextValue = (contexts as AuthSuccess<UserContexts>).value;
      final String id = contextValue.businessContexts.single.businessId;
      final read = await service.read(id);
      live.success(read);
      final persisted = (read as AuthSuccess<BusinessSettings>).value;
      expect(persisted.fields['phone'], '+44 20 5555 0100');
      expect(persisted.weeklyHours!.first.opensAtMinute, 570);
      expect(persisted.weeklyHours!.first.closesAtMinute, 1440);
      expect(persisted.weeklyHours!.skip(1).every((d) => d.closed), isTrue);
      final other = persisted.change(
        fields: {...persisted.fields, 'name': 'Concurrent winner'},
      );
      live.success(await service.save(other));
      await edit(t, 'İşletme adı', 'Stale loser');
      await live.tap(t, 'Değişiklikleri kaydet');
      await live.visible(
        t,
        find.textContaining('Başka bir oturum kaydı değiştirdi'),
      );
      await live.tap(t, 'Güncel kaydı yükle');
      await live.tap(t, 'Değişiklikleri sil');
      await live.visible(t, find.text('Concurrent winner'));
      expect(
        t.widget<TextField>(input('İşletme adı')).controller!.text,
        'Concurrent winner',
      );
      // Back to Owner home without inventing grants, retaining invitation management.
      await t.tap(find.byType(BackButton));
      await t.pumpAndSettle();
      await live.tap(t, 'Çalışan davetleri');
      await live.visible(t, find.text('Bu işletmede henüz davet yok.'));
      await live.tap(t, 'Çıkış yap');
      await live.visible(t, find.byKey(const ValueKey('email')));
      await t.pumpWidget(const SizedBox.shrink());
      await t.pumpAndSettle();
    },
  );
}
