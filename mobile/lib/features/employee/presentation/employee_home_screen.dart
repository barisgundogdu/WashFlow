import 'package:flutter/material.dart';

/// Temporary destination; assigned-work operations are not implemented yet.
class EmployeeHomeScreen extends StatelessWidget {
  const EmployeeHomeScreen({super.key});
  @override
  Widget build(BuildContext context) => const Column(
    crossAxisAlignment: CrossAxisAlignment.stretch,
    children: [
      Text('Çalışan alanı'),
      Text('Geçici ekran. Atanan işler henüz kullanıma açık değil.'),
    ],
  );
}
