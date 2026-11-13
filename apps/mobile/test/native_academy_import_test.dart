import 'dart:async';
import 'dart:convert';
import 'dart:typed_data';

import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/features/owner/native_academy_import.dart';
import 'package:file_picker/file_picker.dart';
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
  ImportApi({this.work, this.read, this.export});
  final Future<Object?> Function(bool, Map<String, String>, String?)? work;
  final Future<Object?> Function(String)? read;
  final Future<Object?> Function(Uint8List, String, bool)? export;
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
    String? validRowsFromHash,
    bool correctionRows = false,
  }) async {
    if (validRowsFromHash != null) {
      calls.add(
        '${correctionRows ? 'correction' : 'clean'}:$validRowsFromHash',
      );
      if (export case final download?) {
        return download(bytes, validRowsFromHash, correctionRows);
      }
      return Uint8List.fromList([80, 75, 3, 4]);
    }
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
  await reviewAction(t, find.text(text));
}

const importReviewIssues = <Map<String, Object>>[
  {
    'sheet': 'اللاعبون',
    'row': 50,
    'column': 'الاسم الأول',
    'message': 'أدخل الاسم، ثم اضغط "حفظ".',
  },
  {
    'sheet': 'اللاعبون',
    'row': 64,
    'column': 'هاتف ولي الأمر',
    'message': 'اكتب الهاتف كنص.',
  },
  {
    'sheet': 'أولياء الأمور',
    'row': 64,
    'column': 'اسم ولي الأمر',
    'message': 'حقل مطلوب.',
  },
  {
    'sheet': 'أولياء الأمور',
    'row': 64,
    'column': 'اسم ولي الأمر',
    'message': 'حقل مطلوب.',
  },
  {
    'sheet': 'أولياء الأمور',
    'row': 64,
    'column': 'اسم ولي الأمر',
    'message': 'راجع الاسم مع صاحب الحساب.',
  },
];

final splitPreview = <String, dynamic>{
  ...preview,
  'canApprove': false,
  'cleanRows': {'playerCount': 48, 'excludedRows': 2},
  'errors': importReviewIssues,
};

const cleanExport = 'تنزيل ملف اللاعبين السليمين';
const correctionExport = 'تنزيل صفوف التصحيح';
const cleanSaved = 'تم حفظ ملف اللاعبين السليمين.';
const correctionSaved = 'تم حفظ ملف صفوف التصحيح.';

Future<AuthController> openSplit(
  WidgetTester t,
  ImportApi api, {
  Future<bool> Function(String, Uint8List)? save,
  Future<WorkbookSelection?> Function()? choose,
  double scale = 1,
}) async {
  final auth = await login(api);
  await t.pumpWidget(
    app(
      MediaQuery(
        data: MediaQueryData(textScaler: TextScaler.linear(scale)),
        child: NativeAcademyImport(
          auth: auth,
          pickWorkbook:
              choose ??
              () async => (name: 'mixed.xlsx', bytes: Uint8List.fromList([1])),
          saveWorkbook: save,
        ),
      ),
    ),
  );
  await t.pumpAndSettle();
  await pick(t);
  return auth;
}

Future<void> reviewAction(WidgetTester tester, Finder target) async {
  await revealTestTarget(tester, target);
  await tester.tap(target);
  await tester.pumpAndSettle();
}

Finder get issueRowField => find.widgetWithText(TextField, 'رقم الصف في Excel');

Future<void> searchIssue(WidgetTester tester, String row) async {
  await revealTestTarget(tester, issueRowField);
  await tester.enterText(issueRowField, row);
  await tester.pumpAndSettle();
}

class IssueFilePicker extends FilePickerPlatform {
  final pending = Completer<Uri?>();
  final requests =
      <({String name, String mime, String? title, Uint8List data})>[];

  @override
  Future<Uri?> saveFile({
    required String fileName,
    required Uint8List bytes,
    required String mimeType,
    String? dialogTitle,
    String? initialDirectory,
    void Function(FilePickerStatus)? onFileSaving,
    WindowsOptions windowsOptions = const WindowsOptions(),
    LinuxOptions linuxOptions = const LinuxOptions(),
    WebOptions webOptions = const WebOptions(),
  }) {
    requests.add((
      name: fileName,
      mime: mimeType,
      title: dialogTitle,
      data: bytes,
    ));
    return pending.future;
  }
}

