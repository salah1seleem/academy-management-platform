import 'dart:io';

import 'package:integration_test/integration_test_driver_extended.dart';

Future<void> main() async {
  await integrationDriver(
    onScreenshot: (name, bytes, [args]) async {
      if (!RegExp(r'^phase-[a-z]+-[a-z0-9-]+$').hasMatch(name)) return false;
      final folder = Directory('../../tmp/mobile-visuals')
        ..createSync(recursive: true);
      await File('${folder.path}/$name.png').writeAsBytes(bytes);
      return bytes.isNotEmpty; // Capture only. Visual pass requires human/model inspection separately.
    },
  );
}
