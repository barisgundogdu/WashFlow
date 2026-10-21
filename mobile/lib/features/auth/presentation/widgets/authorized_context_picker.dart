import 'package:flutter/material.dart';

/// Future API-provided authorized options, never a public role selector.
class AuthorizedContextOption {
  const AuthorizedContextOption({required this.id, required this.label});
  final String id;
  final String label;
}

class AuthorizedContextPicker extends StatelessWidget {
  const AuthorizedContextPicker({
    super.key,
    required this.options,
    required this.onSelected,
  });
  final List<AuthorizedContextOption> options;
  final ValueChanged<String> onSelected;

  @override
  Widget build(BuildContext context) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Text(
          'Hesap bağlamınızı seçin',
          style: Theme.of(context).textTheme.titleLarge,
        ),
        const SizedBox(height: 12),
        if (options.isEmpty)
          const Text('Kullanılabilir hesap bağlamı bulunamadı.'),
        for (final option in options)
          OutlinedButton(
            onPressed: () => onSelected(option.id),
            child: Text(option.label),
          ),
      ],
    );
  }
}
