import 'package:flutter/material.dart';

/// Owner entry point; other operational features remain temporary.
class OwnerHomeScreen extends StatelessWidget {
  const OwnerHomeScreen({
    super.key,
    this.onInvitations,
    this.onSettings,
    this.onCustomers,
    this.onCatalog,
  });
  final VoidCallback? onInvitations, onSettings, onCustomers, onCatalog;
  @override
  Widget build(BuildContext context) => Column(
    crossAxisAlignment: CrossAxisAlignment.stretch,
    children: [
      const Text('İşletme sahibi alanı'),
      const Text(
        'Geçici ekran. Diğer işletme yönetimi işlemleri henüz kullanıma açık değil.',
      ),
      if (onCatalog != null)
        FilledButton(
          onPressed: onCatalog,
          child: const Text('Hizmetler ve Fiyatlar'),
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
