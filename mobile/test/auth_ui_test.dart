import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:washflow/app/app.dart';
import 'package:washflow/features/auth/domain/auth_submission.dart';
import 'package:washflow/features/auth/presentation/auth_form_screen.dart';
import 'package:washflow/features/auth/presentation/widgets/authorized_context_picker.dart';

class PendingSubmission implements AuthSubmission {
  final completer = Completer<void>();
  int calls = 0;
  @override
  Future<void> submit(AuthFormInput input) {
    calls++;
    return completer.future;
  }
}

Future<void> tapVisible(WidgetTester tester, Finder finder) async {
  await tester.ensureVisible(finder);
  await tester.tap(finder);
  await tester.pumpAndSettle();
}

Future<void> fillLogin(WidgetTester tester) async {
  await tester.enterText(
    find.byKey(const ValueKey('email')),
    'ayse@example.com',
  );
  await tester.enterText(find.byKey(const ValueKey('password')), 'deneme');
}

void main() {
  testWidgets('Welcome links, registration choices and return to login work', (
    tester,
  ) async {
    await tester.pumpWidget(const WashFlowApp());
    await tapVisible(tester, find.text('Kayıt ol'));
    await tapVisible(tester, find.text('Müşteri hesabı'));
    expect(find.text('Müşteri kaydı'), findsOneWidget);
    expect(find.byKey(const ValueKey('businessName')), findsNothing);
    await tapVisible(tester, find.text('Zaten hesabınız var mı? Giriş yap'));
    expect(find.text('Tekrar hoş geldiniz'), findsOneWidget);
    await tapVisible(tester, find.text('Hesabınız yok mu? Kayıt ol'));
    await tapVisible(tester, find.text('İşletme kaydı'));
    expect(find.byKey(const ValueKey('businessName')), findsOneWidget);
    expect(find.text('İşletme sahibinin adı soyadı'), findsOneWidget);
  });

  testWidgets(
    'Login validates, toggles visibility and reports unavailable service',
    (tester) async {
      await tester.pumpWidget(const WashFlowApp());
      await tapVisible(tester, find.text('Giriş yap'));
      await tapVisible(tester, find.byKey(const ValueKey('submit')));
      expect(find.text('E-posta adresinizi girin.'), findsOneWidget);
      expect(find.text('Şifrenizi girin.'), findsOneWidget);
      await tester.enterText(find.byKey(const ValueKey('email')), 'invalid');
      await tester.pump();
      expect(find.text('Geçerli bir e-posta adresi girin.'), findsOneWidget);
      await fillLogin(tester);
      expect(
        tester
            .widget<EditableText>(
              find.descendant(
                of: find.byKey(const ValueKey('password')),
                matching: find.byType(EditableText),
              ),
            )
            .obscureText,
        isTrue,
      );
      await tapVisible(tester, find.byTooltip('Şifreyi göster'));
      expect(
        tester
            .widget<EditableText>(
              find.descendant(
                of: find.byKey(const ValueKey('password')),
                matching: find.byType(EditableText),
              ),
            )
            .obscureText,
        isFalse,
      );
      await tapVisible(tester, find.byKey(const ValueKey('submit')));
      expect(
        find.text(
          'Giriş hizmeti henüz kullanıma açık değil. Şu anda giriş yapılamıyor.',
        ),
        findsOneWidget,
      );
      expect(find.byType(AuthFormScreen), findsOneWidget);
      await tapVisible(tester, find.text('Şifremi unuttum'));
      expect(
        find.text('Şifre yenileme hizmeti henüz kullanıma açık değil.'),
        findsOneWidget,
      );
    },
  );

  for (final kind in [
    AuthFormKind.customerRegistration,
    AuthFormKind.businessRegistration,
  ]) {
    testWidgets(
      '$kind validates required fields, confirmation and never creates an account',
      (tester) async {
        await tester.pumpWidget(MaterialApp(home: AuthFormScreen(kind: kind)));
        await tapVisible(tester, find.byKey(const ValueKey('submit')));
        expect(find.text('Ad soyad alanını doldurun.'), findsOneWidget);
        if (kind == AuthFormKind.businessRegistration) {
          expect(find.text('İşletme adı alanını doldurun.'), findsOneWidget);
          await tester.enterText(
            find.byKey(const ValueKey('businessName')),
            'Örnek Yıkama',
          );
        }
        await tester.enterText(
          find.byKey(const ValueKey('fullName')),
          'Ayşe Yılmaz',
        );
        await fillLogin(tester);
        await tester.enterText(
          find.byKey(const ValueKey('confirmation')),
          'farkli',
        );
        await tester.pump();
        expect(find.text('Şifreler eşleşmiyor.'), findsOneWidget);
        await tester.enterText(
          find.byKey(const ValueKey('confirmation')),
          'deneme',
        );
        await tapVisible(tester, find.byKey(const ValueKey('submit')));
        expect(
          find.text(
            'Kayıt hizmeti henüz kullanıma açık değil. Hesabınız oluşturulmadı.',
          ),
          findsOneWidget,
        );
        expect(find.byType(AuthFormScreen), findsOneWidget);
      },
    );
  }

  testWidgets(
    'Pending submission prevents duplicates and recovers after failure',
    (tester) async {
      final submission = PendingSubmission();
      await tester.pumpWidget(
        MaterialApp(
          home: AuthFormScreen(
            kind: AuthFormKind.login,
            submission: submission,
          ),
        ),
      );
      await fillLogin(tester);
      await tester.ensureVisible(find.byKey(const ValueKey('submit')));
      await tester.tap(find.byKey(const ValueKey('submit')));
      await tester.pump();
      expect(find.text('İşlem sürüyor…'), findsOneWidget);
      expect(
        tester
            .widget<FilledButton>(find.byKey(const ValueKey('submit')))
            .onPressed,
        isNull,
      );
      expect(
        tester
            .widget<TextFormField>(find.byKey(const ValueKey('email')))
            .enabled,
        isFalse,
      );
      expect(submission.calls, 1);
      submission.completer.completeError(StateError('internal details'));
      await tester.pumpAndSettle();
      expect(
        find.text('İşlem tamamlanamadı. Lütfen tekrar deneyin.'),
        findsOneWidget,
      );
      expect(find.textContaining('internal details'), findsNothing);
      expect(
        tester
            .widget<FilledButton>(find.byKey(const ValueKey('submit')))
            .onPressed,
        isNotNull,
      );
    },
  );

  testWidgets(
    'Registration fits compact screen, large text and visible keyboard',
    (tester) async {
      tester.view.physicalSize = const Size(375, 667);
      tester.view.devicePixelRatio = 1;
      tester.view.viewInsets = const FakeViewPadding(bottom: 280);
      tester.platformDispatcher.textScaleFactorTestValue = 1.6;
      addTearDown(tester.view.resetPhysicalSize);
      addTearDown(tester.view.resetDevicePixelRatio);
      addTearDown(tester.view.resetViewInsets);
      addTearDown(tester.platformDispatcher.clearTextScaleFactorTestValue);
      await tester.pumpWidget(
        const MaterialApp(
          home: AuthFormScreen(kind: AuthFormKind.businessRegistration),
        ),
      );
      await tester.enterText(
        find.byKey(const ValueKey('confirmation')),
        'deneme',
      );
      await tapVisible(tester, find.byKey(const ValueKey('submit')));
      expect(tester.takeException(), isNull);
    },
  );

  testWidgets('Context picker only emits supplied authorized option', (
    tester,
  ) async {
    String? selected;
    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(
          body: AuthorizedContextPicker(
            options: const [
              AuthorizedContextOption(
                id: 'membership-1',
                label: 'Örnek işletme · Çalışan',
              ),
            ],
            onSelected: (value) => selected = value,
          ),
        ),
      ),
    );
    await tester.tap(find.text('Örnek işletme · Çalışan'));
    expect(selected, 'membership-1');
    expect(find.text('İşletme sahibi'), findsNothing);
  });
}
