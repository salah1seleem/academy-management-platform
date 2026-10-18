import 'dart:convert';
import 'dart:io';
import 'dart:typed_data';

import 'package:academy_mobile/core/api/api_client.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  test('Actual multipart transport sends dated media privacy fields but does not expand unrelated upload purposes', () async {
    final server = await HttpServer.bind(InternetAddress.loopbackIPv4, 0),
        requests = <String>[];
    final listener = server.listen((r) async {
      expect(r.uri.path, '/api/v1/assets/upload');
      expect(
        r.headers.value(HttpHeaders.authorizationHeader),
        'Bearer synthetic-test-token',
      );
      expect(r.headers.contentType!.mimeType, 'multipart/form-data');
      final bytes = await r.fold<List<int>>([], (a, b) => a..addAll(b));
      requests.add(latin1.decode(bytes));
      r.response.statusCode = 201;
      r.response.headers.contentType = ContentType.json;
      r.response.write(
        jsonEncode({'id': 'synthetic', 'url': '/api/v1/assets/synthetic'}),
      );
      await r.response.close();
    });
    final api = HttpApiTransport(
      AppConfig('Demo', 'http://127.0.0.1:${server.port}'),
    );
    try {
      final fields = {
        'purpose': 'Media',
        'groupId': 'group-test',
        'eventOn': '2026-10-18',
        'subjectPlayerIds': 'child-test',
        'noRecognizableChildren': 'false',
        'privacyConfirmed': 'true',
        'sessionId': 'session-test',
      };
      await api.upload(
        Uint8List.fromList(List.filled(20, 0)),
        'image/png',
        fields,
        'synthetic-test-token',
      );
      expect(requests, hasLength(1));
      for (final e in fields.entries) {
        expect(
          requests.single,
          contains('name="${e.key}"\r\n\r\n${e.value}\r\n'),
        );
      }
      await expectLater(
        api.upload(Uint8List(20), 'image/png', {
          'purpose': 'Proof',
          'sessionId': 'session-test',
        }, 'synthetic-test-token'),
        throwsA(isA<ApiFailure>().having((e) => e.status, 'status', 400)),
      );
      await expectLater(
        api.upload(Uint8List(20), 'image/png', {
          'purpose': 'Media',
          'AcademyId': 'forged',
        }, 'synthetic-test-token'),
        throwsA(isA<ApiFailure>().having((e) => e.status, 'status', 400)),
      );
      expect(requests, hasLength(1));
    } finally {
      api.close();
      await listener.cancel();
      await server.close(force: true);
    }
  });
  test('Rejected privacy response uses a bounded human message and never exposes server HTML or stack traces', () async {
    final server = await HttpServer.bind(InternetAddress.loopbackIPv4, 0);
    final listener = server.listen((r) async {
      await r.drain<void>();
      r.response.statusCode = 422;
      r.response.write('<html>synthetic stack trace secret</html>');
      await r.response.close();
    });
    final api = HttpApiTransport(
      AppConfig('Demo', 'http://127.0.0.1:${server.port}'),
    );
    try {
      await expectLater(
        api.upload(Uint8List(20), 'image/png', {
          'purpose': 'Media',
          'privacyConfirmed': 'true',
        }, 'synthetic-test-token'),
        throwsA(
          isA<ApiFailure>()
              .having((e) => e.status, 'status', 422)
              .having((e) => e.message, 'message', contains('موافقات الأطفال'))
              .having(
                (e) => e.message,
                'no leaked HTML',
                isNot(contains('secret')),
              ),
        ),
      );
    } finally {
      api.close();
      await listener.cancel();
      await server.close(force: true);
    }
  });
}
