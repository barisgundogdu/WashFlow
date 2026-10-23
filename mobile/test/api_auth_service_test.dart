import 'dart:async';
import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:washflow/app/app.dart';
import 'package:washflow/app/routing/app_router.dart';
import 'package:washflow/features/auth/data/api_auth_service.dart';
import 'package:washflow/features/auth/data/api_context_service.dart';
import 'package:washflow/features/auth/data/token_store.dart';
import 'package:washflow/features/auth/domain/auth_service.dart';
import 'package:washflow/features/auth/domain/session_service.dart';

class MemoryStore implements TokenStore {
  String? value;
  bool failWrite = false;
  @override
  Future<String?> read() async => value;
  @override
  Future<void> write(String value) async {
    if (failWrite) throw StateError('storage detail');
    this.value = value;
  }

  @override
  Future<void> clear() async => value = null;
}

final clock = DateTime.utc(2026, 10, 23);
Map<String, dynamic> tokens([String suffix = '1', bool expired = false]) => {
  'accessToken': 'access-$suffix',
  'refreshToken': 'refresh-$suffix',
  'tokenType': 'Bearer',
  'accessTokenExpiresAt': clock
      .add(Duration(minutes: expired ? -1 : 15))
      .toIso8601String(),
  'sessionExpiresAt': clock.add(const Duration(days: 30)).toIso8601String(),
};
http.Response tokenResponse([String suffix = '1', bool expired = false]) =>
    http.Response(jsonEncode(tokens(suffix, expired)), 200);

ApiAuthService api(
  MemoryStore store,
  Future<http.Response> Function(http.Request) handler, {
  Duration timeout = const Duration(seconds: 15),
}) => ApiAuthService(
  baseUrl: Uri.parse('https://api.example.test'),
  store: store,
  client: MockClient(handler),
  now: () => clock,
  timeout: timeout,
);

const login = LoginRequest(email: ' a@example.com ', password: ' p ');

