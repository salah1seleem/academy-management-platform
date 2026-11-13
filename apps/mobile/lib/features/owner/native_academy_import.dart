import 'dart:async';
import 'dart:convert';
import 'dart:typed_data';

import 'package:file_picker/file_picker.dart';
import 'package:flutter/material.dart';
import 'package:intl/intl.dart' show Bidi, DateFormat;

import '../../core/api/api_client.dart';
import '../../core/auth/auth_controller.dart';
import '../../core/theme/ui_text.dart';
import '../../core/widgets/arabic_number_input.dart';
import '../admin/native_operation_form.dart';
import '../guardian/guardian_ui.dart';

typedef WorkbookSelection = ({String name, Uint8List bytes});
typedef _ImportIssue = ({
  String sheetId,
  String sheet,
  int? row,
  String column,
  String message,
});

String? _importDate(Object? value) {
  final date = value is String ? DateTime.tryParse(value) : null;
  if (date == null) return null;
  final format = DateFormat.yMMMd('ar')..useNativeDigits = false;
  return format.format(date);
}

int? _importCount(Object? value) => value is int && value >= 0 ? value : null;

String _historicalPaymentTotal(Json data, Object? recordCurrency) {
  final amount = data['historicalPaymentTotal'];
  if (amount is! num || !amount.isFinite || amount < 0) {
    return 'إجمالي التحصيل السابق غير متاح.';
  }
  for (final code in [data['historicalPaymentCurrency'], recordCurrency]) {
    if (code is String && RegExp(r'^[A-Z]{3}$').hasMatch(code)) {
      return 'الإجمالي ${money(amount, code)}.';
    }
  }
  return 'الإجمالي ${amountText(amount)}، والعملة غير متاحة.';
}

class NativeAcademyImport extends StatefulWidget {
  const NativeAcademyImport({
    super.key,
    required this.auth,
    this.pickWorkbook,
    this.saveWorkbook,
  });
  final AuthController auth;
  final Future<WorkbookSelection?> Function()? pickWorkbook;

  /// File destination override for workbook and report saves.
  final Future<bool> Function(String, Uint8List)? saveWorkbook;
  @override
  State<NativeAcademyImport> createState() => _NativeAcademyImportState();
}

