import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:washflow/app/app.dart';
import 'package:washflow/app/routing/app_router.dart';
import 'package:washflow/features/auth/domain/auth_service.dart';
import 'package:washflow/features/invitations/domain/invitation.dart';

import 'support/invitation_fakes.dart';
import 'support/session_fakes.dart';

Future<void> open(
  WidgetTester tester,
  FakeSession session,
  FakeContexts contexts,
  FakeInvitations service, {
  bool enter = true,
}) async {
  addTearDown(session.dispose);
  addTearDown(() => tester.pumpWidget(const SizedBox.shrink()));
  await tester.pumpWidget(
    WashFlowApp(
      authService: session,
      contextService: contexts,
      invitationService: service,
      initialRoute: AppRouter.session,
    ),
  );
  await tester.pumpAndSettle();
  if (enter) {
    await tester.ensureVisible(find.text('Çalışan davetleri'));
    await tester.tap(find.text('Çalışan davetleri'));
    await tester.pumpAndSettle();
  }
}

Future<void> tap(WidgetTester tester, String text) async {
  await tester.ensureVisible(find.text(text).last);
  await tester.tap(find.text(text).last);
  await tester.pumpAndSettle();
}

void main() {
  testWidgets(
    'owner entry revalidates context; empty list and record-only create success',
    (tester) async {
      final contexts = FakeContexts(grants(businesses: [businessContext()]));
      final service = FakeInvitations();
      await open(tester, FakeSession(), contexts, service);
      expect(contexts.calls, 2);
      expect(find.text('İşletme A'), findsOneWidget);
      expect(find.text('Bu işletmede henüz davet yok.'), findsOneWidget);
      await tester.enterText(
        find.byKey(const ValueKey('invitation-email')),
        'bad',
      );
      await tap(tester, 'Davet oluştur');
      expect(service.calls.where((c) => c.startsWith('create')), isEmpty);
      expect(find.textContaining('Geçerli bir e-posta'), findsOneWidget);
      await tester.enterText(
        find.byKey(const ValueKey('invitation-email')),
        'employee@example.test',
      );
      await tap(tester, 'Davet oluştur');
      expect(find.text('employee@example.test'), findsOneWidget);
      expect(find.text('Kabul bekliyor'), findsOneWidget);
      expect(
        find.text('Davet kaydedildi. E-posta teslimi doğrulanmadı.'),
        findsOneWidget,
      );
      expect(service.calls.where((c) => c.startsWith('create')), [
        'create:$businessA',
      ]);
    },
  );

  testWidgets(
    'revoke requires confirmation; cancel is harmless; resend returns replacement ID',
    (tester) async {
      final service = FakeInvitations()..items = [invitation()];
      await open(
        tester,
        FakeSession(),
        FakeContexts(grants(businesses: [businessContext()])),
        service,
      );
      await tap(tester, 'Daveti iptal et');
      expect(find.text('İptali onayla'), findsOneWidget);
      await tap(tester, 'Vazgeç');
      expect(service.calls.where((c) => c.startsWith('revoke')), isEmpty);
      await tap(tester, 'Yeniden gönder');
      expect(service.calls, contains('resend:$businessA:$invitationA'));
      expect(
        find.textContaining('Önceki davet kodu geçersizdir'),
        findsOneWidget,
      );
      await tap(tester, 'Daveti iptal et');
      await tap(tester, 'İptali onayla');
      expect(service.calls, contains('revoke:$businessA:$invitationB'));
      expect(find.text('Davet iptal edildi.'), findsOneWidget);
    },
  );

  testWidgets(
    'accepted and revoked rows disable actions; expired row allows backend recheck',
    (tester) async {
      final now = DateTime.now().toUtc();
      final service = FakeInvitations()
        ..items = [
          EmployeeInvitation.fromJson(
            invitationJson(accepted: now.toIso8601String()),
          ),
          EmployeeInvitation.fromJson(
            invitationJson(id: invitationB, revoked: now.toIso8601String()),
          ),
          EmployeeInvitation.fromJson(
            invitationJson(
              id: '88888888-8888-8888-8888-888888888888',
              expiry: now.subtract(const Duration(hours: 1)).toIso8601String(),
            ),
          ),
        ];
      await open(
        tester,
        FakeSession(),
        FakeContexts(grants(businesses: [businessContext()])),
        service,
      );
      expect(find.text('Kabul edildi'), findsOneWidget);
      expect(find.text('İptal edildi'), findsOneWidget);
      expect(find.text('Süresi doldu'), findsOneWidget);
      final actions = tester
          .widgetList<TextButton>(
            find.widgetWithText(TextButton, 'Yeniden gönder'),
          )
          .toList();
      expect(actions[0].onPressed, isNull);
      expect(actions[1].onPressed, isNull);
      expect(actions[2].onPressed, isNotNull);
    },
  );

  testWidgets(
    'list errors show retry and block mutations until read succeeds',
    (tester) async {
      final service = FakeInvitations()
        ..onList = (_) async =>
            AuthFailure(AuthError(code: AuthErrorCode.network));
      await open(
        tester,
        FakeSession(),
        FakeContexts(grants(businesses: [businessContext()])),
        service,
      );
      expect(find.textContaining('Liste yenilenemedi'), findsOneWidget);
      expect(
        tester
            .widget<FilledButton>(
              find.widgetWithText(FilledButton, 'Davet oluştur'),
            )
            .onPressed,
        isNull,
      );
      service.onList = null;
      await tap(tester, 'Listeyi yeniden dene');
      expect(find.text('Bu işletmede henüz davet yok.'), findsOneWidget);
    },
  );

  testWidgets(
    '403 refreshes contexts and removes revoked business without continuing',
    (tester) async {
      final contexts = FakeContexts(grants(businesses: [businessContext()]));
      final service = FakeInvitations();
      await open(tester, FakeSession(), contexts, service, enter: false);
      service.onList = (_) async {
        contexts.value = grants();
        return AuthFailure(AuthError(code: AuthErrorCode.forbidden));
      };
      await tester.tap(find.text('Çalışan davetleri'));
      await tester.pumpAndSettle();
      expect(contexts.calls, 3);
      expect(
        find.textContaining('Kullanılabilir hesap bağlamı bulunamadı.'),
        findsOneWidget,
      );
      expect(find.byKey(const ValueKey('invitation-email')), findsNothing);
      expect(service.calls, ['list:$businessA']);
    },
  );

  testWidgets('401 clears protected UI and returns to login without replay', (
    tester,
  ) async {
    final service = FakeInvitations()
      ..onList = (_) async =>
          AuthFailure(AuthError(code: AuthErrorCode.unauthenticated));
    await open(
      tester,
      FakeSession(),
      FakeContexts(grants(businesses: [businessContext()])),
      service,
    );
    expect(find.text('Tekrar hoş geldiniz'), findsOneWidget);
    expect(service.calls, ['list:$businessA']);
    expect(find.text('İşletme A'), findsNothing);
  });

  for (final role in ['Employee', 'Customer']) {
    testWidgets(
      '$role cannot enter invitation management or forge nested routes',
      (tester) async {
        final contexts = FakeContexts(
          role == 'Customer'
              ? grants(customer: true)
              : grants(
                  businesses: [
                    businessContext(roles: [role]),
                  ],
                ),
        );
        final service = FakeInvitations();
        await open(tester, FakeSession(), contexts, service, enter: false);
        expect(find.text('Çalışan davetleri'), findsNothing);
        final navigator = tester.state<NavigatorState>(
          find.byType(Navigator).last,
        );
        unawaited(
          navigator.pushNamed(
            '/employee-invitations',
            arguments: {'businessId': businessA, 'role': 'Owner'},
          ),
        );
        await tester.pumpAndSettle();
        expect(find.text('Sayfa bulunamadı.'), findsOneWidget);
        expect(service.calls, isEmpty);
      },
    );
  }

  testWidgets(
    'owner entry revocation before screen creation prevents even a list request',
    (tester) async {
      final contexts = FakeContexts(grants(businesses: [businessContext()]));
      final service = FakeInvitations();
      await open(tester, FakeSession(), contexts, service, enter: false);
      contexts.value = grants();
      await tester.tap(find.text('Çalışan davetleri'));
      await tester.pumpAndSettle();
      expect(service.calls, isEmpty);
      expect(find.byKey(const ValueKey('invitation-email')), findsNothing);
    },
  );

  testWidgets(
    'pending old business mutation cannot populate another business or replay',
    (tester) async {
      final contexts = FakeContexts(
        grants(
          businesses: [
            businessContext(),
            businessContext(id: businessB, name: 'İşletme B'),
          ],
        ),
      );
      final session = FakeSession();
      final service = FakeInvitations();
      await open(tester, session, contexts, service, enter: false);
      await tap(tester, 'İşletme A · İşletme sahibi');
      await tap(tester, 'Çalışan davetleri');
      await tester.enterText(
        find.byKey(const ValueKey('invitation-email')),
        'old-business@example.test',
      );
      final pending = Completer<AuthResult<EmployeeInvitation>>();
      service.onCreate = (_, _) => pending.future;
      await tester.ensureVisible(find.text('Davet oluştur'));
      await tester.tap(find.text('Davet oluştur'));
      await tester.pump();
      await tap(tester, 'Hesap değiştir');
      await tap(tester, 'İşletme B · İşletme sahibi');
      service.onList = (business) async => AuthSuccess([
        invitation(
          email: business == businessB
              ? 'business-b@example.test'
              : 'old-business@example.test',
        ),
      ]);
      await tap(tester, 'Çalışan davetleri');
      pending.complete(
        AuthSuccess(invitation(email: 'old-business@example.test')),
      );
      await tester.pumpAndSettle();
      expect(find.text('business-b@example.test'), findsOneWidget);
      expect(find.text('old-business@example.test'), findsNothing);
      expect(service.calls.where((c) => c.startsWith('create')), [
        'create:$businessA',
      ]);
      expect(
        tester
            .widget<TextField>(find.byKey(const ValueKey('invitation-email')))
            .controller!
            .text,
        isEmpty,
      );
    },
  );

  testWidgets(
    'pending read and revoke dialog are destroyed on account change',
    (tester) async {
      final session = FakeSession();
      final service = FakeInvitations()..items = [invitation()];
      await open(
        tester,
        session,
        FakeContexts(grants(businesses: [businessContext()])),
        service,
      );
      await tap(tester, 'Daveti iptal et');
      session.changeIdentity();
      await tester.pumpAndSettle();
      expect(find.text('İptali onayla'), findsNothing);
      expect(find.text('Tekrar hoş geldiniz'), findsOneWidget);
      expect(service.calls.where((c) => c.startsWith('revoke')), isEmpty);
    },
  );

  testWidgets('invitation screen fits compact viewport with large text', (
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
      FakeInvitations()..items = [invitation()],
    );
    expect(tester.takeException(), isNull);
    await tester.ensureVisible(find.text('Çıkış yap'));
    await tester.tap(find.text('Çıkış yap'));
    await tester.pumpAndSettle();
    expect(find.text('Tekrar hoş geldiniz'), findsOneWidget);
  });
  testWidgets(
    'late list response cannot appear after switching to another business',
    (tester) async {
      final pending = Completer<AuthResult<List<EmployeeInvitation>>>();
      final service = FakeInvitations()
        ..onList = (id) async => id == businessA
            ? pending.future
            : AuthSuccess([invitation(email: 'business-b@example.test')]);
      final contexts = FakeContexts(
        grants(
          businesses: [
            businessContext(),
            businessContext(id: businessB, name: 'İşletme B'),
          ],
        ),
      );
      await open(tester, FakeSession(), contexts, service, enter: false);
      await tap(tester, 'İşletme A · İşletme sahibi');
      await tester.tap(find.text('Çalışan davetleri'));
      await tester.pump();
      await tester.pump();
      await tester.pump(const Duration(milliseconds: 500));
      expect(service.calls, ['list:$businessA']);
      expect(find.byType(CircularProgressIndicator), findsOneWidget);
      await tap(tester, 'Hesap değiştir');
      await tap(tester, 'İşletme B · İşletme sahibi');
      await tap(tester, 'Çalışan davetleri');
      pending.complete(
        AuthSuccess([invitation(email: 'business-a@example.test')]),
      );
      await tester.pumpAndSettle();
      expect(find.text('business-b@example.test'), findsOneWidget);
      expect(find.text('business-a@example.test'), findsNothing);
      expect(service.calls, ['list:$businessA', 'list:$businessB']);
    },
  );

  testWidgets(
    'old account list denial cannot close a new account invitation screen',
    (tester) async {
      final pending = Completer<AuthResult<List<EmployeeInvitation>>>();
      final service = FakeInvitations()
        ..onList = (id) async => id == businessA
            ? pending.future
            : AuthSuccess([invitation(email: 'new-account@example.test')]);
      await open(
        tester,
        FakeSession(),
        FakeContexts(grants(businesses: [businessContext()])),
        service,
        enter: false,
      );
      await tester.tap(find.text('Çalışan davetleri'));
      await tester.pump();
      await tester.pump();
      await tester.pump(const Duration(milliseconds: 500));
      final next = FakeSession();
      addTearDown(next.dispose);
      await tester.pumpWidget(
        WashFlowApp(
          authService: next,
          contextService: FakeContexts(
            grants(
              businesses: [businessContext(id: businessB, name: 'İşletme B')],
            ),
          ),
          invitationService: service,
          initialRoute: AppRouter.session,
        ),
      );
      await tester.pumpAndSettle();
      await tap(tester, 'Çalışan davetleri');
      pending.complete(AuthFailure(AuthError(code: AuthErrorCode.forbidden)));
      await tester.pumpAndSettle();
      expect(find.text('new-account@example.test'), findsOneWidget);
      expect(find.text('İşletme B'), findsOneWidget);
      expect(find.text('Tekrar hoş geldiniz'), findsNothing);
    },
  );
}
