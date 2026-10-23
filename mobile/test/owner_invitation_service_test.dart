import 'dart:async';
import 'dart:convert';

import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:washflow/core/network/api_client.dart';
import 'package:washflow/features/auth/domain/auth_service.dart';
import 'package:washflow/features/auth/domain/session_service.dart';
import 'package:washflow/features/invitations/data/api_owner_invitation_service.dart';
import 'package:washflow/features/invitations/domain/invitation.dart';
import 'package:washflow/features/invitations/domain/owner_invitation_service.dart';

import 'support/invitation_fakes.dart';
import 'support/session_fakes.dart';

class Credential extends FakeSession implements AccessTokenProvider {
  AuthResult<String> result = const AuthSuccess('test-fixture');
  Future<AuthResult<String>> Function()? handler;
  @override
  Future<AuthResult<String>> accessToken() async =>
      handler == null ? result : handler!();
}

http.Response json(Object data, int status) => http.Response(
  jsonEncode(data),
  status,
  headers: {'content-type': 'application/json; charset=utf-8'},
);

void main() {
  test('actual DTO fields, Turkish state and backend action hints', () {
    final now = DateTime.now().toUtc();
    final pending = invitation();
    expect(pending.statusLabel(now), 'Kabul bekliyor');
    final expired = EmployeeInvitation.fromJson(
      invitationJson(
        expiry: now.subtract(const Duration(hours: 1)).toIso8601String(),
      ),
    );
    expect(expired.statusLabel(now), 'Süresi doldu');
    expect(expired.canResend, isTrue);
    final accepted = EmployeeInvitation.fromJson(
      invitationJson(accepted: now.toIso8601String()),
    );
    expect(accepted.statusLabel(now), 'Kabul edildi');
    expect(accepted.canResend, isFalse);
    expect(accepted.canRevoke, isFalse);
    final revoked = EmployeeInvitation.fromJson(
      invitationJson(revoked: now.toIso8601String()),
    );
    expect(revoked.statusLabel(now), 'İptal edildi');
    expect(revoked.canResend, isFalse);
    for (final patch in [
      {'id': 'bad'},
      {'email': null},
      {'expiresAt': 'local time'},
      {'acceptedAt': 1},
      {'acceptedAt': now.toIso8601String(), 'revokedAt': now.toIso8601String()},
    ]) {
      expect(
        () => EmployeeInvitation.fromJson({...invitationJson(), ...patch}),
        throwsFormatException,
      );
    }
  });

  test('exact owner URLs/bodies/statuses, per-request scopes and no credentials in metadata', () async {
    final requests = <http.Request>[];
    final session = Credential();
    final service = ApiOwnerInvitationService(
      session: session,
      apiClient: ApiClient(
        baseUrl: Uri.parse('https://example.test/prefix'),
        client: MockClient((r) async {
          requests.add(r);
          if (r.method == 'GET') return json([invitationJson()], 200);
          if (r.url.path.endsWith('/revoke')) return http.Response('', 204);
          return json(
            invitationJson(
              id: r.url.path.endsWith('/resend') ? invitationB : invitationA,
            ),
            r.url.path.endsWith('/resend') ? 200 : 201,
          );
        }),
      ),
    );
    final list = (await service.list(
      businessA,
    ) as AuthSuccess<List<EmployeeInvitation>>).value;
    expect(() => list.clear(), throwsUnsupportedError);
    await service.create(businessB, ' employee@example.test ');
    await service.revoke(businessA, invitationA);
    await service.resend(businessB, invitationA);
    expect(requests.map((r) => r.url.path), [
      '/prefix/api/v1/businesses/$businessA/employee-invitations',
      '/prefix/api/v1/businesses/$businessB/employee-invitations',
      '/prefix/api/v1/businesses/$businessA/employee-invitations/$invitationA/revoke',
      '/prefix/api/v1/businesses/$businessB/employee-invitations/$invitationA/resend',
    ]);
    expect(jsonDecode(requests[1].body), {'email': 'employee@example.test'});
    expect(requests[2].body, isEmpty);
    expect(requests[3].body, isEmpty);
    expect(
      requests.every(
        (r) =>
            !r.followRedirects &&
            r.url.query.isEmpty &&
            r.headers['Authorization'] == 'Bearer test-fixture',
      ),
      isTrue,
    );
  });

  test(
    'HTTP failure mappings never replay mutations or expose raw server text',
    () async {
      for (final entry in {
        400: AuthErrorCode.validation,
        401: AuthErrorCode.unauthenticated,
        403: AuthErrorCode.forbidden,
        404: AuthErrorCode.notFound,
        409: AuthErrorCode.conflict,
        429: AuthErrorCode.rateLimited,
        503: AuthErrorCode.unavailable,
      }.entries) {
        var calls = 0;
        final service = ApiOwnerInvitationService(
          session: Credential(),
          apiClient: ApiClient(
            baseUrl: Uri.parse('https://example.test'),
            client: MockClient((_) async {
              calls++;
              return json({'detail': 'private detail'}, entry.key);
            }),
          ),
        );
        final result = await service.create(
          businessA,
          'employee@example.test',
        ) as AuthFailure<EmployeeInvitation>;
        expect(result.error.code, entry.value);
        expect(calls, 1);
        expect(
          invitationErrorMessage(result.error),
          isNot(contains('private detail')),
        );
      }
      final service = ApiOwnerInvitationService(
        session: Credential(),
        apiClient: ApiClient(
          baseUrl: Uri.parse('https://example.test'),
          client: MockClient(
            (_) async => json({'code': 'invalidInvitation'}, 400),
          ),
        ),
      );
      expect(
        (await service.resend(
          businessA,
          invitationA,
        ) as AuthFailure<EmployeeInvitation>).error.code,
        AuthErrorCode.invalidInvitation,
      );
    },
  );

  test('session errors and account changes during credential acquisition prevent dispatch', () async {
    var calls = 0;
    final session = Credential();
    final service = ApiOwnerInvitationService(
      session: session,
      apiClient: ApiClient(
        baseUrl: Uri.parse('https://example.test'),
        client: MockClient((_) async {
          calls++;
          return json([], 200);
        }),
      ),
    );
    session.result = AuthFailure(AuthError(code: AuthErrorCode.storage));
    expect(
      (await service.list(businessA) as AuthFailure).error.code,
      AuthErrorCode.storage,
    );
    final pending = Completer<AuthResult<String>>();
    session.handler = () => pending.future;
    final write = service.create(businessA, 'employee@example.test');
    session.changeIdentity();
    pending.complete(const AuthSuccess('test-fixture'));
    expect(
      (await write as AuthFailure).error.code,
      AuthErrorCode.unauthenticated,
    );
    expect(calls, 0);
  });

  test('malformed/duplicate responses fail safely, including successful mutation parse failures', () async {
    for (final response in [
      json({}, 200),
      json([invitationJson(), invitationJson()], 200),
      http.Response('broken', 200),
    ]) {
      final service = ApiOwnerInvitationService(
        session: Credential(),
        apiClient: ApiClient(
          baseUrl: Uri.parse('https://example.test'),
          client: MockClient((_) async => response),
        ),
      );
      expect(
        (await service.list(businessA) as AuthFailure).error.code,
        AuthErrorCode.unexpected,
      );
    }
    var calls = 0;
    final service = ApiOwnerInvitationService(
      session: Credential(),
      apiClient: ApiClient(
        baseUrl: Uri.parse('https://example.test'),
        client: MockClient((_) async {
          calls++;
          return json({}, 201);
        }),
      ),
    );
    expect(
      (await service.create(
        businessA,
        'employee@example.test',
      ) as AuthFailure).error.code,
      AuthErrorCode.unexpected,
    );
    expect(calls, 1);
    await service.revoke('../invalid', invitationA);
    expect(calls, 1);
  });

  test('uncertain network and timeout mutations are sent only once', () async {
    for (final timeout in [false, true]) {
      var calls = 0;
      final pending = Completer<http.Response>();
      final service = ApiOwnerInvitationService(
        session: Credential(),
        apiClient: ApiClient(
          baseUrl: Uri.parse('https://example.test'),
          timeout: const Duration(milliseconds: 5),
          client: MockClient((_) {
            calls++;
            if (timeout) return pending.future;
            throw http.ClientException('private provider detail');
          }),
        ),
      );
      final result =
          await service.resend(businessA, invitationA) as AuthFailure;
      expect(
        result.error.code,
        timeout ? AuthErrorCode.timeout : AuthErrorCode.network,
      );
      expect(calls, 1);
      if (timeout) pending.complete(json(invitationJson(), 200));
    }
  });
}