class _NativeAcademyImportState
    extends NativeOperationState<NativeAcademyImport> {
  @override
  AuthController get auth => widget.auth;
  Json? schema, preview, job;
  List<Json> jobs = [], records = [];
  bool schemaIssue = false, historyIssue = false, recordsLoaded = false;
  bool batchUnavailable = false;
  int jobView = 0;
  String? recordsIssue;
  WorkbookSelection? file;
  bool approved = false,
      balancesApproved = false,
      paymentsApproved = false,
      financialReview = false,
      uncertain = false;
  final resolutions = <String, String>{};
  final issueRow = TextEditingController();
  String issueSheet = '';
  String? reportFeedback;
  final splitFeedback = <bool, String>{};
  bool splitNeedsRecheck = false;
  String? intent;
  Map<String, String>? stagedFields;
  Timer? poll;
  final scroll = ScrollController();
  void showStart() => WidgetsBinding.instance.addPostFrameCallback((_) {
    if (current && scroll.hasClients) scroll.jumpTo(0);
  });
  static const root = '/api/v1/people/import';
  static const jobStates = [
    'Queued',
    'Running',
    'Completed',
    'Failed',
    'Expired',
  ];

  Json readJob(Object? value) {
    final batch = value is Map ? obj(value) : const <String, dynamic>{};
    final id = batch['id'];
    if (id is! String ||
        !RegExp(r'^[a-zA-Z0-9-]+$').hasMatch(id) ||
        !jobStates.contains(batch['status'])) {
      throw const ApiFailure(0, 'تعذر تأكيد اعتماد الدفعة.');
    }
    return batch;
  }

  @override
  void initState() {
    super.initState();
    load();
  }

  @override
  void changedContext() {
    super.changedContext();
    if (!current) poll?.cancel();
  }

  @override
  void dispose() {
    poll?.cancel();
    issueRow.dispose();
    scroll.dispose();
    super.dispose();
  }

  Future<void> load() => run(() async {
    await Future.wait([readSchema(), readHistory()]);
  });

  Future<void> readSchema() async {
    try {
      final value = await auth.request('GET', '$root/schema');
      if (value is! Map || value['connectedSheets'] is! List) {
        throw const ApiFailure(0, 'تعذر قراءة دليل الحقول.');
      }
      if (current) {
        setState(() {
          schema = obj(value);
          schemaIssue = false;
        });
      }
    } on ApiFailure catch (failure) {
      if ([401, 403].contains(failure.status)) rethrow;
      if (current) setState(() => schemaIssue = true);
    }
  }

  Future<void> readHistory() async {
    try {
      final value = await auth.request('GET', '$root/jobs');
      if (value is! List) throw const ApiFailure(0, 'تعذر قراءة سجل الدفعات.');
      final batches = value.map(readJob).toList();
      if (!current) return;
      setState(() {
        jobs = batches;
        historyIssue = false;
      });
      if (job == null && preview == null && !uncertain && jobs.isNotEmpty) {
        await showJob(jobs.first);
      }
    } on ApiFailure catch (failure) {
      if ([401, 403].contains(failure.status)) rethrow;
      if (current) setState(() => historyIssue = true);
    }
  }

  Future<void> readRecords() async {
    final id = job?['id'];
    if (!current || id == null) return;
    try {
      final value = await auth.request('GET', '$root/jobs/$id/records');
      if (value is! List || value.any((row) => row is! Map)) {
        throw const ApiFailure(0, 'تعذر قراءة التسجيلات المحفوظة.');
      }
      if (current && job?['id'] == id) {
        setState(() {
          records = rows(value);
          recordsLoaded = true;
          recordsIssue = null;
          batchUnavailable = false;
        });
      }
    } on ApiFailure catch (failure) {
      if (!current || job?['id'] != id) return;
      setState(() {
        records = [];
        recordsLoaded = false;
        if ([401, 403, 404].contains(failure.status)) batchUnavailable = true;
        recordsIssue = 'تعذر تحميل التسجيلات المحفوظة.';
      });
      if ([401, 403].contains(failure.status)) rethrow;
    }
  }

  Future<void> track() async {
    if (!current || job == null) return;
    poll?.cancel();
    if (job!['status'] == 'Completed') {
      await readRecords();
    } else if (['Queued', 'Running'].contains(job!['status'])) {
      poll = Timer.periodic(const Duration(seconds: 2), (_) {
        if (!busy && current) refreshJob();
      });
    }
  }

  Future<void> refreshJob() => run(() async {
    final id = job?['id'];
    if (id == null) return;
    Json value;
    try {
      value = readJob(await auth.request('GET', '$root/jobs/$id'));
      if (value['id'] != id) {
        throw const ApiFailure(0, 'تعذر قراءة حالة الدفعة المطلوبة.');
      }
    } on ApiFailure catch (failure) {
      if ([401, 403, 404, 429].contains(failure.status)) poll?.cancel();
      if (current && [401, 403, 404].contains(failure.status)) {
        setState(() {
          records = [];
          recordsLoaded = false;
          batchUnavailable = true;
        });
        if (failure.status == 404) return;
      }
      rethrow;
    }
    if (current) {
      setState(() {
        job = {
          ...value,
          if (job!['replayedRequest'] == true) 'replayedRequest': true,
        };
        if (value['status'] != 'Completed') batchUnavailable = false;
      });
      await track();
    }
  });
  Future<void> download(bool sample) => run(() async {
    final bytes = await auth.asset(
      '$root/${sample ? 'connected-sample' : 'connected-template'}',
    );
    if (!current) return;
    final name = sample
        ? 'I-Academy-One-Sample.xlsx'
        : 'I-Academy-One-Template.xlsx';
    await save(name, bytes);
  });

  String? get previewFileHash {
    final hash = preview?['fileHash'];
    return hash is String && hash.trim().isNotEmpty ? hash : null;
  }

  Future<void> downloadPlayerRows({bool corrections = false}) => run(() async {
    final selected = file;
    final hash = previewFileHash;
    if (selected == null || hash == null || splitNeedsRecheck) return;
    setState(() => splitFeedback.remove(corrections));
    final Object? bytes;
    try {
      bytes = await auth.importWorkbook(
        selected.bytes,
        validRowsFromHash: hash,
        correctionRows: corrections,
      );
    } on ApiFailure catch (failure) {
      if (failure.status == 401 || failure.status == 403) rethrow;
      if (!current) return;
      setState(() {
        if (failure.status == 409) {
          splitFeedback.clear();
          splitNeedsRecheck = true;
          return;
        }
        splitFeedback[corrections] = failure.status == 429
            ? 'محاولات كثيرة. انتظر قليلًا ثم أعد التنزيل.'
            : 'تعذّر تنزيل الملف. أعد المحاولة.';
      });
      return;
    }
    if (!current) return;
    if (bytes is! Uint8List) {
      setState(() => splitFeedback[corrections] = 'تعذّر تنزيل ملف XLSX صالح.');
      return;
    }
    final String feedback;
    try {
      final saved = await saveXlsx(
        corrections
            ? 'academy-player-corrections.xlsx'
            : 'academy-valid-players.xlsx',
        bytes,
      );
      if (!saved) return;
      feedback = corrections
          ? 'تم حفظ ملف صفوف التصحيح.'
          : 'تم حفظ ملف اللاعبين السليمين.';
    } catch (_) {
      if (current) {
        setState(() {
          splitFeedback[corrections] = 'تعذّر حفظ الملف. أعد المحاولة.';
        });
      }
      return;
    }
    if (current) setState(() => splitFeedback[corrections] = feedback);
  });
  Future<bool> saveFile(
    String name,
    Uint8List bytes, {
    required String mimeType,
    required String title,
  }) async {
    if (widget.saveWorkbook case final save?) return save(name, bytes);
    return await FilePicker.saveFile(
          fileName: name,
          bytes: bytes,
          mimeType: mimeType,
          dialogTitle: title,
        ) !=
        null;
  }

  Future<bool> saveXlsx(String name, Uint8List bytes) => saveFile(
    name,
    bytes,
    mimeType:
        'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
    title: 'حفظ ملف الأكاديمية',
  );

  Future<void> save(String name, Uint8List bytes) async {
    final saved = await saveXlsx(name, bytes);
    if (current && saved) setState(() => message = 'تم حفظ ملف XLSX.');
  }

  Future<void> saveIssues(List<_ImportIssue> issues) => run(() async {
    setState(() => reportFeedback = null);
    String csvCell(Object? value) {
      final text = '${value ?? ''}';
      final escaped = RegExp(r'^\s*[=+@-]').hasMatch(text) ? "'$text" : text;
      return '"${escaped.replaceAll('"', '""')}"';
    }

    final csv = [
      ['الورقة', 'الصف', 'الحقل', 'المشكلة'],
      for (final issue in issues)
        [issue.sheet, issue.row, issue.column, issue.message],
    ].map((line) => line.map(csvCell).join(',')).join('\n');
    String feedback;
    try {
      final saved = await saveFile(
        'academy-import-errors.csv',
        Uint8List.fromList(utf8.encode('\uFEFF$csv')),
        mimeType: 'text/csv',
        title: 'حفظ تقرير الأخطاء',
      );
      if (!saved) return;
      feedback = 'تم حفظ تقرير الأخطاء.';
    } catch (_) {
      feedback = 'تعذر حفظ تقرير الأخطاء. أعد المحاولة.';
    }
    if (current) setState(() => reportFeedback = feedback);
  });

  Future<WorkbookSelection?> pick() async {
    if (widget.pickWorkbook != null) return widget.pickWorkbook!();
    final chosen = await FilePicker.pickFile(
      type: FileType.custom,
      allowedExtensions: ['xlsx'],
    );
    if (chosen == null) return null;
    final length = await chosen.length();
    if (length == null || length == 0 || length > 5 * 1024 * 1024) {
      throw const ApiFailure(413, 'اختر ملف XLSX بحجم لا يزيد عن 5 ميجابايت.');
    }
    return (name: chosen.name, bytes: await chosen.readAsBytes());
  }

  Future<void> upload({bool reuse = false}) => run(() async {
    if (uncertain) return;
    final chosen = reuse ? file : await pick();
    if (chosen == null || !current) return;
    if (!chosen.name.toLowerCase().endsWith('.xlsx') ||
        chosen.bytes.isEmpty ||
        chosen.bytes.length > 5 * 1024 * 1024) {
      throw const ApiFailure(413, 'اختر ملف XLSX بحجم لا يزيد عن 5 ميجابايت.');
    }
    poll?.cancel();
    issueRow.clear();
    setState(() {
      file = chosen;
      preview = null;
      issueSheet = '';
      reportFeedback = null;
      splitFeedback.clear();
      splitNeedsRecheck = false;
      job = null;
      records = [];
      recordsLoaded = false;
      recordsIssue = null;
      batchUnavailable = false;
      approved = false;
      balancesApproved = false;
      paymentsApproved = false;
      financialReview = false;
      resolutions.clear();
      intent = commandKey();
      stagedFields = null;
      uncertain = false;
    });
    final result = obj(await auth.importWorkbook(chosen.bytes));
    final issues = result['errors'];
    final approval = result['canApprove'];
    if (issues is! List ||
        issues.any((issue) => issue is! Json) ||
        result['rows'] is! List ||
        approval is! bool ||
        (approval && issues.isNotEmpty)) {
      throw const ApiFailure(0, 'تعذر قراءة نتيجة المعاينة.');
    }
    if (current) {
      setState(() => preview = result);
      showStart();
    }
  });
  num get historicalPayments => preview?['historicalPayments'] as num? ?? 0;
  bool get canCommit =>
      preview?['canApprove'] == true &&
      approved &&
      (historicalPayments == 0 || paymentsApproved) &&
      ((preview!['financiallyIncomplete'] as num? ?? 0) == 0 ||
          financialReview);
  bool get linksResolved =>
      rows(preview?['guardianMatches'] ?? <Json>[])
          .every((m) => resolutions.containsKey(m['householdKey']));
  Future<void> commit() async {
    if (!current || busy || file == null || !canCommit || !linksResolved) {
      return;
    }
    stagedFields ??= {
      'ExpectedFileHash': textOf(preview!['fileHash']),
      'ExpectedReferenceStamp': textOf(preview!['referenceStamp']),
      'GuardianLinksApproved': 'true',
      'OpeningBalancesApproved': '$balancesApproved',
      'HistoricalPaymentsApproved': '$paymentsApproved',
      'AcceptFinancialReview': '$financialReview',
      'GuardianResolutions': jsonEncode(
        resolutions.entries
            .map((e) => {'householdKey': e.key, 'guardianId': e.value})
            .toList(),
      ),
    };
    await run(() async {
      Json result;
      try {
        result = readJob(
          await auth.importWorkbook(
            file!.bytes,
            commit: true,
            fields: stagedFields!,
            idempotencyKey: intent,
          ),
        );
      } catch (e) {
        if (current) {
          if (e is ApiFailure && e.status == 409 && e.code == 'stale') {
            setState(() {
              preview = null;
              stagedFields = null;
              uncertain = false;
            });
          } else if (uncertain ||
              e is! ApiFailure ||
              e.status == 0 ||
              e.status == 408 ||
              e.status >= 500) {
            setState(() => uncertain = true);
          } else {
            stagedFields = null;
          }
        }
        rethrow;
      }
      if (current) await showJob(result);
    });
  }

  Future<void> verifyApproval() => run(() async {
    final key = intent;
    if (!uncertain || key == null) return;
    Json result;
    try {
      result = readJob(
        await auth.request(
          'GET',
          '$root/jobs/by-request?requestKey=${Uri.encodeQueryComponent(key)}',
        ),
      );
    } on ApiFailure catch (failure) {
      if (failure.status == 404) {
        throw const ApiFailure(
          404,
          'لم تظهر دفعة مرتبطة بهذا الطلب بعد. يمكنك إعادة إرسال الطلب نفسه.',
        );
      }
      rethrow;
    }
    if (current) await showJob(result);
  });

  Future<void> showJob(Json value) async {
    setState(() {
      if (job?['id'] != value['id']) jobView++;
      job = value;
      preview = null;
      file = null;
      records = [];
      recordsLoaded = false;
      recordsIssue = null;
      batchUnavailable = false;
      uncertain = false;
      stagedFields = null;
    });
    showStart();
    await track();
  }

  List<Widget> uploadControls() => [
    FilledButton.icon(
      onPressed: busy ? null : () => upload(),
      icon: const Icon(Icons.upload_file_outlined),
      label: const UiText('اختيار ملف XLSX ومعاينته'),
    ),
    const UiText(
      'ملف واحد · حتى 1000 تسجيل و5 ميجابايت',
      style: TextStyle(fontSize: 11),
    ),
  ];

  List<Widget> workbookTools() => [
    OutlinedButton.icon(
      onPressed: () => download(false),
      icon: const Icon(Icons.download_outlined),
      label: const UiText('تنزيل قالب فارغ'),
    ),
    OutlinedButton.icon(
      onPressed: () => download(true),
      icon: const Icon(Icons.download_outlined),
      label: const UiText('تنزيل نموذج مكتمل'),
    ),
    if (schemaIssue)
      OutlinedButton(
        onPressed: () => run(readSchema),
        child: const UiText('إعادة تحميل دليل الحقول'),
      ),
    if (schema != null)
      ExpansionTile(
        title: const UiText('الحقول المطلوبة والاختيارية'),
        children: [
          for (final sheet in rows(schema!['connectedSheets']))
            ListTile(
              title: UiText(textOf(sheet['name'])),
              subtitle: UiText(
                rows(sheet['columns'] ?? <Json>[])
                    .where((field) => field['key'] != 'ExistingId')
                    .map(
                      (field) =>
                          '${field['label']}${field['required'] == true ? ' *' : ''}',
                    )
                    .join('، '),
                style: const TextStyle(fontSize: 11),
              ),
            ),
          const UiText('النجمة تحدد الحقل المطلوب.'),
        ],
      ),
  ];

  List<Widget> reviewIssues(List<Json> errors) {
    if (errors.isEmpty) return const [];
    final issues = <_ImportIssue>{
      for (final error in errors)
        (
          sheetId: textOf(error['sheet']),
          sheet: textOf(error['sheetLabel'] ?? error['sheet']),
          row: switch (error['row']) {
            int row when row > 0 => row,
            _ => null,
          },
          column: '${error['columnLabel'] ?? error['column'] ?? ''}',
          message: textOf(error['message']),
        ),
    }.toList();
    final sheets = {for (final issue in issues) issue.sheetId: issue.sheet};
    int countRows(List<_ImportIssue> entries) => {
      for (final issue in entries)
        if (issue.row != null) (issue.sheetId, issue.row),
    }.length;

    final query = canonicalNumericInput(issueRow.text).trim();
    final rowNumber = RegExp(r'^[0-9]+$').hasMatch(query)
        ? int.tryParse(query)
        : null;
    final shown = issues
        .where(
          (issue) =>
              (issueSheet.isEmpty || issue.sheetId == issueSheet) &&
              (query.isEmpty || (rowNumber != null && issue.row == rowNumber)),
        )
        .toList();
    final affectedRows = countRows(shown);
    final showRowFilter = countRows(issues) > 1;
    final muted = Theme.of(context).colorScheme.onSurfaceVariant;

    return [
      if (errors.length >= 300)
        const UiText('يعرض الفحص حتى 300 مشكلة في المرة الواحدة.'),
      if (sheets.length > 1)
        DropdownButtonFormField<String>(
          initialValue: issueSheet,
          decoration: const InputDecoration(labelText: 'الورقة'),
          isExpanded: true,
          itemHeight: null,
          items: [
            const DropdownMenuItem(value: '', child: UiText('كل الأوراق')),
            for (final sheet in sheets.entries)
              DropdownMenuItem(value: sheet.key, child: UiText(sheet.value)),
          ],
          onChanged: (value) => setState(() => issueSheet = value ?? ''),
        ),
      if (sheets.length > 1 && showRowFilter) const SizedBox(height: 8),
      if (showRowFilter)
        TextField(
          controller: issueRow,
          keyboardType: TextInputType.number,
          textDirection: TextDirection.ltr,
          decoration: const InputDecoration(labelText: 'رقم الصف في Excel'),
          onChanged: (_) => setState(() {}),
        ),
      if (issueSheet.isNotEmpty || query.isNotEmpty)
        Align(
          alignment: AlignmentDirectional.centerStart,
          child: TextButton(
            onPressed: () => setState(() {
              issueRow.clear();
              issueSheet = '';
            }),
            child: const UiText('مسح التصفية'),
          ),
        ),
      Semantics(
        liveRegion: true,
        child: Wrap(
          spacing: 12,
          children: [
            UiText('المشكلات المعروضة ${shown.length}'),
            if (affectedRows > 0) UiText('الصفوف المتأثرة $affectedRows'),
          ],
        ),
      ),
      OutlinedButton.icon(
        onPressed: busy ? null : () => saveIssues(issues),
        icon: const Icon(Icons.download_outlined),
        label: const UiText('تنزيل تقرير الأخطاء'),
      ),
      if (reportFeedback case final feedback?)
        Semantics(liveRegion: true, child: UiText(feedback)),
      if (shown.isEmpty) const UiText('لا توجد مشكلات تطابق التصفية.'),
      for (final issue in shown)
        GCard(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              UiText(
                issue.row == null
                    ? issue.sheet
                    : '${issue.sheet} · الصف ${issue.row}',
                style: const TextStyle(fontWeight: FontWeight.w700),
              ),
              if (issue.column.isNotEmpty)
                UiText(
                  issue.column,
                  style: TextStyle(color: muted, fontSize: 12),
                ),
              const SizedBox(height: 6),
              UiText(issue.message),
            ],
          ),
        ),
    ];
  }

  @override
  Widget build(BuildContext context) {
    final state = job?['status'];
    final cleanRows =
        preview?['cleanRows'] as Json? ?? const <String, dynamic>{};
    final previewRows = rows(preview?['rows'] ?? <Json>[]);
    final registrationLabels = preview?['registrationLabels'];
    final matches = rows(preview?['guardianMatches'] ?? <Json>[]);
    final result = job?['result'];
    final savedResult = result is Map ? obj(result) : const <String, dynamic>{};
    final savedPayments = _importCount(savedResult['historicalPayments']) ?? 0;
    final savedCurrency = records.firstOrNull?['currencySnapshot'];
    final publishedRows = _importCount(job?['publishedRows']);
    final replayedRows = _importCount(job?['replayedRows']);
    final replayedPayment =
        job?['replayedRequest'] == true ||
        (publishedRows == 0 && (replayedRows ?? 0) > 0);
    final rejected = preview?['canApprove'] == false;
    final importErrors = rows(preview?['errors'] ?? <Json>[]);
    final canSplit =
        rejected &&
        file != null &&
        (cleanRows['playerCount'] as num? ?? 0) > 0 &&
        (cleanRows['excludedRows'] as num? ?? 0) > 0;
    final needsSplitRecheck = splitNeedsRecheck || previewFileHash == null;
    return page(
      'استيراد بيانات الأكاديمية',
      [
        if (!uncertain && job == null) ...uploadControls(),
        if (file != null) ...[
          Text(
            file!.name,
            textDirection: Bidi.detectRtlDirectionality(file!.name)
                ? TextDirection.rtl
                : TextDirection.ltr,
            textAlign: Directionality.of(context) == TextDirection.rtl
                ? TextAlign.right
                : TextAlign.left,
            style: const TextStyle(fontSize: 12),
          ),
          if (preview == null && !busy && !uncertain)
            TextButton(
              onPressed: () => upload(reuse: true),
              child: const UiText('إعادة فحص الملف'),
            ),
        ],
        if (preview == null && job == null) ...workbookTools(),
        if (preview != null) ...[
          Heading(
            rejected ? 'الملف يحتاج تصحيحًا' : 'اكتملت المعاينة دون أخطاء',
          ),
          if (rejected) ...[
            UiText(
              importErrors.isEmpty
                  ? 'لم يُحفظ أي سجل. تعذر عرض تفاصيل الفحص.'
                  : 'لم يُحفظ أي سجل.',
            ),
            if (file != null && !(canSplit && needsSplitRecheck))
              TextButton(
                onPressed: () => upload(reuse: true),
                child: const UiText('إعادة فحص الملف'),
              ),
          ],
          if (preview!['canApprove'] == true)
            GCard(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  UiText(
                    'اللاعبون ${preview!['playerCount']} · التسجيلات ${preview!['rowCount']} · الأسر ${preview!['householdCount']}',
                  ),
                  if (_importDate(preview!['cutoffDate'])
                      case final String cutoff)
                    UiText('تاريخ القطع $cutoff'),
                ],
              ),
            ),
          if (canSplit)
            GCard(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  UiText(
                    '${cleanRows['playerCount']} سليمًا · ${cleanRows['excludedRows']} للتصحيح',
                    style: const TextStyle(fontWeight: FontWeight.w600),
                  ),
                  if ((cleanRows['relatedRows'] as num? ?? 0) > 0)
                    UiText(
                      'سجلات مرتبطة تنتقل إلى ملف التصحيح (${cleanRows['relatedRows']}).',
                    ),
                  if (needsSplitRecheck) ...[
                    Semantics(
                      liveRegion: true,
                      child: const UiText('أعد فحص الملف لإتاحة تنزيل الصفوف.'),
                    ),
                    OutlinedButton(
                      onPressed: busy ? null : () => upload(reuse: true),
                      child: const UiText('إعادة فحص الملف'),
                    ),
                  ] else ...[
                    const UiText(
                      'افحص ملف السليمين واعتمده أولًا. صحّح الملف الثاني، ثم افحصه واعتمده.',
                    ),
                    for (final corrections in [false, true]) ...[
                      const SizedBox(height: 8),
                      OutlinedButton.icon(
                        onPressed: busy
                            ? null
                            : () =>
                                  downloadPlayerRows(corrections: corrections),
                        icon: const Icon(Icons.download_outlined),
                        label: UiText(
                          corrections
                              ? 'تنزيل صفوف التصحيح'
                              : 'تنزيل ملف اللاعبين السليمين',
                        ),
                      ),
                      if (splitFeedback[corrections] case final feedback?)
                        Semantics(liveRegion: true, child: UiText(feedback)),
                    ],
                  ],
                ],
              ),
            ),
          if (rejected) ...reviewIssues(importErrors),
          if (preview!['canApprove'] == true) ...[
            if (matches.isNotEmpty) ...[
              const Heading('مطابقة أولياء الأمور'),
              if (!linksResolved)
                const UiText('راجع المطابقات قبل اعتماد الدفعة.'),
            ],
            for (final match in matches)
              GCard(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                    UiText('${match['guardianName']} · ${match['phone']}'),
                    if ((match['candidates'] as List).length == 1)
                      CheckboxListTile(
                        title: const UiText(
                          'ربط هذه الأسرة بالحساب المطابق القائم',
                        ),
                        value: resolutions.containsKey(match['householdKey']),
                        onChanged: uncertain
                            ? null
                            : (v) => setState(() {
                                v == true
                                    ? resolutions[textOf(
                                        match['householdKey'],
                                      )] = textOf(
                                        (match['candidates'] as List).single,
                                      )
                                    : resolutions.remove(match['householdKey']);
                              }),
                      ),
                    if ((match['candidates'] as List).length != 1)
                      const UiText(
                        'تعذر تحديد حساب مطابق. صحّح اسم ولي الأمر والهاتف وكود الأسرة في الملف ثم أعد فحصه.',
                      ),
                  ],
                ),
              ),
            if (previewRows.isNotEmpty)
              _ImportRows(
                key: ValueKey('preview-${preview!['fileHash']}-$intent'),
                entries: previewRows,
                preview: true,
                registrationLabels: registrationLabels is Json
                    ? registrationLabels
                    : const {},
              ),
            _ImportEffects(
              key: ValueKey('effects-preview-$intent'),
              data: preview!,
              preview: true,
            ),
            CheckboxListTile(
              title: const UiText(
                'راجعت علاقات أولياء الأمور وأعتمد ربط الدفعة.',
              ),
              value: approved,
              onChanged: uncertain
                  ? null
                  : (v) => setState(() => approved = v == true),
            ),
            CheckboxListTile(
              title: const UiText(
                'راجعت الأرصدة الافتتاحية وسندها إن وجدت؛ ليست قبض اليوم.',
              ),
              value: balancesApproved,
              onChanged: uncertain
                  ? null
                  : (v) => setState(() => balancesApproved = v == true),
            ),
            if (historicalPayments > 0)
              CheckboxListTile(
                title: UiText(
                  'راجعت $historicalPayments سند تحصيل سابق وأعتمد تسجيل القبض بتاريخه. ${_historicalPaymentTotal(preview!, previewRows.firstOrNull?['currency'])}',
                ),
                value: paymentsApproved,
                onChanged: uncertain
                    ? null
                    : (v) => setState(() => paymentsApproved = v == true),
              ),
            if ((preview!['financiallyIncomplete'] as num? ?? 0) > 0)
              CheckboxListTile(
                title: UiText(
                  '${preview!['financiallyIncomplete']} تسجيل يحتاج مراجعة مالية؛ السعر غير المعروف ليس صفرًا.',
                ),
                value: financialReview,
                onChanged: uncertain
                    ? null
                    : (v) => setState(() => financialReview = v == true),
              ),
            if (uncertain) ...[
              const GCard(
                child: UiText(
                  'لم يتأكد اعتماد الدفعة. تحقّق من حالتها قبل إعادة الإرسال.',
                ),
              ),
              FilledButton(
                onPressed: busy ? null : verifyApproval,
                child: const UiText('التحقق من حالة الاعتماد'),
              ),
              OutlinedButton(
                onPressed: busy ? null : commit,
                child: const UiText('إعادة تأكيد الطلب نفسه'),
              ),
            ] else
              FilledButton(
                onPressed: !busy && canCommit && linksResolved ? commit : null,
                child: const UiText('اعتماد وبدء الاستيراد'),
              ),
          ],
          if (!rejected && !uncertain && file != null)
            TextButton(
              onPressed: () => upload(reuse: true),
              child: const UiText('إعادة فحص الملف'),
            ),
          ExpansionTile(
            title: const UiText('القوالب ودليل الحقول'),
            expandedCrossAxisAlignment: CrossAxisAlignment.stretch,
            children: workbookTools(),
          ),
        ],
        if (job != null && batchUnavailable)
          GCard(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                const UiText('الدفعة غير متاحة في السياق الحالي.'),
                OutlinedButton(
                  onPressed: refreshJob,
                  child: const UiText('إعادة التحقق من حالة الدفعة'),
                ),
              ],
            ),
          )
        else if (job != null) ...[
          Heading(
            state == 'Completed'
                ? 'اكتمل الاستيراد'
                : state == 'Queued'
                ? 'الدفعة في انتظار التنفيذ'
                : state == 'Running'
                ? 'جارٍ تنفيذ الدفعة كاملة'
                : 'لم تُنشر الدفعة',
          ),
          if (job!['replayedRequest'] == true)
            const UiText(
              'هذا الملف معتمد بالفعل؛ نعرض نتيجة الدفعة السابقة دون إنشاء تسجيلات جديدة.',
            ),
          if (state == 'Completed') ...[
            Wrap(
              spacing: 16,
              runSpacing: 6,
              children: [
                UiText(
                  publishedRows == null
                      ? 'عدد التسجيلات الجديدة غير متاح'
                      : 'التسجيلات الجديدة $publishedRows',
                ),
                if (replayedRows case final count? when count > 0)
                  UiText('تسجيلات سبق استيرادها $count'),
              ],
            ),
            if (savedPayments > 0)
              UiText(
                '${replayedPayment ? 'تم التحقق من' : 'تتضمن الدفعة'} $savedPayments سند تحصيل سابق. ${_historicalPaymentTotal(savedResult, savedCurrency)}'
                '${replayedPayment ? ' لم يُضف قبض جديد.' : ''}',
                style: const TextStyle(fontSize: 12),
              )
            else
              const UiText(
                'بيانات الاشتراك وحدها لا تنشئ تحصيلًا أو إيصالًا.',
                style: TextStyle(fontSize: 12),
              ),
            if (recordsIssue != null)
              GCard(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                    UiText(recordsIssue!),
                    OutlinedButton(
                      onPressed: () => run(readRecords),
                      child: const UiText('إعادة تحميل التسجيلات المحفوظة'),
                    ),
                  ],
                ),
              )
            else if (recordsLoaded && records.isEmpty)
              const UiText('لا توجد تسجيلات محفوظة لهذه الدفعة.')
            else if (records.isNotEmpty)
              _ImportRows(
                key: ValueKey('saved-${job!['id']}'),
                entries: records,
                preview: false,
              ),
            _ImportEffects(
              key: ValueKey('effects-saved-$jobView'),
              data: savedResult,
            ),
          ] else if (job!['message'] != null)
            GCard(child: UiText(textOf(job!['message']))),
          OutlinedButton(
            onPressed: refreshJob,
            child: const UiText('تحديث حالة الدفعة'),
          ),
        ],
        if (historyIssue && !uncertain)
          OutlinedButton(
            onPressed: () => run(readHistory),
            child: const UiText('إعادة تحميل سجل الدفعات'),
          ),
        if (jobs.isNotEmpty && !uncertain)
          ExpansionTile(
            title: const UiText('دفعات الاستيراد السابقة'),
            children: [
              for (final old in jobs)
                ListTile(
                  title: UiText(
                    '${old['sourceSystem']} · ${old['rowCount']} تسجيل',
                  ),
                  subtitle: UiText(
                    old['status'] == 'Completed'
                        ? 'مكتملة'
                        : old['status'] == 'Running'
                        ? 'قيد التنفيذ'
                        : old['status'] == 'Queued'
                        ? 'في الانتظار'
                        : 'غير منشورة',
                  ),
                  onTap: () => run(() => showJob(old)),
                ),
            ],
          ),
        if (job != null && !uncertain) ...[
          ...uploadControls(),
          ExpansionTile(
            title: const UiText('القوالب ودليل الحقول'),
            expandedCrossAxisAlignment: CrossAxisAlignment.stretch,
            children: workbookTools(),
          ),
        ],
      ],
      screen: 'import',
      scrollController: scroll,
      onRetry: uncertain
          ? verifyApproval
          : job != null
          ? refreshJob
          : load,
    );
  }
}

