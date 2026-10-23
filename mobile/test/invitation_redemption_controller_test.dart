import 'dart:async';

import 'package:flutter_test/flutter_test.dart';
import 'package:washflow/features/auth/domain/auth_service.dart';
import 'package:washflow/features/auth/domain/user_contexts.dart';
import 'package:washflow/features/invitations/presentation/invitation_redemption_controller.dart';

import 'support/redemption_fakes.dart';
import 'support/session_fakes.dart';

void main() {
  late RedemptionSession session;
  late FakeRedemption service;
  late FakeContexts contexts;
  late InvitationRedemptionController controller;
  setUp(() {
    session = RedemptionSession();
    service = FakeRedemption();
    contexts = FakeContexts(grants());
    controller = InvitationRedemptionController(
      auth: session,
      session: session,
      access: session,
      contexts: contexts,
      service: service,
    );
  });
  tearDown(() {
    controller.dispose();
    session.dispose();
  });

  test('existing signed-in account requires explicit acceptance, then discards credential and reloads contexts', () async {
    session.signedIn = true;
    await controller.begin(redemptionFixture());
    expect(controller.step, RedemptionStep.confirm);
    expect(service.acceptances, 0);
    await controller.accept();
    expect(controller.acceptedConfirmed, isTrue);
    expect(controller.hasCredential, isFalse);
    expect(controller.contextsLoaded, isTrue);
    expect(contexts.calls, 1);
    await controller.accept();
    expect(service.acceptances, 1);
  });

  test(
    'new account acknowledgement/verification/login never automatically accept',
    () async {
      await controller.begin(redemptionFixture());
      controller.showRegistration();
      await controller.register(
        fullName: 'Test',
        email: 'new@example.test',
        password: 'fixture',
      );
      expect(controller.step, RedemptionStep.verification);
      expect(controller.authenticated, isFalse);
      expect(controller.hasCredential, isTrue);
      expect(controller.acceptedConfirmed, isFalse);
      await session.verifyEmail('verification-fixture');
      expect(service.acceptances, 0);
      expect(controller.step, RedemptionStep.verification);
      controller.showLogin();
      await controller.login('new@example.test', 'fixture');
      expect(controller.step, RedemptionStep.confirm);
      expect(controller.hasCredential, isTrue);
      expect(service.acceptances, 0);
      expect(session.customerRegistrations + session.businessRegistrations, 0);
      await controller.accept();
      expect(service.acceptances, 1);
      expect(contexts.calls, 1);
    },
  );

  test(
    'format check is not validity and empty input never dispatches',
    () async {
      await controller.begin('invalid');
      expect(controller.hasCredential, isFalse);
      expect(controller.message!.contains('geçerliliğini doğrulamaz'), isTrue);
      expect(service.registrations + service.acceptances, 0);
      await controller.begin(redemptionFixture());
      expect(controller.acceptedConfirmed, isFalse);
    },
  );

  test('wrong/unverified email login and invalid invitation failures grant no membership', () async {
    await controller.begin(redemptionFixture());
    controller.showLogin();
    session.onLogin = () async =>
        AuthFailure(AuthError(code: AuthErrorCode.invalidCredentials));
    await controller.login('unverified@example.test', 'fixture');
    expect(controller.step, RedemptionStep.login);
    expect(controller.authenticated, isFalse);
    session.onLogin = null;
    await controller.login('wrong@example.test', 'fixture');
    service.onAccept = () async =>
        AuthFailure(AuthError(code: AuthErrorCode.invalidInvitation));
    await controller.accept();
    expect(controller.acceptedConfirmed, isFalse);
    expect(contexts.calls, 0);
    expect(controller.message!.contains('doğrulanmış e-posta'), isTrue);
    expect(controller.message!.contains(redemptionFixture()), isFalse);
  });

  test('duplicate register and accept callbacks send each mutation once while pending', () async {
    await controller.begin(redemptionFixture());
    controller.showRegistration();
    final registration = Completer<AuthResult<void>>();
    service.onRegister = (_) => registration.future;
    final first = controller.register(
      fullName: 'Test',
      email: 'a@example.test',
      password: 'fixture',
    );
    await controller.register(
      fullName: 'Test',
      email: 'a@example.test',
      password: 'fixture',
    );
    expect(service.registrations, 1);
    registration.complete(const AuthSuccess(null));
    await first;
    controller.showLogin();
    await controller.login('a@example.test', 'fixture');
    final accept = Completer<AuthResult<void>>();
    service.onAccept = () => accept.future;
    final pending = controller.accept();
    await controller.accept();
    expect(service.acceptances, 1);
    accept.complete(const AuthSuccess(null));
    await pending;
    expect(controller.hasCredential, isFalse);
  });

  test('uncertain acceptance needs deliberate retry; context refresh is not acceptance proof', () async {
    session.signedIn = true;
    await controller.begin(redemptionFixture());
    service.onAccept = () async =>
        AuthFailure(AuthError(code: AuthErrorCode.timeout));
    await controller.accept();
    await controller.accept();
    expect(service.acceptances, 1);
    contexts.value = grants(
      businesses: [
        businessContext(roles: ['Employee']),
      ],
    );
    await controller.refreshContexts();
    expect(controller.contextsLoaded, isTrue);
    expect(controller.acceptedConfirmed, isFalse);
    expect(controller.hasCredential, isTrue);
    expect(controller.message!.contains('doğrulanmadı'), isTrue);
    service.onAccept = () async =>
        AuthFailure(AuthError(code: AuthErrorCode.invalidInvitation));
    await controller.accept(deliberateRetry: true);
    expect(service.acceptances, 2);
    expect(controller.acceptedConfirmed, isFalse);
  });

  test('uncertain registration can proceed to verification/login without an automatic retry', () async {
    await controller.begin(redemptionFixture());
    controller.showRegistration();
    service.onRegister = (_) async =>
        AuthFailure(AuthError(code: AuthErrorCode.network));
    await controller.register(
      fullName: 'Test',
      email: 'a@example.test',
      password: 'fixture',
    );
    expect(controller.registrationUncertain, isTrue);
    expect(service.registrations, 1);
    controller.showVerification();
    controller.showLogin();
    await controller.login('a@example.test', 'fixture');
    expect(service.registrations, 1);
    expect(controller.hasCredential, isTrue);
    expect(service.acceptances, 0);
  });

  test('confirmed acceptance with failed context reload cannot replay accept or restore credential', () async {
    session.signedIn = true;
    await controller.begin(redemptionFixture());
    contexts.handler = () async =>
        AuthFailure(AuthError(code: AuthErrorCode.network));
    await controller.accept();
    expect(controller.acceptedConfirmed, isTrue);
    expect(controller.hasCredential, isFalse);
    expect(controller.contextsLoaded, isFalse);
    expect(controller.message!.startsWith('Davet kabul edildi'), isTrue);
    contexts.handler = null;
    await controller.refreshContexts();
    await controller.accept();
    expect(controller.contextsLoaded, isTrue);
    expect(service.acceptances, 1);
  });

  test(
    'cancellation/disposal ignore late registration and acceptance success',
    () async {
      await controller.begin(redemptionFixture());
      controller.showRegistration();
      final registration = Completer<AuthResult<void>>();
      service.onRegister = (_) => registration.future;
      final pending = controller.register(
        fullName: 'Test',
        email: 'a@example.test',
        password: 'fixture',
      );
      controller.cancel();
      registration.complete(const AuthSuccess(null));
      await pending;
      expect(controller.step, RedemptionStep.closed);
      expect(controller.hasCredential, isFalse);
      final local = InvitationRedemptionController(
        auth: session,
        session: session,
        access: session,
        contexts: contexts,
        service: service,
      );
      session.signedIn = true;
      await local.begin(redemptionFixture());
      final accept = Completer<AuthResult<void>>();
      service.onAccept = () => accept.future;
      final later = local.accept();
      local.dispose();
      accept.complete(const AuthSuccess(null));
      await later;
      expect(local.hasCredential, isFalse);
      expect(contexts.calls, 0);
    },
  );

  test('only the active flow login identity signal is retained; unrelated account changes clear memory', () async {
    await controller.begin(redemptionFixture());
    controller.showLogin();
    final login = Completer<AuthResult<LoginResult>>();
    session.onLogin = () => login.future;
    final pending = controller.login('a@example.test', 'fixture');
    expect(controller.hasCredential, isTrue);
    session.changeIdentity();
    login.complete(const AuthSuccess(LoginResult()));
    await pending;
    expect(controller.hasCredential, isFalse);
    expect(controller.step, RedemptionStep.closed);
    expect(controller.authenticated, isFalse);
  });

  test('logout clears pending credential immediately even on storage failure and permits cleanup retry', () async {
    session.signedIn = true;
    await controller.begin(redemptionFixture());
    session.logoutResult = AuthFailure(AuthError(code: AuthErrorCode.storage));
    expect(await controller.logout(), isFalse);
    expect(controller.hasCredential, isFalse);
    expect(controller.step, RedemptionStep.closed);
    session.logoutResult = const AuthSuccess(null);
    expect(await controller.logout(), isTrue);
  });

  test('401 acceptance returns to active flow login while retaining credential, never silently retrying', () async {
    session.signedIn = true;
    await controller.begin(redemptionFixture());
    service.onAccept = () async =>
        AuthFailure(AuthError(code: AuthErrorCode.unauthenticated));
    await controller.accept();
    expect(controller.step, RedemptionStep.login);
    expect(controller.hasCredential, isTrue);
    await controller.login('a@example.test', 'fixture');
    expect(controller.step, RedemptionStep.confirm);
    expect(service.acceptances, 1);
  });

  test('late contexts after account change do not report access or acceptance to the new account', () async {
    session.signedIn = true;
    await controller.begin(redemptionFixture());
    final read = Completer<AuthResult<UserContexts>>();
    contexts.handler = () => read.future;
    final accept = controller.accept();
    await Future<void>.delayed(Duration.zero);
    session.changeIdentity();
    read.complete(AuthSuccess(grants()));
    await accept;
    expect(controller.hasCredential, isFalse);
    expect(controller.contextsLoaded, isFalse);
    expect(controller.acceptedConfirmed, isFalse);
  });
}
