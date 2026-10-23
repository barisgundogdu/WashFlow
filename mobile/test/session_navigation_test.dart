import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:washflow/app/app.dart';
import 'package:washflow/app/routing/app_router.dart';
import 'package:washflow/features/auth/domain/auth_service.dart';
import 'package:washflow/features/auth/domain/user_contexts.dart';
import 'package:washflow/features/auth/presentation/widgets/authorized_context_picker.dart';

import 'support/session_fakes.dart';

Future<void> open(
  WidgetTester tester,
  FakeSession session,
  FakeContexts contexts, {
  bool settle = true,
  String initialRoute = AppRouter.session,
}) async {
  addTearDown(session.dispose);
  addTearDown(() => tester.pumpWidget(const SizedBox.shrink()));
  await tester.pumpWidget(
    WashFlowApp(
      authService: session,
      contextService: contexts,
      initialRoute: initialRoute,
    ),
  );
  if (settle) await tester.pumpAndSettle();
}

void main() {
  testWidgets('context loading blocks every role before server response', (
    tester,
  ) async {
    final pending = Completer<AuthResult<UserContexts>>();
    final contexts = FakeContexts(grants())..handler = () => pending.future;
    await open(tester, FakeSession(), contexts, settle: false);
    await tester.pump();
    expect(find.byType(CircularProgressIndicator), findsOneWidget);
    expect(find.text('Müşteri alanı'), findsNothing);
    expect(find.text('İşletme sahibi alanı'), findsNothing);
    pending.complete(AuthSuccess(grants(customer: true)));
    await tester.pumpAndSettle();
    expect(find.text('Müşteri alanı'), findsOneWidget);
    expect(find.textContaining('Geçici ekran.'), findsOneWidget);
  });

  for (final role in ['Owner', 'Employee']) {
    testWidgets(
      'sole $role experience opens automatically with business name',
      (tester) async {
        await open(
          tester,
          FakeSession(),
          FakeContexts(
            grants(
              businesses: [
                businessContext(roles: [role]),
              ],
            ),
          ),
        );
        expect(
          find.text(role == 'Owner' ? 'İşletme sahibi alanı' : 'Çalışan alanı'),
          findsOneWidget,
        );
        expect(find.textContaining('İşletme A'), findsOneWidget);
        expect(find.byType(AuthorizedContextPicker), findsNothing);
      },
    );
  }

  testWidgets(
    'multiple roles within one business and customer use existing picker',
    (tester) async {
      final contexts = FakeContexts(
        grants(
          customer: true,
          businesses: [
            businessContext(roles: ['Owner', 'Employee']),
          ],
        ),
      );
      await open(tester, FakeSession(), contexts);
      expect(find.byType(AuthorizedContextPicker), findsOneWidget);
      expect(find.text('Müşteri'), findsOneWidget);
      expect(find.text('İşletme A · İşletme sahibi'), findsOneWidget);
      await tester.tap(find.text('İşletme A · Çalışan'));
      await tester.pumpAndSettle();
      expect(contexts.calls, 2);
      expect(find.text('Çalışan alanı'), findsOneWidget);
      expect(find.text('İşletme sahibi alanı'), findsNothing);
      await tester.tap(find.text('Hesap değiştir'));
      await tester.pumpAndSettle();
      expect(contexts.calls, 3);
      expect(find.byType(AuthorizedContextPicker), findsOneWidget);
      await tester.tap(find.text('Müşteri'));
      await tester.pumpAndSettle();
      expect(find.text('Müşteri alanı'), findsOneWidget);
    },
  );

  testWidgets(
    'zero or unknown-only grants show signed-in empty state without fake actions',
    (tester) async {
      final contexts = FakeContexts(
        grants(
          businesses: [
            businessContext(roles: ['Administrator', 'owner']),
          ],
        ),
      );
      await open(tester, FakeSession(), contexts);
      expect(
        find.textContaining('Kullanılabilir hesap bağlamı bulunamadı.'),
        findsOneWidget,
      );
      expect(find.textContaining('Davet'), findsNothing);
      expect(find.byType(AuthorizedContextPicker), findsNothing);
      await tester.tap(find.text('Yenile'));
      await tester.pumpAndSettle();
      expect(contexts.calls, 2);
      await tester.tap(find.text('Çıkış yap'));
      await tester.pumpAndSettle();
      expect(find.text('Tekrar hoş geldiniz'), findsOneWidget);
    },
  );

  testWidgets(
    'switch removes old business and discards its nested navigation stack',
    (tester) async {
      final contexts = FakeContexts(
        grants(
          businesses: [
            businessContext(),
            businessContext(id: businessB, name: 'İşletme B'),
          ],
        ),
      );
      await open(tester, FakeSession(), contexts);
      await tester.tap(find.text('İşletme A · İşletme sahibi'));
      await tester.pumpAndSettle();
      final oldNavigator = tester.state<NavigatorState>(
        find.byType(Navigator).last,
      );
      // An unregistered nested route cannot become a role destination.
      unawaited(oldNavigator.pushNamed('/owner/private'));
      await tester.pumpAndSettle();
      expect(find.text('Sayfa bulunamadı.'), findsOneWidget);
      oldNavigator.pop();
      await tester.pumpAndSettle();
      final pending = Completer<AuthResult<UserContexts>>();
      contexts.handler = () => pending.future;
      await tester.tap(find.text('Hesap değiştir'));
      await tester.pump();
      expect(find.textContaining('İşletme A'), findsNothing);
      expect(oldNavigator.mounted, isFalse);
      pending.complete(AuthSuccess(contexts.value));
      await tester.pumpAndSettle();
      contexts.handler = null;
      await tester.tap(find.text('İşletme B · İşletme sahibi'));
      await tester.pumpAndSettle();
      expect(find.textContaining('İşletme B'), findsOneWidget);
      expect(find.textContaining('İşletme A'), findsNothing);
      final currentNavigator = tester.state<NavigatorState>(
        find.byType(Navigator).last,
      );
      expect(currentNavigator.canPop(), isFalse);
    },
  );

  testWidgets('resume fetches grants and removes revoked owner content', (
    tester,
  ) async {
    final contexts = FakeContexts(grants(businesses: [businessContext()]));
    await open(tester, FakeSession(), contexts);
    expect(find.text('İşletme sahibi alanı'), findsOneWidget);
    tester.binding.handleAppLifecycleStateChanged(AppLifecycleState.inactive);
    await tester.pump();
    expect(find.text('İşletme sahibi alanı'), findsNothing);
    contexts.value = grants();
    tester.binding.handleAppLifecycleStateChanged(AppLifecycleState.resumed);
    await tester.pumpAndSettle();
    expect(contexts.calls, 2);
    expect(
      find.textContaining('Kullanılabilir hesap bağlamı bulunamadı.'),
      findsOneWidget,
    );
  });

  testWidgets(
    'forged role route and role arguments never select an unauthorized destination',
    (tester) async {
      final contexts = FakeContexts(
        grants(
          businesses: [
            businessContext(roles: ['Employee']),
          ],
        ),
      );
      await open(tester, FakeSession(), contexts, initialRoute: '/owner');
      expect(find.text('Sayfa bulunamadı.'), findsOneWidget);
      expect(contexts.calls, 0);
      final navigator = tester.state<NavigatorState>(
        find.byType(Navigator).first,
      );
      unawaited(
        navigator.pushNamedAndRemoveUntil(
          AppRouter.session,
          (_) => false,
          arguments: {'role': 'Owner', 'businessId': businessB},
        ),
      );
      await tester.pumpAndSettle();
      expect(find.text('Çalışan alanı'), findsOneWidget);
      expect(find.text('İşletme sahibi alanı'), findsNothing);
      expect(find.textContaining('İşletme B'), findsNothing);
    },
  );

  testWidgets('late read after logout cannot resurrect role content', (
    tester,
  ) async {
    final pending = Completer<AuthResult<UserContexts>>();
    final contexts = FakeContexts(grants())..handler = () => pending.future;
    final session = FakeSession();
    await open(tester, session, contexts, settle: false);
    await tester.pump();
    await tester.tap(find.text('Çıkış yap'));
    await tester.pumpAndSettle();
    expect(session.logouts, 1);
    expect(find.text('Tekrar hoş geldiniz'), findsOneWidget);
    pending.complete(AuthSuccess(grants(businesses: [businessContext()])));
    await tester.pumpAndSettle();
    expect(find.text('İşletme sahibi alanı'), findsNothing);
    expect(find.text('Tekrar hoş geldiniz'), findsOneWidget);
  });

  testWidgets('account changes invalidate in-flight context responses', (
    tester,
  ) async {
    final pending = Completer<AuthResult<UserContexts>>();
    final contexts = FakeContexts(grants())..handler = () => pending.future;
    final session = FakeSession();
    await open(tester, session, contexts, settle: false);
    await tester.pump();
    session.changeIdentity();
    await tester.pumpAndSettle();
    pending.complete(AuthSuccess(grants(customer: true)));
    await tester.pumpAndSettle();
    expect(find.text('Müşteri alanı'), findsNothing);
    expect(find.text('Tekrar hoş geldiniz'), findsOneWidget);
  });

  testWidgets(
    'network failures allow retry; unauthorized context read returns to login',
    (tester) async {
      final contexts = FakeContexts(grants())
        ..handler = () async =>
            AuthFailure(AuthError(code: AuthErrorCode.network));
      await open(tester, FakeSession(), contexts);
      expect(find.textContaining('Sunucuya ulaşılamadı.'), findsOneWidget);
      contexts.handler = null;
      contexts.value = grants(customer: true);
      await tester.tap(find.text('Yenile'));
      await tester.pumpAndSettle();
      expect(find.text('Müşteri alanı'), findsOneWidget);
      contexts.handler = () async =>
          AuthFailure(AuthError(code: AuthErrorCode.unauthenticated));
      await tester.tap(find.text('Yenile'));
      await tester.pumpAndSettle();
      expect(find.text('Müşteri alanı'), findsNothing);
      expect(find.text('Tekrar hoş geldiniz'), findsOneWidget);
    },
  );

  testWidgets(
    'logout cleanup error retains no role content and permits retry',
    (tester) async {
      final session = FakeSession()
        ..logoutResult = AuthFailure(AuthError(code: AuthErrorCode.storage));
      await open(tester, session, FakeContexts(grants(customer: true)));
      await tester.tap(find.text('Çıkış yap'));
      await tester.pumpAndSettle();
      expect(find.text('Müşteri alanı'), findsNothing);
      expect(find.textContaining('Güvenli oturum kaydı'), findsOneWidget);
      session.logoutResult = const AuthSuccess(null);
      await tester.tap(find.text('Çıkış yap'));
      await tester.pumpAndSettle();
      expect(find.text('Tekrar hoş geldiniz'), findsOneWidget);
    },
  );

  testWidgets('role shell fits compact viewport and large text', (
    tester,
  ) async {
    tester.view.physicalSize = const Size(320, 640);
    tester.view.devicePixelRatio = 1;
    tester.platformDispatcher.textScaleFactorTestValue = 1.8;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    addTearDown(tester.platformDispatcher.clearTextScaleFactorTestValue);
    await open(
      tester,
      FakeSession(),
      FakeContexts(grants(businesses: [businessContext()])),
    );
    expect(tester.takeException(), isNull);
    await tester.ensureVisible(find.text('Çıkış yap'));
    await tester.tap(find.text('Çıkış yap'));
    await tester.pumpAndSettle();
    expect(find.text('Tekrar hoş geldiniz'), findsOneWidget);
  });
  testWidgets(
    'replacing app account dependencies destroys old controller and responses',
    (tester) async {
      final pending = Completer<AuthResult<UserContexts>>();
      final oldContexts = FakeContexts(grants())
        ..handler = () => pending.future;
      await open(tester, FakeSession(), oldContexts, settle: false);
      await tester.pump();
      final next = FakeSession();
      addTearDown(next.dispose);
      final nextContexts = FakeContexts(
        grants(
          businesses: [
            businessContext(
              id: businessB,
              name: 'İşletme B',
              roles: ['Employee'],
            ),
          ],
        ),
      );
      await tester.pumpWidget(
        WashFlowApp(
          authService: next,
          contextService: nextContexts,
          initialRoute: AppRouter.session,
        ),
      );
      await tester.pumpAndSettle();
      pending.complete(AuthSuccess(grants(businesses: [businessContext()])));
      await tester.pumpAndSettle();
      expect(find.textContaining('İşletme B'), findsOneWidget);
      expect(find.textContaining('İşletme A'), findsNothing);
      expect(find.text('Çalışan alanı'), findsOneWidget);
    },
  );

  testWidgets(
    'offline logout clears role content and reports unconfirmed server logout',
    (tester) async {
      final session = FakeSession()
        ..logoutResult = AuthFailure(AuthError(code: AuthErrorCode.network));
      await open(tester, session, FakeContexts(grants(customer: true)));
      await tester.tap(find.text('Çıkış yap'));
      await tester.pumpAndSettle();
      expect(find.text('Müşteri alanı'), findsNothing);
      expect(find.text('Tekrar hoş geldiniz'), findsOneWidget);
      expect(
        find.text('Yerel oturum kapatıldı. Sunucuda çıkış doğrulanamadı.'),
        findsOneWidget,
      );
    },
  );
  testWidgets(
    'periodic session check removes role content after invalidation',
    (tester) async {
      final session = FakeSession();
      await open(tester, session, FakeContexts(grants(customer: true)));
      session.refreshResult = AuthFailure(
        AuthError(code: AuthErrorCode.invalidCredentials),
      );
      await tester.pump(const Duration(seconds: 31));
      await tester.pumpAndSettle();
      expect(session.checks, 1);
      expect(find.text('Müşteri alanı'), findsNothing);
      expect(find.text('Tekrar hoş geldiniz'), findsOneWidget);
    },
  );
}
