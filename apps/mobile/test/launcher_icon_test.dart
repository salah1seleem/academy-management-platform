import 'package:academy_mobile/features/shared/launcher_icon.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';

import 'v2_role_workspaces_test.dart' as fixtures;

void main() {
  TestWidgetsFlutterBinding.ensureInitialized();
  Map<String, dynamic> native = {};
  final sets = <Map<dynamic, dynamic>>[];
  setUp(() {
    native = {
      'supported': true,
      'keys': ['professional-demo'],
      'currentKey': 'professional-demo',
      'academyId': 'previous-academy',
      'build': 1,
      'pending': false,
    };
    sets.clear();
    TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger
        .setMockMethodCallHandler(LauncherIconService.channel, (call) async {
          if (call.method == 'state') return native;
          if (call.method == 'set') {
            final args = call.arguments as Map;
            sets.add(args);
            native = {
              ...native,
              'currentKey': args['key'],
              'academyId': args['academyId'],
            };
            return null;
          }
          throw MissingPluginException();
        });
  });
  tearDown(
    () => TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger
        .setMockMethodCallHandler(LauncherIconService.channel, null),
  );
  test('another current academy preserves the previously chosen authorized installed icon', () async {
    final auth = await fixtures.authenticate('Guardian', (m, p, b, t) async {
      expect(p, contains('selectionAcademyId=previous-academy'));
      return {'selectedAllowed': true, 'academyId': 'academy', 'key': null};
    });
    await LauncherIconService().reconcile(auth);
    expect(sets, isEmpty);
  });
  test('live revocation restores the platform icon while offline leaves the installed choice intact', () async {
    final revoked = await fixtures.authenticate(
      'Guardian',
      (m, p, b, t) async => {'selectedAllowed': false},
    );
    await LauncherIconService().reconcile(revoked);
    expect(sets.single['key'], isNull);
    sets.clear();
    native['currentKey'] = 'professional-demo';
    native['academyId'] = 'previous-academy';
    final offline = await fixtures.authenticate(
      'Guardian',
      (m, p, b, t) async => throw StateError('offline'),
    );
    await LauncherIconService().reconcile(offline);
    expect(sets, isEmpty);
  });
  testWidgets(
    'explicit selection revalidates entitlement and cannot apply a stale eligible choice',
    (t) async {
      var reads = 0;
      final auth = await fixtures.authenticate(
        'AcademyOwner',
        (m, p, b, token) async => {
          'enabled': ++reads == 1,
          'key': 'professional-demo',
          'academyId': 'academy',
          'minimumBuild': 1,
        },
      );
      await t.pumpWidget(fixtures.app(LauncherIconPage(auth: auth)));
      await t.pumpAndSettle();
      await t.tap(find.text('استخدام شعار أكاديميتي كأيقونة التطبيق'));
      await t.pumpAndSettle();
      expect(reads, 2);
      expect(sets, isEmpty);
      expect(find.textContaining('تعذر تطبيق الأيقونة'), findsOneWidget);
    },
  );
  testWidgets(
    'unsupported/awaiting artwork and large text keep actionable honest states without other academy icons',
    (t) async {
      t.view.physicalSize = const Size(360, 900);
      t.view.devicePixelRatio = 1;
      addTearDown(t.view.resetPhysicalSize);
      addTearDown(t.view.resetDevicePixelRatio);
      final auth = await fixtures.authenticate(
        'Guardian',
        (m, p, b, token) async => {
          'enabled': true,
          'key': 'future-icon',
          'academyId': 'academy',
          'minimumBuild': 2,
        },
      );
      await t.pumpWidget(fixtures.app(LauncherIconPage(auth: auth), scale: 2));
      await t.pumpAndSettle();
      expect(find.textContaining('ينتظر إضافته'), findsOneWidget);
      expect(find.text('استخدام شعار أكاديميتي كأيقونة التطبيق'), findsNothing);
      expect(t.takeException(), isNull);
      await t.ensureVisible(find.text('استخدام أيقونة iAcademy One القياسية'));
      await t.pumpAndSettle();
      await t.tap(find.text('استخدام أيقونة iAcademy One القياسية'));
      await t.pumpAndSettle();
      expect(sets.single['key'], isNull);
    },
  );
}
