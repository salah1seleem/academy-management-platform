import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

Future<void> scrollForNextPage(WidgetTester tester) async {
  await tester.drag(find.byType(ListView).first, const Offset(0, -700));
  await tester.pump(const Duration(milliseconds: 350));
}
