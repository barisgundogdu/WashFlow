import 'dart:async';
import 'dart:convert';

import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:washflow/core/network/api_client.dart';
import 'package:washflow/features/auth/data/api_auth_service.dart';
import 'package:washflow/features/auth/data/api_context_service.dart';
import 'package:washflow/features/auth/domain/auth_service.dart';
import 'package:washflow/features/auth/domain/session_service.dart';
import 'package:washflow/features/auth/domain/user_contexts.dart';

import 'api_auth_service_test.dart' as fixtures;

const profile = '11111111-1111-1111-1111-111111111111';
const business = '22222222-2222-2222-2222-222222222222';
const membership = '33333333-3333-3333-3333-333333333333';
Map<String, dynamic> businessJson() => {
  'businessId': business,
  'membershipId': membership,
  'businessName': 'Test işletmesi',
  'roles': ['Owner', 'Employee'],
};
Map<String, dynamic> contextsJson() => {
  'customerContext': {'customerProfileId': profile},
  'businessContexts': [businessJson()],
};

class Session implements AccessTokenProvider {
  AuthResult<String> result = const AuthSuccess('fixture');
  int calls = 0;
  @override
  Future<AuthResult<String>> accessToken() async {
    calls++;
    return result;
  }
}

void main() {
  test('typed immutable contexts preserve independent customer and business grants', () {
    final json = contextsJson();
    final result = UserContexts.fromJson(json);
    expect(result.customerContext!.customerProfileId, profile);
    expect(result.businessContexts.single.businessId, business);
    expect(result.businessContexts.single.membershipId, membership);
    expect(result.businessContexts.single.roles, {
      BusinessRole.owner,
      BusinessRole.employee,
    });
    expect(() => result.businessContexts.clear(), throwsUnsupportedError);
    expect(
      () => result.businessContexts.single.roles.clear(),
      throwsUnsupportedError,
    );
    (json['businessContexts'] as List).clear();
    expect(result.businessContexts, hasLength(1));
    final empty = UserContexts.fromJson({
      'customerContext': null,
      'businessContexts': [],
    });
    expect(empty.customerContext, isNull);
    expect(empty.businessContexts, isEmpty);
  });

  test('multiple businesses, both roles and unknown roles never imply extra access', () {
    final result = UserContexts.fromJson({
      'customerContext': null,
      'businessContexts': [
        {
          ...businessJson(),
          'roles': ['Owner', 'Owner', 'Admin', 'owner'],
        },
        {
          ...businessJson(),
          'businessId': profile,
          'membershipId': '44444444-4444-4444-4444-444444444444',
          'roles': ['Employee'],
        },
      ],
    });
    expect(result.businessContexts[0].roles, {BusinessRole.owner});
    expect(result.businessContexts[1].roles, {BusinessRole.employee});
    expect(
      BusinessContext.fromJson({
        ...businessJson(),
        'roles': ['Admin'],
      }).roles,
      isEmpty,
    );
  });

  test(
    'malformed shapes, IDs, roles and duplicate memberships fail closed',
    () {
      for (final json in <Map<String, dynamic>>[
        {},
        {'businessContexts': []},
        {'customerContext': {}, 'businessContexts': []},
        {'customerContext': null, 'businessContexts': 'bad'},
        {
          'customerContext': {
            'customerProfileId': '00000000-0000-0000-0000-000000000000',
          },
          'businessContexts': [],
        },
        {
          'customerContext': null,
          'businessContexts': [null],
        },
        {
          'customerContext': null,
          'businessContexts': [businessJson(), businessJson()],
        },
        for (final patch in [
          {'businessId': 'bad'},
          {'membershipId': null},
          {'roles': 'Owner'},
          {
            'roles': [1],
          },
          {'businessName': ' '},
        ])
          {
            'customerContext': null,
            'businessContexts': [
              {...businessJson(), ...patch},
            ],
          },
      ]) {
        expect(() => UserContexts.fromJson(json), throwsFormatException);
      }
    },
  );

  test('adapter obtains current bearer per call; no user/tenant/role hints or cache', () async {
    final session = Session();
    var calls = 0;
    final api = ApiClient(
      baseUrl: Uri.parse('https://example.test/prefix'),
      client: MockClient((request) async {
        calls++;
        expect(request.method, 'GET');
        expect(request.url.path, '/prefix/api/v1/auth/contexts');
        expect(request.url.query, isEmpty);
        expect(request.body, isEmpty);
        expect(request.headers['Authorization'], 'Bearer fixture');
        expect(
          request.headers.keys.any((k) => k.toLowerCase().startsWith('x-')),
          isFalse,
        );
        return http.Response(
          jsonEncode(
            calls == 1
                ? contextsJson()
                : {'customerContext': null, 'businessContexts': []},
          ),
          200,
          headers: {'content-type': 'application/json; charset=utf-8'},
        );
      }),
    );
    final service = ApiContextService(apiClient: api, session: session);
    expect(
      (await service.getContexts() as AuthSuccess<UserContexts>)
          .value
          .businessContexts,
      hasLength(1),
    );
    expect(
      (await service.getContexts() as AuthSuccess<UserContexts>)
          .value
          .businessContexts,
      isEmpty,
    );
    expect(session.calls, 2);
  });

  test('session failure prevents request and preserves storage/refresh failure codes', () async {
    final session = Session();
    var calls = 0;
    final service = ApiContextService(
      session: session,
      apiClient: ApiClient(
        baseUrl: Uri.parse('https://example.test'),
        client: MockClient((_) async {
          calls++;
          return http.Response('{}', 200);
        }),
      ),
    );
    for (final code in [
      AuthErrorCode.invalidCredentials,
      AuthErrorCode.storage,
      AuthErrorCode.network,
      AuthErrorCode.timeout,
    ]) {
      session.result = AuthFailure(AuthError(code: code));
      final result = await service.getContexts() as AuthFailure<UserContexts>;
      expect(
        result.error.code,
        code == AuthErrorCode.invalidCredentials
            ? AuthErrorCode.unauthenticated
            : code,
      );
    }
    session.result = const AuthSuccess('');
    expect(
      (await service.getContexts() as AuthFailure<UserContexts>).error.code,
      AuthErrorCode.unauthenticated,
    );
    expect(calls, 0);
  });

  test(
    'authorization failures and malformed responses are safe and never retried',
    () async {
      for (final entry in {
        401: AuthErrorCode.unauthenticated,
        403: AuthErrorCode.forbidden,
        429: AuthErrorCode.rateLimited,
        503: AuthErrorCode.unavailable,
      }.entries) {
        var calls = 0;
        final service = ApiContextService(
          session: Session(),
          apiClient: ApiClient(
            baseUrl: Uri.parse('https://example.test'),
            client: MockClient((_) async {
              calls++;
              return http.Response('{"detail":"private detail"}', entry.key);
            }),
          ),
        );
        final result = await service.getContexts() as AuthFailure<UserContexts>;
        expect(result.error.code, entry.value);
        expect(calls, 1);
        expect(
          authErrorMessage(result.error),
          isNot(contains('private detail')),
        );
      }
      for (final body in [
        '[]',
        '{}',
        'not-json',
        '{"customerContext":null,"businessContexts":[{}]}',
      ]) {
        final service = ApiContextService(
          session: Session(),
          apiClient: ApiClient(
            baseUrl: Uri.parse('https://example.test'),
            client: MockClient((_) async => http.Response(body, 200)),
          ),
        );
        expect(
          (await service.getContexts() as AuthFailure<UserContexts>).error.code,
          AuthErrorCode.unexpected,
        );
      }
    },
  );

  test(
    'real authenticated boundary rotates once for concurrent context consumers',
    () async {
      final store = fixtures.MemoryStore();
      var rotations = 0;
      var reads = 0;
      final auth = ApiAuthService(
        baseUrl: Uri.parse('https://example.test'),
        store: store,
        now: () => fixtures.clock,
        client: MockClient((request) async {
          if (request.url.path.endsWith('/login')) {
            return fixtures.tokenResponse('1', true);
          }
          if (request.url.path.endsWith('/refresh')) {
            rotations++;
            expect(store.value, isNull);
            return fixtures.tokenResponse('2');
          }
          reads++;
          expect(request.headers['Authorization'], 'Bearer access-2');
          return http.Response(
            jsonEncode(contextsJson()),
            200,
            headers: {'content-type': 'application/json; charset=utf-8'},
          );
        }),
      );
      await auth.login(fixtures.login);
      final service = ApiContextService(
        apiClient: auth.apiClient,
        session: auth,
      );
      final results = await Future.wait(
        List.generate(8, (_) => service.getContexts()),
      );
      expect(results.every((r) => r is AuthSuccess<UserContexts>), isTrue);
      expect(rotations, 1);
      expect(reads, 8);
    },
  );

  test(
    'context 401 does not replay or clear/rotate a potentially newer session',
    () async {
      var rotations = 0;
      var reads = 0;
      final store = fixtures.MemoryStore();
      final auth = ApiAuthService(
        baseUrl: Uri.parse('https://example.test'),
        store: store,
        now: () => fixtures.clock,
        client: MockClient((request) async {
          if (request.url.path.endsWith('/login')) {
            return fixtures.tokenResponse();
          }
          if (request.url.path.endsWith('/refresh')) {
            rotations++;
            return fixtures.tokenResponse('2');
          }
          reads++;
          return http.Response('{}', 401);
        }),
      );
      await auth.login(fixtures.login);
      final before = store.value;
      final service = ApiContextService(
        apiClient: auth.apiClient,
        session: auth,
      );
      expect(
        (await service.getContexts() as AuthFailure<UserContexts>).error.code,
        AuthErrorCode.unauthenticated,
      );
      expect(rotations, 0);
      expect(reads, 1);
      expect(store.value, before);
    },
  );
  test('late context 401 cannot clear a subsequent login', () async {
    final pending = Completer<http.Response>();
    final started = Completer<void>();
    var logins = 0;
    final store = fixtures.MemoryStore();
    final auth = ApiAuthService(
      baseUrl: Uri.parse('https://example.test'),
      store: store,
      now: () => fixtures.clock,
      client: MockClient((request) async {
        if (request.url.path.endsWith('/login')) {
          logins++;
          return fixtures.tokenResponse('$logins');
        }
        started.complete();
        return pending.future;
      }),
    );
    await auth.login(fixtures.login);
    final service = ApiContextService(apiClient: auth.apiClient, session: auth);
    final oldRead = service.getContexts();
    await started.future;
    await auth.login(fixtures.login);
    final current = store.value;
    pending.complete(http.Response('{}', 401));
    expect(
      (await oldRead as AuthFailure<UserContexts>).error.code,
      AuthErrorCode.unauthenticated,
    );
    expect(store.value, current);
    expect((await auth.accessToken() as AuthSuccess<String>).value, 'access-2');
    expect(logins, 2);
  });
}
