// Fixture preparation only. This is not acceptance of the product upload/gallery workflow.
import 'dart:io';
import 'dart:typed_data';

import 'package:file_picker/file_picker.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

void main() {
  IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'prepare two synthetic local files with the real iOS export picker',
    (t) async {
      expect(const bool.fromEnvironment('ISOLATED_DEMO'), isTrue);
      expect((t.view.physicalSize / t.view.devicePixelRatio).width, 375);
      var saving = false;
      String? error;
      final saved = <String>{};
      const files = [
        (
          'synthetic-training.png',
          'I-Academy-Photo-Test.png',
          'image/png',
          'حفظ صورة اختبار',
        ),
        (
          'synthetic-training.mp4',
          'I-Academy-Video-Test.mp4',
          'video/mp4',
          'حفظ فيديو اختبار',
        ),
      ];
      await t.pumpWidget(
        MaterialApp(
          home: StatefulBuilder(
            builder: (context, setState) => Scaffold(
              body: SafeArea(
                child: Directionality(
                  textDirection: TextDirection.rtl,
                  child: Padding(
                    padding: const EdgeInsets.all(20),
                    child: Column(
                      children: [
                        const Text('تجهيز ملفات وسائط اصطناعية للاختبار'),
                        for (final fixture in files)
                          FilledButton(
                            onPressed: saving
                                ? null
                                : () async {
                                    setState(() => saving = true);
                                    final client = HttpClient();
                                    try {
                                      final request = await client.getUrl(
                                        Uri.parse(
                                          'http://127.0.0.1:4183/${fixture.$1}',
                                        ),
                                      );
                                      request.followRedirects = false;
                                      final response = await request.close();
                                      if (response.statusCode != 200) {
                                        throw StateError(
                                          'تعذر قراءة ملف الاختبار المحلي',
                                        );
                                      }
                                      final bytes = BytesBuilder();
                                      await for (final chunk in response) {
                                        bytes.add(chunk);
                                      }
                                      final data = bytes.takeBytes();
                                      expect(data.length, greaterThan(500));
                                      final result = await FilePicker.saveFile(
                                        fileName: fixture.$2,
                                        bytes: data,
                                        mimeType: fixture.$3,
                                        dialogTitle: 'حفظ ملف اختبار اصطناعي',
                                      );
                                      if (result == null) {
                                        throw StateError(
                                          'لم يتم حفظ ملف الاختبار',
                                        );
                                      }
                                      saved.add(fixture.$2);
                                      debugPrint(
                                        'Synthetic OS fixture saved: ${fixture.$2} / ${data.length} bytes',
                                      );
                                    } catch (e) {
                                      error = e.toString();
                                    } finally {
                                      client.close();
                                      setState(() => saving = false);
                                    }
                                  },
                            child: Text(fixture.$4),
                          ),
                        if (saving) const Text('بانتظار حفظ ملف الاختبار'),
                        if (error != null)
                          const Text('تعذر تجهيز ملف الاختبار'),
                      ],
                    ),
                  ),
                ),
              ),
            ),
          ),
        ),
      );
      for (final fixture in files) {
        await t.tap(find.text(fixture.$4));
        await t.pump(const Duration(milliseconds: 300));
        debugPrint('FIXTURE_EXPORT_WAIT: ${fixture.$2}');
        for (var i = 0; i < 1800 && saving; i++) {
          await t.pump(const Duration(milliseconds: 150));
        }
        expect(saving, isFalse);
        expect(error, isNull, reason: error);
        expect(saved, contains(fixture.$2));
      }
      expect(saved, hasLength(2));
      expect(t.takeException(), isNull);
    },
  );
}
