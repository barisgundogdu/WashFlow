import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:washflow/features/auth/data/api_auth_service.dart';
import 'package:washflow/features/auth/data/token_store.dart';
import 'package:washflow/features/auth/domain/auth_service.dart';
import 'package:washflow/features/auth/presentation/verification_screen.dart';

class _Store implements TokenStore {
  @override
  Future<String?> read() async => null;
  @override
  Future<void> write(String value) async {}
  @override
  Future<void> clear() async {}
}

ApiAuthService _service(
  Future<http.Response> Function(http.Request) handler, {
  Duration timeout = const Duration(seconds: 15),
}) => ApiAuthService(
  baseUrl: Uri.parse('https://example.test'),
  store: _Store(),
  client: MockClient(handler),
  timeout: timeout,
);

const _success = 'E-posta adresiniz doğrulandı. Giriş yapabilirsiniz.';
const _invalid =
    'Doğrulama bilgisi geçersiz veya süresi dolmuş. Yeni e-posta isteyin.';

Future<void> _open(WidgetTester tester, ApiAuthService service) async {
  await tester.pumpWidget(
    MaterialApp(
      home: VerificationScreen(service: service, email: 'local@example.test'),
    ),
  );
  await tester.enterText(find.byType(TextField).first, 'one-use-token');
  await tester.ensureVisible(find.text('E-postayı doğrula'));
}

void main() {
  testWidgets('rapid callbacks before rebuild send one request while pending', (
    tester,
  ) async {
    final response = Completer<http.Response>();
    var calls = 0;
    await _open(
      tester,
      _service((r) {
        calls++;
        return response.future;
      }),
    );
    final verify = tester
        .widget<FilledButton>(find.byType(FilledButton))
        .onPressed!;
    final resend = tester
        .widget<TextButton>(
          find.widgetWithText(
            TextButton,
            'Doğrulama e-postasını yeniden gönder',
          ),
        )
        .onPressed!;
    verify();
    verify();
    resend();
    await tester.pump();
    expect(calls, 1);
    expect(
      tester.widget<FilledButton>(find.byType(FilledButton)).onPressed,
      isNull,
    );
    response.complete(http.Response('{"message":"ok"}', 200));
    await tester.pumpAndSettle();
    expect(find.text(_success), findsOneWidget);
    expect(find.text(_invalid), findsNothing);
    expect(calls, 1);
  });

  testWidgets(
    'success stays terminal and cannot be overwritten by a later replay',
    (tester) async {
      var calls = 0;
      await _open(
        tester,
        _service((r) async {
          calls++;
          return calls == 1
              ? http.Response('{"message":"ok"}', 200)
              : http.Response('{"code":"invalidVerificationToken"}', 400);
        }),
      );
      // Keep callbacks from before the rebuild to exercise the synchronous guard too.
      final verify = tester
          .widget<FilledButton>(find.byType(FilledButton))
          .onPressed!;
      final resend = tester
          .widget<TextButton>(
            find.widgetWithText(
              TextButton,
              'Doğrulama e-postasını yeniden gönder',
            ),
          )
          .onPressed!;
      verify();
      await tester.pumpAndSettle();
      expect(find.text(_success), findsOneWidget);
      // Simulate re-entering the consumed token plus stale callbacks. After the fix
      // controls are disabled; the handler must also reject such callbacks.
      tester.widget<TextField>(find.byType(TextField).first).controller!.text =
          'one-use-token';
      verify();
      resend();
      await tester.pumpAndSettle();
      expect(find.text(_success), findsOneWidget);
      expect(find.text(_invalid), findsNothing);
      expect(
        tester.widget<FilledButton>(find.byType(FilledButton)).onPressed,
        isNull,
      );
      expect(
        tester
            .widget<TextButton>(
              find.widgetWithText(
                TextButton,
                'Doğrulama e-postasını yeniden gönder',
              ),
            )
            .onPressed,
        isNull,
      );
      expect(calls, 1);
    },
  );

  testWidgets(
    'actual invalid-token response remains a failure, never success',
    (tester) async {
      var calls = 0;
      await _open(
        tester,
        _service((r) async {
          calls++;
          return http.Response('{"code":"invalidVerificationToken"}', 400);
        }),
      );
      await tester.tap(find.text('E-postayı doğrula'));
      await tester.pumpAndSettle();
      expect(find.text(_invalid), findsOneWidget);
      expect(find.text(_success), findsNothing);
      expect(calls, 1);
      expect(
        tester.widget<FilledButton>(find.byType(FilledButton)).onPressed,
        isNotNull,
      );
    },
  );

  test(
    '200 parse failure is unexpected, never invalidVerificationToken; no retry',
    () async {
      var calls = 0;
      final service = _service((r) async {
        calls++;
        return http.Response('not-json', 200);
      });
      final result = await service.verifyEmail('token') as AuthFailure<void>;
      expect(result.error.code, AuthErrorCode.unexpected);
      expect(calls, 1);
    },
  );

  testWidgets(
    'late server success after timeout does not retry or claim invalid token',
    (tester) async {
      final response = Completer<http.Response>();
      var calls = 0;
      await _open(
        tester,
        _service((r) {
          calls++;
          return response.future;
        }, timeout: const Duration(milliseconds: 50)),
      );
      await tester.tap(find.text('E-postayı doğrula'));
      await tester.pump();
      await tester.pump(const Duration(milliseconds: 60));
      await tester.pumpAndSettle();
      expect(find.text(_invalid), findsNothing);
      expect(find.text(_success), findsNothing);
      expect(find.textContaining('Önce giriş yapmayı deneyin'), findsOneWidget);
      response.complete(http.Response('{"message":"ok"}', 200));
      await tester.pumpAndSettle();
      expect(calls, 1);
      expect(find.text(_success), findsNothing);
      expect(find.text(_invalid), findsNothing);
    },
  );
}
