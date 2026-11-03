import 'package:flutter/material.dart';

import '../../../app/theme/washflow_theme.dart';

class OwnerHomeScreen extends StatelessWidget {
  const OwnerHomeScreen({
    super.key,
    required this.businessName,
    required this.accountActions,
    this.onInvitations,
    this.onSettings,
    this.onCustomers,
    this.onCatalog,
    this.onScheduling,
  });

  final String businessName;
  final List<Widget> accountActions;
  final VoidCallback? onInvitations,
      onSettings,
      onCustomers,
      onCatalog,
      onScheduling;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Scaffold(
      appBar: AppBar(
        title: const Text('WashFlow'),
        automaticallyImplyLeading: false,
      ),
      body: SafeArea(
        top: false,
        child: Align(
          alignment: Alignment.topCenter,
          child: SingleChildScrollView(
            padding: const EdgeInsets.fromLTRB(
              WashFlowSpacing.lg,
              WashFlowSpacing.lg,
              WashFlowSpacing.lg,
              WashFlowSpacing.xxl,
            ),
            child: ConstrainedBox(
              constraints: const BoxConstraints(maxWidth: 600),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  Text(
                    'İşletme sahibi',
                    style: theme.textTheme.labelLarge?.copyWith(
                      color: theme.colorScheme.onSurfaceVariant,
                    ),
                  ),
                  const SizedBox(height: WashFlowSpacing.xs),
                  Semantics(
                    header: true,
                    child: Text(
                      businessName,
                      softWrap: true,
                      overflow: TextOverflow.visible,
                      style: theme.textTheme.headlineLarge,
                    ),
                  ),
                  if (onScheduling != null) ...[
                    const SizedBox(height: WashFlowSpacing.xl),
                    Card(
                      margin: EdgeInsets.zero,
                      color: WashFlowTheme.ink,
                      child: Padding(
                        padding: const EdgeInsets.all(WashFlowSpacing.lg),
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.stretch,
                          children: [
                            Text(
                              'Operasyon',
                              style: theme.textTheme.titleLarge?.copyWith(
                                color: Colors.white,
                              ),
                            ),
                            const SizedBox(height: WashFlowSpacing.sm),
                            const Text(
                              'Takvimi görüntüleyin ve randevuları yönetin.',
                              style: TextStyle(color: WashFlowTheme.selected),
                            ),
                            const SizedBox(height: WashFlowSpacing.lg),
                            FilledButton(
                              onPressed: onScheduling,
                              style: FilledButton.styleFrom(
                                backgroundColor: WashFlowTheme.selected,
                                foregroundColor: WashFlowTheme.ink,
                              ),
                              child: const Text(
                                'Planlama ve randevular',
                                textAlign: TextAlign.center,
                              ),
                            ),
                          ],
                        ),
                      ),
                    ),
                  ],
                  if (onCustomers != null || onCatalog != null)
                    _Section(
                      title: 'Günlük işlemler',
                      children: [
                        if (onCustomers case final action?)
                          Card(
                            margin: EdgeInsets.zero,
                            child: ListTile(
                              leading: const Icon(
                                Icons.directions_car_outlined,
                              ),
                              title: const Text('Müşteriler ve araçlar'),
                              subtitle: const Text(
                                'Müşteri ve araç kayıtlarını yönetin.',
                              ),
                              onTap: action,
                            ),
                          ),
                        if (onCatalog case final action?)
                          Card(
                            margin: EdgeInsets.zero,
                            child: ListTile(
                              leading: const Icon(Icons.price_change_outlined),
                              title: const Text('Hizmetler ve fiyatlar'),
                              subtitle: const Text(
                                'Hizmet kataloğunu ve fiyatları düzenleyin.',
                              ),
                              onTap: action,
                            ),
                          ),
                      ],
                    ),
                  if (onSettings != null || onInvitations != null)
                    _Section(
                      title: 'İşletme yönetimi',
                      children: [
                        if (onSettings case final action?)
                          Card(
                            margin: EdgeInsets.zero,
                            clipBehavior: Clip.antiAlias,
                            child: ListTile(
                              minTileHeight: 48,
                              leading: const Icon(Icons.settings_outlined),
                              title: const Text('İşletme ayarları'),
                              onTap: action,
                            ),
                          ),
                        if (onInvitations case final action?)
                          Card(
                            margin: EdgeInsets.zero,
                            clipBehavior: Clip.antiAlias,
                            child: ListTile(
                              minTileHeight: 48,
                              leading: const Icon(Icons.group_add_outlined),
                              title: const Text('Çalışan davetleri'),
                              onTap: action,
                            ),
                          ),
                      ],
                    ),
                  _Section(title: 'Hesap işlemleri', children: accountActions),
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }
}

class _Section extends StatelessWidget {
  const _Section({required this.title, required this.children});

  final String title;
  final List<Widget> children;

  @override
  Widget build(BuildContext context) => Padding(
    padding: const EdgeInsets.only(top: WashFlowSpacing.xl),
    child: Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      spacing: WashFlowSpacing.md,
      children: [
        Semantics(
          header: true,
          child: Text(title, style: Theme.of(context).textTheme.titleMedium),
        ),
        ...children,
      ],
    ),
  );
}