void main() {
  test(
    'customer and business registration use actual contracts; no session',
    () async {
      final store = MemoryStore();
      final requests = <http.Request>[];
      final service = api(store, (r) async {
        requests.add(r);
        return http.Response('{"message":"ack"}', 202);
      });
      expect(
        await service.registerCustomer(
          const CustomerRegistrationRequest(
            fullName: ' Ayşe ',
            email: ' a@example.com ',
            password: ' p ',
          ),
        ),
        isA<AuthSuccess<CustomerRegistrationResult>>(),
      );
      expect(
        await service.registerBusiness(
          const BusinessRegistrationRequest(
            businessName: ' Wash ',
            ownerFullName: ' Ayşe ',
            email: 'a@example.com',
            password: ' p ',
          ),
        ),
        isA<AuthSuccess<BusinessRegistrationResult>>(),
      );
      expect(requests[0].url.path, '/api/v1/auth/customers/register');
      expect(jsonDecode(requests[0].body), {
        'fullName': 'Ayşe',
        'email': 'a@example.com',
        'password': ' p ',
      });
      expect(requests[1].url.path, '/api/v1/auth/businesses/register');
      expect(jsonDecode(requests[1].body), {
        'businessName': 'Wash',
        'ownerFullName': 'Ayşe',
        'email': 'a@example.com',
        'password': ' p ',
      });
      expect(store.value, isNull);
    },
  );

  test(
    'login saves pair; restart validates server session with rotated token',
    () async {
      final store = MemoryStore();
      final service = api(store, (r) async {
        expect(jsonDecode(r.body), {
          'email': 'a@example.com',
          'password': ' p ',
        });
        expect(r.followRedirects, isFalse);
        return tokenResponse();
      });
      expect(await service.login(login), isA<AuthSuccess<LoginResult>>());
      expect(jsonDecode(store.value!)['refreshToken'], 'refresh-1');
      final restarted = api(store, (r) async {
        expect(r.url.path, '/api/v1/auth/refresh');
        expect(jsonDecode(r.body), {'refreshToken': 'refresh-1'});
        expect(
          store.value,
          isNull,
        ); // predecessor removed before uncertain rotation
        return tokenResponse('2');
      });
      final result = await restarted.restoreSession();
      expect((result as AuthSuccess<bool>).value, isTrue);
      expect(jsonDecode(store.value!)['refreshToken'], 'refresh-2');
      expect(
        (await restarted.accessToken() as AuthSuccess<String>).value,
        'access-2',
      );
    },
  );

  test('expired access refreshes once for concurrent consumers', () async {
    final store = MemoryStore();
    var calls = 0;
    final service = api(store, (r) async {
      if (r.url.path.endsWith('/login')) return tokenResponse('1', true);
      calls++;
      await Future<void>.delayed(Duration.zero);
      return tokenResponse('2');
    });
    await service.login(login);
    final results = await Future.wait(
      List.generate(8, (_) => service.accessToken()),
    );
    expect(calls, 1);
    expect(
      results.every((r) => r is AuthSuccess<String> && r.value == 'access-2'),
      isTrue,
    );
  });

  test(
    'invalid/revoked, expired, corrupt and uncertain refresh fail closed',
    () async {
      for (final status in [401, 500]) {
        final store = MemoryStore()..value = jsonEncode(tokens());
        final service = api(
          store,
          (_) async => http.Response('{"detail":"SECRET"}', status),
        );
        expect(await service.restoreSession(), isA<AuthFailure<bool>>());
        expect(store.value, isNull);
        expect(await service.accessToken(), isA<AuthFailure<String>>());
      }
      final store = MemoryStore()
        ..value = jsonEncode({
          ...tokens(),
          'accessTokenExpiresAt': clock
              .subtract(const Duration(days: 2))
              .toIso8601String(),
          'sessionExpiresAt': clock
              .subtract(const Duration(days: 1))
              .toIso8601String(),
        });
      var calls = 0;
      final service = api(store, (_) async {
        calls++;
        return tokenResponse();
      });
      expect(await service.restoreSession(), isA<AuthFailure<bool>>());
      expect(calls, 0);
      store.value = 'invalid-json';
      expect(
        (await service.restoreSession() as AuthSuccess<bool>).value,
        isFalse,
      );
      expect(store.value, isNull);
      expect(
        (await service.restoreSession() as AuthSuccess<bool>).value,
        isFalse,
      );
    },
  );

  test(
    'logout uses bearer without body and clears despite network failure',
    () async {
      for (final offline in [false, true]) {
        final store = MemoryStore();
        final service = api(store, (r) async {
          if (r.url.path.endsWith('/login')) return tokenResponse();
          expect(r.url.path, '/api/v1/auth/logout');
          expect(r.headers['Authorization'], 'Bearer access-1');
          expect(r.body, isEmpty);
          if (offline) throw http.ClientException('SECRET');
          return http.Response('', 204);
        });
        await service.login(login);
        final result = await service.logout();
        expect(
          result,
          offline ? isA<AuthFailure<void>>() : isA<AuthSuccess<void>>(),
        );
        expect(store.value, isNull);
        expect(await service.accessToken(), isA<AuthFailure<String>>());
      }
    },
  );

  test('verification and resend send only backend-defined fields', () async {
    final requests = <http.Request>[];
    final service = api(MemoryStore(), (r) async {
      requests.add(r);
      return http.Response(
        '{"message":"ack"}',
        r.url.path.endsWith('resend') ? 202 : 200,
      );
    });
    expect(
      await service.verifyEmail(' secret-token '),
      isA<AuthSuccess<void>>(),
    );
    expect(
      await service.resendVerification(' a@example.com '),
      isA<AuthSuccess<void>>(),
    );
    expect(jsonDecode(requests[0].body), {'token': 'secret-token'});
    expect(requests[0].url.query, isEmpty);
    expect(jsonDecode(requests[1].body), {'email': 'a@example.com'});
  });

  test(
    'safe semantic errors for login and verification without server text',
    () async {
      for (final entry in {
        400: AuthErrorCode.validation,
        401: AuthErrorCode.invalidCredentials,
        429: AuthErrorCode.rateLimited,
        500: AuthErrorCode.unexpected,
        503: AuthErrorCode.unavailable,
        302: AuthErrorCode.unexpected,
      }.entries) {
        final service = api(
          MemoryStore(),
          (_) async => http.Response(
            '{"detail":"SECRET","errors":{"password":["SECRET"]}}',
            entry.key,
          ),
        );
        final result = await service.login(login) as AuthFailure<LoginResult>;
        expect(result.error.code, entry.value);
        expect(authErrorMessage(result.error), isNot(contains('SECRET')));
      }
      final service = api(
        MemoryStore(),
        (_) async => http.Response('{"code":"invalidVerificationToken"}', 400),
      );
      final result = await service.verifyEmail('bad') as AuthFailure<void>;
      expect(result.error.code, AuthErrorCode.invalidVerificationToken);
    },
  );

  test(
    'network, timeout, malformed token responses and storage errors are safe',
    () async {
      final store = MemoryStore();
      final network = api(
        store,
        (_) async => throw http.ClientException('SECRET'),
      );
      expect(
        (await network.login(login) as AuthFailure).error.code,
        AuthErrorCode.network,
      );
      final slow = api(
        store,
        (_) => Completer<http.Response>().future,
        timeout: const Duration(milliseconds: 5),
      );
      expect(
        (await slow.login(login) as AuthFailure).error.code,
        AuthErrorCode.timeout,
      );
      final malformed = api(
        store,
        (_) async => http.Response('{"accessToken":"SECRET"}', 200),
      );
      expect(await malformed.login(login), isA<AuthFailure<LoginResult>>());
      expect(store.value, isNull);
      store.failWrite = true;
      final broken = api(store, (_) async => tokenResponse());
      expect(
        (await broken.login(login) as AuthFailure).error.code,
        AuthErrorCode.storage,
      );
      expect(store.value, isNull);
    },
  );

  testWidgets('real adapter login success shows session then logs out', (
    tester,
  ) async {
    final store = MemoryStore();
    final service = api(
      store,
      (r) async => r.url.path.endsWith('logout')
          ? http.Response('', 204)
          : r.url.path.endsWith('contexts')
          ? http.Response('{"customerContext":null,"businessContexts":[]}', 200)
          : tokenResponse(),
    );
    await tester.pumpWidget(
      WashFlowApp(
        authService: service,
        contextService: ApiContextService(
          apiClient: service.apiClient,
          session: service,
        ),
      ),
    );
    await tester.tap(find.text('Giriş yap'));
    await tester.pumpAndSettle();
    await tester.enterText(
      find.byKey(const ValueKey('email')),
      'a@example.com',
    );
    await tester.enterText(find.byKey(const ValueKey('password')), 'p');
    await tester.ensureVisible(find.byKey(const ValueKey('submit')));
    await tester.tap(find.byKey(const ValueKey('submit')));
    await tester.pumpAndSettle();
    expect(find.text('Oturum açıldı'), findsOneWidget);
    expect(store.value, isNotNull);
    await tester.tap(find.text('Çıkış yap'));
    await tester.pumpAndSettle();
    expect(find.text('Tekrar hoş geldiniz'), findsOneWidget);
    expect(store.value, isNull);
  });

  testWidgets('backend login failure stays on form with safe Turkish message', (
    tester,
  ) async {
    final store = MemoryStore();
    final service = api(
      store,
      (_) async =>
          http.Response('{"code":"invalidCredentials","detail":"SECRET"}', 401),
    );
    await tester.pumpWidget(WashFlowApp(authService: service));
    await tester.tap(find.text('Giriş yap'));
    await tester.pumpAndSettle();
    await tester.enterText(
      find.byKey(const ValueKey('email')),
      'a@example.com',
    );
    await tester.enterText(find.byKey(const ValueKey('password')), 'wrong');
    await tester.ensureVisible(find.byKey(const ValueKey('submit')));
    await tester.tap(find.byKey(const ValueKey('submit')));
    await tester.pumpAndSettle();
    expect(
      find.text(
        'Giriş yapılamadı. Bilgilerinizi ve e-posta doğrulamanızı kontrol edin.',
      ),
      findsOneWidget,
    );
    expect(find.textContaining('SECRET'), findsNothing);
    expect(find.text('Oturum açıldı'), findsNothing);
    expect(store.value, isNull);
  });

  for (final owner in [false, true]) {
    testWidgets('registration $owner leads to verification without session', (
      tester,
    ) async {
      final store = MemoryStore();
      final paths = <String>[];
      final service = api(store, (r) async {
        paths.add(r.url.path);
        return http.Response(
          '{"message":"ack"}',
          r.url.path.endsWith('verify') ? 200 : 202,
        );
      });
      await tester.pumpWidget(WashFlowApp(authService: service));
      await tester.tap(find.text('Kayıt ol'));
      await tester.pumpAndSettle();
      await tester.tap(find.text(owner ? 'İşletme kaydı' : 'Müşteri hesabı'));
      await tester.pumpAndSettle();
      if (owner) {
        await tester.enterText(
          find.byKey(const ValueKey('businessName')),
          'Wash',
        );
      }
      await tester.enterText(find.byKey(const ValueKey('fullName')), 'Ayşe');
      await tester.enterText(
        find.byKey(const ValueKey('email')),
        'a@example.com',
      );
      await tester.enterText(find.byKey(const ValueKey('password')), 'p');
      await tester.enterText(find.byKey(const ValueKey('confirmation')), 'p');
      await tester.ensureVisible(find.byKey(const ValueKey('submit')));
      await tester.tap(find.byKey(const ValueKey('submit')));
      await tester.pumpAndSettle();
      expect(find.text('E-posta doğrulama'), findsOneWidget);
      expect(store.value, isNull);
      await tester.ensureVisible(
        find.text('Doğrulama e-postasını yeniden gönder'),
      );
      await tester.tap(find.text('Doğrulama e-postasını yeniden gönder'));
      await tester.pumpAndSettle();
      expect(
        find.text('Adres uygunsa doğrulama e-postası gönderilecektir.'),
        findsOneWidget,
      );
      await tester.enterText(find.byType(TextField).first, 'emailed-token');
      await tester.ensureVisible(find.text('E-postayı doğrula'));
      await tester.tap(find.text('E-postayı doğrula'));
      await tester.pumpAndSettle();
      expect(
        find.text('E-posta adresiniz doğrulandı. Giriş yapabilirsiniz.'),
        findsOneWidget,
      );
      expect(store.value, isNull);
      expect(
        paths,
        containsAll(['/api/v1/auth/email/verify', '/api/v1/auth/email/resend']),
      );
    });
  }
  testWidgets('restored session rotates before loading authorized home', (
    tester,
  ) async {
    final store = MemoryStore()..value = jsonEncode(tokens());
    final paths = <String>[];
    final service = api(store, (request) async {
      paths.add(request.url.path);
      if (request.url.path.endsWith('/refresh')) {
        return tokenResponse('restored');
      }
      expect(request.url.path, '/api/v1/auth/contexts');
      expect(request.headers['Authorization'], 'Bearer access-restored');
      return http.Response(
        '{"customerContext":{"customerProfileId":"11111111-1111-1111-1111-111111111111"},"businessContexts":[]}',
        200,
      );
    });
    addTearDown(() => tester.pumpWidget(const SizedBox.shrink()));
    expect((await service.restoreSession() as AuthSuccess<bool>).value, isTrue);
    await tester.pumpWidget(
      WashFlowApp(
        authService: service,
        contextService: ApiContextService(
          apiClient: service.apiClient,
          session: service,
        ),
        initialRoute: AppRouter.session,
      ),
    );
    await tester.pumpAndSettle();
    expect(paths, ['/api/v1/auth/refresh', '/api/v1/auth/contexts']);
    expect(find.text('Müşteri alanı'), findsOneWidget);
    expect(find.text('İşletme sahibi alanı'), findsNothing);
  });

  test(
    'identity revision changes on account boundaries, not token rotation',
    () async {
      final service = api(
        MemoryStore(),
        (request) async => request.url.path.endsWith('/logout')
            ? http.Response('', 204)
            : tokenResponse('fixture', request.url.path.endsWith('/login')),
      );
      final original = service.sessionRevision;
      await service.login(login);
      expect(service.sessionRevision, original + 1);
      await service.accessToken();
      expect(service.sessionRevision, original + 1);
      await service.logout();
      expect(service.sessionRevision, original + 2);
    },
  );
}
