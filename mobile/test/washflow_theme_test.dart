import 'dart:math' as math;

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:washflow/app/theme/washflow_theme.dart';
import 'package:washflow/app/widgets/inline_error.dart';

double contrast(Color foreground, Color background) {
  final first = foreground.computeLuminance();
  final second = background.computeLuminance();
  return (math.max(first, second) + 0.05) / (math.min(first, second) + 0.05);
}

void main() {
  final theme = WashFlowTheme.light;
  final colors = theme.colorScheme;

  test('theme text pairs meet 4.5:1 and control outline meets 3:1', () {
    for (final (foreground, background) in [
      (colors.onPrimary, colors.primary),
      (colors.onPrimaryContainer, colors.primaryContainer),
      (colors.onSecondaryContainer, colors.secondaryContainer),
      (colors.onSurface, colors.surface),
      (colors.onSurface, theme.scaffoldBackgroundColor),
      (colors.onSurfaceVariant, colors.surface),
      (colors.onSurfaceVariant, theme.scaffoldBackgroundColor),
      (colors.primary, theme.scaffoldBackgroundColor),
      (colors.error, colors.surface),
      (colors.onError, colors.error),
      (colors.onErrorContainer, colors.errorContainer),
    ]) {
      expect(contrast(foreground, background), greaterThanOrEqualTo(4.5));
    }
    expect(contrast(colors.outline, colors.surface), greaterThanOrEqualTo(3));
  });

  testWidgets('common actions provide 48dp and errors remain live regions', (
    tester,
  ) async {
    final semantics = tester.ensureSemantics();
    const message = 'İşlem tamamlanamadı.';
    await tester.pumpWidget(
      MaterialApp(
        theme: theme,
        home: Scaffold(
          body: Column(
            children: [
              FilledButton(onPressed: () {}, child: const Text('Kaydet')),
              OutlinedButton(onPressed: () {}, child: const Text('Yenile')),
              TextButton(onPressed: () {}, child: const Text('Vazgeç')),
              const InlineError(message),
            ],
          ),
        ),
      ),
    );

    for (final type in [FilledButton, OutlinedButton, TextButton]) {
      final size = tester.getSize(find.byType(type));
      expect(size.width, greaterThanOrEqualTo(48));
      expect(size.height, greaterThanOrEqualTo(48));
    }
    expect(
      tester.getSemantics(find.text(message)),
      matchesSemantics(label: message, isLiveRegion: true),
    );
    semantics.dispose();
  });
}
