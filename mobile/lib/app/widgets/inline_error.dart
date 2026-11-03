import 'package:flutter/material.dart';

import '../theme/washflow_theme.dart';

class InlineError extends StatelessWidget {
  const InlineError(this.message, {super.key});

  final String message;

  @override
  Widget build(BuildContext context) {
    final colors = Theme.of(context).colorScheme;
    return Semantics(
      liveRegion: true,
      child: Container(
        padding: const EdgeInsets.all(WashFlowSpacing.lg),
        decoration: BoxDecoration(
          color: colors.errorContainer,
          borderRadius: WashFlowTheme.controlRadius,
        ),
        child: Text(message, style: TextStyle(color: colors.onErrorContainer)),
      ),
    );
  }
}
