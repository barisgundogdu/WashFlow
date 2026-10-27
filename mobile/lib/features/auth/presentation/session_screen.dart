import '../../customer_vehicles/presentation/customer_directory_screen.dart';
import '../../customer_vehicles/domain/customer_vehicles.dart';

import 'dart:async';

import 'package:flutter/material.dart';

import '../../../app/routing/app_router.dart';
import '../../business_settings/domain/business_settings.dart';
import '../../business_settings/presentation/business_settings_screen.dart';
import '../../customer/presentation/customer_home_screen.dart';
import '../../employee/presentation/employee_home_screen.dart';
import '../../invitations/domain/owner_invitation_service.dart';
import '../../owner/presentation/employee_invitations_screen.dart';
import '../../owner/presentation/owner_home_screen.dart';
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
    this.settingsService,
    this.customerVehicleService,
    this.redemptionEnabled = false,
  });
  final SessionService? service;
  final ContextService? contextService;
  final OwnerInvitationService? invitationService;
  final BusinessSettingsService? settingsService;
  final CustomerVehicleService? customerVehicleService;
  final bool redemptionEnabled;
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
        !identical(oldWidget.invitationService, widget.invitationService) ||
        !identical(oldWidget.settingsService, widget.settingsService) ||
        !identical(
          oldWidget.customerVehicleService,
          widget.customerVehicleService,
        )) {
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
                ExperienceRole.customer => CustomerHomeScreen(
                  onVehicles: widget.customerVehicleService == null
                      ? null
                      : () => _openDirectory(false),
                ),
                ExperienceRole.owner => OwnerHomeScreen(
                  onCustomers: widget.customerVehicleService == null
                      ? null
                      : () => _openDirectory(true),
                  onSettings: widget.settingsService == null
                      ? null
                      : () => _openOwner('/business-settings'),
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
          if (widget.redemptionEnabled &&
              controller.phase == SessionPhase.ready)
            TextButton(
              onPressed: () {
                controller.releaseForInvitationFlow();
                Navigator.of(
                  context,
                  rootNavigator: true,
                ).pushNamedAndRemoveUntil(AppRouter.redeem, (_) => false);
              },
              child: const Text('Davet koduyla katıl'),
            ),
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

  Future<void> _openDirectory(bool owner) async {
    final controller = _controller;
    final selected = controller.selected;
    final role = owner ? ExperienceRole.owner : ExperienceRole.customer;
    if (selected?.role != role || widget.customerVehicleService == null) return;
    final future = controller.refresh(requestedId: selected!.id);
    final g = controller.generation;
    await future;
    if (!mounted ||
        !identical(controller, _controller) ||
        controller.generation != g ||
        controller.selected?.id != selected.id ||
        controller.selected?.role != role) {
      return;
    }
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (mounted &&
          identical(controller, _controller) &&
          controller.generation == g &&
          _navigatorGeneration == g) {
        unawaited(
          _tenantNavigator?.currentState?.pushNamed<void>(
            owner ? '/local-customers' : '/my-vehicles',
          ),
        );
      }
    });
  }

  Future<void> _openInvitations() => _openOwner('/employee-invitations');
  Future<void> _openOwner(String route) async {
    final controller = _controller;
    final selected = controller.selected;
    if (selected?.role != ExperienceRole.owner ||
        (route == '/employee-invitations'
            ? widget.invitationService == null
            : widget.settingsService == null)) {
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
        final navigator = _tenantNavigator?.currentState;
        if (navigator != null) {
          unawaited(
            navigator.pushNamed<void>(route).then((_) {
              if (route == '/business-settings' &&
                  mounted &&
                  identical(controller, _controller) &&
                  controller.generation == generation &&
                  controller.selected?.id == selected.id) {
                unawaited(controller.refresh(requestedId: selected.id));
              }
            }),
          );
        }
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
    bool currentScope(ExperienceRole role) =>
        mounted &&
        identical(controller, _controller) &&
        controller.generation == generation &&
        controller.phase == SessionPhase.ready &&
        controller.selected?.id == selected?.id &&
        controller.selected?.role == role;
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
                  if ((settings.name == '/local-customers' && currentOwner() ||
                          settings.name == '/my-vehicles' &&
                              currentScope(ExperienceRole.customer)) &&
                      widget.customerVehicleService != null) {
                    final owner = settings.name == '/local-customers';
                    return CustomerDirectoryScreen(
                      service: widget.customerVehicleService!,
                      business: owner ? selected.business!.businessId : null,
                      isCurrent: () => currentScope(
                        owner ? ExperienceRole.owner : ExperienceRole.customer,
                      ),
                      onAuthorizationFailure: (error) =>
                          controller.handleAuthorizationFailure(
                            error,
                            generation: generation,
                          ),
                      onSwitch: controller.switchContext,
                      onLogout: controller.logout,
                    );
                  }
                  if (settings.name == '/business-settings' &&
                      currentOwner() &&
                      widget.settingsService != null) {
                    return BusinessSettingsScreen(
                      businessId: selected.business!.businessId,
                      service: widget.settingsService!,
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
