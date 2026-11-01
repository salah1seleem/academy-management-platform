import 'dart:async';
import 'dart:typed_data';

import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/features/owner/native_academy_import.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'support/visibility.dart';

import 'auth_controller_test.dart' show credentials, MemoryStore;
import 'v2_role_workspaces_test.dart' show app;

final preview = <String, dynamic>{
  'canApprove': true,
  'fileHash': 'file-hash',
  'referenceStamp': 'reference-stamp',
  'rowCount': 11,
  'playerCount': 10,
  'householdCount': 9,
  'financiallyIncomplete': 1,
  'guardianMatches': <Object>[],
  'effects': [
    {'sheet': 'المدربون', 'create': 2, 'reuse': 0},
  ],
  'coachAssignments': 2,
  'tournamentParticipations': 17,
  'rows': <Object>[],
  'errors': <Object>[],
};
final job = <String, dynamic>{
  'id': 'job-1',
  'sourceSystem': 'مصدر اصطناعي',
  'status': 'Completed',
  'rowCount': 11,
  'publishedRows': 11,
  'replayedRows': 0,
  'result': {
    'effects': <Object>[],
    'coachAssignments': 2,
    'tournamentParticipations': 17,
  },
};

class ImportApi implements ApiTransport, AssetTransport, WorkbookTransport {
  ImportApi({this.work, this.read});
  final Future<Object?> Function(bool, Map<String, String>, String?)? work;
  final Future<Object?> Function(String)? read;
  final calls = <String>[];
  @override
  Future<Object?> send(
    String method,
    String path, {
    Object? body,
    String? token,
    String? idempotencyKey,
  }) async {
    if (path.contains('/auth/')) {
      return {
        ...credentials(),
        'membershipId': 'owner',
        'memberships': [
          {
            'id': 'owner',
            'academyId': 'academy',
            'academyName': 'أكاديمية اختبار',
            'role': 'AcademyOwner',
          },
        ],
      };
    }
    calls.add('$method $path');
    if (read != null) return read!(path);
    if (path.endsWith('/schema')) return {'connectedSheets': <Object>[]};
    return <Object>[];
  }

  @override
  Future<Object?> importWorkbook(
    Uint8List bytes,
    bool commit,
    Map<String, String> fields,
    String token, {
    String? idempotencyKey,
  }) async {
    calls.add(commit ? 'commit' : 'preview');
    return work == null ? preview : work!(commit, fields, idempotencyKey);
  }

  @override
  Future<Uint8List> asset(String path, String token) async {
    calls.add(path);
    return Uint8List.fromList([80, 75, 3, 4]);
  }

  @override
  Future<Object?> upload(
    Uint8List bytes,
    String contentType,
    Map<String, String> fields,
    String token,
  ) => throw UnimplementedError();
}

Future<AuthController> login(ImportApi api) async {
  final auth = AuthController(api, MemoryStore())..restoring = false;
  await auth.verify('phone', 'challenge', 'otp');
  return auth;
}

Future<void> pick(WidgetTester t) async {
  await t.tap(find.text('اختيار ملف XLSX ومعاينته'));
  await t.pumpAndSettle();
}

Future<void> tap(WidgetTester t, String text) async {
  await t.scrollUntilVisible(
    find.text(text),
    180,
    scrollable: find.byType(Scrollable).first,
  );
  await t.tap(find.text(text));
  await t.pumpAndSettle();
}

