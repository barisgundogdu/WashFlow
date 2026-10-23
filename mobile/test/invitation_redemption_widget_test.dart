import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:washflow/app/app.dart';
import 'package:washflow/app/routing/app_router.dart';
import 'package:washflow/features/auth/domain/auth_service.dart';
import 'package:washflow/features/auth/presentation/auth_form_screen.dart';
import 'package:washflow/features/auth/presentation/verification_screen.dart';

import 'support/redemption_fakes.dart';
import 'support/session_fakes.dart';

Future<void> open(
  WidgetTester tester,
  RedemptionSession session,
  FakeRedemption service,
  FakeContexts contexts, {
  String initial = AppRouter.welcome,
}) async {
  addTearDown(session.dispose);
  addTearDown(() => tester.pumpWidget(const SizedBox.shrink()));
  await tester.pumpWidget(
    WashFlowApp(
      authService: session,
      contextService: contexts,
      redemptionService: service,
      initialRoute: initial,
    ),
  );
  await tester.pumpAndSettle();
}

Future<void> tap(WidgetTester tester, String label) async {
  await tester.ensureVisible(find.text(label).last);
  await tester.tap(find.text(label).last);
  await tester.pumpAndSettle();
}

Future<void> enter(WidgetTester tester) async {
  await tap(tester, 'Davet koduyla katıl');
  final field = find.byKey(const ValueKey('invitation-credential'));
  expect(tester.widget<TextField>(field).obscureText, isTrue);
  await tester.enterText(field, redemptionFixture());
  await tap(tester, 'Davetle devam et');
}

Future<void> fillLogin(WidgetTester tester) async {
  await tester.enterText(
    find.byKey(const ValueKey('email')),
    'invited@example.test',
  );
  await tester.enterText(
    find.byKey(const ValueKey('password')),
    'fixture password',
  );
  await tester.ensureVisible(find.byKey(const ValueKey('submit')));
  await tester.tap(find.byKey(const ValueKey('submit')));
  await tester.pumpAndSettle();
}

Future<void> accept(WidgetTester tester) async {
  await tap(tester, 'Daveti kabul et');
  await tap(tester, 'Kabul etmeyi onayla');
}

