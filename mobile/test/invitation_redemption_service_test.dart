import 'dart:async';
import 'dart:convert';

import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:washflow/core/network/api_client.dart';
import 'package:washflow/features/auth/domain/auth_service.dart';
import 'package:washflow/features/invitations/data/api_invitation_redemption_service.dart';
import 'package:washflow/features/invitations/domain/invitation_redemption_service.dart';

import 'support/redemption_fakes.dart';

void main() {
  test('register uses actual anonymous contract and preserves exact password; accept uses bearer and 204', () async {
    final session = RedemptionSession()..signedIn = true;
    final paths = <String>[];
    final api = ApiClient(
      baseUrl: Uri.parse('https://example.test/prefix'),
      client: MockClient((request) async {
        paths.add(request.url.path);
        expect(request.url.query.isEmpty, isTrue);
        expect(request.followRedirects, isFalse);
        final body = jsonDecode(request.body) as Map;
        expect(
          body['token'] == redemptionFixture(),
          isTrue,
          reason: 'Credential must only travel in the JSON body',
        );
        if (request.url.path.endsWith('/register')) {
          expect(body.keys.toSet(), {'token', 'fullName', 'email', 'password'});
          expect(body['password'] == ' test password ', isTrue);
          expect(body['fullName'] == 'Test', isTrue);
          expect(body['email'] == 'new@example.test', isTrue);
          expect(request.headers.containsKey('Authorization'), isFalse);
          return http.Response('{"message":"ack"}', 202);
        }
        expect(body.keys.toSet(), {'token'});
        expect(
          request.headers['Authorization'] == 'Bearer synthetic-session',
          isTrue,
        );
        return http.Response('', 204);
      }),
    );
    final service = ApiInvitationRedemptionService(
      apiClient: api,
      session: session,
    );
    expect(
      await service.register(
        InvitationRegistrationRequest(
          token: redemptionFixture(),
          fullName: ' Test ',
          email: ' new@example.test ',
          password: ' test password ',
        ),
      ),
      isA<AuthSuccess<void>>(),
    );
    expect(await service.accept(redemptionFixture()), isA<AuthSuccess<void>>());
    expect(paths, [
      '/prefix/api/v1/employee-invitations/register',
      '/prefix/api/v1/employee-invitations/accept',
    ]);
    expect(session.customerRegistrations + session.businessRegistrations, 0);
  });

  test(
    'generic 202 does not assert validity, create a session, verify or accept',
    () async {
      final session = RedemptionSession();
      var calls = 0;
      final service = ApiInvitationRedemptionService(
        session: session,
        apiClient: ApiClient(
          baseUrl: Uri.parse('https://example.test'),
          client: MockClient((_) async {
            calls++;
            return http.Response('{"message":"generic ack"}', 202);
          }),
        ),
      );
      await service.register(
        InvitationRegistrationRequest(
          token: redemptionFixture(),
          fullName: 'Test',
          email: 'existing@example.test',
          password: 'fixture',
        ),
      );
      expect(calls, 1);
      expect(session.signedIn, isFalse);
      expect(session.verifications, 0);
    },
  );

  test('actual invalid/conflict and HTTP statuses map to safe messages with no raw detail', () async {
    for (final entry in {
      400: AuthErrorCode.invalidInvitation,
      401: AuthErrorCode.unauthenticated,
      403: AuthErrorCode.forbidden,
      404: AuthErrorCode.notFound,
      409: AuthErrorCode.conflict,
      429: AuthErrorCode.rateLimited,
      503: AuthErrorCode.unavailable,
    }.entries) {
      var calls = 0;
      final service = ApiInvitationRedemptionService(
        session: RedemptionSession()..signedIn = true,
        apiClient: ApiClient(
          baseUrl: Uri.parse('https://example.test'),
          client: MockClient((_) async {
            calls++;
            return http.Response(
              '{"code":"invalidInvitation","detail":"private provider detail"}',
              entry.key,
            );
          }),
        ),
      );
      final result =
          await service.accept(redemptionFixture()) as AuthFailure<void>;
      expect(result.error.code, entry.value);
      expect(calls, 1);
      expect(
        redemptionErrorMessage(result.error)
            .contains('private provider detail'),
        isFalse,
      );
      expect(
        redemptionErrorMessage(result.error).contains(redemptionFixture()),
        isFalse,
      );
    }
  });

  test('validation 400 and malformed registration acknowledgement are not acceptance success', () async {
    for (final response in [
      http.Response('{"errors":{}}', 400),
      http.Response('{}', 202),
      http.Response('broken', 202),
    ]) {
      var calls = 0;
      final service = ApiInvitationRedemptionService(
        session: RedemptionSession(),
        apiClient: ApiClient(
          baseUrl: Uri.parse('https://example.test'),
          client: MockClient((_) async {
            calls++;
            return response;
          }),
        ),
      );
      expect(
        await service.register(
          InvitationRegistrationRequest(
            token: redemptionFixture(),
            fullName: 'Test',
            email: 'a@example.test',
            password: 'fixture',
          ),
        ),
        isA<AuthFailure<void>>(),
      );
      expect(calls, 1);
    }
  });

  test(
    'network, timeout, redirects and wrong acceptance status never retry',
    () async {
      for (final failure in ['network', 'timeout', 'redirect', 'status']) {
        var calls = 0;
        final pending = Completer<http.Response>();
        final service = ApiInvitationRedemptionService(
          session: RedemptionSession()..signedIn = true,
          apiClient: ApiClient(
            baseUrl: Uri.parse('https://example.test'),
            timeout: const Duration(milliseconds: 5),
            client: MockClient((_) {
              calls++;
              if (failure == 'network') {
                throw http.ClientException('private detail');
              }
              if (failure == 'timeout') return pending.future;
              return Future.value(
                http.Response('{}', failure == 'redirect' ? 307 : 200),
              );
            }),
          ),
        );
        expect(
          await service.accept(redemptionFixture()),
          isA<AuthFailure<void>>(),
        );
        expect(calls, 1);
        if (failure == 'timeout') pending.complete(http.Response('', 204));
      }
    },
  );

  test('account change while acquiring access prevents a write under the new account', () async {
    final session = RedemptionSession();
    final pending = Completer<AuthResult<String>>();
    session.onAccess = () => pending.future;
    var calls = 0;
    final service = ApiInvitationRedemptionService(
      session: session,
      apiClient: ApiClient(
        baseUrl: Uri.parse('https://example.test'),
        client: MockClient((_) async {
          calls++;
          return http.Response('', 204);
        }),
      ),
    );
    final result = service.accept(redemptionFixture());
    session.changeIdentity();
    pending.complete(const AuthSuccess('synthetic-session'));
    expect(
      (await result as AuthFailure<void>).error.code,
      AuthErrorCode.unauthenticated,
    );
    expect(calls, 0);
  });
}
