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
import 'package:washflow/features/auth/domain/user_contexts.dart';
import 'package:washflow/features/invitations/data/api_invitation_redemption_service.dart';
import 'package:washflow/features/invitations/data/api_owner_invitation_service.dart';

// Opt-in only. Run against the disposable Phase 5 backend, never normal app data.
// No screenshots, body diagnostics, credential fixtures or credential dart-defines.
const base = String.fromEnvironment('PHASE5_API_URL');
const password = 'Synthetic phase five password 123!';
const store = SecureTokenStore();
String email() =>
    'phase5-${DateTime.now().microsecondsSinceEpoch}@example.test';
ApiAuthService auth() => ApiAuthService(
  baseUrl: Uri.parse(base),
  store: store,
  client: StatusClient(),
);
final statuses = <String>[];

class StatusClient extends http.BaseClient {
  final _inner = http.Client();
  @override
  Future<http.StreamedResponse> send(http.BaseRequest request) async {
    final response = await _inner.send(request);
    statuses.add(
      '${request.method} ${request.url.path}: ${response.statusCode}',
    );
    return response;
  }
}

void success<T>(AuthResult<T> result) =>
    expect(result is AuthSuccess<T>, isTrue);
Future<String> mail(String address, String purpose) async {
  for (var i = 0; i < 100; i++) {
    final response = await http.post(
      Uri.parse('$base/api/dev/email/$purpose/take'),
      headers: {'Content-Type': 'application/json'},
      body: jsonEncode({'email': address}),
    );
    if (response.statusCode == 200) {
      return (jsonDecode(response.body) as Map)['token'] as String;
    }
    expect(response.statusCode, 404);
    await Future<void>.delayed(const Duration(milliseconds: 100));
  }
  debugPrint(statuses.join('\n'));
  throw StateError('Isolated test mailbox did not become ready.');
}

Future<void> visible(WidgetTester tester, Finder finder) async {
  for (var i = 0; i < 150; i++) {
    await tester.pump(const Duration(milliseconds: 100));
    if (finder.evaluate().isNotEmpty) {
      await tester.pumpAndSettle();
      return;
    }
  }
  expect(finder, findsWidgets);
}

Future<void> tap(WidgetTester tester, String text) async {
  final candidates = find.ancestor(
    of: find.text(text),
    matching: find.byWidgetPredicate(
      (w) => w is ButtonStyleButton && w.onPressed != null,
    ),
  );
  await visible(tester, candidates);
  final finder = candidates.first;
  await tester.ensureVisible(finder);
  await tester.tap(finder);
  await tester.pumpAndSettle();
}

Future<void> invitationEmail(WidgetTester tester, String address) async {
  final field = find.byWidgetPredicate(
    (w) =>
        w is TextField &&
        w.key == const ValueKey('invitation-email') &&
        w.enabled != false,
  );
  await visible(tester, field);
  await tester.ensureVisible(field);
  await tester.tap(field);
  await tester.pumpAndSettle();
  await tester.enterText(field, address);
  await tester.pumpAndSettle();
  expect((tester.widget<TextField>(field).controller?.text == address), isTrue);
}

Future<void> submit(WidgetTester tester) async {
  final finder = find.byKey(const ValueKey('submit'));
  await tester.ensureVisible(finder);
  await tester.tap(finder);
  await tester.pumpAndSettle();
}

