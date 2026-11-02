import 'support/infinite_list.dart';

import 'dart:async';

import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/features/owner/owner_quick_operations.dart';
import 'package:academy_mobile/features/shared/native_media_groups.dart';
import 'package:academy_mobile/features/shared/private_media.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'owner_test.dart' as fixture;
import 'v2_role_workspaces_test.dart' as role;

void main() {
  testWidgets(
    'group search and paging apply explicitly and the previous group disappears while the next page is loading',
    (t) async {
      await t.binding.setSurfaceSize(const Size(375, 667));
      addTearDown(() => t.binding.setSurfaceSize(null));
      final reads = <Uri>[];
      final delayed = Completer<Object?>();
      final auth = await fixture.owner((m, p, b, k) async {
        if (p.contains('media-audience')) {
          return {
            'eventOn': '2026-10-21',
            'asOfDate': '2026-10-21',
            'players': <Object>[],
          };
        }
        if (!p.contains('media-groups')) return <Object>[];
        final uri = Uri.parse(p);
        reads.add(uri);
        if (uri.queryParameters['page'] == '2') return delayed.future;
        return {
          'items': [
            {
              'id': 'g1',
              'arabicName': 'مجموعة أولى',
              'branchName': 'فرع اصطناعي',
              'sportName': 'كرة القدم',
            },
          ],
          'page': 1,
          'hasMore': true,
        };
      });
      await t.pumpWidget(fixture.shell(NativeMediaGroups(auth: auth)));
      await t.pumpAndSettle();
      expect(find.text('مجموعة أولى'), findsOneWidget);
      final input = find.widgetWithText(TextField, 'بحث باسم المجموعة');
      await t.enterText(input, 'بحث اصطناعي');
      await t.pumpAndSettle();
      expect(reads.length, 1);
      await t.tap(find.text('بحث'));
      await t.pumpAndSettle();
      expect(reads.last.queryParameters['search'], 'بحث اصطناعي');
      await scrollForNextPage(t);
      await t.pump();
      expect(find.text('مجموعة أولى'), findsOneWidget);
      delayed.complete({
        'items': [
          {
            'id': 'g2',
            'arabicName': 'مجموعة ثانية',
            'branchName': 'فرع ثان',
            'sportName': 'كرة القدم',
          },
        ],
        'page': 2,
        'hasMore': false,
      });
      await t.pumpAndSettle();
      expect(reads.last.queryParameters['search'], 'بحث اصطناعي');
      expect(reads.last.queryParameters['page'], '2');
      expect(find.text('مجموعة ثانية'), findsOneWidget);
      await t.tap(find.text('مجموعة ثانية'));
      await t.pumpAndSettle();
      expect(
        t.widget<UploadSessionMedia>(find.byType(UploadSessionMedia)).groupId,
        'g2',
      );
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
  testWidgets(
    'server rejection exposes a clear retry without offering a group or upload form',
    (t) async {
      final auth = await fixture.owner(
        (m, p, b, k) async =>
            throw const ApiFailure(403, 'هذه العملية خارج صلاحياتك.'),
      );
      await t.pumpWidget(fixture.shell(NativeMediaGroups(auth: auth)));
      await t.pumpAndSettle();
      expect(find.text('هذه العملية خارج صلاحياتك.'), findsOneWidget);
      expect(find.text('إعادة المحاولة'), findsOneWidget);
      expect(find.byType(UploadSessionMedia), findsNothing);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
  for (final add in [false, true]) {
    testWidgets(
      'media operation entry requires server read and add grants: $add',
      (t) async {
        final auth = await role.authenticate(
          'AcademyAdmin',
          (m, p, b, k) async => p.endsWith('my-permissions')
              ? ['content.media.manage']
              : {
                  'useActionGrants': true,
                  'actions': [
                    {'module': 'media', 'read': true, 'add': add},
                  ],
                  'workflowActions': <Object>[],
                },
        );
        await t.pumpWidget(role.app(NativeOperationActions(auth: auth)));
        await t.pumpAndSettle();
        expect(
          find.text('رفع صور وفيديو'),
          add ? findsOneWidget : findsNothing,
        );
        await t.pumpWidget(const SizedBox());
        auth.dispose();
      },
    );
  }
}