class _ImportEffects extends StatelessWidget {
  const _ImportEffects({super.key, required this.data, this.preview = false});

  final Json data;
  final bool preview;

  @override
  Widget build(BuildContext context) {
    final assignments = _importCount(data['coachAssignments']) ?? 0;
    final participations = _importCount(data['tournamentParticipations']) ?? 0;
    final effects = data['effects'];
    final details = <Widget>[
      if (assignments > 0) UiText('$assignments إسناد مدرب لمجموعة'),
      if (participations > 0) UiText('$participations مشاركة في البطولات'),
    ];
    if (effects is List) {
      for (final effect in effects.whereType<Json>()) {
        final sheet = effect['sheet'];
        if (sheet is! String || sheet.trim().isEmpty) continue;
        final created = _importCount(effect['create']) ?? 0;
        final reused = _importCount(effect['reuse']) ?? 0;
        if (created == 0 && reused == 0) continue;
        details.add(
          UiText(
            [
              sheet,
              if (created > 0) '${preview ? 'سيُضاف' : 'أُضيف'} $created',
              if (reused > 0)
                '${preview ? 'سيُستخدم من الموجود' : 'استُخدم من الموجود'} $reused',
            ].join(' · '),
          ),
        );
      }
    }
    if (details.isEmpty) return const SizedBox.shrink();
    return ExpansionTile(
      key: PageStorageKey(key),
      title: UiText(preview ? 'الإضافة والربط من الملف' : 'تفاصيل الاستيراد'),
      expandedCrossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        for (final detail in details)
          Padding(
            padding: const EdgeInsetsDirectional.fromSTEB(16, 4, 16, 8),
            child: detail,
          ),
      ],
    );
  }
}

