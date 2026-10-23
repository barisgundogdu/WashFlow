import 'dart:async';
import 'dart:convert';

import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:washflow/core/network/api_client.dart';

Matcher failure(ApiErrorCode code) =>
    isA<ApiException>().having((e) => e.code, 'safe code', code);

void main() {
  test(
    'GET and POST preserve deployment prefix, JSON and explicit bearer',
    () async {
      final requests = <http.Request>[];
      final api = ApiClient(
        baseUrl: Uri.parse('https://api.example.test/deployment/'),
        client: MockClient((request) async {
          requests.add(request);
          return request.method == 'GET'
              ? http.Response('[{"id":"fixture"}]', 200)
              : http.Response('{"message":"ok"}', 201);
        }),
      );
      expect(
        await api.get('/api/v1/auth/contexts', access: 'fixture'),
        isA<List>(),
      );
      expect(
        await api.post('/api/v1/example', {'name': 'Ayşe'}, expected: 201),
        {'message': 'ok'},
      );
      expect(
        requests[0].url.toString(),
        'https://api.example.test/deployment/api/v1/auth/contexts',
      );
      expect(requests[0].method, 'GET');
      expect(requests[0].body, isEmpty);
      expect(requests[0].headers['Authorization'], 'Bearer fixture');
      expect(requests[1].headers.containsKey('Authorization'), isFalse);
      expect(jsonDecode(requests[1].body), {'name': 'Ayşe'});
      expect(
        requests.every((r) => !r.followRedirects && r.url.query.isEmpty),
        isTrue,
      );
    },
  );

  test(
    '204 has no JSON requirement; malformed successful JSON fails safely',
    () async {
      for (final body in ['', 'not-json', 'null', '"text"', 'true']) {
        final api = ApiClient(
          baseUrl: Uri.parse('https://example.test'),
          client: MockClient((_) async => http.Response(body, 200)),
        );
        await expectLater(
          api.get('/api/v1/auth/contexts'),
          throwsA(failure(ApiErrorCode.unexpected)),
        );
      }
      final api = ApiClient(
        baseUrl: Uri.parse('https://example.test'),
        client: MockClient((_) async => http.Response('', 204)),
      );
      expect(
        await api.post('/api/v1/auth/logout', null, expected: 204),
        isNull,
      );
    },
  );

  test(
    'HTTP errors are semantic, do not retry or expose arbitrary response text',
    () async {
      for (final entry in {
        400: ApiErrorCode.validation,
        401: ApiErrorCode.unauthenticated,
        403: ApiErrorCode.forbidden,
        409: ApiErrorCode.conflict,
        429: ApiErrorCode.rateLimited,
        503: ApiErrorCode.unavailable,
        500: ApiErrorCode.unexpected,
        302: ApiErrorCode.unexpected,
        307: ApiErrorCode.unexpected,
      }.entries) {
        var calls = 0;
        final api = ApiClient(
          baseUrl: Uri.parse('https://example.test'),
          client: MockClient((_) async {
            calls++;
            return http.Response(
              '<html>private provider detail</html>',
              entry.key,
              headers: {'location': 'https://other.example.test'},
            );
          }),
        );
        await expectLater(
          api.post('/api/v1/example', {'value': 'fixture'}),
          throwsA(failure(entry.value)),
        );
        expect(calls, 1);
        try {
          await api.get('/api/v1/example');
        } on ApiException catch (e) {
          expect(e.toString(), isNot(contains('private provider detail')));
        }
      }
      final api = ApiClient(
        baseUrl: Uri.parse('https://example.test'),
        client: MockClient(
          (_) async =>
              http.Response('{"code":"invalidVerificationToken"}', 400),
        ),
      );
      await expectLater(
        api.post('/api/v1/auth/email/verify', {}),
        throwsA(failure(ApiErrorCode.invalidVerificationToken)),
      );
    },
  );

  test(
    'network errors and timeouts, including response body waits, never retry',
    () async {
      var calls = 0;
      final api = ApiClient(
        baseUrl: Uri.parse('https://example.test'),
        client: MockClient((_) async {
          calls++;
          throw http.ClientException('private detail');
        }),
      );
      await expectLater(
        api.get('/api/v1/auth/contexts'),
        throwsA(failure(ApiErrorCode.network)),
      );
      expect(calls, 1);
      for (final waitingOnBody in [false, true]) {
        final complete = Completer<List<int>>();
        var attempts = 0;
        final slow = ApiClient(
          baseUrl: Uri.parse('https://example.test'),
          timeout: const Duration(milliseconds: 5),
          client: MockClient.streaming((request, body) async {
            attempts++;
            if (!waitingOnBody) await complete.future;
            return http.StreamedResponse(
              Stream.fromFuture(complete.future),
              200,
            );
          }),
        );
        await expectLater(
          slow.get('/api/v1/auth/contexts'),
          throwsA(failure(ApiErrorCode.timeout)),
        );
        complete.complete(utf8.encode('{}'));
        expect(attempts, 1);
      }
    },
  );

  test(
    'invalid origins and unsafe paths cannot redirect a bearer credential',
    () async {
      for (final origin in [
        'file:///tmp/api',
        'https://user:pass@example.test',
        'https://example.test?x=1',
        'https://example.test#fragment',
      ]) {
        expect(
          () => ApiClient(baseUrl: Uri.parse(origin)),
          throwsArgumentError,
        );
      }
      var calls = 0;
      final api = ApiClient(
        baseUrl: Uri.parse('https://example.test'),
        client: MockClient((_) async {
          calls++;
          return http.Response('{}', 200);
        }),
      );
      for (final path in [
        'https://other.example.test/api/v1/example',
        '//other.example.test',
        '/api/v1/../example',
        '/api/v1/example?token=fixture',
        '/api/v1/%2e%2e/example',
      ]) {
        await expectLater(
          api.get(path, access: 'fixture'),
          throwsA(failure(ApiErrorCode.unexpected)),
        );
      }
      expect(calls, 0);
    },
  );
}
