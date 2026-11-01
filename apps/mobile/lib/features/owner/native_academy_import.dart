import 'dart:async';
import 'dart:convert';
import 'dart:typed_data';

import 'package:file_picker/file_picker.dart';
import 'package:flutter/material.dart';

import '../../core/api/api_client.dart';
import '../../core/auth/auth_controller.dart';
import '../../core/theme/ui_text.dart';
import '../admin/native_operation_form.dart';
import '../guardian/guardian_ui.dart';

typedef WorkbookSelection = ({String name, Uint8List bytes});

class NativeAcademyImport extends StatefulWidget {
  const NativeAcademyImport({
    super.key,
    required this.auth,
    this.pickWorkbook,
    this.saveWorkbook,
  });
  final AuthController auth;
  final Future<WorkbookSelection?> Function()? pickWorkbook;
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
  WorkbookSelection? file;
  bool approved = false,
      balancesApproved = false,
      financialReview = false,
      uncertain = false;
  final resolutions = <String, String>{};
  String? intent;
  Map<String, String>? stagedFields;
  Timer? poll;
  final scroll = ScrollController();
  void showStart() => WidgetsBinding.instance.addPostFrameCallback((_) {
    if (current && scroll.hasClients) scroll.jumpTo(0);
  });
  static const root = '/api/v1/people/import';
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
    scroll.dispose();
    super.dispose();
  }

  Future<void> load() => run(() async {
    final values = await Future.wait([
      auth.request('GET', '$root/schema'),
      auth.request('GET', '$root/jobs'),
    ]);
    if (!current) return;
    setState(() {
      schema = obj(values[0]);
      jobs = rows(values[1]);
    });
    if (job == null && preview == null && jobs.isNotEmpty) {
      setState(() => job = jobs.first);
      await track();
    }
  });
  Future<void> track() async {
    if (!current || job == null) return;
    poll?.cancel();
    if (job!['status'] == 'Completed') {
      final result = rows(
        await auth.request('GET', '$root/jobs/${job!['id']}/records'),
      );
      if (current) setState(() => records = result);
    } else if (['Queued', 'Running'].contains(job!['status'])) {
      poll = Timer.periodic(const Duration(seconds: 2), (_) {
        if (!busy && current) {
          refreshJob();
        }
      });
    }
  }

  Future<void> refreshJob() => run(() async {
    Json value;
    try {
      value = obj(await auth.request('GET', '$root/jobs/${job!['id']}'));
    } catch (e) {
      if (e is ApiFailure && [401, 403, 404, 429].contains(e.status)) {
        poll?.cancel();
      }
      rethrow;
    }
    if (current) {
      setState(() => job = value);
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
    final saved = widget.saveWorkbook != null
        ? await widget.saveWorkbook!(name, bytes)
        : (await FilePicker.saveFile(
                fileName: name,
                bytes: bytes,
                mimeType: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
                dialogTitle: 'حفظ ملف الأكاديمية',
              )) !=
              null;
    if (current && saved) setState(() => message = 'تم حفظ ملف XLSX.');
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
      throw const ApiFailure(413, 'اختر ملف XLSX ضمن5ميجابايت.');
    }
    return (name: chosen.name, bytes: await chosen.readAsBytes());
  }

  Future<void> upload({bool reuse = false}) => run(() async {
    final chosen = reuse ? file : await pick();
    if (chosen == null || !current) return;
    if (!chosen.name.toLowerCase().endsWith('.xlsx') ||
        chosen.bytes.isEmpty ||
        chosen.bytes.length > 5 * 1024 * 1024) {
      throw const ApiFailure(413, 'اختر ملف XLSX ضمن5ميجابايت.');
    }
    poll?.cancel();
    setState(() {
      file = chosen;
      preview = null;
      job = null;
      records = [];
      approved = false;
      balancesApproved = false;
      financialReview = false;
      resolutions.clear();
      intent = commandKey();
      stagedFields = null;
      uncertain = false;
    });
    final result = obj(await auth.importWorkbook(chosen.bytes));
    if (result['errors'] is! List || result['rows'] is! List) {
      throw const ApiFailure(0, 'تعذر قراءة نتيجة المعاينة.');
    }
    if (current) {
      setState(() => preview = result);
      showStart();
    }
  });
  bool get canCommit =>
      preview?['canApprove'] == true &&
      approved &&
      ((preview!['financiallyIncomplete'] as num? ?? 0) == 0 ||
          financialReview);
  bool get linksResolved =>
      rows(preview?['guardianMatches'] ?? <Json>[])
          .every((m) => resolutions.containsKey(m['householdKey']));
  Future<void> commit() async {
    if (file == null || !canCommit || !linksResolved) return;
    stagedFields ??= {
      'ExpectedFileHash': textOf(preview!['fileHash']),
      'ExpectedReferenceStamp': textOf(preview!['referenceStamp']),
      'GuardianLinksApproved': 'true',
      'OpeningBalancesApproved': '$balancesApproved',
      'AcceptFinancialReview': '$financialReview',
      'GuardianResolutions': jsonEncode(
        resolutions.entries
            .map((e) => {'householdKey': e.key, 'guardianId': e.value})
            .toList(),
      ),
    };
    await run(() async {
      Object? result;
      try {
        result = await auth.importWorkbook(
          file!.bytes,
          commit: true,
          fields: stagedFields!,
          idempotencyKey: intent,
        );
      } catch (e) {
        if (e is! ApiFailure || e.status == 0 || e.status >= 500) {
          uncertain = true;
        } else {
          stagedFields = null;
        }
        rethrow;
      }
      if (current) {
        setState(() {
          job = obj(result);
          preview = null;
          file = null;
          uncertain = false;
          stagedFields = null;
        });
        await track();
        showStart();
      }
    });
  }

  Widget effects(Json data) {
    final rowsData = rows(data['effects'] ?? <Json>[]);
    return GCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          UiText(
            '${data['coachAssignments'] ?? 0} إسناد مدرب لمجموعة · ${data['tournamentParticipations'] ?? 0} مشاركة في البطولات',
            style: const TextStyle(fontWeight: FontWeight.w600),
          ),
          for (final effect in rowsData)
            Padding(
              padding: const EdgeInsets.only(top: 8),
              child: UiText(
                '${effect['sheet']} · إنشاء ${effect['create']} · إعادة استخدام ${effect['reuse']}',
                style: const TextStyle(fontSize: 12),
              ),
            ),
        ],
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    final state = job?['status'];
    final stage = state == 'Completed'
        ? 4
        : job != null
        ? 3
        : preview != null
        ? 2
        : busy && file != null
        ? 1
        : 0;
    return page(
      'استيراد بيانات الأكاديمية',
      [
        Wrap(
          spacing: 8,
          runSpacing: 8,
          children: [
            for (final item in [
              (0, 'القالب والرفع'),
              (1, 'التحقق'),
              (2, 'المعاينة والتأكيد'),
              (3, 'التنفيذ'),
              (4, 'الملخص'),
            ])
              Chip(
                label: UiText(
                  item.$2,
                  style: TextStyle(
                    fontSize: 11,
                    fontWeight: item.$1 == stage
                        ? FontWeight.w700
                        : FontWeight.w400,
                  ),
                ),
                side: item.$1 == stage
                    ? BorderSide(color: Theme.of(context).colorScheme.primary)
                    : null,
              ),
          ],
        ),
        const Heading('بيانات مترابطة لأكاديمية واحدة'),
        const UiText(
          'المدربون والمجموعات والحصص · الأبناء وأولياء الأمور والاشتراكات · البطولات. أكواد قصيرة تربط الأوراق.',
          style: TextStyle(fontSize: 12),
        ),
        if (schema != null) ...[
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
          if (schema!['connectedSheets'] is List)
            ExpansionTile(
              title: UiText(
                'أوراق الملف · ${(schema!['connectedSheets'] as List).length}',
              ),
              children: [
                for (final sheet in rows(schema!['connectedSheets']))
                  ListTile(
                    title: UiText(textOf(sheet['name'])),
                    subtitle: UiText(
                      sheet['optional'] == true
                          ? 'اختياري بحسب بياناتك'
                          : 'أساسي عند الربط',
                      style: const TextStyle(fontSize: 11),
                    ),
                  ),
              ],
            ),
        ],
        if (!uncertain)
          FilledButton.icon(
            onPressed: () => upload(),
            icon: const Icon(Icons.upload_file_outlined),
            label: const UiText('اختيار ملف XLSX ومعاينته'),
          ),
        const UiText(
          'حتى1000تسجيل · ملف واحد ضمن5ميجابايت',
          style: TextStyle(fontSize: 11),
        ),
        if (file != null)
          UiText(file!.name, style: const TextStyle(fontSize: 12)),
        if (preview != null) ...[
          Heading(
            preview!['canApprove'] == true
                ? 'اكتملت المعاينة دون أخطاء'
                : 'الملف يحتاج تصحيحًا',
          ),
          GCard(
            child: UiText(
              '${preview!['playerCount']} لاعب مختلف · ${preview!['rowCount']} تسجيل · ${preview!['householdCount']} أسرة',
            ),
          ),
          if ((preview!['effects'] as List? ?? []).isNotEmpty)
            effects(preview!),
          for (final issue in rows(preview!['errors']))
            GCard(
              child: UiText(
                '${issue['sheetLabel'] ?? issue['sheet']} · الصف ${issue['row']} · ${issue['columnLabel'] ?? issue['column']}\n${issue['message']}',
              ),
            ),
          if (preview!['canApprove'] == true) ...[
            for (final match in rows(preview!['guardianMatches'] ?? <Json>[]))
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
                        'يوجد تعارض في هوية الأسرة. صحح بيانات الاسم والهاتف وكود الأسرة قبل الاعتماد.',
                      ),
                  ],
                ),
              ),
            for (final row in rows(preview!['rows']).take(10))
              ListTile(
                contentPadding: EdgeInsets.zero,
                title: UiText(
                  '${row['playerName']} · ${row['legacyPlayerId']}',
                ),
                subtitle: UiText(
                  '${row['guardianName']} · ${row['guardianPhone']}\n${row['agreedNet'] == null ? 'السعر التاريخي يحتاج مراجعة' : 'الصافي التاريخي ${money(row['agreedNet'], row['currency'])}'}',
                  style: const TextStyle(fontSize: 11),
                ),
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
            if (uncertain)
              const GCard(
                child: UiText(
                  'لم يتأكد اعتماد الدفعة. أعد تأكيد الطلب نفسه أو راجع الدفعات؛ لا تنشئ طلبًا آخر.',
                ),
              ),
            FilledButton(
              onPressed: canCommit && linksResolved ? commit : null,
              child: UiText(
                uncertain ? 'إعادة تأكيد الطلب نفسه' : 'اعتماد وبدء الاستيراد',
              ),
            ),
          ],
          if (!uncertain && file != null)
            TextButton(
              onPressed: () => upload(reuse: true),
              child: const UiText('إعادة فحص الملف'),
            ),
        ],
        if (job != null) ...[
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
            UiText(
              'تم إنشاء ${job!['publishedRows']} تسجيل، وإعادة استخدام ${job!['replayedRows']} دون تكرار.',
            ),
            if (job!['result'] is Map) effects(obj(job!['result'])),
            const UiText(
              'الاشتراك الافتتاحي لا ينشئ تحصيلًا أو إيصالًا مصطنعًا.',
              style: TextStyle(fontSize: 12),
            ),
            for (final row in records.take(20))
              GCard(
                child: UiText(
                  '${row['player']} · ${row['playerCode']}\n${row['sport']} · ${row['branch']} · ${row['groupName']}\n${day(row['startDate'])} — ${day(row['endDate'])}',
                  style: const TextStyle(fontSize: 12),
                ),
              ),
          ] else if (job!['message'] != null)
            GCard(child: UiText(textOf(job!['message']))),
          OutlinedButton(
            onPressed: refreshJob,
            child: const UiText('تحديث حالة الدفعة'),
          ),
        ],
        if (jobs.isNotEmpty)
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
                  onTap: () => run(() async {
                    setState(() {
                      job = old;
                      preview = null;
                      file = null;
                      records = [];
                      uncertain = false;
                    });
                    await track();
                  }),
                ),
            ],
          ),
      ],
      screen: 'import',
      scrollController: scroll,
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
