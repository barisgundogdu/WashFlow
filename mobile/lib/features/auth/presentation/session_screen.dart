import 'dart:async';

import 'package:flutter/material.dart';

import '../../../app/routing/app_router.dart';
import '../../customer/presentation/customer_home_screen.dart';
import '../../employee/presentation/employee_home_screen.dart';
import '../../owner/presentation/owner_home_screen.dart';
import '../../owner/presentation/employee_invitations_screen.dart';
import '../../invitations/domain/owner_invitation_service.dart';
import '../domain/context_service.dart';
import '../domain/session_service.dart';
import 'session_controller.dart';
import 'widgets/auth_layout.dart';
import 'widgets/authorized_context_picker.dart';

/// The sole authenticated route. Route arguments never select or grant a role.
class SessionScreen extends StatefulWidget {
  const SessionScreen({
    super.key,
    required this.service,
    this.contextService,
    this.invitationService,
  });
  final SessionService? service;
  final ContextService? contextService;
  final OwnerInvitationService? invitationService;
  @override
  State<SessionScreen> createState() => _SessionScreenState();
}

class _SessionScreenState extends State<SessionScreen>
    with WidgetsBindingObserver {
  late SessionController _controller;
  Timer? _timer;
  bool _redirecting = false;
  GlobalKey<NavigatorState>? _tenantNavigator;
  int _navigatorGeneration = -1;

  void _createController() {
    _redirecting = false;
    _tenantNavigator = null;
    _navigatorGeneration = -1;
    _controller = SessionController(
      session: widget.service,
      contexts: widget.contextService,
    );
    _controller.addListener(_changed);
    final controller = _controller;
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (mounted && identical(controller, _controller)) {
        unawaited(controller.refresh());
      }
    });
  }

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
    _createController();
    _timer = Timer.periodic(
      const Duration(seconds: 30),
      (_) => _controller.checkSession(),
    );
  }

  @override
  void didUpdateWidget(SessionScreen oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (!identical(oldWidget.service, widget.service) ||
        !identical(oldWidget.contextService, widget.contextService) ||
        !identical(oldWidget.invitationService, widget.invitationService)) {
      _controller.removeListener(_changed);
      _controller.dispose();
      _createController();
    }
  }

  void _changed() {
    if (!mounted) return;
    setState(() {});
    if (_controller.phase == SessionPhase.signedOut && !_redirecting) {
      _redirecting = true;
      final controller = _controller;
      final message = controller.message;
      WidgetsBinding.instance.addPostFrameCallback((_) {
        if (!mounted || !identical(controller, _controller)) return;
        Navigator.of(
          context,
          rootNavigator: true,
        ).pushNamedAndRemoveUntil(AppRouter.login, (_) => false);
        if (message != null) {
          ScaffoldMessenger.of(context)
              .showSnackBar(SnackBar(content: Text(message)));
        }
      });
    }
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    if (state == AppLifecycleState.resumed) {
      unawaited(_controller.refresh());
    } else {
      _controller.suspend();
    }
  }

  @override
  void dispose() {
    _timer?.cancel();
    WidgetsBinding.instance.removeObserver(this);
    _controller.removeListener(_changed);
    _controller.dispose();
    super.dispose();
  }

  Widget _layout() {
    final controller = _controller;
    final selected = controller.selected;
    final generation = controller.generation;
    final busy =
        controller.phase == SessionPhase.loading ||
        controller.phase == SessionPhase.loggingOut;
    return AuthLayout(
      allowBack: false,
      title: 'Oturum açıldı',
      subtitle: selected?.label ?? 'Sunucunun izin verdiği hesap erişimleri.',
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          if (busy) ...[
            const Center(child: CircularProgressIndicator()),
            const Text('Hesap erişimleri kontrol ediliyor.'),
          ] else if (controller.phase == SessionPhase.ready) ...[
            if (selected != null)
              switch (selected.role) {
                ExperienceRole.customer => const CustomerHomeScreen(),
                ExperienceRole.owner => OwnerHomeScreen(
                  onInvitations: widget.invitationService == null
                      ? null
                      : _openInvitations,
                ),
                ExperienceRole.employee => const EmployeeHomeScreen(),
              }
            else if (controller.options.isEmpty)
              const Text(
                'Kullanılabilir hesap bağlamı bulunamadı. Oturumunuz açık; erişimlerinizi yenileyebilirsiniz.',
              )
            else
              AuthorizedContextPicker(
                options: [
                  for (final option in controller.options)
                    AuthorizedContextOption(id: option.id, label: option.label),
                ],
                onSelected: (id) =>
                    controller.select(id, generation: generation),
              ),
            if (selected != null && controller.options.length > 1)
              TextButton(
                onPressed: controller.switchContext,
                child: const Text('Hesap değiştir'),
              ),
          ],
          if (controller.message case final message?)
            Semantics(liveRegion: true, child: Text(message)),
          const SizedBox(height: 16),
          TextButton(
            onPressed: busy ? null : controller.refresh,
            child: const Text('Yenile'),
          ),
          FilledButton(
            // Logout is available during reads and invalidates their responses immediately.
            onPressed: controller.phase == SessionPhase.loggingOut
                ? null
                : controller.logout,
            child: const Text('Çıkış yap'),
          ),
        ],
      ),
    );
  }

  Future<void> _openInvitations() async {
    final controller = _controller;
    final selected = controller.selected;
    if (selected?.role != ExperienceRole.owner ||
        widget.invitationService == null) {
      return;
    }
    final revalidation = controller.refresh(requestedId: selected!.id);
    final requestedGeneration = controller.generation;
    await revalidation;
    if (!mounted ||
        controller.generation != requestedGeneration ||
        !identical(controller, _controller) ||
        controller.selected?.id != selected.id ||
        controller.selected?.role != ExperienceRole.owner) {
      return;
    }
    final generation = controller.generation;
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (mounted &&
          identical(controller, _controller) &&
          controller.generation == generation &&
          _navigatorGeneration == generation) {
        unawaited(
          _tenantNavigator?.currentState?.pushNamed<void>(
            '/employee-invitations',
          ),
        );
      }
    });
  }

  @override
  Widget build(BuildContext context) {
    final controller = _controller;
    final selected = controller.selected;
    final generation = controller.generation;
    if (selected != null && _navigatorGeneration != generation) {
      _navigatorGeneration = generation;
      _tenantNavigator = GlobalKey<NavigatorState>();
    }
    bool currentOwner() =>
        mounted &&
        identical(controller, _controller) &&
        controller.generation == generation &&
        controller.phase == SessionPhase.ready &&
        controller.selected?.id == selected?.id &&
        controller.selected?.role == ExperienceRole.owner;
    return PopScope(
      canPop: false,
      child: selected == null
          ? _layout()
          : Navigator(
              key: _tenantNavigator,
              onGenerateRoute: (settings) => MaterialPageRoute<void>(
                settings: settings,
                builder: (_) {
                  if (settings.name == '/') return _layout();
                  if (settings.name == '/employee-invitations' &&
                      currentOwner() &&
                      widget.invitationService != null) {
                    return EmployeeInvitationsScreen(
                      businessId: selected.business!.businessId,
                      businessName: selected.business!.businessName,
                      service: widget.invitationService!,
                      isCurrent: currentOwner,
                      onAuthorizationFailure: (error) =>
                          controller.handleAuthorizationFailure(
                            error,
                            generation: generation,
                          ),
                      onSwitch: controller.switchContext,
                      onLogout: controller.logout,
                    );
                  }
                  return const Scaffold(
                    body: SafeArea(
                      child: Center(child: Text('Sayfa bulunamadı.')),
                    ),
                  );
                },
              ),
            ),
    );
  }
}
