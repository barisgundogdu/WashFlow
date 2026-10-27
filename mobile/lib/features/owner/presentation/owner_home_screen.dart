import 'package:flutter/material.dart';

/// Owner entry point; other operational features remain temporary.
class OwnerHomeScreen extends StatelessWidget {
  const OwnerHomeScreen({
    super.key,
    this.onInvitations,
    this.onSettings,
    this.onCustomers,
  });
  final VoidCallback? onInvitations, onSettings, onCustomers;
  @override
  Widget build(BuildContext context) => Column(
    crossAxisAlignment: CrossAxisAlignment.stretch,
    children: [
      const Text('İşletme sahibi alanı'),
      const Text(
        'Geçici ekran. Diğer işletme yönetimi işlemleri henüz kullanıma açık değil.',
      ),
      if (onCustomers != null)
        FilledButton(
          onPressed: onCustomers,
          child: const Text('Müşteriler ve araçlar'),
        ),
      if (onSettings != null)
        FilledButton(
          onPressed: onSettings,
          child: const Text('İşletme ayarları'),
        ),
      if (onInvitations != null)
        FilledButton(
          onPressed: onInvitations,
          child: const Text('Çalışan davetleri'),
        ),
    ],
  );
}