void main() {
  testWidgets(
    'Real template paths save XLSX bytes and downloading does not import',
    (t) async {
      final api = ImportApi();
      final owner = await login(api);
      final files = <String>[];
      await t.pumpWidget(
        app(
          NativeAcademyImport(
            auth: owner,
            saveWorkbook: (name, bytes) async {
              expect(bytes, [80, 75, 3, 4]);
              files.add(name);
              return true;
            },
          ),
        ),
      );
      await t.pumpAndSettle();
      await tap(t, 'تنزيل قالب فارغ');
      await tap(t, 'تنزيل نموذج مكتمل');
      expect(files, [
        'I-Academy-One-Template.xlsx',
        'I-Academy-One-Sample.xlsx',
      ]);
      expect(api.calls, contains('/api/v1/people/import/connected-template'));
      expect(api.calls, contains('/api/v1/people/import/connected-sample'));
      expect(api.calls, isNot(contains('commit')));
      await t.pumpWidget(const SizedBox());
      owner.dispose();
    },
  );
  testWidgets(
    'Sheet/row reference error blocks confirmation without any commit',
    (t) async {
      final api = ImportApi(
        work: (commit, fields, key) async => {
          ...preview,
          'canApprove': false,
          'errors': [
            {
              'sheet': 'المدربون',
              'row': 3,
              'column': 'كود المدرب',
              'message': 'كود المدرب غير موجود.',
            },
          ],
        },
      );
      final auth = await login(api);
      await t.pumpWidget(
        app(
          NativeAcademyImport(
            auth: auth,
            pickWorkbook: () async =>
                (name: 'linked.xlsx', bytes: Uint8List.fromList([1])),
          ),
        ),
      );
      await t.pumpAndSettle();
      await pick(t);
      expect(find.text('الملف يحتاج تصحيحًا'), findsOneWidget);
      await t.scrollUntilVisible(
        find.textContaining('كود المدرب غير موجود'),
        150,
        scrollable: find.byType(Scrollable).first,
      );
      expect(find.textContaining('كود المدرب غير موجود'), findsOneWidget);
      expect(find.text('اعتماد وبدء الاستيراد'), findsNothing);
      expect(api.calls.where((c) => c == 'commit'), isEmpty);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
  testWidgets(
    'Explicit linked/finance review and uncertain commit keep one intent and display real completion counts',
    (t) async {
      final attempts = <({Map<String, String> fields, String? key})>[];
      final api = ImportApi(
        work: (commit, fields, key) async {
          if (!commit) return preview;
          attempts.add((fields: Map.from(fields), key: key));
          if (attempts.length == 1) throw const ApiFailure(0, 'تعذر الاتصال');
          return job;
        },
      );
      final auth = await login(api);
      await t.pumpWidget(
        app(
          NativeAcademyImport(
            auth: auth,
            pickWorkbook: () async =>
                (name: 'linked.xlsx', bytes: Uint8List.fromList([1])),
          ),
        ),
      );
      await t.pumpAndSettle();
      await pick(t);
      await t.scrollUntilVisible(
        find.text('اعتماد وبدء الاستيراد'),
        180,
        scrollable: find.byType(Scrollable).first,
      );
      expect(
        t
            .widget<FilledButton>(
              find.widgetWithText(FilledButton, 'اعتماد وبدء الاستيراد'),
            )
            .onPressed,
        isNull,
      );
      final boxes = find.byType(CheckboxListTile);
      t.widget<CheckboxListTile>(boxes.at(0)).onChanged!(true);
      await t.pumpAndSettle();
      expect(
        t
            .widget<FilledButton>(
              find.widgetWithText(FilledButton, 'اعتماد وبدء الاستيراد'),
            )
            .onPressed,
        isNull,
      );
      t.widget<CheckboxListTile>(find.byType(CheckboxListTile).last).onChanged!(
        true,
      );
      await t.pumpAndSettle();
      await tap(t, 'اعتماد وبدء الاستيراد');
      expect(find.textContaining('لم يتأكد اعتماد الدفعة'), findsOneWidget);
      await tap(t, 'إعادة تأكيد الطلب نفسه');
      expect(attempts.length, 2);
      expect(attempts[0].key, attempts[1].key);
      expect(attempts[0].fields, attempts[1].fields);
      expect(attempts[0].fields['ExpectedFileHash'], 'file-hash');
      expect(attempts[0].fields['ExpectedReferenceStamp'], 'reference-stamp');
      expect(attempts[0].fields['AcceptFinancialReview'], 'true');
      expect(find.text('اكتمل الاستيراد'), findsOneWidget);
      await revealTestTarget(t, find.textContaining('17 مشاركة في البطولات'));
      expect(find.textContaining('17 مشاركة في البطولات'), findsOneWidget);
      expect(
        api.calls.any(
          (p) => p.contains('/payments') || p.contains('/collections'),
        ),
        isFalse,
      );
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
  testWidgets(
    'Logout during preview destroys the private result and stops future work',
    (t) async {
      final pending = Completer<Object?>();
      final api = ImportApi(work: (commit, fields, key) => pending.future);
      final auth = await login(api);
      await t.pumpWidget(
        app(
          NativeAcademyImport(
            auth: auth,
            pickWorkbook: () async =>
                (name: 'private.xlsx', bytes: Uint8List.fromList([1])),
          ),
        ),
      );
      await t.pumpAndSettle();
      await t.tap(find.text('اختيار ملف XLSX ومعاينته'));
      await t.pump();
      await auth.logout();
      pending.complete(preview);
      await t.pumpAndSettle();
      expect(find.text('اكتملت المعاينة دون أخطاء'), findsNothing);
      expect(find.text('private.xlsx'), findsNothing);
      expect(api.calls, isNot(contains('commit')));
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
}
