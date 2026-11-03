import 'package:flutter/material.dart';
import 'package:flutter/rendering.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:washflow/app/theme/washflow_theme.dart';
import 'package:washflow/features/owner/presentation/owner_home_screen.dart';

void main() {
  testWidgets('owner dashboard actions fit compact viewport at large text', (
    tester,
  ) async {
    tester.view.physicalSize = const Size(320, 640);
    tester.view.devicePixelRatio = 1;
    tester.platformDispatcher.textScaleFactorTestValue = 2;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    addTearDown(tester.platformDispatcher.clearTextScaleFactorTestValue);

    const businessName =
        'İstanbulProfesyonelAraçYıkamaVeBakımMerkezi İstanbul Avrupa Yakası';
    final activated = <String>[];
    await tester.pumpWidget(
      MaterialApp(
        theme: WashFlowTheme.light,
        home: OwnerHomeScreen(
          businessName: businessName,
          onScheduling: () => activated.add('planlama'),
          onCustomers: () => activated.add('müşteriler'),
          onCatalog: () => activated.add('hizmetler'),
          onSettings: () => activated.add('ayarlar'),
          onInvitations: () => activated.add('davetler'),
          accountActions: [
            TextButton(
              onPressed: () => activated.add('yenileme'),
              child: const Text('Yenile'),
            ),
          ],
        ),
      ),
    );

    expect(find.byType(FilledButton), findsOneWidget);
    final title = find.text(businessName);
    final paragraph = tester.renderObject<RenderParagraph>(title);
    final lines = paragraph.getBoxesForSelection(
      TextSelection(baseOffset: 0, extentOffset: businessName.length),
    );
    expect(lines.length, greaterThan(1));
    expect(paragraph.didExceedMaxLines, isFalse);
    for (final line in lines) {
      expect(line.right, lessThanOrEqualTo(paragraph.size.width + 0.01));
      expect(line.bottom, lessThanOrEqualTo(paragraph.size.height + 0.01));
    }
    expect(
      tester.getRect(title).height,
      greaterThan(WashFlowTheme.light.textTheme.headlineLarge!.fontSize! * 2),
    );
    final settings = find.ancestor(
      of: find.text('İşletme ayarları'),
      matching: find.byType(Card),
    );
    final invitations = find.ancestor(
      of: find.text('Çalışan davetleri'),
      matching: find.byType(Card),
    );
    expect(settings, findsOneWidget);
    expect(invitations, findsOneWidget);
    expect(
      tester.getRect(invitations).top - tester.getRect(settings).bottom,
      WashFlowSpacing.md,
    );
    for (final (label, action) in [
      ('Planlama ve randevular', 'planlama'),
      ('Müşteriler ve araçlar', 'müşteriler'),
      ('Hizmetler ve fiyatlar', 'hizmetler'),
      ('İşletme ayarları', 'ayarlar'),
      ('Çalışan davetleri', 'davetler'),
      ('Yenile', 'yenileme'),
    ]) {
      final text = find.text(label);
      await tester.ensureVisible(text);
      await tester.tap(text);
      await tester.pumpAndSettle();
      expect(activated.last, action);
      expect(tester.takeException(), isNull);
    }

    for (final type in [FilledButton, ListTile, TextButton]) {
      for (final element in find.byType(type).evaluate()) {
        expect(element.size!.height, greaterThanOrEqualTo(48));
      }
    }
  });
}
