import 'dart:io';

import 'package:academy_mobile/core/api/api_client.dart';
import 'package:flutter_test/flutter_test.dart';

const path = '/api/v1/assets/00000000-0000-0000-0000-000000000001';
void main() {
  test('Actual private asset bytes follow exactly one same-origin ticket redirect with the same bearer', () async {
    final server = await HttpServer.bind(InternetAddress.loopbackIPv4, 0);
    final seen = <Uri>[];
    final subscription = server.listen((r) async {
      seen.add(r.uri);
      expect(
        r.headers.value(HttpHeaders.authorizationHeader),
        'Bearer synthetic-token',
      );
      if (r.uri.query.isEmpty) {
        r.response.statusCode = 302;
        r.response.headers.set(
          HttpHeaders.locationHeader,
          '$path?access=ML1.synthetic',
        );
      } else {
        expect(r.uri.path, path);
        expect(r.uri.queryParameters, {'access': 'ML1.synthetic'});
        r.response.add([1, 2, 3]);
      }
      await r.response.close();
    });
    final api = HttpApiTransport(
      AppConfig('Demo', 'http://127.0.0.1:${server.port}'),
    );
    try {
      expect(await api.asset(path, 'synthetic-token'), [1, 2, 3]);
      expect(seen.length, 2);
    } finally {
      api.close();
      await subscription.cancel();
      await server.close(force: true);
    }
  });
  for (final location in [
    'http://127.0.0.1:1$path?access=ML1.synthetic',
    'https://example.test$path?access=ML1.synthetic',
    'mailto:unsafe@example.test',
    '$path?access=ML1.synthetic&extra=1',
    '$path?access=ML1.synthetic&access=ML1.other',
    '/api/v1/assets/00000000-0000-0000-0000-000000000002?access=ML1.synthetic',
    '$path?access=ML1.synthetic#fragment',
  ]) {
    test(
      'Rejects an unsafe redirect without a credential-bearing follow-up: $location',
      () async {
        final server = await HttpServer.bind(InternetAddress.loopbackIPv4, 0);
        var count = 0;
        final subscription = server.listen((r) async {
          count++;
          r.response.statusCode = 302;
          r.response.headers.set(HttpHeaders.locationHeader, location);
          await r.response.close();
        });
        final api = HttpApiTransport(
          AppConfig('Demo', 'http://127.0.0.1:${server.port}'),
        );
        try {
          await expectLater(
            api.asset(path, 'synthetic-token'),
            throwsA(isA<ApiFailure>().having((e) => e.status, 'status', 400)),
          );
          expect(count, 1);
        } finally {
          api.close();
          await subscription.cancel();
          await server.close(force: true);
        }
      },
    );
  }
  test('A second redirect is never followed and JSON commands still refuse redirects', () async {
    final server = await HttpServer.bind(InternetAddress.loopbackIPv4, 0);
    var count = 0;
    final subscription = server.listen((r) async {
      count++;
      r.response.statusCode = 302;
      r.response.headers.set(
        HttpHeaders.locationHeader,
        '$path?access=ML1.synthetic',
      );
      await r.response.close();
    });
    final api = HttpApiTransport(
      AppConfig('Demo', 'http://127.0.0.1:${server.port}'),
    );
    try {
      await expectLater(
        api.asset(path, 'synthetic-token'),
        throwsA(isA<ApiFailure>().having((e) => e.status, 'status', 302)),
      );
      expect(count, 2);
      await expectLater(
        api.send('GET', '/api/v1/unrelated', token: 'synthetic-token'),
        throwsA(isA<ApiFailure>().having((e) => e.status, 'status', 302)),
      );
      expect(count, 3);
    } finally {
      api.close();
      await subscription.cancel();
      await server.close(force: true);
    }
  });
}