class _ImportRows extends StatefulWidget {
  const _ImportRows({
    super.key,
    required this.entries,
    required this.preview,
    this.registrationLabels = const {},
  });
  final List<Json> entries;
  final bool preview;
  final Json registrationLabels;

  @override
  State<_ImportRows> createState() => _ImportRowsState();
}

class _ImportRowsState extends State<_ImportRows> {
  static const pageSize = 10;
  final search = TextEditingController();
  int page = 0;

  @override
  void dispose() {
    search.dispose();
    super.dispose();
  }

  String normalize(String value) =>
      canonicalNumericInput(value).trim().toLowerCase();

  String registrationDetails(Json row) {
    final code = row['legacyEnrollmentId']?.toString() ?? '';
    final labels = widget.registrationLabels[code];
    if (labels is! Json) return code;
    final description = [
      labels['sport'],
      labels['group'],
      labels['branch'],
    ].whereType<String>().where((value) => value.isNotEmpty).join(' · ');
    return description.isEmpty ? code : description;
  }

  @override
  Widget build(BuildContext context) {
    final query = normalize(search.text);
    final draft = widget.preview;
    final fields = draft
        ? [
            'playerName',
            'legacyPlayerId',
            'legacyEnrollmentId',
            'guardianName',
            'guardianPhone',
          ]
        : ['player', 'playerCode', 'sport', 'branch', 'groupName'];
    final visible = query.isEmpty
        ? widget.entries
        : widget.entries
              .where(
                (row) =>
                    fields.any(
                      (key) => normalize('${row[key] ?? ''}').contains(query),
                    ) ||
                    draft &&
                        normalize(registrationDetails(row)).contains(query),
              )
              .toList();
    final last = visible.isEmpty ? 0 : (visible.length - 1) ~/ pageSize;
    final index = page.clamp(0, last);
    final start = index * pageSize;
    final shown = visible.skip(start).take(pageSize).toList();
    final title = draft ? 'معاينة التسجيلات' : 'التسجيلات المحفوظة';

    return ExpansionTile(
      tilePadding: EdgeInsets.zero,
      childrenPadding: const EdgeInsets.symmetric(vertical: 12),
      expandedCrossAxisAlignment: CrossAxisAlignment.stretch,
      title: UiText('$title · ${widget.entries.length}'),
      children: [
        TextField(
          key: const ValueKey('import-record-search'),
          controller: search,
          decoration: InputDecoration(
            labelText: 'بحث في التسجيلات',
            prefixIcon: const Icon(Icons.search),
            suffixIcon: search.text.isEmpty
                ? null
                : IconButton(
                    tooltip: 'مسح البحث',
                    icon: const Icon(Icons.clear),
                    onPressed: () {
                      search.clear();
                      setState(() => page = 0);
                    },
                  ),
          ),
          onChanged: (_) => setState(() => page = 0),
        ),
        const SizedBox(height: 12),
        if (shown.isEmpty)
          const UiText('لا توجد تسجيلات مطابقة.')
        else ...[
          UiText('${start + 1}–${start + shown.length} من ${visible.length}'),
          if (last > 0)
            Wrap(
              spacing: 8,
              children: [
                OutlinedButton(
                  onPressed: index == 0
                      ? null
                      : () => setState(() => page = index - 1),
                  child: const UiText('السابق'),
                ),
                OutlinedButton(
                  onPressed: index == last
                      ? null
                      : () => setState(() => page = index + 1),
                  child: const UiText('التالي'),
                ),
              ],
            ),
          for (final row in shown)
            ListTile(
              contentPadding: EdgeInsets.zero,
              title: UiText(textOf(row[draft ? 'playerName' : 'player'])),
              subtitle: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  UiText(
                    textOf(row[draft ? 'legacyPlayerId' : 'playerCode']),
                    textDirection: TextDirection.ltr,
                  ),
                  if (draft) ...[
                    if (registrationDetails(row).isNotEmpty)
                      UiText(registrationDetails(row)),
                    UiText('${row['guardianName']} · ${row['guardianPhone']}'),
                  ] else
                    UiText(
                      '${row['sport']} · ${row['branch']} · ${row['groupName']}',
                    ),
                  if (_importDate(row[draft ? 'start' : 'startDate'])
                      case final String start)
                    UiText('البداية $start'),
                  if (_importDate(row[draft ? 'end' : 'endDate'])
                      case final String end)
                    UiText('النهاية $end'),
                  if (row['remainingSessions'] is num)
                    UiText('الحصص المتبقية ${row['remainingSessions']}'),
                  if (draft || row.containsKey('priceSnapshot'))
                    UiText(
                      row[draft ? 'agreedNet' : 'priceSnapshot'] == null
                          ? 'الصافي التاريخي يحتاج مراجعة'
                          : 'الصافي التاريخي ${money(row[draft ? 'agreedNet' : 'priceSnapshot'], row[draft ? 'currency' : 'currencySnapshot'])}',
                    ),
                ],
              ),
            ),
        ],
      ],
    );
  }
}