Future<void> mount(
  WidgetTester tester,
  ApiAuthService service, {
  bool signedIn = false,
}) async {
  await tester.pumpWidget(
    WashFlowApp(
      authService: service,
      contextService: ApiContextService(
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
      initialRoute: signedIn ? AppRouter.session : AppRouter.welcome,
    ),
  );
  await tester.pumpAndSettle();
}

Future<void> loginUi(WidgetTester tester, String address) async {
  await visible(tester, find.byKey(const ValueKey('email')));
  await tester.enterText(find.byKey(const ValueKey('email')), address);
  await tester.enterText(find.byKey(const ValueKey('password')), password);
  await submit(tester);
}

Future<void> enterCredential(WidgetTester tester, String credential) async {
  await tap(tester, 'Davet koduyla katıl');
  await tester.enterText(
    find.byKey(const ValueKey('invitation-credential')),
    credential,
  );
  await tap(tester, 'Davetle devam et');
}

Future<void> acceptUi(WidgetTester tester) async {
  await tap(tester, 'Daveti kabul et');
  await tap(tester, 'Kabul etmeyi onayla');
  await tap(tester, 'Hesabıma devam et');
}

Future<ApiAuthService> owner() async {
  final service = auth();
  final address = email();
  success(
    await service.registerBusiness(
      BusinessRegistrationRequest(
        businessName: 'Phase 5 test business',
        ownerFullName: 'Test owner',
        email: address,
        password: password,
      ),
    ),
  );
  success(await service.verifyEmail(await mail(address, 'verification')));
  success(
    await service.login(LoginRequest(email: address, password: password)),
  );
  return service;
}

void main() {
  IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  setUp(() async {
    expect(
      base == 'http://127.0.0.1:5189',
      isTrue,
      reason: 'Use the isolated Phase 5 loopback backend only.',
    );
    await store.clear();
    statuses.clear();
  });
  tearDown(() async {
    await store.clear();
  });

  testWidgets(
    'live Owner create/list/resend/revoke and existing recipient explicit acceptance',
    (tester) async {
      final service = await owner();
      final contexts = await ApiContextService(
        apiClient: service.apiClient,
        session: service,
      ).getContexts();
      success(contexts);
      final business = (contexts as AuthSuccess<UserContexts>)
          .value
          .businessContexts
          .single
          .businessId;
      final recipient = auth();
      final address = email();
      success(
        await recipient.registerCustomer(
          CustomerRegistrationRequest(
            fullName: 'Existing customer',
            email: address,
            password: password,
          ),
        ),
      );
      success(await recipient.verifyEmail(await mail(address, 'verification')));
      await mount(tester, service, signedIn: true);
      await tap(tester, 'Çalışan davetleri');
      await visible(tester, find.text('Bu işletmede henüz davet yok.'));
      await invitationEmail(tester, address);
      await tap(tester, 'Davet oluştur');
      await visible(tester, find.text('Kabul bekliyor'));
      final old = await mail(address, 'invitation');
      await tap(tester, 'Yeniden gönder');
      final credential = await mail(address, 'invitation');
      expect(old != credential, isTrue);
      // Wait for the replacement list, not merely the server's mailbox side effect.
      await visible(tester, find.text('İptal edildi'));
      // Revoke replacement through the real confirmation UI, then issue a fresh invitation.
      await tap(tester, 'Daveti iptal et');
      await tap(tester, 'İptali onayla');
      await visible(tester, find.text('Davet iptal edildi.'));
      await invitationEmail(tester, address);
      await tap(tester, 'Davet oluştur');
      final fresh = await mail(address, 'invitation');
      await tap(tester, 'Çıkış yap');
      await visible(tester, find.byKey(const ValueKey('email')));
      // Start from welcome with a new authenticated boundary, preserving real secure storage behavior.
      await tester.pumpWidget(const SizedBox.shrink());
      await tester.pumpAndSettle();
      await mount(tester, recipient);
      await enterCredential(tester, fresh);
      await tap(tester, 'Mevcut hesabımla giriş yap');
      await loginUi(tester, address);
      await acceptUi(tester);
      await visible(tester, find.text('Hesap bağlamınızı seçin'));
      await tap(tester, 'Phase 5 test business · Çalışan');
      await visible(tester, find.text('Çalışan alanı'));
      expect(find.text('Çalışan davetleri'), findsNothing);
      final actual = await ApiContextService(
        apiClient: recipient.apiClient,
        session: recipient,
      ).getContexts();
      success(actual);
      final value = (actual as AuthSuccess<UserContexts>).value;
      expect(value.customerContext != null, isTrue);
      expect(value.businessContexts.single.businessId == business, isTrue);
      expect(value.businessContexts.single.roles, {BusinessRole.employee});
      final denied = await ApiOwnerInvitationService(
        apiClient: recipient.apiClient,
        session: recipient,
      ).list(business);
      expect(switch (denied) {
        AuthFailure(:final error) => error.code == AuthErrorCode.forbidden,
        _ => false,
      }, isTrue);
      await tap(tester, 'Hesap değiştir');
      await visible(tester, find.text('Hesap bağlamınızı seçin'));
      await tap(tester, 'Müşteri');
      await visible(tester, find.text('Müşteri alanı'));
      await tap(tester, 'Çıkış yap');
      await tester.pumpWidget(const SizedBox.shrink());
      await tester.pumpAndSettle();
    },
  );

  testWidgets(
    'live new recipient registers verifies logs in accepts and restores secure session',
    (tester) async {
      final issuer = await owner();
      final contexts = await ApiContextService(
        apiClient: issuer.apiClient,
        session: issuer,
      ).getContexts();
      success(contexts);
      final business = (contexts as AuthSuccess<UserContexts>)
          .value
          .businessContexts
          .single
          .businessId;
      final address = email();
      success(
        await ApiOwnerInvitationService(
          apiClient: issuer.apiClient,
          session: issuer,
        ).create(business, address),
      );
      final credential = await mail(address, 'invitation');
      success(await issuer.logout());
      final service = auth();
      await mount(tester, service);
      await enterCredential(tester, credential);
      await tap(tester, 'Davet için hesap oluştur');
      await tester.enterText(
        find.byKey(const ValueKey('fullName')),
        'New test employee',
      );
      await tester.enterText(find.byKey(const ValueKey('email')), address);
      await tester.enterText(find.byKey(const ValueKey('password')), password);
      await tester.enterText(
        find.byKey(const ValueKey('confirmation')),
        password,
      );
      await submit(tester);
      await visible(tester, find.text('E-postayı doğrula'));
      await tester.enterText(
        find.byType(TextField).first,
        await mail(address, 'verification'),
      );
      await tap(tester, 'E-postayı doğrula');
      await tap(tester, 'Girişe dön');
      await loginUi(tester, address);
      final before = await ApiContextService(
        apiClient: service.apiClient,
        session: service,
      ).getContexts();
      success(before);
      expect(
        (before as AuthSuccess<UserContexts>).value.businessContexts.isEmpty,
        isTrue,
      );
      expect(before.value.customerContext == null, isTrue);
      await acceptUi(tester);
      await visible(tester, find.text('Çalışan alanı'));
      expect(find.text('İşletme sahibi alanı'), findsNothing);
      final saved = await store.read();
      expect(saved != null && !saved.contains(credential), isTrue);
      await tester.pumpWidget(const SizedBox.shrink());
      await tester.pumpAndSettle();
      final restored = auth();
      final restoration = await restored.restoreSession();
      expect(restoration is AuthSuccess<bool> && restoration.value, isTrue);
      success(await restored.refreshSession());
      await mount(tester, restored, signedIn: true);
      await visible(tester, find.text('Çalışan alanı'));
      await tap(tester, 'Çıkış yap');
      await visible(tester, find.byKey(const ValueKey('email')));
      expect(await store.read() == null, isTrue);
      await tester.pumpWidget(const SizedBox.shrink());
      await tester.pumpAndSettle();
    },
  );
}
