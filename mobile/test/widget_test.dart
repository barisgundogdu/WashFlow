import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:washflow/app/app.dart';

void main() {
  testWidgets('Welcome is Turkish and fits a small screen with large text', (
    tester,
  ) async {
    tester.view.physicalSize = const Size(320, 568);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);

    tester.platformDispatcher.textScaleFactorTestValue = 2;
    addTearDown(tester.platformDispatcher.clearTextScaleFactorTestValue);
    await tester.pumpWidget(const WashFlowApp());
    await tester.pumpAndSettle();

    expect(find.text('WashFlow’a hoş geldiniz'), findsOneWidget);
    expect(
      find.text('Aracınız ve işletmeniz için tek bir uygulama.'),
      findsOneWidget,
    );
    expect(
      Localizations.localeOf(tester.element(find.byType(Scaffold))),
      const Locale('tr'),
    );
    expect(tester.takeException(), isNull);
  });

  testWidgets('Unknown route shows a Turkish fallback', (tester) async {
    await tester.pumpWidget(const WashFlowApp());
    await tester.pumpAndSettle();
    final context = tester.element(find.byType(Scaffold));
    Navigator.of(context).pushNamed('/unknown');
    await tester.pumpAndSettle();
    expect(find.text('Sayfa bulunamadı.'), findsOneWidget);
    expect(tester.takeException(), isNull);
  });
}
