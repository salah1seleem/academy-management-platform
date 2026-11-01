import 'package:academy_mobile/features/shared/mobile_push.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'v2_role_workspaces_test.dart' as fixtures;

void main() {
  TestWidgetsFlutterBinding.ensureInitialized();
  Map<String, dynamic> state = {};
  setUp(
    () => TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger
        .setMockMethodCallHandler(
          MobilePushService.channel,
          (call) async => state,
        ),
  );
  tearDown(
    () => TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger
        .setMockMethodCallHandler(MobilePushService.channel, null),
  );
  test('only an authorized native token registers; denied permission revokes this session device', () async {
    final writes = <String>[];
    final auth = await fixtures.authenticate('Guardian', (m, p, b, t) async {
      writes.add('$m $p');
      if (m == 'PUT') {
        expect(b, {'provider': 'APNs', 'token': 'synthetic-device-token'});
      }
      return null;
    }, userId: 'synthetic-push-user');
    state = {'configured': false};
    await MobilePushService().register(auth);
    expect(writes, isEmpty);
    state = {
      'permission': 'granted',
      'provider': 'APNs',
      'token': 'synthetic-device-token',
    };
    await MobilePushService().register(auth);
    expect(writes.single, 'PUT /api/v1/mobile-push/device');
    state = {'permission': 'denied'};
    await MobilePushService().register(auth);
    expect(writes.last, 'DELETE /api/v1/mobile-push/device');
  });
  testWidgets(
    'an intent for another academy never renders a private notification or fetches its target',
    (t) async {
      final reads = <String>[];
      final auth = await fixtures.authenticate('Guardian', (
        m,
        p,
        b,
        token,
      ) async {
        reads.add(p);
        return {};
      });
      pushIntent(auth).value = {
        'academyId': 'foreign',
        'noticeId': 'foreign-notice',
      };
      await t.pumpWidget(
        fixtures.app(PushIntentGate(auth: auth, child: const SizedBox())),
      );
      await t.pumpAndSettle();
      expect(reads, isEmpty);
    },
  );
}
