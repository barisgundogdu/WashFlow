import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:washflow/app/app.dart';
import 'package:washflow/features/auth/data/api_auth_service.dart';
import 'package:washflow/features/auth/data/api_context_service.dart';
import 'package:washflow/features/invitations/data/api_invitation_redemption_service.dart';

import 'api_auth_service_test.dart' as fixtures;
import 'support/redemption_fakes.dart';

Future<void> tap(WidgetTester tester, String label) async {
  await tester.ensureVisible(find.text(label).last);
  await tester.tap(find.text(label).last);
  await tester.pumpAndSettle();
}

void main() {
  testWidgets(
    'real adapters complete new-user flow with session-only secure storage and no credential URLs',
    (tester) async {
      final store = fixtures.MemoryStore();
      final paths = <String>[];
      final auth = ApiAuthService(
        baseUrl: Uri.parse('https://example.test'),
        store: store,
        now: () => fixtures.clock,
        client: MockClient((request) async {
          paths.add(request.url.path);
          expect(
            request.url.query.isEmpty &&
                !request.url.toString().contains(redemptionFixture()),
            isTrue,
          );
          expect(request.followRedirects, isFalse);
          expect(store.value?.contains(redemptionFixture()) ?? false, isFalse);
          final body = request.body.isEmpty
              ? null
              : jsonDecode(request.body) as Map;
          switch (request.url.path) {
            case '/api/v1/employee-invitations/register':
              expect(body!['token'] == redemptionFixture(), isTrue);
              expect(store.value == null, isTrue);
              return http.Response('{"message":"ack"}', 202);
            case '/api/v1/auth/email/verify':
              expect(body!['token'] == 'verification-fixture', isTrue);
              expect(store.value == null, isTrue);
              return http.Response('{"message":"ok"}', 200);
            case '/api/v1/auth/login':
              return fixtures.tokenResponse();
            case '/api/v1/employee-invitations/accept':
              expect(body!['token'] == redemptionFixture(), isTrue);
              expect(request.headers.containsKey('Authorization'), isTrue);
              return http.Response('', 204);
            case '/api/v1/auth/contexts':
              return http.Response(
                '{"customerContext":null,"businessContexts":[{"businessId":"22222222-2222-2222-2222-222222222222","businessName":"Business","membershipId":"33333333-3333-3333-3333-333333333333","roles":["Employee"]}]}',
                200,
              );
            default:
              fail('Unexpected endpoint in invitation flow');
          }
        }),
      );
      addTearDown(() => tester.pumpWidget(const SizedBox.shrink()));
      await tester.pumpWidget(
        WashFlowApp(
          authService: auth,
          contextService: ApiContextService(
            apiClient: auth.apiClient,
            session: auth,
          ),
          redemptionService: ApiInvitationRedemptionService(
            apiClient: auth.apiClient,
            session: auth,
          ),
        ),
      );
      await tap(tester, 'Davet koduyla katıl');
      await tester.enterText(
        find.byKey(const ValueKey('invitation-credential')),
        redemptionFixture(),
      );
      await tap(tester, 'Davetle devam et');
      await tap(tester, 'Davet için hesap oluştur');
      await tester.enterText(
        find.byKey(const ValueKey('fullName')),
        'Employee',
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
      await tester.enterText(
        find.byType(TextField).first,
        'verification-fixture',
      );
      await tap(tester, 'E-postayı doğrula');
      await tap(tester, 'Girişe dön');
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
      await tap(tester, 'Daveti kabul et');
      await tap(tester, 'Kabul etmeyi onayla');
      await tap(tester, 'Hesabıma devam et');
      expect(find.text('Çalışan alanı'), findsOneWidget);
      expect(find.text('İşletme sahibi alanı'), findsNothing);
      expect(paths, [
        '/api/v1/employee-invitations/register',
        '/api/v1/auth/email/verify',
        '/api/v1/auth/login',
        '/api/v1/employee-invitations/accept',
        '/api/v1/auth/contexts',
        '/api/v1/auth/contexts',
      ]);
      final stored = jsonDecode(store.value!) as Map;
      expect(stored.keys.toSet(), {
        'accessToken',
        'refreshToken',
        'tokenType',
        'accessTokenExpiresAt',
        'sessionExpiresAt',
      });
      expect(
        store.value!.contains(redemptionFixture()) ||
            store.value!.contains('verification-fixture') ||
            store.value!.contains('fixture password'),
        isFalse,
      );
    },
  );
}
