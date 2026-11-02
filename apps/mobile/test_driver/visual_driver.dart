import 'dart:io';
import 'dart:convert';

import 'package:flutter_driver/flutter_driver.dart';
import 'package:integration_test/integration_test_driver_extended.dart';

Future<void> main() async {
  final captures = <Map<String, Object>>[];
  await integrationDriver(
    // The default wire log repeats PNG byte arrays (2 GB in the last run).
    // Keep real PNGs, test stdout and the compact report, not that duplicate.
    driver: await FlutterDriver.connect(logCommunicationToFile: false),
    // PNGs are already saved below. Do not JSON-encode their byte arrays again:
    // large multi-role runs exhausted disk while writing the duplicate report.
    responseDataCallback: (data) async {
      final proposed =
          Platform.environment['ACADEMY_VISUAL_RUN'] ?? 'last-visual';
      final run = RegExp(r'^[a-z0-9-]+$').hasMatch(proposed)
          ? proposed
          : 'last-visual';
      final folder = Directory('../../tmp/mobile-visuals')
        ..createSync(recursive: true);
      await File('${folder.path}/$run-response.json').writeAsString(
        jsonEncode({
          'allTestsPassed': true,
          'visualReviewRequired': true,
          'screenshots': captures,
          if (data?['frameMetrics'] is Map)
            'frameMetrics': data!['frameMetrics'],
          if (data?['selectionVerified'] is bool)
            'selectionVerified': data!['selectionVerified'],
          if (data?['fallbackVerified'] is bool)
            'fallbackVerified': data!['fallbackVerified'],
          if (data?['nativeErrorObserved'] is String)
            'nativeErrorObserved': data!['nativeErrorObserved'],
          if (data?['source'] is String) 'source': data!['source'],
          if (data?['after'] is Map)
            'nativeIconState': {
              for (final key in [
                'supported',
                'currentKey',
                'academyId',
                'build',
                'pending',
              ])
                key: (data!['after'] as Map)[key],
            },
          if (data?['source'] == 'real-isolated-api')
            'professionalDemo': {
              for (final key in [
                'players',
                'sports',
                'stockBefore',
                'stockAfter',
                'productSaleId',
              ])
                key: data?[key],
            },
          'responseKeys': data?.keys.toList() ?? <String>[],
        }),
      );
    },
    onScreenshot: (name, bytes, [args]) async {
      if (!RegExp(r'^phase-[a-z]+-[a-z0-9-]+$').hasMatch(name)) return false;
      final folder = Directory('../../tmp/mobile-visuals')
        ..createSync(recursive: true);
      await File('${folder.path}/$name.png').writeAsBytes(bytes);
      captures.add({'name': name, 'byteLength': bytes.length});
      return bytes.isNotEmpty; // Capture only. Visual pass requires human/model inspection separately.
    },
  );
}