void main() {
  testWidgets(
    'signed-in existing user explicitly accepts, refreshes and sees only backend Employee grant',
    (tester) async {
      final session = RedemptionSession()..signedIn = true;
      final service = FakeRedemption();
      final contexts = FakeContexts(grants());
      await open(
        tester,
        session,
        service,
        contexts,
        initial: AppRouter.session,
      );
      await enter(tester);
      expect(find.text('Daveti kabul et'), findsOneWidget);
      expect(service.acceptances, 0);
      expect(
        find.textContaining('kabul öncesinde doğrulanamaz'),
        findsOneWidget,
      );
      await tap(tester, 'Daveti kabul et');
      await tap(tester, 'Vazgeç');
      expect(service.acceptances, 0);
      contexts.value = grants(
        businesses: [
          businessContext(roles: ['Employee']),
        ],
      );
      await accept(tester);
      expect(service.acceptances, 1);
      expect(find.text('Daveti kabul et'), findsNothing);
      expect(find.textContaining('Davet kabul edildi.'), findsOneWidget);
      await tap(tester, 'Hesabıma devam et');
      expect(find.text('Çalışan alanı'), findsOneWidget);
      expect(find.text('İşletme sahibi alanı'), findsNothing);
      expect(
        contexts.calls,
        3,
      ); // Initial shell, post-accept read, fresh shell entry.
    },
  );

  testWidgets(
    'existing user login belongs to flow, not a premature authenticated destination',
    (tester) async {
      final session = RedemptionSession();
      final service = FakeRedemption();
      await open(tester, session, service, FakeContexts(grants()));
      await enter(tester);
      await tap(tester, 'Mevcut hesabımla giriş yap');
      await fillLogin(tester);
      expect(session.logins, 1);
      expect(find.text('Daveti kabul et'), findsOneWidget);
      expect(find.text('Müşteri alanı'), findsNothing);
      expect(service.acceptances, 0);
      await accept(tester);
      expect(service.acceptances, 1);
    },
  );

  testWidgets(
    'new user uses invitation registration, shared verification, login and explicit acceptance',
    (tester) async {
      final session = RedemptionSession();
      final service = FakeRedemption();
      final contexts = FakeContexts(grants());
      await open(tester, session, service, contexts);
      await enter(tester);
      await tap(tester, 'Davet için hesap oluştur');
      expect(find.text('Davetli çalışan kaydı'), findsOneWidget);
      await tester.enterText(
        find.byKey(const ValueKey('fullName')),
        'Test Employee',
      );
      await tester.enterText(
        find.byKey(const ValueKey('email')),
        'invited@example.test',
      );
      await tester.enterText(
        find.byKey(const ValueKey('password')),
        'fixture password',
      );
      await tester.enterText(
        find.byKey(const ValueKey('confirmation')),
        'fixture password',
      );
      await tester.ensureVisible(find.byKey(const ValueKey('submit')));
      await tester.tap(find.byKey(const ValueKey('submit')));
      await tester.pumpAndSettle();
      expect(service.registrations, 1);
      expect(session.customerRegistrations + session.businessRegistrations, 0);
      expect(find.byType(VerificationScreen), findsOneWidget);
      expect(service.acceptances, 0);
      await tester.enterText(
        find.byType(TextField).first,
        'verification-fixture',
      );
      await tap(tester, 'E-postayı doğrula');
      expect(session.verifications, 1);
      expect(service.acceptances, 0);
      await tap(tester, 'Girişe dön');
      expect(find.byType(AuthFormScreen), findsOneWidget);
      await fillLogin(tester);
      expect(service.acceptances, 0);
      await accept(tester);
      expect(service.acceptances, 1);
      expect(contexts.calls, 1);
    },
  );

  testWidgets(
    'login verification link remains within active flow and preserves pending invitation',
    (tester) async {
      final session = RedemptionSession();
      final service = FakeRedemption();
      await open(tester, session, service, FakeContexts(grants()));
      await enter(tester);
      await tap(tester, 'Mevcut hesabımla giriş yap');
      await tap(tester, 'E-posta doğrula / yeniden gönder');
      expect(find.byType(VerificationScreen), findsOneWidget);
      await tap(tester, 'Girişe dön');
      await fillLogin(tester);
      await accept(tester);
      expect(service.acceptances, 1);
    },
  );

  testWidgets(
    'cancellation clears memory and fresh entry never restores the credential',
    (tester) async {
      final session = RedemptionSession();
      final service = FakeRedemption();
      await open(tester, session, service, FakeContexts(grants()));
      await enter(tester);
      await tap(tester, 'Davet akışını iptal et');
      await tap(tester, 'Davet koduyla katıl');
      expect(
        tester
            .widget<TextField>(
              find.byKey(const ValueKey('invitation-credential')),
            )
            .controller!
            .text
            .isEmpty,
        isTrue,
      );
      expect(service.registrations + service.acceptances, 0);
    },
  );

  testWidgets(
    'wrong-account invalid response is never claimed as acceptance; logout discards invitation',
    (tester) async {
      final session = RedemptionSession()..signedIn = true;
      final service = FakeRedemption()
        ..onAccept = () async =>
            AuthFailure(AuthError(code: AuthErrorCode.invalidInvitation));
      await open(tester, session, service, FakeContexts(grants()));
      await enter(tester);
      await accept(tester);
      expect(find.textContaining('Davet kabul edilemedi.'), findsOneWidget);
      expect(
        find.text(
          'Kabul, sunucu tarafından doğrulandı. Erişimler yalnızca sunucudan alınır.',
        ),
        findsNothing,
      );
      await tap(tester, 'Farklı hesap için çıkış yap');
      expect(session.logouts, 1);
      expect(find.text('Tekrar hoş geldiniz'), findsOneWidget);
      expect(find.text('Daveti kabul et'), findsNothing);
    },
  );

  testWidgets(
    'uncertain acceptance does not replay and context refresh does not prove acceptance',
    (tester) async {
      final session = RedemptionSession()..signedIn = true;
      final service = FakeRedemption()
        ..onAccept = () async =>
            AuthFailure(AuthError(code: AuthErrorCode.timeout));
      await open(tester, session, service, FakeContexts(grants()));
      await enter(tester);
      await accept(tester);
      expect(service.acceptances, 1);
      expect(find.textContaining('Kabul sonucu belirsiz'), findsOneWidget);
      await tap(tester, 'Hesap erişimlerini yenile');
      expect(service.acceptances, 1);
      expect(
        find.text(
          'Mevcut hesap erişimleri yenilendi. Bu davetin kabul edildiği doğrulanmadı.',
        ),
        findsOneWidget,
      );
      await tap(tester, 'Kabul isteğini bilerek yeniden dene');
      await tap(tester, 'Vazgeç');
      expect(service.acceptances, 1);
      service.onAccept = null;
      await tap(tester, 'Kabul isteğini bilerek yeniden dene');
      await tap(tester, 'Kabul etmeyi onayla');
      expect(service.acceptances, 2);
    },
  );

  testWidgets(
    'cancelling an in-flight acceptance ignores late success and cannot show accepted state',
    (tester) async {
      final session = RedemptionSession()..signedIn = true;
      final service = FakeRedemption();
      final contexts = FakeContexts(grants());
      final pending = Completer<AuthResult<void>>();
      service.onAccept = () => pending.future;
      await open(tester, session, service, contexts);
      await enter(tester);
      await tap(tester, 'Daveti kabul et');
      await tester.tap(find.text('Kabul etmeyi onayla'));
      await tester.pump();
      await tap(tester, 'Davet akışını iptal et');
      pending.complete(const AuthSuccess(null));
      await tester.pumpAndSettle();
      expect(service.acceptances, 1);
      expect(find.textContaining('Davet kabul edildi'), findsNothing);
      expect(
        find.textContaining('Kullanılabilir hesap bağlamı bulunamadı.'),
        findsOneWidget,
      );
    },
  );

  testWidgets(
    'unrelated account change dismisses pending confirmation and clears credential',
    (tester) async {
      final session = RedemptionSession()..signedIn = true;
      final service = FakeRedemption();
      await open(tester, session, service, FakeContexts(grants()));
      await enter(tester);
      await tap(tester, 'Daveti kabul et');
      session.changeIdentity();
      await tester.pumpAndSettle();
      expect(find.text('Kabul etmeyi onayla'), findsNothing);
      expect(find.text('Daveti kabul et'), findsNothing);
      expect(service.acceptances, 0);
      expect(find.textContaining('Davet bilgisi temizlendi'), findsOneWidget);
    },
  );

  testWidgets(
    'context failure after confirmed acceptance allows only context retry, no duplicate accept',
    (tester) async {
      final session = RedemptionSession()..signedIn = true;
      final service = FakeRedemption();
      final contexts = FakeContexts(grants())
        ..handler = () async =>
            AuthFailure(AuthError(code: AuthErrorCode.network));
      await open(tester, session, service, contexts);
      await enter(tester);
      await accept(tester);
      expect(find.textContaining('Davet kabul edildi;'), findsOneWidget);
      expect(find.text('Daveti kabul et'), findsNothing);
      contexts.handler = null;
      await tap(tester, 'Hesap erişimlerini yenile');
      expect(service.acceptances, 1);
      await tap(tester, 'Hesabıma devam et');
      expect(
        find.textContaining('Kullanılabilir hesap bağlamı bulunamadı.'),
        findsOneWidget,
      );
      expect(
        find.text('Çalışan alanı'),
        findsNothing,
      ); // No locally invented grant.
    },
  );

  testWidgets(
    'small screen/large text registration and confirmation have no overflow',
    (tester) async {
      tester.view.physicalSize = const Size(320, 640);
      tester.view.devicePixelRatio = 1;
      tester.platformDispatcher.textScaleFactorTestValue = 1.6;
      tester.view.viewInsets = const FakeViewPadding(bottom: 250);
      addTearDown(tester.view.resetViewInsets);
      addTearDown(tester.view.resetPhysicalSize);
      addTearDown(tester.view.resetDevicePixelRatio);
      addTearDown(tester.platformDispatcher.clearTextScaleFactorTestValue);
      await open(
        tester,
        RedemptionSession(),
        FakeRedemption(),
        FakeContexts(grants()),
      );
      await enter(tester);
      await tap(tester, 'Davet için hesap oluştur');
      expect(tester.takeException(), isNull);
      await tap(tester, 'Davet akışını iptal et');
      expect(tester.takeException(), isNull);
    },
  );
  testWidgets(
    'late verification response after cancellation cannot restore the invitation flow',
    (tester) async {
      final session = RedemptionSession();
      final service = FakeRedemption();
      final pending = Completer<AuthResult<void>>();
      session.onVerify = () => pending.future;
      await open(tester, session, service, FakeContexts(grants()));
      await enter(tester);
      await tap(tester, 'Mevcut hesabımla giriş yap');
      await tap(tester, 'E-posta doğrula / yeniden gönder');
      await tester.enterText(
        find.byType(TextField).first,
        'verification-fixture',
      );
      await tester.ensureVisible(find.text('E-postayı doğrula'));
      await tester.tap(find.text('E-postayı doğrula'));
      await tester.pump();
      await tap(tester, 'Davet akışını iptal et');
      pending.complete(const AuthSuccess(null));
      await tester.pumpAndSettle();
      expect(find.byType(VerificationScreen), findsNothing);
      expect(service.acceptances, 0);
      await tap(tester, 'Davet koduyla katıl');
      expect(
        tester
            .widget<TextField>(
              find.byKey(const ValueKey('invitation-credential')),
            )
            .controller!
            .text
            .isEmpty,
        isTrue,
      );
    },
  );
  testWidgets(
    'offline flow logout reports unconfirmed server logout without retaining invitation',
    (tester) async {
      final session = RedemptionSession()..signedIn = true;
      session.logoutResult = AuthFailure(
        AuthError(code: AuthErrorCode.network),
      );
      await open(tester, session, FakeRedemption(), FakeContexts(grants()));
      await enter(tester);
      await tap(tester, 'Farklı hesap için çıkış yap');
      expect(find.text('Tekrar hoş geldiniz'), findsOneWidget);
      expect(
        find.text('Yerel oturum kapatıldı. Sunucuda çıkış doğrulanamadı.'),
        findsOneWidget,
      );
    },
  );

  testWidgets(
    'rapid in-flow login is not interrupted by the departing shell identity listener',
    (tester) async {
      final session = RedemptionSession();
      final service = FakeRedemption();
      await open(
        tester,
        session,
        service,
        FakeContexts(grants()),
        initial: AppRouter.session,
      );
      tester
          .widget<TextButton>(
            find.widgetWithText(TextButton, 'Davet koduyla katıl'),
          )
          .onPressed!();
      await tester.pump();
      await tester.pump(
        const Duration(milliseconds: 1),
      ); // Build the new route without finishing its transition.
      await tester.enterText(
        find.byKey(const ValueKey('invitation-credential')),
        redemptionFixture(),
      );
      tester
          .widget<FilledButton>(
            find.widgetWithText(FilledButton, 'Davetle devam et'),
          )
          .onPressed!();
      await tester.pump();
      await tester.pump();
      tester
          .widget<FilledButton>(
            find.widgetWithText(FilledButton, 'Mevcut hesabımla giriş yap'),
          )
          .onPressed!();
      await tester.pump();
      await tester.enterText(
        find.byKey(const ValueKey('email')),
        'invited@example.test',
      );
      await tester.enterText(
        find.byKey(const ValueKey('password')),
        'fixture password',
      );
      tester
          .widget<FilledButton>(find.byKey(const ValueKey('submit')))
          .onPressed!();
      await tester.pumpAndSettle();
      expect(find.text('Daveti kabul et'), findsOneWidget);
      expect(find.text('Tekrar hoş geldiniz'), findsNothing);
      expect(service.acceptances, 0);
    },
  );
}
