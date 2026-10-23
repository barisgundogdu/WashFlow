import 'package:flutter/material.dart';

/// Owner entry point; other operational features remain temporary.
class OwnerHomeScreen extends StatelessWidget {
  const OwnerHomeScreen({super.key, this.onInvitations});
  final VoidCallback? onInvitations;
  @override
  Widget build(BuildContext context) => Column(
    crossAxisAlignment: CrossAxisAlignment.stretch,
    children: [
      const Text('İşletme sahibi alanı'),
      const Text(
        'Geçici ekran. Diğer işletme yönetimi işlemleri henüz kullanıma açık değil.',
      ),
      if (onInvitations != null)
        FilledButton(
          onPressed: onInvitations,
          child: const Text('Çalışan davetleri'),
        ),
    ],
  );
}
