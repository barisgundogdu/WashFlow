import 'dart:async';

import 'package:flutter_test/flutter_test.dart';
import 'package:washflow/features/auth/domain/auth_service.dart';
import 'package:washflow/features/invitations/domain/invitation.dart';
import 'package:washflow/features/owner/presentation/employee_invitations_controller.dart';

import 'support/invitation_fakes.dart';
import 'support/session_fakes.dart';

void main() {
  late FakeInvitations service;
  late EmployeeInvitationsController controller;
  late bool current;
  late List<AuthErrorCode> failures;
  setUp(() {
    current = true;
    failures = [];
    service = FakeInvitations();
    controller = EmployeeInvitationsController(
      businessId: businessA,
      service: service,
      isCurrent: () => current,
      onAuthorizationFailure: (error) async {
        failures.add(error.code);
      },
    );
  });
  tearDown(() => controller.dispose());

  test('loading, empty state, validation, record-only success and read reconciliation', () async {
    await controller.load();
    expect(controller.items, isEmpty);
    expect(controller.canMutate, isTrue);
    await controller.create('bad');
    expect(service.calls, ['list:$businessA']);
    expect(controller.error, contains('e-posta'));
    await controller.create(' employee@example.test ');
    expect(service.calls, [
      'list:$businessA',
      'create:$businessA',
      'list:$businessA',
    ]);
    expect(controller.items.single.email, 'employee@example.test');
    expect(controller.message, contains('Davet kaydedildi'));
    expect(controller.message, contains('teslimi doğrulanmadı'));
    expect(() => controller.items.clear(), throwsUnsupportedError);
  });

  test('resend and revoke serialize against each other and cannot act on foreign IDs', () async {
    service.items = [invitation()];
    await controller.load();
    final pending = Completer<AuthResult<EmployeeInvitation>>();
    service.onResend = (_, _) => pending.future;
    final resend = controller.resend(invitationA);
    await controller.resend(invitationA);
    await controller.revoke(invitationA);
    await controller.create('other@example.test');
    await controller.load();
    expect(
      service.calls.where((call) => call.startsWith('resend')),
      hasLength(1),
    );
    expect(service.calls.where((call) => call.startsWith('revoke')), isEmpty);
    pending.complete(AuthSuccess(invitation(id: invitationB)));
    service.items = [invitation(id: invitationB)];
    await resend;
    await controller.revoke(invitationA);
    expect(service.calls.where((call) => call.startsWith('revoke')), isEmpty);
    await controller.revoke(invitationB);
    expect(service.calls.last, 'list:$businessA');
    expect(controller.message, 'Davet iptal edildi.');
  });

  test('stale read, mutation and failure cannot cross business/role/account boundaries', () async {
    for (final mutation in [false, true]) {
      service.items = [invitation()];
      await controller.load();
      final read = Completer<AuthResult<List<EmployeeInvitation>>>();
      final write = Completer<AuthResult<EmployeeInvitation>>();
      if (mutation) {
        service.onCreate = (_, _) => write.future;
      } else {
        service.onList = (_) => read.future;
      }
      final result = mutation
          ? controller.create('employee@example.test')
          : controller.load();
      current = false;
      if (mutation) {
        write.complete(AuthFailure(AuthError(code: AuthErrorCode.forbidden)));
      } else {
        read.complete(
          AuthSuccess([invitation(email: 'old-business@example.test')]),
        );
      }
      await result;
      expect(failures, isEmpty);
      expect(
        controller.items.any(
          (item) => item.email == 'old-business@example.test',
        ),
        isFalse,
      );
      expect(service.calls.every((call) => call.contains(businessA)), isTrue);
      current = true;
      service.onList = null;
      service.onCreate = null;
    }
  });

  test('latest read wins over older success and failure', () async {
    final first = Completer<AuthResult<List<EmployeeInvitation>>>();
    service.onList = (_) => first.future;
    final earlier = controller.load();
    service.onList = null;
    service.items = [invitation(id: invitationB)];
    await controller.load();
    first.complete(AuthFailure(AuthError(code: AuthErrorCode.unauthenticated)));
    await earlier;
    expect(controller.items.single.id, invitationB);
    expect(failures, isEmpty);
  });

  test(
    'accepted/revoked actions are blocked; expired invitations can be resent',
    () async {
      final now = DateTime.now().toUtc();
      service.items = [
        EmployeeInvitation.fromJson(
          invitationJson(accepted: now.toIso8601String()),
        ),
      ];
      await controller.load();
      await controller.resend(invitationA);
      await controller.revoke(invitationA);
      expect(service.calls.length, 1);
      service.items = [
        EmployeeInvitation.fromJson(
          invitationJson(
            expiry: now.subtract(const Duration(hours: 1)).toIso8601String(),
          ),
        ),
      ];
      await controller.load();
      await controller.resend(invitationA);
      expect(service.calls.any((c) => c.startsWith('resend:')), isTrue);
    },
  );

  test(
    'auth failures clear list, block actions, notify shell without looping',
    () async {
      for (final code in [
        AuthErrorCode.forbidden,
        AuthErrorCode.unauthenticated,
      ]) {
        final local = EmployeeInvitationsController(
          businessId: businessA,
          service: service,
          isCurrent: () => true,
          onAuthorizationFailure: (error) async => failures.add(error.code),
        );
        service.onList = (_) async => AuthFailure(AuthError(code: code));
        await local.load();
        final count = service.calls.length;
        expect(local.phase, InvitationListPhase.blocked);
        expect(local.items, isEmpty);
        await local.load();
        await local.create('employee@example.test');
        expect(service.calls.length, count);
        local.dispose();
      }
      expect(failures, [
        AuthErrorCode.forbidden,
        AuthErrorCode.unauthenticated,
      ]);
    },
  );

  test('races, not found, validation and rate limit reconcile with GET, never replay POST', () async {
    for (final code in [
      AuthErrorCode.invalidInvitation,
      AuthErrorCode.notFound,
      AuthErrorCode.conflict,
      AuthErrorCode.validation,
      AuthErrorCode.rateLimited,
    ]) {
      service.items = [invitation()];
      await controller.load();
      final before = service.calls.length;
      service.onResend = (_, _) async => AuthFailure(AuthError(code: code));
      await controller.resend(invitationA);
      expect(service.calls.skip(before), [
        'resend:$businessA:$invitationA',
        'list:$businessA',
      ]);
      expect(controller.error, isNotNull);
      expect(controller.phase, InvitationListPhase.ready);
    }
  });

  test('uncertain write warns and failed reconciliation disables writes until a successful read', () async {
    await controller.load();
    service.onCreate = (_, _) async =>
        AuthFailure(AuthError(code: AuthErrorCode.timeout));
    service.onList = (_) async =>
        AuthFailure(AuthError(code: AuthErrorCode.network));
    await controller.create('employee@example.test');
    expect(controller.error, contains('tamamlanmış olabilir'));
    expect(controller.canMutate, isFalse);
    await controller.create('employee@example.test');
    expect(service.calls.where((c) => c.startsWith('create')), hasLength(1));
    service.onList = null;
    await controller.load();
    expect(controller.canMutate, isTrue);
    expect(service.calls.where((c) => c.startsWith('create')), hasLength(1));
  });

  test('disposed controller ignores a pending read', () async {
    final local = EmployeeInvitationsController(
      businessId: businessA,
      service: service,
      isCurrent: () => true,
      onAuthorizationFailure: (_) async {},
    );
    final pending = Completer<AuthResult<List<EmployeeInvitation>>>();
    service.onList = (_) => pending.future;
    final read = local.load();
    local.dispose();
    pending.complete(AuthSuccess([invitation()]));
    await read;
    expect(local.items, isEmpty);
  });
}
