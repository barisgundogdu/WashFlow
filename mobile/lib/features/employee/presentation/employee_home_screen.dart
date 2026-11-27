import 'package:flutter/material.dart';

class EmployeeHomeScreen extends StatelessWidget {
  const EmployeeHomeScreen({super.key, this.onWorkOrders});
  final VoidCallback? onWorkOrders;
  @override
  Widget build(BuildContext context) => Column(
    crossAxisAlignment: CrossAxisAlignment.stretch,
    children: [
      const Text('Çalışan alanı'),
      if (onWorkOrders != null)
        FilledButton.icon(
          onPressed: onWorkOrders,
          icon: const Icon(Icons.assignment),
          label: const Text('Atanan işlerim'),
        )
      else
        const Text('İş emri bağlantısı yapılandırılmamış.'),
    ],
  );
}
