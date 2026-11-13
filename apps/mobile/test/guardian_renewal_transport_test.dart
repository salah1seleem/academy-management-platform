import 'dart:io';

import 'package:academy_mobile/core/api/api_client.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  test('HTTP renewal conflicts preserve only the recognized code', () async {
    const root = '/api/v1/guardian/subscriptions';
    const code = 'renewal_already_open';
    const response = '{"code":"$code","message":"private metadata"}';
    final cases = [
      ('$root/renewals', 409, response, code),
      ('$root/external/renewals', 409, response, code),
      ('$root/external/renewals', 409, '<html>private metadata</html>', null),
      ('$root/external/renewals', 409, '{"code":"unknown"}', null),
      ('$root/external/renewals', 422, response, null),
      ('$root/payments', 409, response, null),
    ];
    final server = await HttpServer.bind(InternetAddress.loopbackIPv4, 0);
    final api = HttpApiTransport(
      AppConfig('Demo', 'http://127.0.0.1:${server.port}'),
    );
    var requests = 0;
    final listener = server.listen((request) async {
      final (path, status, body, _) = cases[requests++];
      expect(request.uri.path, path);
      await request.drain<void>();
      request.response.statusCode = status;
      request.response.write(body);
      await request.response.close();
    });
    try {
      for (final (path, status, _, expectedCode) in cases) {
        await expectLater(
          api.send('POST', path, body: const {'subscriptionPlanId': 'plan'}),
          throwsA(
            isA<ApiFailure>()
                .having((error) => error.status, 'status', status)
                .having((error) => error.code, 'recognized code', expectedCode)
                .having(
                  (error) => error.message,
                  'localized message',
                  isNot(contains('private metadata')),
                ),
          ),
        );
      }
      expect(requests, cases.length);
    } finally {
      api.close();
      await listener.cancel();
      await server.close(force: true);
    }
  });
}
