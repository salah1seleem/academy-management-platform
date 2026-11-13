import 'dart:convert';
import 'dart:io';
import 'dart:typed_data';

import 'package:academy_mobile/core/api/api_client.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  test(
    'Player exports are hash bound, return XLSX bytes and cannot commit',
    () async {
      const mime =
          'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet';
      final hash = List.filled(64, 'A').join();
      final server = await HttpServer.bind(InternetAddress.loopbackIPv4, 0);
      final workbook = Uint8List.fromList([80, 75, 3, 4]);
      var requests = 0;
      final listener = server.listen((request) async {
        requests++;
        expect(
          request.uri.toString(),
          requests == 1
              ? '/api/v1/people/import/preview?downloadValidRows=true'
              : '/api/v1/people/import/preview/corrections',
        );
        expect(request.headers.value('Idempotency-Key'), isNull);
        expect(
          request.headers.value('Authorization'),
          'Bearer synthetic-import-token',
        );
        expect(request.headers.value('Accept'), mime);
        final data = await request.fold<List<int>>([], (a, b) => a..addAll(b));
        expect(
          latin1.decode(data),
          contains('name="ExpectedFileHash"\r\n\r\n$hash\r\n'),
        );
        request.response.headers.set('Content-Type', mime);
        request.response.add(workbook);
        await request.response.close();
      });
      final api = HttpApiTransport(
        AppConfig('Demo', 'http://127.0.0.1:${server.port}'),
      );
      try {
        final result = await api.importWorkbook(
          workbook,
          false,
          {},
          'synthetic-import-token',
          validRowsFromHash: hash,
        );
        expect(result, workbook);
        expect(
          await api.importWorkbook(
            workbook,
            false,
            {},
            'synthetic-import-token',
            validRowsFromHash: hash,
            correctionRows: true,
          ),
          workbook,
        );
        await expectLater(
          api.importWorkbook(
            workbook,
            true,
            {},
            'synthetic-import-token',
            validRowsFromHash: hash,
          ),
          throwsA(isA<ApiFailure>()),
        );
        await expectLater(
          api.importWorkbook(
            workbook,
            false,
            {},
            'synthetic-import-token',
            validRowsFromHash: 'wrong',
          ),
          throwsA(isA<ApiFailure>()),
        );
        await expectLater(
          api.importWorkbook(
            workbook,
            false,
            {},
            'synthetic-import-token',
            correctionRows: true,
          ),
          throwsA(isA<ApiFailure>()),
        );
        expect(requests, 2);
      } finally {
        api.close();
        await listener.cancel();
        await server.close(force: true);
      }
    },
  );

  test(
    'Player export rejects an error document with the workbook media type',
    () async {
      final server = await HttpServer.bind(InternetAddress.loopbackIPv4, 0);
      final listener = server.listen((request) async {
        await request.drain<void>();
        request.response.headers.set(
          'Content-Type',
          'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
        );
        request.response.write('{"title":"unavailable"}');
        await request.response.close();
      });
      final api = HttpApiTransport(
        AppConfig('Demo', 'http://127.0.0.1:${server.port}'),
      );
      try {
        await expectLater(
          api.importWorkbook(
            Uint8List.fromList([80, 75, 3, 4]),
            false,
            {},
            'synthetic-token',
            validRowsFromHash: List.filled(64, 'A').join(),
            correctionRows: true,
          ),
          throwsA(
            isA<ApiFailure>().having((error) => error.status, 'status', 422),
          ),
        );
      } finally {
        api.close();
        await listener.cancel();
        await server.close(force: true);
      }
    },
  );

  test('Workbook multipart preserves preview/commit fields and header-only authorization without automatic retries', () async {
    final server = await HttpServer.bind(InternetAddress.loopbackIPv4, 0);
    final paths = <String>[];
    final bodies = <String>[];
    final listener = server.listen((request) async {
      paths.add(request.uri.toString());
      expect(request.uri.query, isEmpty);
      expect(
        request.headers.value('Authorization'),
        'Bearer synthetic-import-token',
      );
      expect(request.headers.contentType!.mimeType, 'multipart/form-data');
      final bytes = await request.fold<List<int>>([], (a, b) => a..addAll(b));
      bodies.add(latin1.decode(bytes));
      expect(bodies.last, isNot(contains('synthetic-import-token')));
      expect(bodies.last, contains('filename="academy-import.xlsx"'));
      expect(
        bodies.last,
        contains(
          'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
        ),
      );
      if (request.uri.path.endsWith('/commit')) {
        expect(request.headers.value('Idempotency-Key'), 'one-import-intent');
        request.response.statusCode = 202;
      }
      request.response.headers.contentType = ContentType.json;
      request.response.write(
        jsonEncode({'id': 'job-synthetic', 'status': 'Queued'}),
      );
      await request.response.close();
    });
    final api = HttpApiTransport(
      AppConfig('Demo', 'http://127.0.0.1:${server.port}'),
    );
    try {
      final bytes = Uint8List.fromList([80, 75, 3, 4]);
      await api.importWorkbook(bytes, false, {}, 'synthetic-import-token');
      final fields = {
        'ExpectedFileHash': 'hash',
        'ExpectedReferenceStamp': 'stamp',
        'GuardianLinksApproved': 'true',
        'OpeningBalancesApproved': 'true',
        'AcceptFinancialReview': 'true',
        'GuardianResolutions': '{"ref":"guardian"}',
      };
      final result = await api.importWorkbook(
        bytes,
        true,
        fields,
        'synthetic-import-token',
        idempotencyKey: 'one-import-intent',
      );
      expect((result as Map)['status'], 'Queued');
      expect(paths, [
        '/api/v1/people/import/preview',
        '/api/v1/people/import/commit',
      ]);
      for (final field in fields.entries) {
        expect(
          bodies.last,
          contains('name="${field.key}"\r\n\r\n${field.value}\r\n'),
        );
      }
      for (final forged in [
        {'AcademyId': 'other'},
        {'unknown': 'value'},
      ]) {
        await expectLater(
          api.importWorkbook(bytes, false, forged, 'synthetic-import-token'),
          throwsA(isA<ApiFailure>().having((e) => e.status, 'status', 422)),
        );
      }
      await expectLater(
        api.importWorkbook(
          Uint8List(5 * 1024 * 1024 + 1),
          false,
          {},
          'synthetic-import-token',
        ),
        throwsA(isA<ApiFailure>()),
      );
      await expectLater(
        api.importWorkbook(
          bytes,
          true,
          {},
          'synthetic-import-token',
          idempotencyKey: 'bad\r\nheader',
        ),
        throwsA(isA<ApiFailure>()),
      );
      expect(paths, hasLength(2));
    } finally {
      api.close();
      await listener.cancel();
      await server.close(force: true);
    }
  });
  for (final status in [302, 401, 403, 409, 422, 429]) {
    test(
      'Workbook failure $status is bounded Arabic, never follows redirects or replays a mutation',
      () async {
        final server = await HttpServer.bind(InternetAddress.loopbackIPv4, 0);
        var requests = 0;
        final listener = server.listen((request) async {
          requests++;
          await request.drain<void>();
          request.response.statusCode = status;
          if (status == 302) {
            request.response.headers.set(
              'Location',
              'http://127.0.0.1:${server.port}/leaked',
            );
          }
          request.response.write('<html>synthetic private stack trace</html>');
          await request.response.close();
        });
        final api = HttpApiTransport(
          AppConfig('Demo', 'http://127.0.0.1:${server.port}'),
        );
        try {
          await expectLater(
            api.importWorkbook(
              Uint8List.fromList([80, 75, 3, 4]),
              true,
              {},
              'synthetic-import-token',
              idempotencyKey: 'one-import-intent',
            ),
            throwsA(
              isA<ApiFailure>()
                  .having((e) => e.status, 'status', status)
                  .having((e) => e.message, 'Arabic', matches(RegExp('[ء-ي]')))
                  .having(
                    (e) => e.message,
                    'no server details',
                    isNot(contains('stack trace')),
                  ),
            ),
          );
          expect(requests, 1);
          if (status == 422) {
            await expectLater(
              api.importWorkbook(
                Uint8List.fromList([1, 2, 3]),
                false,
                {},
                'synthetic-import-token',
              ),
              throwsA(
                isA<ApiFailure>()
                    .having((e) => e.status, 'status', 422)
                    .having(
                      (e) => e.message,
                      'preview recovery',
                      'تعذّر قراءة ملف Excel. اختر ملف XLSX صالحًا من القالب الحالي.',
                    ),
              ),
            );
            expect(requests, 2);
          }
        } finally {
          api.close();
          await listener.cancel();
          await server.close(force: true);
        }
      },
    );
  }
}
