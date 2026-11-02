import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'reports the actual native Flutter viewport before any business request',
    (t) async {
      final logical = t.view.physicalSize / t.view.devicePixelRatio;
      debugPrint(
        'Native viewport: physical=${t.view.physicalSize}, ratio=${t.view.devicePixelRatio}, logical=$logical',
      );
      await t.pumpWidget(
        MaterialApp(
          debugShowCheckedModeBanner: false,
          home: Scaffold(
            body: SafeArea(
              child: Center(
                child: Text('Viewport ${logical.width} × ${logical.height}'),
              ),
            ),
          ),
        ),
      );
      await t.pumpAndSettle();
      await binding.takeScreenshot('phase-approved-native-viewport-probe');
      const width = String.fromEnvironment('EXPECTED_LOGICAL_WIDTH');
      if (width.isNotEmpty) expect(logical.width, double.parse(width));
    },
  );
}