class NativeImportEntry extends StatefulWidget {
  const NativeImportEntry({super.key, required this.auth});
  final AuthController auth;
  @override
  State<NativeImportEntry> createState() => _NativeImportEntryState();
}

class _NativeImportEntryState extends State<NativeImportEntry> {
  String? get contextId => widget.auth.session?.selected == null
      ? null
      : '${widget.auth.session!.selected!.id}:${widget.auth.session!.selected!.contextRevision}';
  late final opened = contextId;
  late final access = load();
  Future<bool> load() async {
    if (widget.auth.session?.selected?.role == 'AcademyOwner') return true;
    if (widget.auth.session?.selected?.role != 'AcademyAdmin') return false;
    final permissions =
        await widget.auth.request('GET', '/api/v1/my-permissions') as List;
    if (!permissions.contains('people.manage')) return false;
    final data = obj(await widget.auth.request('GET', '/api/v1/my-access'));
    return data['allBranches'] == true &&
        data['allSports'] == true &&
        data['useActionGrants'] == true &&
        ['academy', 'staff', 'players', 'subscriptions', 'matches'].every(
          (module) => rows(data['actions']).any(
            (a) =>
                a['module'] == module && a['read'] == true && a['add'] == true,
          ),
        );
  }

  @override
  Widget build(BuildContext context) => ListenableBuilder(
    listenable: widget.auth,
    builder: (context, _) {
      if (opened == null || opened != contextId) return const SizedBox.shrink();
      return FutureBuilder<bool>(
        future: access,
        builder: (context, s) => s.data == true
            ? GLink(
                'استيراد بيانات الأكاديمية',
                Icons.file_upload_outlined,
                () => openPage(context, NativeAcademyImport(auth: widget.auth)),
              )
            : const SizedBox.shrink(),
      );
    },
  );
}
