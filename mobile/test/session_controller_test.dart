import 'dart:async';

import 'package:flutter_test/flutter_test.dart';
import 'package:washflow/features/auth/domain/auth_service.dart';
import 'package:washflow/features/auth/domain/user_contexts.dart';
import 'package:washflow/features/auth/presentation/session_controller.dart';

import 'support/session_fakes.dart';

void main() {
  late FakeSession session;
  late FakeContexts contexts;
  late SessionController controller;
  setUp(() {
    session = FakeSession();
    contexts = FakeContexts(grants());
    controller = SessionController(session: session, contexts: contexts);
  });
  tearDown(() {
    controller.dispose();
    session.dispose();
  });

  test(
    'zero/one/multiple experiences and combined business grants are distinct',
    () async {
      await controller.refresh();
      expect(controller.phase, SessionPhase.ready);
      expect(controller.options, isEmpty);
      contexts.value = grants(customer: true);
      await controller.refresh();
      expect(controller.selected!.role, ExperienceRole.customer);
      contexts.value = grants(
        businesses: [
          businessContext(roles: ['Owner', 'Employee']),
        ],
      );
      await controller.refresh(choose: true);
      expect(controller.selected, isNull);
      expect(controller.options.map((e) => e.role), [
        ExperienceRole.owner,
        ExperienceRole.employee,
      ]);
      expect(controller.options.map((e) => e.id).toSet(), hasLength(2));
      expect(controller.options.first.label, 'İşletme A · İşletme sahibi');
      contexts.value = grants(
        businesses: [
          businessContext(roles: ['Employee']),
        ],
      );
      await controller.refresh();
      expect(controller.selected!.role, ExperienceRole.employee);
      contexts.value = grants(
        businesses: [
          businessContext(roles: ['Admin']),
        ],
      );
      await controller.refresh();
      expect(controller.options, isEmpty);
      expect(controller.selected, isNull);
    },
  );

  test(
    'switch and selection re-fetch grants and discard tenant state immediately',
    () async {
      contexts.value = grants(
        businesses: [
          businessContext(),
          businessContext(id: businessB, name: 'İşletme B'),
        ],
      );
      await controller.refresh();
      final id = controller.options.first.id;
      await controller.select(id, generation: controller.generation);
      expect(contexts.calls, 2);
      expect(controller.selected!.business!.businessId, businessA);
      final pending = Completer<AuthResult<UserContexts>>();
      contexts.handler = () => pending.future;
      final switching = controller.switchContext();
      expect(controller.selected, isNull);
      expect(controller.options, isEmpty);
      pending.complete(AuthSuccess(contexts.value));
      await switching;
      contexts.handler = null;
      expect(controller.selected, isNull);
      await controller.select(
        controller.options.last.id,
        generation: controller.generation,
      );
      expect(controller.selected!.business!.businessId, businessB);
    },
  );

  test('forged and stale selection callbacks cannot create access', () async {
    contexts.value = grants(customer: true);
    await controller.refresh();
    final generation = controller.generation;
    await controller.select('owner:forged', generation: generation);
    expect(contexts.calls, 1);
    expect(controller.selected!.role, ExperienceRole.customer);
    await controller.refresh();
    await controller.select(
      controller.options.single.id,
      generation: generation,
    );
    expect(contexts.calls, 2);
  });

  test('revocation between picker and selection fails closed', () async {
    contexts.value = grants(customer: true, businesses: [businessContext()]);
    await controller.refresh();
    final owner = controller.options.last.id;
    contexts.value = grants(customer: true);
    await controller.select(owner, generation: controller.generation);
    expect(controller.selected, isNull);
    expect(controller.message, contains('artık kullanılamıyor'));
    expect(controller.options.single.role, ExperienceRole.customer);
  });

  test('authorization failure rechecks current grants but ignores old tenant failures', () async {
    contexts.value = grants(businesses: [businessContext()]);
    await controller.refresh();
    final old = controller.generation;
    contexts.value = grants();
    await controller.handleAuthorizationFailure(
      AuthError(code: AuthErrorCode.forbidden),
      generation: old,
    );
    expect(contexts.calls, 2);
    expect(controller.options, isEmpty);
    await controller.handleAuthorizationFailure(
      AuthError(code: AuthErrorCode.unauthenticated),
      generation: old,
    );
    expect(controller.phase, SessionPhase.ready);
  });

  test('newest response wins, including late failures', () async {
    final first = Completer<AuthResult<UserContexts>>();
    contexts.handler = () => first.future;
    final earlier = controller.refresh();
    contexts.handler = null;
    contexts.value = grants(customer: true);
    await controller.refresh();
    first.complete(AuthFailure(AuthError(code: AuthErrorCode.unauthenticated)));
    await earlier;
    expect(controller.phase, SessionPhase.ready);
    expect(controller.selected!.role, ExperienceRole.customer);
  });

  test(
    'logout/account change/disposal prevent late context resurrection',
    () async {
      for (final action in ['logout', 'identity', 'suspend']) {
        // Each iteration gets a fresh authenticated controller.
        final local = SessionController(session: session, contexts: contexts);
        final pending = Completer<AuthResult<UserContexts>>();
        contexts.handler = () => pending.future;
        final read = local.refresh();
        if (action == 'logout') {
          await local.logout();
        } else if (action == 'identity') {
          session.changeIdentity();
        } else {
          local.suspend();
        }
        pending.complete(AuthSuccess(grants(businesses: [businessContext()])));
        await read;
        expect(local.selected, isNull);
        expect(local.options, isEmpty);
        local.dispose();
      }
      final local = SessionController(session: session, contexts: contexts);
      final pending = Completer<AuthResult<UserContexts>>();
      contexts.handler = () => pending.future;
      final read = local.refresh();
      local.dispose();
      pending.complete(AuthSuccess(grants(customer: true)));
      await read;
      expect(local.selected, isNull);
    },
  );

  test(
    'context network errors hide grants; 401 signs out; 403 never loops',
    () async {
      contexts.value = grants(customer: true);
      await controller.refresh();
      for (final code in [AuthErrorCode.network, AuthErrorCode.forbidden]) {
        contexts.handler = () async => AuthFailure(AuthError(code: code));
        await controller.refresh();
        expect(controller.phase, SessionPhase.failed);
        expect(controller.selected, isNull);
        expect(controller.options, isEmpty);
      }
      contexts.handler = () async =>
          AuthFailure(AuthError(code: AuthErrorCode.unauthenticated));
      await controller.refresh();
      expect(controller.phase, SessionPhase.signedOut);
    },
  );

  test(
    'logout storage failure permits cleanup retry and never restores grants',
    () async {
      contexts.value = grants(customer: true);
      await controller.refresh();
      session.logoutResult = AuthFailure(
        AuthError(code: AuthErrorCode.storage),
      );
      await controller.logout();
      expect(controller.phase, SessionPhase.failed);
      expect(controller.selected, isNull);
      session.logoutResult = const AuthSuccess(null);
      await controller.logout();
      expect(controller.phase, SessionPhase.signedOut);
      expect(session.logouts, 2);
    },
  );

  test(
    'periodic session failure invalidates content without replaying refresh',
    () async {
      contexts.value = grants(customer: true);
      await controller.refresh();
      session.refreshResult = AuthFailure(
        AuthError(code: AuthErrorCode.invalidCredentials),
      );
      await controller.checkSession();
      expect(controller.phase, SessionPhase.signedOut);
      expect(controller.selected, isNull);
      expect(session.checks, 1);
    },
  );
  test(
    'account change during pending logout invalidates its late cleanup result',
    () async {
      final pending = Completer<AuthResult<void>>();
      session.logoutHandler = () => pending.future;
      final ending = controller.logout();
      session.changeIdentity();
      expect(controller.phase, SessionPhase.signedOut);
      pending.complete(AuthFailure(AuthError(code: AuthErrorCode.storage)));
      await ending;
      expect(controller.phase, SessionPhase.signedOut);
      expect(controller.message, isNull);
    },
  );

  test('late successful snapshot from a previous business cannot replace current selection', () async {
    final oldSnapshot = grants(businesses: [businessContext()]);
    contexts.value = oldSnapshot;
    await controller.refresh();
    final pending = Completer<AuthResult<UserContexts>>();
    contexts.handler = () => pending.future;
    final oldRead = controller.refresh();
    contexts.handler = null;
    contexts.value = grants(
      businesses: [businessContext(id: businessB, name: 'İşletme B')],
    );
    await controller.refresh();
    expect(controller.selected!.business!.businessId, businessB);
    pending.complete(AuthSuccess(oldSnapshot));
    await oldRead;
    expect(controller.selected!.business!.businessId, businessB);
    expect(controller.options.single.business!.businessId, businessB);
  });
}
