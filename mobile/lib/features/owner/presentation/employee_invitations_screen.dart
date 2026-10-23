import 'dart:async';

import 'package:flutter/material.dart';

import '../../auth/domain/auth_service.dart';
import '../../invitations/domain/invitation.dart';
import '../../invitations/domain/owner_invitation_service.dart';
import 'employee_invitations_controller.dart';

/// Created only inside the currently authorized Owner context's navigator.
class EmployeeInvitationsScreen extends StatefulWidget {
  const EmployeeInvitationsScreen({
    super.key,
    required this.businessId,
    required this.businessName,
    required this.service,
    required this.isCurrent,
    required this.onAuthorizationFailure,
    required this.onSwitch,
    required this.onLogout,
  });
  final String businessId, businessName;
  final OwnerInvitationService service;
  final bool Function() isCurrent;
  final Future<void> Function(AuthError) onAuthorizationFailure;
  final VoidCallback onSwitch, onLogout;
  @override
  State<EmployeeInvitationsScreen> createState() =>
      _EmployeeInvitationsScreenState();
}

class _EmployeeInvitationsScreenState extends State<EmployeeInvitationsScreen> {
  late final _controller = EmployeeInvitationsController(
    businessId: widget.businessId,
    service: widget.service,
    isCurrent: widget.isCurrent,
    onAuthorizationFailure: widget.onAuthorizationFailure,
  );
  final _email = TextEditingController();
  bool _confirming = false;
  Timer? _clock;
  @override
  void initState() {
    super.initState();
    _controller.addListener(_changed);
    unawaited(_controller.load());
    // Status is a display hint; server eligibility is authoritative for writes.
    _clock = Timer.periodic(const Duration(minutes: 1), (_) {
      if (mounted) setState(() {});
    });
  }

  void _changed() {
    if (mounted) setState(() {});
  }

  @override
  void dispose() {
    _clock?.cancel();
    _controller.removeListener(_changed);
    _controller.dispose();
    _email.dispose();
    super.dispose();
  }

  Future<void> _create() async {
    FocusScope.of(context).unfocus();
    final recorded = await _controller.create(_email.text);
    if (mounted && widget.isCurrent() && recorded) _email.clear();
  }

  Future<void> _revoke(EmployeeInvitation invitation) async {
    if (_confirming || !_controller.canMutate || !widget.isCurrent()) return;
    _confirming = true;
    final confirmed = await showDialog<bool>(
      context: context,
      useRootNavigator: false,
      builder: (dialog) => AlertDialog(
        title: const Text('Daveti iptal et'),
        content: Text(
          '${invitation.email} adresine ait davet iptal edilsin mi?',
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(dialog, false),
            child: const Text('Vazgeç'),
          ),
          FilledButton(
            onPressed: () => Navigator.pop(dialog, true),
            child: const Text('İptali onayla'),
          ),
        ],
      ),
    );
    _confirming = false;
    if (mounted && widget.isCurrent() && confirmed == true) {
      await _controller.revoke(invitation.id);
    }
  }

  String _expiry(BuildContext context, EmployeeInvitation invitation) {
    final date = invitation.expiresAt.toLocal();
    final locale = MaterialLocalizations.of(context);
    return '${locale.formatMediumDate(date)} ${locale.formatTimeOfDay(TimeOfDay.fromDateTime(date), alwaysUse24HourFormat: true)}';
  }

  @override
  Widget build(BuildContext context) {
    final controller = _controller;
    return Scaffold(
      appBar: AppBar(title: const Text('Çalışan davetleri')),
      body: SafeArea(
        child: SingleChildScrollView(
          padding: const EdgeInsets.all(24),
          child: Center(
            child: ConstrainedBox(
              constraints: const BoxConstraints(maxWidth: 600),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  Text(
                    widget.businessName,
                    style: Theme.of(context).textTheme.headlineSmall,
                  ),
                  const Text(
                    'Davet kaydı oluşturulması, e-postanın teslim edildiği anlamına gelmez.',
                  ),
                  const SizedBox(height: 16),
                  TextField(
                    key: const ValueKey('invitation-email'),
                    controller: _email,
                    enabled: controller.canMutate,
                    keyboardType: TextInputType.emailAddress,
                    autocorrect: false,
                    enableSuggestions: false,
                    decoration: const InputDecoration(
                      labelText: 'Çalışanın e-posta adresi',
                    ),
                  ),
                  FilledButton(
                    onPressed: controller.canMutate ? _create : null,
                    child: const Text('Davet oluştur'),
                  ),
                  if (controller.mutating)
                    const Text(
                      'İşlem sürüyor. Sonuç güncel listeyle kontrol edilecek.',
                    ),
                  if (controller.message case final message?)
                    Semantics(liveRegion: true, child: Text(message)),
                  if (controller.error case final error?)
                    Semantics(liveRegion: true, child: Text(error)),
                  if (controller.phase == InvitationListPhase.loading)
                    const Center(child: CircularProgressIndicator())
                  else if (controller.phase == InvitationListPhase.ready &&
                      controller.items.isEmpty)
                    const Text('Bu işletmede henüz davet yok.'),
                  for (final invitation in controller.items)
                    Card(
                      key: ValueKey(invitation.id),
                      child: Padding(
                        padding: const EdgeInsets.all(16),
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.stretch,
                          children: [
                            Text(invitation.email),
                            Text(invitation.statusLabel(DateTime.now())),
                            Text(
                              'Son geçerlilik: ${_expiry(context, invitation)}',
                            ),
                            Wrap(
                              spacing: 8,
                              children: [
                                TextButton(
                                  onPressed:
                                      controller.canMutate &&
                                          invitation.canResend
                                      ? () => controller.resend(invitation.id)
                                      : null,
                                  child: const Text('Yeniden gönder'),
                                ),
                                TextButton(
                                  onPressed:
                                      controller.canMutate &&
                                          invitation.canRevoke
                                      ? () => _revoke(invitation)
                                      : null,
                                  child: const Text('Daveti iptal et'),
                                ),
                              ],
                            ),
                          ],
                        ),
                      ),
                    ),
                  TextButton(
                    onPressed:
                        !controller.mutating &&
                            controller.phase != InvitationListPhase.loading &&
                            controller.phase != InvitationListPhase.blocked
                        ? controller.load
                        : null,
                    child: Text(
                      controller.phase == InvitationListPhase.failed
                          ? 'Listeyi yeniden dene'
                          : 'Listeyi yenile',
                    ),
                  ),
                  TextButton(
                    onPressed: widget.onSwitch,
                    child: const Text('Hesap değiştir'),
                  ),
                  TextButton(
                    onPressed: widget.onLogout,
                    child: const Text('Çıkış yap'),
                  ),
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }
}