void main() {
  testWidgets(
    'CSV export calls the file picker once with the report MIME and title',
    (t) async {
      final previous = FilePickerPlatform.instance;
      final destination = IssueFilePicker();
      FilePickerPlatform.instance = destination;
      addTearDown(() => FilePickerPlatform.instance = previous);
      final api = ImportApi(
        work: (commit, fields, key) async => {
          ...preview,
          'canApprove': false,
          'errors': [importReviewIssues.first],
        },
      );
      final auth = await login(api);
      await t.pumpWidget(
        app(
          NativeAcademyImport(
            auth: auth,
            pickWorkbook: () async =>
                (name: 'one.xlsx', bytes: Uint8List.fromList([1])),
          ),
        ),
      );
      await t.pumpAndSettle();
      await pick(t);
      final save = find.widgetWithText(OutlinedButton, 'تنزيل تقرير الأخطاء');
      await revealTestTarget(t, save);
      await t.tap(save);
      await t.pump();
      expect(t.widget<OutlinedButton>(save).onPressed, isNull);
      expect(find.text('تم حفظ تقرير الأخطاء.'), findsNothing);
      await t.tap(save, warnIfMissed: false);
      await t.pump();
      final request = destination.requests.single;
      expect(request.name, 'academy-import-errors.csv');
      expect(request.mime, 'text/csv');
      expect(request.title, 'حفظ تقرير الأخطاء');
      expect(
        utf8.decode(request.data),
        contains('"اللاعبون","50","الاسم الأول"'),
      );
      destination.pending.complete(Uri.file('/qa/academy-import-errors.csv'));
      await t.pumpAndSettle();
      expect(find.text('تم حفظ تقرير الأخطاء.'), findsOneWidget);
      expect(t.widget<OutlinedButton>(save).onPressed, isNotNull);
      expect(api.calls, isNot(contains('commit')));
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );

  testWidgets(
    'Filters original issue rows and exports the full deduplicated report',
    (t) async {
      final api = ImportApi(
        work: (commit, fields, key) async => {
          ...preview,
          'canApprove': false,
          'errors': importReviewIssues,
          'cleanRows': {'playerCount': 48, 'excludedRows': 2},
        },
      );
      final auth = await login(api);
      final files = <String, Uint8List>{};
      await t.pumpWidget(
        app(
          NativeAcademyImport(
            auth: auth,
            pickWorkbook: () async =>
                (name: 'mixed.xlsx', bytes: Uint8List.fromList([1])),
            saveWorkbook: (name, bytes) async {
              files[name] = bytes;
              return true;
            },
          ),
        ),
      );
      await t.pumpAndSettle();
      await pick(t);
      await revealTestTarget(t, find.text('المشكلات المعروضة 4'));
      expect(find.text('الصفوف المتأثرة 3'), findsOneWidget);
      expect(find.text('اعتماد وبدء الاستيراد'), findsNothing);
      final sheet = find.byType(DropdownButtonFormField<String>);
      await reviewAction(t, sheet);
      await t.tap(find.text('اللاعبون').last);
      await t.pumpAndSettle();
      for (final digits in ['64', '٦٤', '۶۴']) {
        await searchIssue(t, digits);
        await revealTestTarget(t, find.text('اكتب الهاتف كنص.'));
        expect(find.text('الصفوف المتأثرة 1'), findsOneWidget);
        expect(find.text('اللاعبون · الصف 50'), findsNothing);
      }
      await reviewAction(t, sheet);
      await t.tap(find.text('أولياء الأمور').last);
      await t.pumpAndSettle();
      expect(t.widget<TextField>(issueRowField).controller!.text, '۶۴');
      await revealTestTarget(t, find.text('راجع الاسم مع صاحب الحساب.'));
      expect(find.text('حقل مطلوب.'), findsOneWidget);
      expect(find.text('اكتب الهاتف كنص.'), findsNothing);
      await reviewAction(t, find.text('تنزيل تقرير الأخطاء'));
      final bytes = files['academy-import-errors.csv']!;
      expect(bytes.take(3), [239, 187, 191]);
      final report = utf8.decode(bytes.skip(3).toList()).split('\n');
      expect(report, [
        '"الورقة","الصف","الحقل","المشكلة"',
        '"اللاعبون","50","الاسم الأول","أدخل الاسم، ثم اضغط ""حفظ""."',
        '"اللاعبون","64","هاتف ولي الأمر","اكتب الهاتف كنص."',
        '"أولياء الأمور","64","اسم ولي الأمر","حقل مطلوب."',
        '"أولياء الأمور","64","اسم ولي الأمر","راجع الاسم مع صاحب الحساب."',
      ]);
      await reviewAction(t, find.text('تنزيل ملف اللاعبين السليمين'));
      await reviewAction(t, find.text('تنزيل صفوف التصحيح'));
      expect(
        files.keys,
        containsAll([
          'academy-valid-players.xlsx',
          'academy-player-corrections.xlsx',
        ]),
      );
      await revealTestTarget(t, find.text('المشكلات المعروضة 2'));
      for (final invalid in ['63', '0', 'خطأ']) {
        await searchIssue(t, invalid);
        await revealTestTarget(t, find.text('لا توجد مشكلات تطابق التصفية.'));
      }
      await reviewAction(t, find.text('مسح التصفية'));
      await revealTestTarget(t, find.text('المشكلات المعروضة 4'));
      await revealTestTarget(t, sheet);
      expect(t.state<FormFieldState<String>>(sheet).value, isEmpty);
      expect(find.text('كل الأوراق'), findsOneWidget);
      expect(t.widget<TextField>(issueRowField).controller!.text, isEmpty);
      expect(api.calls.where((call) => !call.startsWith('GET')), [
        'preview',
        'clean:file-hash',
        'correction:file-hash',
      ]);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );

  testWidgets(
    'Rejects empty and malformed responses and restores retry after a rate limit',
    (t) async {
      var attempt = 0;
      final api = ImportApi(
        work: (commit, fields, key) async {
          switch (++attempt) {
            case 1:
              return {...preview, 'canApprove': false};
            case 2:
              throw const ApiFailure(
                429,
                'محاولات كثيرة، يرجى الانتظار ثم المحاولة مجددًا',
              );
            case 3:
              return {...preview, 'errors': importReviewIssues};
            default:
              return {
                ...preview,
                'canApprove': false,
                'errors': [importReviewIssues.first],
              };
          }
        },
      );
      final auth = await login(api);
      await t.pumpWidget(
        app(
          NativeAcademyImport(
            auth: auth,
            pickWorkbook: () async =>
                (name: 'wrong.xlsx', bytes: Uint8List.fromList([1])),
          ),
        ),
      );
      await t.pumpAndSettle();
      await pick(t);
      expect(
        find.text('لم يُحفظ أي سجل. تعذر عرض تفاصيل الفحص.'),
        findsOneWidget,
      );
      expect(find.text('تنزيل تقرير الأخطاء'), findsNothing);
      expect(find.text('اعتماد وبدء الاستيراد'), findsNothing);
      final retry = find.text('إعادة فحص الملف');
      await reviewAction(t, retry);
      expect(
        find.text('محاولات كثيرة، يرجى الانتظار ثم المحاولة مجددًا'),
        findsOneWidget,
      );
      await reviewAction(t, retry);
      expect(find.text('تعذر قراءة نتيجة المعاينة.'), findsOneWidget);
      expect(find.text('اكتملت المعاينة دون أخطاء'), findsNothing);
      await reviewAction(t, retry);
      await revealTestTarget(t, find.text('اللاعبون · الصف 50'));
      expect(find.text('تنزيل تقرير الأخطاء'), findsOneWidget);
      expect(issueRowField, findsNothing);
      expect(find.byType(DropdownButtonFormField<String>), findsNothing);
      expect(api.calls, isNot(contains('commit')));
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );

  testWidgets(
    'Finds a late row in a capped response and resets filters for each preview',
    (t) async {
      var attempt = 0;
      final api = ImportApi(
        work: (commit, fields, key) async {
          if (++attempt == 3) return preview;
          return {
            ...preview,
            'canApprove': false,
            'errors': [
              for (var row = 2; row <= 301; row++)
                {...importReviewIssues.first, 'row': row},
            ],
          };
        },
      );
      final auth = await login(api);
      await t.pumpWidget(
        app(
          NativeAcademyImport(
            auth: auth,
            pickWorkbook: () async =>
                (name: 'bulk.xlsx', bytes: Uint8List.fromList([1])),
          ),
        ),
      );
      await t.pumpAndSettle();
      await pick(t);
      await revealTestTarget(
        t,
        find.text('يعرض الفحص حتى 300 مشكلة في المرة الواحدة.'),
      );
      await searchIssue(t, '301');
      await revealTestTarget(t, find.text('اللاعبون · الصف 301'));
      expect(find.text('المشكلات المعروضة 1'), findsOneWidget);
      await reviewAction(t, find.text('إعادة فحص الملف'));
      await revealTestTarget(t, issueRowField);
      expect(t.widget<TextField>(issueRowField).controller!.text, isEmpty);
      await searchIssue(t, '301');
      await reviewAction(t, find.text('اختيار ملف XLSX ومعاينته'));
      await revealTestTarget(t, find.text('اكتملت المعاينة دون أخطاء'));
      expect(issueRowField, findsNothing);
      expect(find.text('تنزيل تقرير الأخطاء'), findsNothing);
      final commit = find.widgetWithText(FilledButton, 'اعتماد وبدء الاستيراد');
      await revealTestTarget(t, commit);
      expect(t.widget<FilledButton>(commit).onPressed, isNull);
      expect(api.calls, isNot(contains('commit')));
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );

  testWidgets(
    'Reports save outcomes within the preview and clears them for the next file',
    (t) async {
      var attempts = 0;
      var previews = 0;
      final api = ImportApi(
        work: (commit, fields, key) async => ++previews == 1
            ? {
                ...preview,
                'canApprove': false,
                'errors': importReviewIssues.take(2).toList(),
              }
            : preview,
      );
      final auth = await login(api);
      await t.pumpWidget(
        app(
          NativeAcademyImport(
            auth: auth,
            pickWorkbook: () async =>
                (name: 'mixed.xlsx', bytes: Uint8List.fromList([1])),
            saveWorkbook: (_, _) async {
              switch (++attempts) {
                case 1:
                  return false;
                case 2:
                  throw StateError('filesystem failure');
                default:
                  return true;
              }
            },
          ),
        ),
      );
      await t.pumpAndSettle();
      await pick(t);
      await searchIssue(t, '64');
      final save = find.text('تنزيل تقرير الأخطاء');
      await reviewAction(t, save);
      expect(find.text('تم حفظ تقرير الأخطاء.'), findsNothing);
      expect(find.text('تعذر حفظ تقرير الأخطاء. أعد المحاولة.'), findsNothing);
      await reviewAction(t, save);
      expect(
        find.text('تعذر حفظ تقرير الأخطاء. أعد المحاولة.'),
        findsOneWidget,
      );
      await reviewAction(t, save);
      expect(find.text('تم حفظ تقرير الأخطاء.'), findsOneWidget);
      await revealTestTarget(t, issueRowField);
      expect(t.widget<TextField>(issueRowField).controller!.text, '64');
      expect(attempts, 3);
      expect(api.calls.where((call) => !call.startsWith('GET')), ['preview']);
      await reviewAction(t, find.text('اختيار ملف XLSX ومعاينته'));
      expect(find.text('اكتملت المعاينة دون أخطاء'), findsOneWidget);
      expect(find.text('تم حفظ تقرير الأخطاء.'), findsNothing);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );

  testWidgets(
    'Logout while saving an import report suppresses old context feedback',
    (t) async {
      final pending = Completer<bool>();
      final api = ImportApi(
        work: (commit, fields, key) async => {
          ...preview,
          'canApprove': false,
          'errors': importReviewIssues,
        },
      );
      final auth = await login(api);
      await t.pumpWidget(
        app(
          NativeAcademyImport(
            auth: auth,
            pickWorkbook: () async =>
                (name: 'private.xlsx', bytes: Uint8List.fromList([1])),
            saveWorkbook: (_, _) => pending.future,
          ),
        ),
      );
      await t.pumpAndSettle();
      await pick(t);
      final save = find.text('تنزيل تقرير الأخطاء');
      await revealTestTarget(t, save);
      await t.tap(save);
      await t.pump();
      await auth.logout();
      pending.complete(true);
      await t.pumpAndSettle();
      expect(find.text('الملف يحتاج تصحيحًا'), findsNothing);
      expect(find.text('تم حفظ تقرير الأخطاء.'), findsNothing);
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );

  for (final width in [320.0, 375.0, 390.0]) {
    testWidgets(
      'Rejected import review fits at ${width.toInt()}px and 200% text',
      (t) async {
        t.view.physicalSize = Size(width, 850);
        t.view.devicePixelRatio = 1;
        addTearDown(t.view.resetPhysicalSize);
        addTearDown(t.view.resetDevicePixelRatio);
        final api = ImportApi(
          work: (commit, fields, key) async => {
            ...preview,
            'canApprove': false,
            'errors': importReviewIssues,
          },
        );
        final auth = await login(api);
        await t.pumpWidget(
          app(
            NativeAcademyImport(
              auth: auth,
              pickWorkbook: () async =>
                  (name: 'review.xlsx', bytes: Uint8List.fromList([1])),
            ),
            scale: 2,
          ),
        );
        await t.pumpAndSettle();
        await pick(t);
        expect(find.text('تنزيل قالب فارغ'), findsNothing);
        final fields = [
          find.byType(DropdownButtonFormField<String>),
          issueRowField,
          find.text('تنزيل تقرير الأخطاء'),
          find.text('اكتب الهاتف كنص.'),
        ];
        for (final field in fields) {
          await revealTestTarget(t, field);
          final bounds = t.getRect(field);
          expect(bounds.left, greaterThanOrEqualTo(0));
          expect(bounds.right, lessThanOrEqualTo(width));
        }
        await reviewAction(t, find.text('القوالب ودليل الحقول'));
        await reviewAction(t, find.text('الحقول المطلوبة والاختيارية'));
        await revealTestTarget(t, find.text('النجمة تحدد الحقل المطلوب.'));
        expect(t.takeException(), isNull);
        await t.pumpWidget(const SizedBox());
        auth.dispose();
      },
    );
  }

  testWidgets(
    'Mixed player sheet saves the two separate workbooks without committing',
    (t) async {
      final api = ImportApi(
        work: (commit, fields, key) async => {
          ...preview,
          'canApprove': false,
          'cleanRows': {'playerCount': 49, 'excludedRows': 1, 'relatedRows': 1},
          'errors': [
            {
              'sheet': 'اللاعبون',
              'row': 50,
              'column': 'هاتف ولي الأمر',
              'message': 'راجع الهاتف.',
            },
          ],
        },
      );
      final auth = await login(api);
      final files = <String>[];
      await t.pumpWidget(
        app(
          NativeAcademyImport(
            auth: auth,
            pickWorkbook: () async =>
                (name: 'mixed.xlsx', bytes: Uint8List.fromList([1])),
            saveWorkbook: (name, bytes) async {
              files.add(name);
              expect(bytes, [80, 75, 3, 4]);
              return true;
            },
          ),
        ),
      );
      await t.pumpAndSettle();
      await pick(t);
      final download = find.text('تنزيل ملف اللاعبين السليمين');
      await t.scrollUntilVisible(
        download,
        150,
        scrollable: find.byType(Scrollable).first,
      );
      await t.ensureVisible(download);
      await t.pumpAndSettle();
      expect(
        find.text('سجلات مرتبطة تنتقل إلى ملف التصحيح (1).'),
        findsOneWidget,
      );
      await t.tap(download.hitTestable());
      await t.pumpAndSettle();
      expect(find.text(cleanSaved), findsOneWidget);
      expect(find.text(correctionSaved), findsNothing);
      await tap(t, 'تنزيل صفوف التصحيح');
      expect(find.text(cleanSaved), findsOneWidget);
      expect(find.text(correctionSaved), findsOneWidget);
      expect(files, [
        'academy-valid-players.xlsx',
        'academy-player-corrections.xlsx',
      ]);
      expect(api.calls, contains('clean:file-hash'));
      expect(api.calls, contains('correction:file-hash'));
      expect(api.calls, isNot(contains('commit')));
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );

  testWidgets(
    'Split save cancellation and failure leave the other file and issue filter intact',
    (t) async {
      final api = ImportApi(work: (_, _, _) async => splitPreview);
      var corrections = 0;
      final auth = await openSplit(
        t,
        api,
        save: (name, _) async {
          if (name == 'academy-valid-players.xlsx') return true;
          switch (++corrections) {
            case 1:
              return false;
            case 2:
              throw StateError('save unavailable');
            default:
              return true;
          }
        },
      );
      await searchIssue(t, '50');
      await reviewAction(t, find.text(cleanExport));
      await reviewAction(t, find.text(correctionExport));
      expect(find.text(correctionSaved), findsNothing);
      expect(find.text(cleanSaved), findsOneWidget);
      await reviewAction(t, find.text(correctionExport));
      expect(find.text('تعذّر حفظ الملف. أعد المحاولة.'), findsOneWidget);
      expect(find.text(cleanSaved), findsOneWidget);
      await reviewAction(t, find.text(correctionExport));
      expect(find.text(correctionSaved), findsOneWidget);
      expect(find.text('تعذّر حفظ الملف. أعد المحاولة.'), findsNothing);
      await revealTestTarget(t, issueRowField);
      expect(t.widget<TextField>(issueRowField).controller!.text, '50');
      expect(api.calls.where((c) => !c.startsWith('GET')), [
        'preview',
        'clean:file-hash',
        for (var i = 0; i < 3; i++) 'correction:file-hash',
      ]);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );

  testWidgets(
    'Split conflict blocks both downloads until the same file is rechecked',
    (t) async {
      var previews = 0;
      var saves = 0;
      final api = ImportApi(
        work: (_, _, _) async => {
          ...splitPreview,
          'fileHash': 'hash-${++previews}',
        },
        export: (bytes, hash, _) async {
          expect(bytes, [1]);
          if (hash == 'hash-1') throw const ApiFailure(409, 'changed');
          expect(hash, 'hash-2');
          return Uint8List.fromList([80, 75, 3, 4]);
        },
      );
      final auth = await openSplit(
        t,
        api,
        save: (_, _) async {
          saves++;
          return true;
        },
      );
      await reviewAction(t, find.text(correctionExport));
      expect(saves, 0);
      await revealTestTarget(
        t,
        find.text('أعد فحص الملف لإتاحة تنزيل الصفوف.'),
      );
      expect(find.text(cleanExport), findsNothing);
      expect(find.text(correctionExport), findsNothing);
      expect(find.text('أعد فحص الملف لإتاحة تنزيل الصفوف.'), findsOneWidget);
      await reviewAction(t, find.text('إعادة فحص الملف'));
      await reviewAction(t, find.text(correctionExport));
      expect(saves, 1);
      expect(find.text(correctionSaved), findsOneWidget);
      expect(api.calls.where((c) => !c.startsWith('GET')), [
        'preview',
        'correction:hash-1',
        'preview',
        'correction:hash-2',
      ]);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );

  testWidgets(
    'Split without a usable hash or excluded rows offers a recheck only',
    (t) async {
      var attempt = 0;
      final api = ImportApi(
        work: (_, _, _) async {
          return switch (++attempt) {
            1 => {...splitPreview}..remove('fileHash'),
            2 => {...splitPreview, 'fileHash': '  '},
            _ => {
              ...splitPreview,
              'cleanRows': {'playerCount': 48, 'excludedRows': 0},
            },
          };
        },
      );
      final auth = await openSplit(t, api);
      for (var i = 0; i < 2; i++) {
        await revealTestTarget(
          t,
          find.text('أعد فحص الملف لإتاحة تنزيل الصفوف.'),
        );
        expect(find.text(cleanExport), findsNothing);
        expect(find.text(correctionExport), findsNothing);
        await reviewAction(t, find.text('إعادة فحص الملف'));
      }
      expect(find.text('48 سليمًا · 0 للتصحيح'), findsNothing);
      expect(find.text(cleanExport), findsNothing);
      expect(find.text(correctionExport), findsNothing);
      await revealTestTarget(t, find.text('أدخل الاسم، ثم اضغط "حفظ".'));
      expect(
        api.calls.where((c) => !c.startsWith('GET')),
        List.filled(3, 'preview'),
      );
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );

  testWidgets(
    'Choosing the corrected file clears split feedback and still requires approval',
    (t) async {
      var selections = 0;
      var previews = 0;
      final api = ImportApi(
        work: (commit, _, _) async {
          expect(commit, false);
          return ++previews == 1
              ? splitPreview
              : {
                  ...preview,
                  'playerCount': 2,
                  'rowCount': 2,
                  'householdCount': 1,
                };
        },
      );
      final auth = await openSplit(
        t,
        api,
        choose: () async => (
          name: ++selections == 1 ? 'mixed.xlsx' : 'corrected.xlsx',
          bytes: Uint8List.fromList([selections]),
        ),
        save: (_, _) async => true,
      );
      await reviewAction(t, find.text(correctionExport));
      expect(find.text(correctionSaved), findsOneWidget);
      await reviewAction(t, find.text('اختيار ملف XLSX ومعاينته'));
      expect(find.text('corrected.xlsx'), findsOneWidget);
      expect(find.text(correctionSaved), findsNothing);
      await revealTestTarget(
        t,
        find.text('اللاعبون 2 · التسجيلات 2 · الأسر 1'),
      );
      final approve = find.widgetWithText(
        FilledButton,
        'اعتماد وبدء الاستيراد',
      );
      expect(t.widget<FilledButton>(approve).onPressed, isNull);
      expect(api.calls.where((c) => !c.startsWith('GET')), [
        'preview',
        'correction:file-hash',
        'preview',
      ]);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );

  testWidgets(
    'Split service errors and invalid replies keep actions usable at 320 and 200%',
    (t) async {
      t.view.physicalSize = const Size(320, 740);
      t.view.devicePixelRatio = 1;
      addTearDown(t.view.resetPhysicalSize);
      addTearDown(t.view.resetDevicePixelRatio);
      var attempts = 0;
      var saves = 0;
      final api = ImportApi(
        work: (_, _, _) async => splitPreview,
        export: (_, _, _) async {
          switch (++attempts) {
            case 1:
              throw const ApiFailure(503, 'unavailable');
            case 2:
              return {'unexpected': true};
            default:
              return Uint8List.fromList([80, 75, 3, 4]);
          }
        },
      );
      final auth = await openSplit(
        t,
        api,
        scale: 2,
        save: (_, _) async {
          saves++;
          return true;
        },
      );
      await searchIssue(t, '50');
      for (final feedback in [
        'تعذّر تنزيل الملف. أعد المحاولة.',
        'تعذّر تنزيل ملف XLSX صالح.',
        cleanSaved,
      ]) {
        await reviewAction(t, find.text(cleanExport));
        final status = find.text(feedback);
        await revealTestTarget(t, status);
        expect(status.hitTestable(), findsOneWidget);
        expect(t.getRect(status).right, lessThanOrEqualTo(320));
        expect(t.takeException(), isNull);
      }
      expect(saves, 1);
      await revealTestTarget(t, issueRowField);
      expect(t.widget<TextField>(issueRowField).controller!.text, '50');
      expect(api.calls, isNot(contains('commit')));
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );

  testWidgets(
    'Split save requests one system destination and hides confirmation after logout',
    (t) async {
      final previous = FilePickerPlatform.instance;
      final destination = IssueFilePicker();
      FilePickerPlatform.instance = destination;
      addTearDown(() => FilePickerPlatform.instance = previous);
      final api = ImportApi(work: (_, _, _) async => splitPreview);
      final auth = await openSplit(t, api);
      final action = find.text(correctionExport);
      await revealTestTarget(t, action);
      await t.tap(action);
      await t.pump();
      await t.tap(action, warnIfMissed: false);
      await t.pump();
      final request = destination.requests.single;
      expect(request.name, 'academy-player-corrections.xlsx');
      expect(
        request.mime,
        'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
      );
      expect(request.title, 'حفظ ملف الأكاديمية');
      expect(request.data, [80, 75, 3, 4]);
      await auth.logout();
      destination.pending.complete(
        Uri.file('/qa/academy-player-corrections.xlsx'),
      );
      await t.pumpAndSettle();
      expect(find.text(correctionSaved), findsNothing);
      expect(find.text(correctionExport), findsNothing);
      expect(api.calls.where((c) => c.startsWith('correction:')), [
        'correction:file-hash',
      ]);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );

  testWidgets(
    'Forbidden split download hides the preview and never opens system save',
    (t) async {
      final api = ImportApi(
        work: (_, _, _) async => splitPreview,
        export: (_, _, _) async => throw const ApiFailure(
          403,
          'ليست لديك صلاحية استيراد هذه البيانات ضمن نطاقك.',
        ),
      );
      final auth = await openSplit(
        t,
        api,
        save: (_, _) async => throw StateError('unexpected destination'),
      );
      await reviewAction(t, find.text(cleanExport));
      expect(find.text('لا يمكن عرض هذه الصفحة'), findsOneWidget);
      expect(
        find.text('ليست لديك صلاحية استيراد هذه البيانات ضمن نطاقك.'),
        findsOneWidget,
      );
      expect(find.text('48 سليمًا · 2 للتصحيح'), findsNothing);
      expect(find.text(correctionExport), findsNothing);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );

  testWidgets('Historical collections require separate approval on mobile', (
    t,
  ) async {
    final api = ImportApi(
      work: (commit, fields, key) async {
        if (!commit) {
          return {
            ...preview,
            'financiallyIncomplete': 0,
            'historicalPayments': 2,
            'historicalPaymentTotal': 750,
            'historicalPaymentCurrency': 'USD',
          };
        }
        expect(fields['HistoricalPaymentsApproved'], 'true');
        return job;
      },
    );
    final auth = await login(api);
    await t.pumpWidget(
      app(
        NativeAcademyImport(
          auth: auth,
          pickWorkbook: () async =>
              (name: 'history.xlsx', bytes: Uint8List.fromList([1])),
        ),
      ),
    );
    await t.pumpAndSettle();
    await pick(t);
    await tap(t, 'راجعت علاقات أولياء الأمور وأعتمد ربط الدفعة.');
    final approval = find.widgetWithText(FilledButton, 'اعتماد وبدء الاستيراد');
    await t.scrollUntilVisible(
      approval,
      180,
      scrollable: find.byType(Scrollable).first,
    );
    expect(t.widget<FilledButton>(approval).onPressed, isNull);
    expect(api.calls, isNot(contains('commit')));
    final history = find.textContaining('راجعت 2 سند تحصيل سابق');
    await revealTestTarget(t, find.textContaining('750 USD'));
    await t.ensureVisible(history);
    await t.tap(history);
    await t.pumpAndSettle();
    await tap(t, 'اعتماد وبدء الاستيراد');
    expect(api.calls.where((c) => c == 'commit'), hasLength(1));
    await t.pumpWidget(const SizedBox());
    auth.dispose();
  });

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
    'Historical replay keeps its currency while the records request fails',
    (t) async {
      var reads = 0;
      final replay = {
        ...job,
        'replayedRequest': true,
        'result': {
          'historicalPayments': 1,
          'historicalPaymentTotal': 600.25,
          'historicalPaymentCurrency': 'USD',
        },
      };
      final api = ImportApi(
        read: (path) async {
          if (path.endsWith('/schema')) return {'connectedSheets': <Object>[]};
          if (path.endsWith('/jobs')) return [replay];
          if (++reads == 1) throw const ApiFailure(503, 'تعذر الاتصال.');
          return [
            {
              'player': 'لاعب محفوظ',
              'playerCode': 'QA-1',
              'currencySnapshot': 'EGP',
            },
          ];
        },
      );
      final auth = await login(api);
      await t.pumpWidget(app(NativeAcademyImport(auth: auth)));
      await t.pumpAndSettle();
      final message = find.textContaining(
        'تم التحقق من 1 سند تحصيل سابق. الإجمالي 600.25 USD. لم يُضف قبض جديد.',
      );
      await revealTestTarget(t, message);
      await revealTestTarget(t, find.text('التسجيلات الجديدة 11'));
      await reviewAction(t, find.text('إعادة تحميل التسجيلات المحفوظة'));
      await revealTestTarget(t, message);
      expect(reads, 2);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
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
      await reviewAction(t, find.text('تفاصيل الاستيراد'));
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
    'A missing batch identity can be recovered by reading the original request',
    (t) async {
      var lookups = 0;
      String? key;
      final api = ImportApi(
        work: (commit, fields, intent) async {
          if (!commit) return {...preview, 'financiallyIncomplete': 0};
          key = intent;
          return {'status': 'Completed'};
        },
        read: (path) async {
          if (path.endsWith('/schema')) return {'connectedSheets': <Object>[]};
          if (path.contains('/by-request?')) {
            expect(Uri.parse(path).queryParameters['requestKey'], key);
            if (++lookups == 1) throw const ApiFailure(404, 'غير موجود');
            if (lookups == 2) throw const ApiFailure(503, 'تعذر الاتصال.');
            return job;
          }
          return <Object>[];
        },
      );
      final auth = await login(api);
      await t.pumpWidget(
        app(
          NativeAcademyImport(
            auth: auth,
            pickWorkbook: () async =>
                (name: 'synthetic.xlsx', bytes: Uint8List.fromList([1])),
          ),
        ),
      );
      await t.pumpAndSettle();
      await pick(t);
      await tap(t, 'راجعت علاقات أولياء الأمور وأعتمد ربط الدفعة.');
      await tap(t, 'اعتماد وبدء الاستيراد');
      expect(find.text('لم تُنشر الدفعة'), findsNothing);
      expect(find.text('اختيار ملف XLSX ومعاينته'), findsNothing);
      for (var i = 0; i < 3; i++) {
        await revealTestTarget(t, find.text('التحقق من حالة الاعتماد'));
        await t.tap(find.text('التحقق من حالة الاعتماد'));
        await t.pumpAndSettle();
      }
      expect(lookups, 3);
      expect(api.calls.where((call) => call == 'commit'), hasLength(1));
      expect(find.text('اكتمل الاستيراد'), findsOneWidget);
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );

  testWidgets(
    'Published import explains historical payments from the saved batch',
    (t) async {
      final completed = {
        ...job,
        'result': {
          'historicalPayments': 2,
          'historicalPaymentTotal': 800,
          'effects': [],
        },
      };
      final repeated = {
        ...completed,
        'id': 'job-2',
        'sourceSystem': 'إعادة نفس الدفعة',
        'publishedRows': 0,
        'replayedRows': 11,
      };
      final api = ImportApi(
        read: (path) async {
          if (path.endsWith('/schema')) return {'connectedSheets': <Object>[]};
          if (path.endsWith('/jobs')) return [completed, repeated];
          return <Object>[];
        },
      );
      final auth = await login(api);
      await t.pumpWidget(app(NativeAcademyImport(auth: auth)));
      await t.pumpAndSettle();
      await revealTestTarget(
        t,
        find.textContaining('تتضمن الدفعة 2 سند تحصيل سابق'),
      );
      expect(find.textContaining('800'), findsOneWidget);
      await reviewAction(t, find.text('دفعات الاستيراد السابقة'));
      await reviewAction(t, find.text('إعادة نفس الدفعة · 11 تسجيل'));
      await revealTestTarget(t, find.textContaining('لم يُضف قبض جديد.'));
      expect(find.text('تسجيلات سبق استيرادها 11'), findsOneWidget);
      expect(find.textContaining('800'), findsOneWidget);
      expect(api.calls.where((call) => call == 'commit'), isEmpty);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );

  testWidgets('Revoked access removes the published result and its effects', (
    t,
  ) async {
    final api = ImportApi(
      read: (path) async {
        if (path.endsWith('/schema')) return {'connectedSheets': <Object>[]};
        if (path.endsWith('/jobs')) return [job];
        if (path.endsWith('/jobs/job-1')) {
          throw const ApiFailure(403, 'ليست لديك صلاحية عرض هذه الدفعة.');
        }
        return <Object>[];
      },
    );
    final auth = await login(api);
    await t.pumpWidget(app(NativeAcademyImport(auth: auth)));
    await t.pumpAndSettle();
    await reviewAction(t, find.text('تفاصيل الاستيراد'));
    await reviewAction(t, find.text('تحديث حالة الدفعة'));
    expect(find.text('لا يمكن عرض هذه الصفحة'), findsOneWidget);
    expect(find.text('ليست لديك صلاحية عرض هذه الدفعة.'), findsOneWidget);
    expect(find.text('اكتمل الاستيراد'), findsNothing);
    expect(find.text('تفاصيل الاستيراد'), findsNothing);
    expect(api.calls.where((call) => call.endsWith('/records')), hasLength(1));
    expect(api.calls, isNot(contains('commit')));
    await t.pumpWidget(const SizedBox());
    auth.dispose();
  });

  testWidgets(
    'Published import puts records first and keeps each batch effect together',
    (t) async {
      t.view.physicalSize = const Size(320, 760);
      t.view.devicePixelRatio = 1;
      addTearDown(t.view.resetPhysicalSize);
      addTearDown(t.view.resetDevicePixelRatio);
      final recent = {
        ...job,
        'sourceSystem': 'دفعة حديثة',
        'rowCount': 2,
        'publishedRows': 2,
        'result': {
          'effects': [
            {'sheet': 'الفروع', 'create': 0, 'reuse': 3},
            {'sheet': 'مستويات التدريب', 'create': 0, 'reuse': 0},
          ],
        },
      };
      final earlier = {
        ...job,
        'id': 'job-2',
        'sourceSystem': 'دفعة سابقة',
        'rowCount': 48,
        'publishedRows': 48,
        'result': {
          'coachAssignments': 12,
          'tournamentParticipations': 8,
          'effects': [
            {'sheet': 'الفروع', 'create': 3, 'reuse': 0},
          ],
        },
      };
      final api = ImportApi(
        read: (path) async {
          if (path.endsWith('/schema')) return {'connectedSheets': <Object>[]};
          if (path.endsWith('/jobs')) return [recent, earlier];
          if (path.endsWith('/jobs/job-1')) return recent;
          if (path.endsWith('/records')) {
            return [
              {'player': 'لاعب اصطناعي', 'playerCode': 'QA-1'},
            ];
          }
          return <Object>[];
        },
      );
      final auth = await login(api);
      await t.pumpWidget(app(NativeAcademyImport(auth: auth), scale: 2));
      await t.pumpAndSettle();
      final title = find.text('تفاصيل الاستيراد');
      final financial = find.text(
        'بيانات الاشتراك وحدها لا تنشئ تحصيلًا أو إيصالًا.',
      );
      final records = find.text('التسجيلات المحفوظة · 1');
      final reused = find.text('الفروع · استُخدم من الموجود 3');
      final scroll = t.state<ScrollableState>(find.byType(Scrollable).first);
      Future<double> position(Finder target) async {
        await revealTestTarget(t, target);
        return t.getTopLeft(target).dy + scroll.position.pixels;
      }

      expect(await position(financial), lessThan(await position(records)));
      expect(await position(records), lessThan(await position(title)));
      expect(find.text('تنزيل قالب فارغ'), findsNothing);
      expect(reused, findsNothing);
      final initialCalls = api.calls.length;
      await reviewAction(t, title);
      await revealTestTarget(t, reused);
      expect(t.getRect(reused).right, lessThanOrEqualTo(320));
      expect(find.textContaining('مستويات التدريب'), findsNothing);
      expect(find.textContaining('أُضيف 0'), findsNothing);
      await reviewAction(t, title);
      expect(reused, findsNothing);
      expect(api.calls.length, initialCalls);
      await reviewAction(t, records);
      await revealTestTarget(t, find.text('لاعب اصطناعي'));
      await reviewAction(t, title);
      await reviewAction(t, find.text('تحديث حالة الدفعة'));
      await revealTestTarget(t, reused);
      expect(
        api.calls.where((call) => call.endsWith('/job-1/records')),
        hasLength(2),
      );
      await reviewAction(t, find.text('دفعات الاستيراد السابقة'));
      await reviewAction(t, find.text('دفعة سابقة · 48 تسجيل'));
      await revealTestTarget(t, find.text('التسجيلات الجديدة 48'));
      expect(reused, findsNothing);
      expect(find.text('الفروع · أُضيف 3'), findsNothing);
      await reviewAction(t, title);
      await revealTestTarget(t, find.text('الفروع · أُضيف 3'));
      expect(find.text('12 إسناد مدرب لمجموعة'), findsOneWidget);
      expect(find.text('8 مشاركة في البطولات'), findsOneWidget);
      await reviewAction(t, find.text('القوالب ودليل الحقول'));
      await revealTestTarget(t, find.text('تنزيل قالب فارغ'));
      expect(
        api.calls.where((call) => call.endsWith('/job-2/records')),
        hasLength(1),
      );
      expect(api.calls, isNot(contains('commit')));
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );

  testWidgets(
    'Preview effects are optional to read and collapse for a different workbook',
    (t) async {
      final api = ImportApi();
      var selection = 0;
      final auth = await openSplit(
        t,
        api,
        choose: () async => (
          name: 'preview-${++selection}.xlsx',
          bytes: Uint8List.fromList([selection]),
        ),
      );
      final details = find.text('الإضافة والربط من الملف');
      final added = find.text('المدربون · سيُضاف 2');
      final approve = find.widgetWithText(
        FilledButton,
        'اعتماد وبدء الاستيراد',
      );
      await revealTestTarget(t, approve);
      expect(t.widget<FilledButton>(approve).onPressed, isNull);
      expect(added, findsNothing);
      await reviewAction(t, details);
      await revealTestTarget(t, added);
      await revealTestTarget(t, approve);
      expect(t.widget<FilledButton>(approve).onPressed, isNull);
      await reviewAction(t, find.text('اختيار ملف XLSX ومعاينته'));
      await revealTestTarget(t, details);
      expect(added, findsNothing);
      expect(api.calls.where((call) => call == 'preview'), hasLength(2));
      expect(api.calls, isNot(contains('commit')));
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );

  testWidgets(
    'Legacy and incomplete results retain records without false effect numbers',
    (t) async {
      final missing = {...job, 'result': null, 'publishedRows': null};
      final malformed = {
        ...job,
        'id': 'job-2',
        'sourceSystem': 'رد ناقص',
        'publishedRows': '11',
        'replayedRows': -1,
        'result': {
          'effects': [
            null,
            'broken',
            {'sheet': 'الفروع', 'create': '4', 'reuse': -1},
          ],
          'coachAssignments': '10',
        },
      };
      final zeros = {
        ...job,
        'id': 'job-3',
        'sourceSystem': 'دفعة بلا إضافات',
        'publishedRows': 0,
        'replayedRows': 11,
        'result': {
          'effects': [
            {'sheet': 'الفروع', 'create': 0, 'reuse': 0},
          ],
          'tournamentParticipations': 0,
        },
      };
      final api = ImportApi(
        read: (path) async {
          if (path.endsWith('/schema')) return {'connectedSheets': <Object>[]};
          if (path.endsWith('/jobs')) return [missing, malformed, zeros];
          if (path.endsWith('/records')) {
            return [
              {'player': 'لاعب محفوظ', 'playerCode': 'QA-1'},
            ];
          }
          return <Object>[];
        },
      );
      final auth = await login(api);
      await t.pumpWidget(app(NativeAcademyImport(auth: auth)));
      await t.pumpAndSettle();
      for (final source in [null, 'رد ناقص', 'دفعة بلا إضافات']) {
        if (source != null) {
          final history = find.widgetWithText(
            ExpansionTile,
            'دفعات الاستيراد السابقة',
          );
          await revealTestTarget(t, history);
          if (find.text('$source · 11 تسجيل').evaluate().isEmpty) {
            await reviewAction(t, history);
          }
          await reviewAction(t, find.text('$source · 11 تسجيل'));
        }
        await revealTestTarget(
          t,
          find.text(
            source == 'دفعة بلا إضافات'
                ? 'التسجيلات الجديدة 0'
                : 'عدد التسجيلات الجديدة غير متاح',
          ),
        );
        await revealTestTarget(t, find.text('التسجيلات المحفوظة · 1'));
        expect(find.text('تفاصيل الاستيراد'), findsNothing);
        expect(find.textContaining('إسناد مدرب'), findsNothing);
      }
      await revealTestTarget(t, find.text('تسجيلات سبق استيرادها 11'));
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );

  testWidgets('One player has distinct searchable sport registrations', (
    t,
  ) async {
    final api = ImportApi(
      work: (commit, fields, key) async => {
        ...preview,
        'rowCount': 2,
        'playerCount': 1,
        'financiallyIncomplete': 0,
        'rows': [
          for (final entry in [('EN-SW', '600'), ('EN-FOOT', '1000')])
            {
              'legacyEnrollmentId': entry.$1,
              'legacyPlayerId': 'PL01',
              'playerName': 'لاعب اختبار',
              'guardianName': 'ولي أمر اختبار',
              'guardianPhone': '01098765432',
              'start': entry.$1 == 'EN-SW' ? '2026-11-01' : '2026-11-12',
              'end': entry.$1 == 'EN-SW' ? '2026-11-30' : null,
              'agreedNet': entry.$2,
              'currency': 'EGP',
            },
        ],
        'registrationLabels': {
          'EN-SW': {
            'sport': 'السباحة',
            'group': 'مجموعة الموج',
            'branch': 'مدينة نصر',
          },
          'EN-FOOT': {
            'sport': 'كرة القدم',
            'group': 'فريق الكرة',
            'branch': 'مدينة نصر',
          },
        },
      },
    );
    final auth = await login(api);
    await t.pumpWidget(
      app(
        NativeAcademyImport(
          auth: auth,
          pickWorkbook: () async =>
              (name: 'multisport.xlsx', bytes: Uint8List.fromList([1])),
        ),
      ),
    );
    await t.pumpAndSettle();
    await pick(t);
    await tap(t, 'معاينة التسجيلات · 2');
    final search = find.byKey(const ValueKey('import-record-search'));
    final swimming = find.text('السباحة · مجموعة الموج · مدينة نصر');
    final football = find.text('كرة القدم · فريق الكرة · مدينة نصر');
    await revealTestTarget(t, search);
    expect(swimming, findsOneWidget);
    expect(football, findsOneWidget);
    expect(find.text('البداية 1 نوفمبر 2026'), findsOneWidget);
    expect(find.text('النهاية 30 نوفمبر 2026'), findsOneWidget);
    await t.enterText(search, 'السباحة');
    await t.pumpAndSettle();
    expect(swimming, findsOneWidget);
    expect(football, findsNothing);
    await t.enterText(search, 'فريق الكرة');
    await t.pumpAndSettle();
    expect(swimming, findsNothing);
    expect(football, findsOneWidget);
    expect(find.text('البداية 12 نوفمبر 2026'), findsOneWidget);
    expect(find.textContaining('النهاية'), findsNothing);
    await t.tap(find.byTooltip('مسح البحث'));
    await t.pumpAndSettle();
    expect(swimming, findsOneWidget);
    expect(football, findsOneWidget);
    expect(api.calls.where((call) => call == 'commit'), isEmpty);
    await t.pumpWidget(const SizedBox());
    auth.dispose();
  });

  testWidgets(
    'All fifty preview rows are searchable and review has no writes',
    (t) async {
      final api = ImportApi(
        work: (commit, fields, key) async => {
          ...preview,
          'rowCount': 50,
          'playerCount': 50,
          'financiallyIncomplete': 0,
          'rows': [
            for (var n = 1; n <= 50; n++)
              {
                'playerName': 'لاعب معاينة $n',
                'legacyPlayerId': 'P$n',
                'guardianName': n == 50 ? 'ولي أخير اصطناعي' : 'ولي أمر العرض',
                'guardianPhone': '01000000012',
                'agreedNet': 750,
                'currency': 'EGP',
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
                (name: 'preview.xlsx', bytes: Uint8List.fromList([1])),
          ),
        ),
      );
      await t.pumpAndSettle();
      await pick(t);
      await tap(t, 'معاينة التسجيلات · 50');
      expect(find.text('1–10 من 50'), findsOneWidget);
      expect(
        t
            .widget<OutlinedButton>(
              find.widgetWithText(OutlinedButton, 'السابق'),
            )
            .onPressed,
        isNull,
      );
      for (var page = 1; page < 5; page++) {
        await t.tap(find.text('التالي'));
        await t.pumpAndSettle();
      }
      expect(find.text('41–50 من 50'), findsOneWidget);
      expect(find.text('لاعب معاينة 50'), findsOneWidget);
      expect(
        t
            .widget<OutlinedButton>(
              find.widgetWithText(OutlinedButton, 'التالي'),
            )
            .onPressed,
        isNull,
      );
      final search = find.byKey(const ValueKey('import-record-search'));
      await t.enterText(search, 'p٥٠');
      await t.pumpAndSettle();
      expect(find.text('1–1 من 1'), findsOneWidget);
      expect(find.text('لاعب معاينة 50'), findsOneWidget);
      await t.enterText(search, 'ولي أخير اصطناعي');
      await t.pumpAndSettle();
      expect(find.text('لاعب معاينة 50'), findsOneWidget);
      await t.enterText(search, 'غير موجود');
      await t.pumpAndSettle();
      expect(find.text('لا توجد تسجيلات مطابقة.'), findsOneWidget);
      await t.tap(find.byTooltip('مسح البحث'));
      await t.pumpAndSettle();
      expect(find.text('1–10 من 50'), findsOneWidget);
      expect(api.calls.where((call) => call == 'commit'), isEmpty);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );

  testWidgets(
    'A schema error does not suppress the saved batch or its last record',
    (t) async {
      var schemaReads = 0;
      final api = ImportApi(
        read: (path) async {
          if (path.endsWith('/schema')) {
            if (++schemaReads == 1) {
              throw const ApiFailure(503, 'تعذر الاتصال.');
            }
            return {'connectedSheets': <Object>[]};
          }
          if (path.endsWith('/jobs')) {
            return [
              {...job, 'rowCount': 50, 'publishedRows': 50},
            ];
          }
          if (path.endsWith('/records')) {
            return [
              for (var n = 1; n <= 50; n++)
                {
                  'player': 'لاعب محفوظ $n',
                  'playerCode': 'QA-$n',
                  'sport': 'السباحة',
                  'branch': 'فرع اختبار',
                  'groupName': 'مجموعة الاختبار',
                  'priceSnapshot': n == 50 ? 987.5 : 750,
                  'currencySnapshot': 'EGP',
                  'remainingSessions': 7,
                  'startDate': '2026-09-28',
                  'endDate': '2026-10-28',
                },
            ];
          }
          return <Object>[];
        },
      );
      final auth = await login(api);
      await t.pumpWidget(app(NativeAcademyImport(auth: auth)));
      await t.pumpAndSettle();
      await revealTestTarget(t, find.text('اكتمل الاستيراد'));
      await reviewAction(t, find.text('القوالب ودليل الحقول'));
      await revealTestTarget(t, find.text('إعادة تحميل دليل الحقول'));
      await t.tap(find.text('إعادة تحميل دليل الحقول'));
      await t.pumpAndSettle();
      expect(schemaReads, 2);
      expect(api.calls.where((path) => path.endsWith('/jobs')), hasLength(1));
      await revealTestTarget(t, find.text('التسجيلات المحفوظة · 50'));
      await t.tap(find.text('التسجيلات المحفوظة · 50'));
      await t.pumpAndSettle();
      final search = find.byKey(const ValueKey('import-record-search'));
      await t.enterText(search, 'qa-٥٠');
      await t.pumpAndSettle();
      expect(find.text('لاعب محفوظ 50'), findsOneWidget);
      expect(find.textContaining('987.50'), findsOneWidget);
      expect(find.text('الحصص المتبقية 7'), findsOneWidget);
      expect(find.text('البداية 28 سبتمبر 2026'), findsOneWidget);
      expect(find.text('النهاية 28 أكتوبر 2026'), findsOneWidget);
      expect(api.calls.where((call) => call == 'commit'), isEmpty);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );

  testWidgets(
    'Saved rows retry by read and disappear if the batch becomes unavailable',
    (t) async {
      var attempts = 0;
      final api = ImportApi(
        read: (path) async {
          if (path.endsWith('/schema')) return {'connectedSheets': <Object>[]};
          if (path.endsWith('/jobs')) return [job];
          if (path.endsWith('/jobs/job-1')) return job;
          if (path.endsWith('/records')) {
            switch (++attempts) {
              case 1:
                throw const ApiFailure(503, 'تعذر الاتصال.');
              case 3:
                throw const ApiFailure(404, 'غير متاح.');
              default:
                return [
                  {
                    'player': 'لاعب معتمد اصطناعي',
                    'playerCode': 'QA-A',
                    'sport': 'السباحة',
                    'branch': 'فرع الاختبار',
                    'groupName': 'مجموعة أ',
                    'startDate': '2026-09-28',
                    'endDate': '2026-10-28',
                  },
                ];
            }
          }
          return <Object>[];
        },
      );
      final auth = await login(api);
      await t.pumpWidget(app(NativeAcademyImport(auth: auth)));
      await t.pumpAndSettle();
      await revealTestTarget(t, find.text('اكتمل الاستيراد'));
      expect(find.text('لا توجد تسجيلات محفوظة لهذه الدفعة.'), findsNothing);
      await revealTestTarget(t, find.text('إعادة تحميل التسجيلات المحفوظة'));
      await t.tap(find.text('إعادة تحميل التسجيلات المحفوظة'));
      await t.pumpAndSettle();
      expect(api.calls.where((path) => path.endsWith('/jobs/job-1')), isEmpty);
      await revealTestTarget(t, find.text('التسجيلات المحفوظة · 1'));
      await t.tap(find.text('التسجيلات المحفوظة · 1'));
      await t.pumpAndSettle();
      await revealTestTarget(t, find.text('لاعب معتمد اصطناعي'));
      await revealTestTarget(t, find.text('تحديث حالة الدفعة'));
      await t.tap(find.text('تحديث حالة الدفعة'));
      await t.pumpAndSettle();
      await revealTestTarget(
        t,
        find.text('الدفعة غير متاحة في السياق الحالي.'),
      );
      expect(find.text('اكتمل الاستيراد'), findsNothing);
      expect(find.text('لاعب معتمد اصطناعي'), findsNothing);
      await revealTestTarget(t, find.text('إعادة التحقق من حالة الدفعة'));
      await t.tap(find.text('إعادة التحقق من حالة الدفعة'));
      await t.pumpAndSettle();
      await revealTestTarget(t, find.text('اكتمل الاستيراد'));
      expect(attempts, 4);
      expect(api.calls.where((call) => call == 'commit'), isEmpty);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );

  testWidgets(
    'History retries independently and switching batches resets the search',
    (t) async {
      var historyReads = 0;
      final current = {...job, 'sourceSystem': 'دفعة حديثة'};
      final old = {...job, 'id': 'job-2', 'sourceSystem': 'دفعة أقدم'};
      final api = ImportApi(
        read: (path) async {
          if (path.endsWith('/schema')) return {'connectedSheets': <Object>[]};
          if (path.endsWith('/jobs')) {
            if (++historyReads == 1) {
              throw const ApiFailure(503, 'تعذر الاتصال.');
            }
            return [current, old];
          }
          if (path.endsWith('/records')) {
            return [
              {
                'player': path.contains('job-1')
                    ? 'لاعب الدفعة الحديثة'
                    : 'لاعب الدفعة الأقدم',
                'playerCode': path.contains('job-1') ? 'QA-NEW' : 'QA-OLD',
                'sport': 'السباحة',
                'branch': 'فرع الاختبار',
                'groupName': 'مجموعة أ',
              },
            ];
          }
          return <Object>[];
        },
      );
      final auth = await login(api);
      await t.pumpWidget(app(NativeAcademyImport(auth: auth)));
      await t.pumpAndSettle();
      await revealTestTarget(t, find.text('تنزيل قالب فارغ'));
      await revealTestTarget(t, find.text('إعادة تحميل سجل الدفعات'));
      await t.tap(find.text('إعادة تحميل سجل الدفعات'));
      await t.pumpAndSettle();
      expect(api.calls.where((path) => path.endsWith('/schema')), hasLength(1));
      await revealTestTarget(t, find.text('التسجيلات المحفوظة · 1'));
      await t.tap(find.text('التسجيلات المحفوظة · 1'));
      await t.pumpAndSettle();
      final search = find.byKey(const ValueKey('import-record-search'));
      await t.enterText(search, 'QA-NEW');
      await t.pumpAndSettle();
      expect(find.text('لاعب الدفعة الحديثة'), findsOneWidget);
      await revealTestTarget(t, find.text('دفعات الاستيراد السابقة'));
      await t.tap(find.text('دفعات الاستيراد السابقة'));
      await t.pumpAndSettle();
      await revealTestTarget(t, find.textContaining('دفعة أقدم'));
      await t.tap(find.textContaining('دفعة أقدم'));
      await t.pumpAndSettle();
      await revealTestTarget(t, find.text('التسجيلات المحفوظة · 1'));
      await t.tap(find.text('التسجيلات المحفوظة · 1'));
      await t.pumpAndSettle();
      expect(t.widget<TextField>(search).controller!.text, isEmpty);
      expect(find.text('لاعب الدفعة الأقدم'), findsOneWidget);
      expect(find.text('لاعب الدفعة الحديثة'), findsNothing);
      expect(api.calls.where((call) => call == 'commit'), isEmpty);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );

  for (final matched in [true, false]) {
    testWidgets(
      'Household match requires the exact in-academy candidate, matched=$matched',
      (t) async {
        final confirmations = <String?>[];
        final api = ImportApi(
          work: (commit, fields, key) async {
            if (commit) {
              confirmations.add(fields['GuardianResolutions']);
              return job;
            }
            return {
              ...preview,
              'financiallyIncomplete': 0,
              'guardianMatches': [
                {
                  'householdKey': 'H:QA',
                  'guardianName': 'ولي أمر اصطناعي',
                  'phone': '01000000012',
                  'candidates': matched ? ['allowed'] : [],
                },
              ],
            };
          },
        );
        final auth = await login(api);
        await t.pumpWidget(
          app(
            NativeAcademyImport(
              auth: auth,
              pickWorkbook: () async =>
                  (name: 'match.xlsx', bytes: Uint8List.fromList([1])),
            ),
          ),
        );
        await t.pumpAndSettle();
        await pick(t);
        await tap(t, 'راجعت علاقات أولياء الأمور وأعتمد ربط الدفعة.');
        final approve = find.widgetWithText(
          FilledButton,
          'اعتماد وبدء الاستيراد',
        );
        await revealTestTarget(t, approve);
        expect(t.widget<FilledButton>(approve).onPressed, isNull);
        if (matched) {
          await revealTestTarget(
            t,
            find.text('ربط هذه الأسرة بالحساب المطابق القائم'),
          );
          await t.tap(find.text('ربط هذه الأسرة بالحساب المطابق القائم'));
          await t.pumpAndSettle();
          await revealTestTarget(t, approve);
          await t.tap(approve);
          await t.pumpAndSettle();
          expect(confirmations, [
            '[{"householdKey":"H:QA","guardianId":"allowed"}]',
          ]);
        } else {
          await revealTestTarget(
            t,
            find.textContaining('تعذر تحديد حساب مطابق'),
          );
          expect(confirmations, isEmpty);
        }
        await t.pumpWidget(const SizedBox());
        auth.dispose();
      },
    );
  }

  testWidgets(
    'Malformed details are retryable while a successful empty list is explicit',
    (t) async {
      var reads = 0;
      final api = ImportApi(
        read: (path) async {
          if (path.endsWith('/schema')) return {'connectedSheets': <Object>[]};
          if (path.endsWith('/jobs')) return [job];
          if (path.endsWith('/records') && ++reads == 1) {
            return {'unexpected': []};
          }
          return <Object>[];
        },
      );
      final auth = await login(api);
      await t.pumpWidget(app(NativeAcademyImport(auth: auth)));
      await t.pumpAndSettle();
      await revealTestTarget(t, find.text('إعادة تحميل التسجيلات المحفوظة'));
      expect(find.text('لا توجد تسجيلات محفوظة لهذه الدفعة.'), findsNothing);
      await t.tap(find.text('إعادة تحميل التسجيلات المحفوظة'));
      await t.pumpAndSettle();
      await revealTestTarget(
        t,
        find.text('لا توجد تسجيلات محفوظة لهذه الدفعة.'),
      );
      expect(find.text('إعادة تحميل التسجيلات المحفوظة'), findsNothing);
      expect(reads, 2);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );

  testWidgets(
    'Upload leads on an empty history and preserves the preview state',
    (t) async {
      final api = ImportApi();
      final auth = await login(api);
      await t.pumpWidget(
        app(
          NativeAcademyImport(
            auth: auth,
            pickWorkbook: () async =>
                (name: '00-repaired-two.xlsx', bytes: Uint8List.fromList([1])),
          ),
        ),
      );
      await t.pumpAndSettle();
      final upload = find.text('اختيار ملف XLSX ومعاينته');
      final template = find.text('تنزيل قالب فارغ');
      expect(t.getTopLeft(upload).dy, lessThan(t.getTopLeft(template).dy));
      expect(find.byType(Chip), findsNothing);
      expect(find.text('اكتمل الاستيراد'), findsNothing);
      await pick(t);
      await revealTestTarget(t, find.text('اكتملت المعاينة دون أخطاء'));
      final filename = find.text('00-repaired-two.xlsx');
      await revealTestTarget(t, filename);
      expect(t.widget<Text>(filename).textDirection, TextDirection.ltr);
      final summary = find.text('اللاعبون 10 · التسجيلات 11 · الأسر 9');
      await revealTestTarget(t, summary);
      expect(find.byType(Chip), findsNothing);
      expect(api.calls.where((call) => call == 'preview'), hasLength(1));
      expect(api.calls, isNot(contains('commit')));
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );

  for (final width in [320.0, 375.0, 390.0]) {
    testWidgets(
      'Import rows and pagination fit at ${width.toInt()}px with 200% text',
      (t) async {
        t.view.physicalSize = Size(width, 850);
        t.view.devicePixelRatio = 1;
        addTearDown(t.view.resetPhysicalSize);
        addTearDown(t.view.resetDevicePixelRatio);
        final api = ImportApi(
          read: (path) async {
            if (path.endsWith('/schema')) {
              return {'connectedSheets': <Object>[]};
            }
            if (path.endsWith('/jobs')) {
              return [
                {...job, 'rowCount': 12, 'publishedRows': 12},
              ];
            }
            if (path.endsWith('/records')) {
              return [
                for (var n = 1; n <= 12; n++)
                  {
                    'player': 'اسم لاعب اصطناعي طويل لاختبار التفاف الاسم $n',
                    'playerCode': 'QA-PREVIEW-LONG-$n',
                    'sport': 'السباحة',
                    'branch': 'فرع العرض الاصطناعي',
                    'groupName': 'مجموعة العرض طويلة الاسم',
                    'startDate': '2026-09-28',
                    'endDate': '2026-10-28',
                  },
              ];
            }
            return <Object>[];
          },
        );
        final auth = await login(api);
        await t.pumpWidget(app(NativeAcademyImport(auth: auth), scale: 2));
        await t.pumpAndSettle();
        expect(find.text('تنزيل قالب فارغ'), findsNothing);
        expect(find.byType(Chip), findsNothing);
        final summary = find.text('التسجيلات الجديدة 12');
        await revealTestTarget(t, summary);
        final title = find.text('التسجيلات المحفوظة · 12');
        await revealTestTarget(t, title);
        await t.tap(title);
        await t.pumpAndSettle();
        final next = find.text('التالي');
        await revealTestTarget(t, next);
        await t.tap(next);
        await t.pumpAndSettle();
        expect(find.text('11–12 من 12'), findsOneWidget);
        final last = find.text('اسم لاعب اصطناعي طويل لاختبار التفاف الاسم 12');
        await revealTestTarget(t, last);
        final bounds = t.getRect(last);
        expect(bounds.left, greaterThanOrEqualTo(0));
        expect(bounds.right, lessThanOrEqualTo(width));
        expect(t.takeException(), isNull);
        await t.pumpWidget(const SizedBox());
        auth.dispose();
      },
    );
  }

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
