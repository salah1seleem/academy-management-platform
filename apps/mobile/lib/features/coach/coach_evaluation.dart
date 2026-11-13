import '../shared/operation_design.dart';
import '../shared/staff_capabilities.dart';

import 'package:academy_mobile/core/theme/ui_text.dart';
import 'package:flutter/material.dart';

import '../../core/api/api_client.dart';
import '../../core/auth/auth_controller.dart';
import '../../core/widgets/app_widgets.dart';
import '../guardian/guardian_ui.dart';
import 'coach_evaluation_record.dart';

enum _CriteriaState { unknown, missing, inactive, ready }

_CriteriaState? _sessionCriteriaState(Object? value) => switch (value) {
  'Ready' => _CriteriaState.ready,
  'Missing' => _CriteriaState.missing,
  'Inactive' => _CriteriaState.inactive,
  _ => null,
};

class CoachEvaluation extends StatefulWidget {
  final AuthController auth;
  final Json enrollment;
  final String? evaluationId;
  const CoachEvaluation({
    super.key,
    required this.auth,
    required this.enrollment,
    this.evaluationId,
  });
  @override
  State<CoachEvaluation> createState() => _CoachEvaluationState();
}

class _CoachEvaluationState extends State<CoachEvaluation> {
  Json? evaluation;
  String? error, message;
  bool busy = false, criteriaDialogOpen = false;
  _CriteriaState? criteriaState;
  bool canSetupCriteria = false, criteriaPermissionUnconfirmed = false;
  bool criteriaWriteUnconfirmed = false;
  String? criteriaSportId, criteriaCommandKey;
  bool recordUnavailable = false;

  final notes = TextEditingController();
  final scores = <String, double>{};
  final assessed = <String>{};
  String? createdId;
  String? requestedDate;
  bool evaluationDateUnavailable = false;
  bool uncertainCreate = false, publishIssued = false;
  bool shareWithGuardian = true;
  bool publishReviewOpen = false, leaveReviewOpen = false;
  Json? pendingSave;
  String? get contextId => widget.auth.session?.selected == null
      ? null
      : '${widget.auth.session!.selected!.academyId}:${widget.auth.session!.selected!.id}:${widget.auth.session!.selected!.contextRevision}';
  late final openedContext = contextId;
  bool get current => openedContext != null && openedContext == contextId;
  bool get published =>
      ['Published', 'PublishedInternal'].contains(evaluation?['status']);
  bool get locked => busy || pendingSave != null || published;
  bool get hasUnsavedChanges =>
      evaluation != null &&
      !published &&
      !matchesStored(evaluation!, draftPayload(evaluation!));
  bool get guardBack =>
      evaluation != null &&
      !published &&
      (busy || pendingSave != null || hasUnsavedChanges);
  bool get showsDraftContext => criteriaState != null || uncertainCreate;

  List<String> get enrollmentIdentity => [
    for (final value in [
      widget.enrollment['player'],
      if (showsDraftContext)
        widget.enrollment['sport'] ?? widget.enrollment['sportName'],
      widget.enrollment['group'],
    ])
      if (value is String && value.trim().isNotEmpty) value.trim(),
  ];

  @override
  void initState() {
    super.initState();
    openedContext;
    final fromSession = _sessionCriteriaState(
      widget.enrollment['criteriaState'],
    );
    if (widget.evaluationId == null &&
        (widget.enrollment['criteriaReady'] == false ||
            fromSession == _CriteriaState.missing ||
            fromSession == _CriteriaState.inactive)) {
      criteriaState = _CriteriaState.unknown;
    }
    criteriaSportId = widget.enrollment['sportId'] as String?;
    canSetupCriteria = widget.auth.session?.selected?.role == 'AcademyOwner';
    createdId = widget.evaluationId;
    widget.auth.addListener(contextChanged);
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (!mounted) return;
      if (criteriaState == null) {
        create();
      } else {
        refreshCriteria();
      }
    });
  }

  void contextChanged() {
    if (mounted) setState(() {});
  }

  @override
  void dispose() {
    widget.auth.removeListener(contextChanged);
    notes.dispose();
    super.dispose();
  }

  String? validDate(Object? value) {
    if (value is! String || !RegExp(r'^\d{4}-\d{2}-\d{2}$').hasMatch(value)) {
      return null;
    }
    final date = DateTime.tryParse(value);
    return date != null &&
            date.year > 0 &&
            date.toIso8601String().split('T').first == value
        ? value
        : null;
  }

  Future<String> newEvaluationDate() async {
    final sessionId = widget.enrollment['sessionId'];
    if (sessionId is String && sessionId.isNotEmpty) {
      final supplied = validDate(widget.enrollment['evaluationDate']);
      if (supplied != null) return supplied;
      final session = obj(
        await widget.auth.request(
          'GET',
          '/api/v1/attendance/sessions/${Uri.encodeComponent(sessionId)}',
        ),
      );
      final date = validDate(session['sessionDate']);
      if (session['id'] == sessionId && date != null) return date;
    } else {
      final calendar = obj(
        await widget.auth.request('GET', '/api/v1/academy/calendar'),
      );
      final date = validDate(calendar['today']);
      if (date != null) return date;
    }
    throw const ApiFailure(0, 'تعذر التحقق من تاريخ التقييم.');
  }

  void handleCreationFailure(Object caught, {required bool issued}) {
    if (!mounted || !current) return;
    final failure = caught is ApiFailure ? caught : null;
    final denied = [401, 403, 404].contains(failure?.status);
    setState(() {
      recordUnavailable = denied;
      evaluationDateUnavailable = createdId == null && !issued && !denied;
      uncertainCreate =
          issued &&
          createdId == null &&
          (failure == null || failure.status == 0 || failure.status >= 500);
      error = recordUnavailable
          ? 'هذا التقييم غير متاح لحسابك.'
          : evaluationDateUnavailable
          ? widget.enrollment['sessionId'] == null
                ? 'تعذر قراءة تاريخ الأكاديمية.'
                : 'تعذر قراءة تاريخ الحصة.'
          : uncertainCreate
          ? 'تعذر تأكيد إنشاء المسودة.'
          : widget.evaluationId != null || createdId != null
          ? 'تعذر تحميل التقييم.'
          : failure?.message ?? 'تعذر بدء التقييم.';
    });
  }

  Future<void> create() async {
    if (busy || !current || uncertainCreate) return;
    setState(() {
      busy = true;
      error = null;
      evaluationDateUnavailable = false;
      recordUnavailable = false;
      if (criteriaState == _CriteriaState.ready) criteriaState = null;
    });
    var checkCriteria = false, issued = false;
    try {
      if (createdId == null) {
        final date = await newEvaluationDate();
        if (!mounted || !current) return;
        requestedDate = date;
        issued = true;
        final created = obj(
          await widget.auth.request(
            'POST',
            '/api/v1/evaluations',
            body: {
              'sportEnrollmentId': widget.enrollment['id'],
              'evaluationDate': date,
              if (widget.enrollment['sessionId'] != null)
                'trainingSessionId': widget.enrollment['sessionId'],
              'reportingPeriod': 'تقييم دوري',
              'generalNotes': null,
            },
            idempotencyKey: commandKey(),
          ),
        );
        final id = created['id'];
        if (id is! String || id.trim().isEmpty) {
          throw const ApiFailure(0, 'تعذر تأكيد إنشاء المسودة.');
        }
        createdId = id.trim();
      }
      final next = await readEvaluation();
      if (mounted && current) applyEvaluation(next);
    } catch (caught) {
      if (mounted && current) {
        if (issued &&
            createdId == null &&
            caught is ApiFailure &&
            caught.status == 409 &&
            caught.message ==
                'جهّز معايير تقييم الرياضة أولًا، ثم ابدأ تقييم اللاعب.') {
          setState(() {
            criteriaState = _CriteriaState.unknown;
            checkCriteria = true;
          });
        } else {
          handleCreationFailure(caught, issued: issued);
        }
      }
    } finally {
      if (mounted) setState(() => busy = false);
    }
    if (checkCriteria && mounted && current) await refreshCriteria();
  }

  Future<_CriteriaState> readCriteriaState() async {
    final sessionId =
        widget.enrollment['criteriaSessionId'] ??
        widget.enrollment['sessionId'];
    if (sessionId is String && sessionId.isNotEmpty) {
      final roster = obj(
        await widget.auth.request(
          'GET',
          '/api/v1/attendance/sessions/${Uri.encodeComponent(sessionId)}/players',
        ),
      );
      final session = obj(roster['session']);
      if (session['id'] != sessionId ||
          roster['canEvaluate'] != true ||
          !rows(roster['players']).any(
            (player) => player['sportEnrollmentId'] == widget.enrollment['id'],
          )) {
        throw const ApiFailure(403, 'التقييم غير متاح لهذا اللاعب في الحصة.');
      }
      final sport = session['sportId'];
      if (sport is String && sport.isNotEmpty) criteriaSportId = sport;
      final state = _sessionCriteriaState(session['criteriaState']);
      if (state != null) return state;
    }
    if (!mounted || !current) return _CriteriaState.unknown;
    if (criteriaSportId == null || criteriaSportId!.isEmpty) {
      final options = obj(
        await widget.auth.request('GET', '/api/v1/evaluations/options'),
      );
      final enrollment = rows(options['enrollments'])
          .where((item) => item['id'] == widget.enrollment['id'])
          .firstOrNull;
      final sport = enrollment?['sportId'];
      if (sport is! String || sport.isEmpty) {
        throw const ApiFailure(
          404,
          'أعد فتح ملف اللاعب للتحقق من رياضة التقييم.',
        );
      }
      criteriaSportId = sport;
    }
    if (!mounted || !current) return _CriteriaState.unknown;
    final list = rows(
      await widget.auth.request(
        'GET',
        '/api/v1/evaluations/criteria?${Uri(queryParameters: {'sportId': criteriaSportId}).query}',
      ),
    );
    if (list.any(
      (item) => item['sportId'] != criteriaSportId || item['isActive'] is! bool,
    )) {
      throw const ApiFailure(0, 'تعذر التحقق من معايير الرياضة.');
    }
    if (list.any((item) => item['isActive'] == true)) {
      return _CriteriaState.ready;
    }
    return list.isEmpty ? _CriteriaState.missing : _CriteriaState.inactive;
  }

  Future<void> refreshCriteria() async {
    if (busy || !current) return;
    setState(() {
      busy = true;
      error = null;
    });
    try {
      final state = await readCriteriaState();
      if (!mounted || !current) return;
      setState(() {
        criteriaState = state;
        criteriaWriteUnconfirmed = false;
        criteriaPermissionUnconfirmed = false;
      });
      if (state == _CriteriaState.missing &&
          widget.auth.session?.selected?.role == 'AcademyAdmin') {
        try {
          final access = await StaffCapabilities.load(widget.auth);
          if (mounted && current) {
            setState(
              () => canSetupCriteria = access.allows(
                'evaluation.criteria.manage',
                'evaluations',
                'Add',
              ),
            );
          }
        } catch (_) {
          if (mounted && current) {
            setState(() {
              canSetupCriteria = false;
              criteriaPermissionUnconfirmed = true;
            });
          }
        }
      }
    } catch (caught) {
      if (mounted && current) {
        setState(() {
          criteriaState = _CriteriaState.unknown;
          final failure = caught is ApiFailure ? caught : null;
          recordUnavailable = [401, 403, 404].contains(failure?.status);
          error =
              failure?.message ?? 'تعذر قراءة معايير الرياضة. أعد المحاولة.';
        });
      }
    } finally {
      if (mounted) setState(() => busy = false);
    }
  }

  Future<void> setupCriteria() async {
    final sport = criteriaSportId;
    if (busy ||
        !current ||
        !canSetupCriteria ||
        criteriaWriteUnconfirmed ||
        criteriaState != _CriteriaState.missing ||
        sport == null) {
      return;
    }
    setState(() {
      busy = true;
      error = null;
    });
    var issued = false, startAfter = false, refreshAfter = false;
    try {
      final proposal = obj(
        await widget.auth.request(
          'GET',
          '/api/v1/evaluations/criteria/defaults?${Uri(queryParameters: {'sportId': sport}).query}',
        ),
      );
      if (!mounted || !current) return;
      if (proposal['sportId'] != sport) {
        throw const ApiFailure(
          0,
          'تعذر التحقق من المعايير المقترحة لهذه الرياضة.',
        );
      }
      if (proposal['existing'] == true) {
        refreshAfter = true;
      } else {
        final names = rows(proposal['criteria'])
            .map((item) => item['arabicName'])
            .whereType<String>()
            .where((name) => name.trim().isNotEmpty)
            .toList();
        if (names.isEmpty) {
          throw const ApiFailure(0, 'لا توجد معايير مقترحة لهذه الرياضة.');
        }
        setState(() => criteriaDialogOpen = true);
        final accepted = await showDialog<bool>(
          context: context,
          builder: (context) => AlertDialog(
            scrollable: true,
            title: const UiText('المعايير المقترحة'),
            titleTextStyle: Theme.of(context).textTheme.titleMedium
                ?.copyWith(fontSize: 18, fontWeight: FontWeight.w700),
            content: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              mainAxisSize: MainAxisSize.min,
              children: [
                UiText(
                  textOf(proposal['sport']),
                  style: const TextStyle(fontWeight: FontWeight.w700),
                ),
                const SizedBox(height: 8),
                const UiText('ستُستخدم في التقييمات الجديدة لهذه الرياضة.'),
                const SizedBox(height: 6),
                for (final name in names)
                  Padding(
                    padding: const EdgeInsets.only(top: 6),
                    child: UiText(name),
                  ),
              ],
            ),
            actions: [
              TextButton(
                onPressed: () => Navigator.pop(context, false),
                child: const UiText('إلغاء'),
              ),
              FilledButton(
                onPressed: () => Navigator.pop(context, true),
                child: const UiText('إضافة المعايير وبدء التقييم'),
              ),
            ],
          ),
        );
        if (!mounted || !current || accepted != true) return;
        setState(() => criteriaDialogOpen = false);
        issued = true;
        final result = obj(
          await widget.auth.request(
            'POST',
            '/api/v1/evaluations/criteria/defaults',
            body: {'sportId': sport},
            idempotencyKey: criteriaCommandKey ??= commandKey(),
          ),
        );
        if (!mounted || !current) return;
        final count = result['count'];
        final created = result['created'];
        if (count is! int || count <= 0 || created is! int || created < 0) {
          throw const ApiFailure(0, 'تعذر تأكيد حفظ المعايير.');
        }
        setState(() => criteriaState = _CriteriaState.ready);
        startAfter = created > 0 && count == names.length;
      }
    } catch (caught) {
      if (mounted && current) {
        final failed = caught is ApiFailure ? caught : null;
        setState(() {
          if (issued &&
              (failed == null || failed.status == 0 || failed.status >= 500)) {
            criteriaWriteUnconfirmed = true;
            error = 'انقطع تأكيد الإضافة. تحقّق من المعايير المحفوظة.';
          } else if (issued && failed?.status == 409) {
            refreshAfter = true;
          } else {
            error = failed?.message ?? 'تعذر تحميل المعايير المقترحة.';
          }
        });
      }
    } finally {
      if (mounted) {
        setState(() {
          busy = false;
          criteriaDialogOpen = false;
        });
      }
    }
    if (!mounted || !current) return;
    if (refreshAfter) {
      await refreshCriteria();
    } else if (startAfter) {
      await create();
    }
  }

  Widget criteriaStep() {
    final state = criteriaState!;
    final propose =
        state == _CriteriaState.missing &&
        canSetupCriteria &&
        !criteriaPermissionUnconfirmed;
    final title = switch (state) {
      _CriteriaState.unknown => 'معايير التقييم',
      _CriteriaState.missing => 'لا توجد معايير لهذه الرياضة',
      _CriteriaState.inactive => 'معايير الرياضة متوقفة',
      _CriteriaState.ready => 'المعايير جاهزة',
    };
    final detail = criteriaWriteUnconfirmed
        ? null
        : switch (state) {
            _CriteriaState.missing when criteriaPermissionUnconfirmed =>
              'تعذر التحقق من صلاحية تجهيز المعايير.',
            _CriteriaState.missing when !propose =>
              'اطلب من الإدارة إضافة معيار لهذه الرياضة.',
            _CriteriaState.inactive
                when widget.auth.session?.selected?.role == 'AcademyOwner' =>
              'فعّل معيارًا من شاشة معايير التقييم في لوحة الإدارة.',
            _CriteriaState.inactive =>
              'اطلب من الإدارة تفعيل معيار لهذه الرياضة.',
            _ => null,
          };
    final action = criteriaWriteUnconfirmed
        ? 'التحقق من الحفظ'
        : state == _CriteriaState.ready
        ? 'بدء التقييم'
        : propose
        ? 'عرض المعايير المقترحة'
        : 'إعادة قراءة المعايير';
    final onPressed = criteriaWriteUnconfirmed
        ? refreshCriteria
        : state == _CriteriaState.ready
        ? create
        : propose
        ? setupCriteria
        : refreshCriteria;
    return OperationSection(
      title: criteriaWriteUnconfirmed ? 'حفظ المعايير' : title,
      icon: Icons.tune,
      children: [
        if (detail != null) ...[UiText(detail), const SizedBox(height: 12)],
        if (state != _CriteriaState.unknown || !busy)
          FilledButton(
            onPressed: busy ? null : onPressed,
            child: UiText(action),
          ),
      ],
    );
  }

  Json draftPayload(Json snapshot) => {
    'reportingPeriod': snapshot['reportingPeriod'],
    'generalNotes': notes.text.trim(),
    'version': snapshot['version'],
    'scores': rows(snapshot['scores'])
        .map(
          (item) => {
            'criterionId': item['criterionId'],
            'score': assessed.contains(textOf(item['criterionId']))
                ? scores[textOf(item['criterionId'])]!.round()
                : null,
            'notes': item['notes'],
          },
        )
        .toList(),
  };

  Future<void> reviewPublication() async {
    if (locked || !current || assessed.isEmpty || publishReviewOpen) return;
    FocusScope.of(context).unfocus();
    publishReviewOpen = true;
    try {
      final share = await showDialog<bool>(
        context: context,
        builder: (dialog) => AlertDialog(
          scrollable: true,
          title: const UiText('نشر التقييم'),
          titleTextStyle: Theme.of(dialog).textTheme.titleMedium
              ?.copyWith(fontSize: 18, fontWeight: FontWeight.w700),
          content: const UiText(
            'الاعتماد الداخلي لفريق العمل فقط. يُقفل التقييم بعد الاعتماد أو النشر.',
          ),
          actions: [
            TextButton(
              onPressed: () => Navigator.pop(dialog),
              child: const UiText('إلغاء'),
            ),
            OutlinedButton(
              onPressed: () => Navigator.pop(dialog, false),
              child: const UiText('اعتماد داخلي'),
            ),
            FilledButton(
              onPressed: () => Navigator.pop(dialog, true),
              child: const UiText('نشر لولي الأمر'),
            ),
          ],
        ),
      );
      if (share != null && mounted && current) {
        await save(publish: true, share: share);
      }
    } finally {
      publishReviewOpen = false;
    }
  }

  Future<void> confirmLeaving() async {
    if (leaveReviewOpen ||
        busy ||
        !current ||
        !guardBack ||
        !Navigator.of(context).canPop()) {
      return;
    }
    FocusScope.of(context).unfocus();
    leaveReviewOpen = true;
    try {
      final unconfirmed = pendingSave != null;
      final leave = await showDialog<bool>(
        context: context,
        builder: (dialog) => AlertDialog(
          scrollable: true,
          title: UiText(
            unconfirmed ? 'حالة الحفظ غير مؤكدة' : 'تغييرات غير محفوظة',
          ),
          content: UiText(
            unconfirmed ? 'يمكنك مراجعة التقييم لاحقًا من سجل اللاعب.' : 'ارجع للتقييم إذا كنت تريد حفظ الدرجات أو الملاحظات قبل الخروج.',
          ),
          actions: [
            TextButton(
              onPressed: () => Navigator.pop(dialog, true),
              child: UiText(unconfirmed ? 'خروج' : 'خروج دون حفظ'),
            ),
            FilledButton(
              onPressed: () => Navigator.pop(dialog, false),
              child: const UiText('البقاء في التقييم'),
            ),
          ],
        ),
      );
      if (leave == true && mounted && current) Navigator.of(context).pop();
    } finally {
      leaveReviewOpen = false;
    }
  }

  Future<void> save({required bool publish, bool share = true}) async {
    if (evaluation == null || locked || !current) return;
    if (publish ? assessed.isEmpty : !hasUnsavedChanges) return;
    FocusScope.of(context).unfocus();
    final snapshot = evaluation!;
    final draft = draftPayload(snapshot);
    setState(() {
      pendingSave = draft;
      publishIssued = false;
      shareWithGuardian = share;
    });
    await completeEvaluation(() async {
      var stored = snapshot;
      if (!publish || !matchesStored(snapshot, draft)) {
        await widget.auth.request(
          'PUT',
          '/api/v1/evaluations/${snapshot['id']}',
          body: draft,
        );
        if (!mounted || !current) return;
        stored = await verifyStored(draft, 'Draft');
      }
      if (!mounted || !current) return;
      if (!publish) {
        finishEvaluation(stored, 'Draft');
        return;
      }
      publishIssued = true;
      await widget.auth.request(
        'POST',
        '/api/v1/evaluations/${snapshot['id']}/publish',
        body: {'version': stored['version'], 'shareWithGuardian': share},
        idempotencyKey: commandKey(),
      );
      if (!mounted || !current) return;
      final expected = share ? 'Published' : 'PublishedInternal';
      final published = await verifyStored(draft, expected);
      if (mounted && current) finishEvaluation(published, expected);
    });
  }

  Future<void> recheckEvaluation() async {
    final draft = pendingSave;
    if (draft == null || busy || !current) return;
    final expected = !publishIssued
        ? 'Draft'
        : shareWithGuardian
        ? 'Published'
        : 'PublishedInternal';
    await completeEvaluation(() async {
      final stored = await verifyStored(draft, expected);
      if (mounted && current) finishEvaluation(stored, expected);
    });
  }

  Future<void> completeEvaluation(Future<void> Function() action) async {
    setState(() {
      busy = true;
      error = null;
      message = null;
    });
    try {
      await action();
    } on ApiFailure catch (e) {
      if (mounted && current) {
        setState(() {
          if ([401, 403, 404].contains(e.status)) {
            evaluation = null;
            pendingSave = null;
            publishIssued = false;
            recordUnavailable = true;
          }
          error = e.message;
        });
      }
    } catch (_) {
      if (mounted && current) {
        setState(() => error = 'تعذر تأكيد الحفظ. أعد التحقق من التقييم نفسه.');
      }
    } finally {
      if (mounted) setState(() => busy = false);
    }
  }

  void finishEvaluation(Json stored, String expected) {
    setState(() {
      evaluation = stored;
      message = switch (expected) {
        'Published' => 'تم نشر التقييم لولي الأمر.',
        'PublishedInternal' => 'حُفظ التقييم للاستخدام الداخلي.',
        _ => 'تم حفظ المسودة.',
      };
      pendingSave = null;
      publishIssued = false;
    });
  }

  bool matchesStored(Json stored, Json draft) {
    String clean(Object? value) => textOf(value ?? '').trim();
    final expected = rows(draft['scores']);
    final actual = {
      for (final item in rows(stored['scores'])) item['criterionId']: item,
    };
    return stored['id'] == evaluation!['id'] &&
        stored['sportEnrollmentId'] == widget.enrollment['id'] &&
        stored['reportingPeriod'] == draft['reportingPeriod'] &&
        clean(stored['generalNotes']) == clean(draft['generalNotes']) &&
        rows(stored['scores']).length == expected.length &&
        expected.every((item) {
          final saved = actual[item['criterionId']];
          return saved != null &&
              saved['score'] == item['score'] &&
              clean(saved['notes']) == clean(item['notes']);
        });
  }

  Future<Json> verifyStored(Json draft, String status) async {
    final stored = await readEvaluation();
    if (!mounted || !current) return stored;
    if (stored['status'] != status || !matchesStored(stored, draft)) {
      throw const ApiFailure(
        0,
        'لم تتطابق النسخة المحفوظة مع التغييرات المطلوبة.',
      );
    }
    return stored;
  }

  Future<Json> readEvaluation() async {
    final stored = obj(
      await widget.auth.request(
        'GET',
        '/api/v1/evaluations/${Uri.encodeComponent(createdId!)}',
      ),
    );
    if (stored['id'] != createdId ||
        stored['sportEnrollmentId'] != widget.enrollment['id'] ||
        ![
          'Draft',
          'Published',
          'PublishedInternal',
        ].contains(stored['status']) ||
        stored['version'] is! int) {
      throw const ApiFailure(0, 'تعذر التحقق من التقييم المطلوب.');
    }
    return stored;
  }

  void applyEvaluation(Json stored) {
    scores.clear();
    assessed.clear();
    for (final item in rows(stored['scores'])) {
      scores[textOf(item['criterionId'])] = ((item['score'] as num?) ?? 50)
          .toDouble();
      if (item['score'] != null) assessed.add(textOf(item['criterionId']));
    }
    notes.text = textOf(stored['generalNotes'] ?? '');
    setState(() {
      evaluation = stored;
      pendingSave = null;
      publishIssued = false;
    });
  }

  Future<void> reloadEvaluation() async {
    final replace = await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: const UiText('عرض النسخة المحفوظة؟'),
        content: const UiText('ستُستبدل القيم المفتوحة بالنسخة المحفوظة.'),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(context, false),
            child: const UiText('إلغاء'),
          ),
          FilledButton(
            onPressed: () => Navigator.pop(context, true),
            child: const UiText('عرض النسخة'),
          ),
        ],
      ),
    );
    if (replace != true || !mounted || !current || busy) return;
    await completeEvaluation(() async {
      final stored = await readEvaluation();
      if (mounted && current) applyEvaluation(stored);
    });
  }

  Widget evaluationActions(BuildContext context) => Column(
    mainAxisSize: MainAxisSize.min,
    crossAxisAlignment: CrossAxisAlignment.stretch,
    children: [
      if (message != null)
        Semantics(
          liveRegion: true,
          child: UiText(
            message!,
            style: TextStyle(color: Theme.of(context).colorScheme.primary),
          ),
        ),
      if (error != null) ErrorNotice(error!),
      if (pendingSave == null) ...[
        if (assessed.isEmpty) ...[
          UiText(
            'أضف درجة واحدة على الأقل للنشر.',
            style: Theme.of(context).textTheme.bodySmall,
          ),
          const SizedBox(height: 6),
        ],
        FilledButton(
          style: FilledButton.styleFrom(minimumSize: const Size.fromHeight(52)),
          onPressed: locked || assessed.isEmpty ? null : reviewPublication,
          child: const UiText(
            'مراجعة النشر',
            style: TextStyle(fontSize: 15, fontWeight: FontWeight.w700),
          ),
        ),
        const SizedBox(height: 8),
        OutlinedButton(
          onPressed: locked || !hasUnsavedChanges
              ? null
              : () => save(publish: false),
          child: const UiText('حفظ المسودة'),
        ),
      ],
      if (pendingSave != null) ...[
        if (busy)
          const LinearProgressIndicator(
            semanticsLabel: 'جارٍ تحديث حالة التقييم',
          ),
        FilledButton(
          onPressed: busy ? null : recheckEvaluation,
          child: const UiText('إعادة التحقق من التقييم نفسه'),
        ),
        TextButton(
          onPressed: busy ? null : reloadEvaluation,
          child: const UiText('عرض النسخة المحفوظة'),
        ),
      ],
    ],
  );

  @override
  Widget build(BuildContext context) => !current
      ? const SizedBox.shrink()
      : PopScope<Object?>(
          canPop: !guardBack,
          onPopInvokedWithResult: (didPop, _) {
            if (!didPop) confirmLeaving();
          },
          child: Theme(
            data: operationTheme(context),
            child: AppScaffold(
              title: published ? 'تفاصيل التقييم' : 'تقييم اللاعب',
              onBack: guardBack ? () => Navigator.of(context).maybePop() : null,
              bottomBar: evaluation == null || published
                  ? null
                  : evaluationActions(context),
              child: published
                  ? CoachEvaluationRecord(
                      auth: widget.auth,
                      enrollment: widget.enrollment,
                      evaluation: evaluation!,
                      message: message,
                    )
                  : evaluation == null
                  ? ListView(
                      padding: const EdgeInsets.all(20),
                      children: [
                        if (!recordUnavailable &&
                            enrollmentIdentity.isNotEmpty) ...[
                          AppCard(
                            child: !showsDraftContext
                                ? UiText(enrollmentIdentity.join('\n'))
                                : Column(
                                    crossAxisAlignment:
                                        CrossAxisAlignment.start,
                                    children: [
                                      UiText(
                                        enrollmentIdentity.first,
                                        style: const TextStyle(
                                          fontWeight: FontWeight.w700,
                                        ),
                                      ),
                                      for (final detail
                                          in enrollmentIdentity.skip(1))
                                        UiText(detail),
                                      if (uncertainCreate)
                                        if (evaluationDateText(
                                              context,
                                              requestedDate,
                                            )
                                            case final String date)
                                          UiText(date),
                                    ],
                                  ),
                          ),
                          if (showsDraftContext) const SizedBox(height: 12),
                        ],
                        if (criteriaState != null && !recordUnavailable)
                          criteriaStep(),
                        if (busy && !criteriaDialogOpen)
                          const Center(child: CircularProgressIndicator()),
                        if (error != null) ErrorNotice(error!),
                        if (error != null &&
                            !recordUnavailable &&
                            criteriaState == null &&
                            !uncertainCreate)
                          FilledButton(
                            onPressed: busy ? null : create,
                            child: UiText(
                              widget.evaluationId != null
                                  ? 'إعادة تحميل التقييم'
                                  : evaluationDateUnavailable
                                  ? 'إعادة محاولة بدء التقييم'
                                  : createdId == null
                                  ? 'بدء تقييم جديد'
                                  : 'إعادة التحقق من المسودة نفسها',
                            ),
                          ),
                        if (uncertainCreate)
                          FilledButton(
                            onPressed: () => openPage(
                              context,
                              CoachPlayerEvaluations(
                                auth: widget.auth,
                                enrollment: widget.enrollment,
                                canEvaluate: true,
                              ),
                            ),
                            child: const UiText('عرض تقييمات اللاعب'),
                          ),
                      ],
                    )
                  : ListView(
                      padding: const EdgeInsets.fromLTRB(16, 14, 16, 20),
                      children: [
                        Padding(
                          padding: const EdgeInsets.only(bottom: 10),
                          child: Row(
                            children: [
                              PlayerAvatar(
                                textOf(evaluation!['player']),
                                auth: widget.auth,
                                reference: widget.enrollment['photoReference'],
                                gender: widget.enrollment['gender'],
                                size: 52,
                                square: true,
                              ),
                              const SizedBox(width: 12),
                              Expanded(
                                child: Column(
                                  crossAxisAlignment: CrossAxisAlignment.start,
                                  children: [
                                    UiText(
                                      textOf(evaluation!['player']),
                                      style: const TextStyle(
                                        fontSize: 18,
                                        fontWeight: FontWeight.w500,
                                      ),
                                    ),
                                    if (evaluationDateText(
                                          context,
                                          evaluation!['evaluationDate'] ??
                                              requestedDate,
                                        )
                                        case final String date)
                                      UiText(
                                        date,
                                        style: Theme.of(context)
                                            .textTheme
                                            .bodySmall,
                                      ),
                                    UiText(
                                      'الدرجات المسجّلة ${assessed.length} من ${scores.length}',
                                      style: TextStyle(
                                        fontSize: 12,
                                        color: Theme.of(context)
                                            .colorScheme
                                            .onSurfaceVariant,
                                      ),
                                    ),
                                  ],
                                ),
                              ),
                            ],
                          ),
                        ),
                        for (final item in rows(evaluation!['scores'])) ...[
                          const Divider(height: 24),
                          Row(
                            crossAxisAlignment: CrossAxisAlignment.baseline,
                            textBaseline: TextBaseline.alphabetic,
                            children: [
                              Expanded(
                                child: UiText(
                                  textOf(item['name']),
                                  key: ValueKey(
                                    'evaluation-criterion-${item['criterionId']}',
                                  ),
                                  style: const TextStyle(
                                    fontSize: 15,
                                    fontWeight: FontWeight.w600,
                                  ),
                                ),
                              ),
                              const SizedBox(width: 8),
                              UiText(
                                assessed.contains(textOf(item['criterionId']))
                                    ? '${scores[textOf(item['criterionId'])]!.round()}'
                                    : 'لم يُقيّم',
                                style: TextStyle(
                                  fontSize:
                                      assessed.contains(
                                        textOf(item['criterionId']),
                                      )
                                      ? 24
                                      : 13,
                                  fontWeight: FontWeight.w600,
                                  color:
                                      assessed.contains(
                                        textOf(item['criterionId']),
                                      )
                                      ? scores[textOf(item['criterionId'])] == 0
                                            ? Theme.of(context)
                                                  .colorScheme
                                                  .error
                                            : Theme.of(context)
                                                  .colorScheme
                                                  .primary
                                      : Theme.of(context)
                                            .colorScheme
                                            .onSurfaceVariant,
                                ),
                              ),
                            ],
                          ),
                          SliderTheme(
                            data: SliderTheme.of(context).copyWith(
                              thumbShape: const RoundSliderThumbShape(
                                enabledThumbRadius: 12,
                                disabledThumbRadius: 12,
                                elevation: 1,
                                pressedElevation: 2,
                              ),
                              overlayShape: const RoundSliderOverlayShape(
                                overlayRadius: 22,
                              ),
                              trackShape: const RoundedRectSliderTrackShape(),
                              trackHeight: 4,
                              activeTrackColor:
                                  assessed.contains(textOf(item['criterionId']))
                                  ? Theme.of(context).colorScheme.primary
                                  : Theme.of(context)
                                        .colorScheme
                                        .outlineVariant,
                              inactiveTrackColor: Theme.of(context)
                                  .colorScheme
                                  .outlineVariant,
                              thumbColor:
                                  assessed.contains(textOf(item['criterionId']))
                                  ? Theme.of(context).colorScheme.primary
                                  : Theme.of(context)
                                        .colorScheme
                                        .onSurfaceVariant,
                              showValueIndicator:
                                  ShowValueIndicator.onlyForContinuous,
                            ),
                            child: Slider(
                              key: ValueKey(
                                'evaluation-score-${item['criterionId']}',
                              ),
                              semanticFormatterCallback: (v) =>
                                  '${item['name']} ${v.round()} من100',
                              min: 0,
                              max: 100,
                              divisions: 100,
                              value: scores[textOf(item['criterionId'])]!,
                              onChanged: locked
                                  ? null
                                  : (v) => setState(() {
                                      message = null;
                                      scores[textOf(item['criterionId'])] = v;
                                      assessed.add(textOf(item['criterionId']));
                                    }),
                            ),
                          ),
                          Align(
                            alignment: AlignmentDirectional.centerStart,
                            child: Semantics(
                              label: 'اعتماد درجة ${item['name']}',
                              child: OutlinedButton(
                                style: OutlinedButton.styleFrom(
                                  minimumSize: const Size(0, 32),
                                  padding: const EdgeInsets.symmetric(
                                    horizontal: 10,
                                  ),
                                ),
                                key: ValueKey(
                                  'evaluation-assess-${item['criterionId']}',
                                ),
                                onPressed: locked
                                    ? null
                                    : () => setState(() {
                                        message = null;
                                        if (assessed.contains(
                                          textOf(item['criterionId']),
                                        )) {
                                          assessed.remove(
                                            textOf(item['criterionId']),
                                          );
                                        } else {
                                          assessed.add(
                                            textOf(item['criterionId']),
                                          );
                                        }
                                      }),
                                child: UiText(
                                  assessed.contains(textOf(item['criterionId']))
                                      ? 'إلغاء التقييم'
                                      : 'اعتماد ${scores[textOf(item['criterionId'])]!.round()}',
                                  style: const TextStyle(fontSize: 12),
                                ),
                              ),
                            ),
                          ),
                          const SizedBox(height: 6),
                        ],
                        TextField(
                          readOnly: locked,
                          controller: notes,
                          onChanged: (_) => setState(() => message = null),
                          maxLines: 3,
                          decoration: InputDecoration(
                            labelText: ui(context, 'ملاحظات المدرب'),
                          ),
                        ),
                      ],
                    ),
            ),
          ),
        );
}

class CoachPlayerEvaluations extends StatefulWidget {
  const CoachPlayerEvaluations({
    super.key,
    required this.auth,
    required this.enrollment,
    required this.canEvaluate,
  });
  final AuthController auth;
  final Json enrollment;
  final bool canEvaluate;
  @override
  State<CoachPlayerEvaluations> createState() => _CoachPlayerEvaluationsState();
}

class _CoachPlayerEvaluationsState extends State<CoachPlayerEvaluations> {
  int revision = 0;

  @override
  Widget build(BuildContext context) => AppScaffold(
    title: 'تقييمات اللاعب',
    child: RemoteBody(
      key: ValueKey(revision),
      auth: widget.auth,
      path:
          '/api/v1/evaluations?${Uri(queryParameters: {if (widget.enrollment['playerId'] case final String playerId) 'playerId': playerId, 'sportEnrollmentId': textOf(widget.enrollment['id'])}).query}',
      builder: (context, value) {
        final items = rows(value).where(
          (item) =>
              item['sportEnrollmentId'] == widget.enrollment['id'] &&
              (item['status'] == 'Published' ||
                  item['status'] == 'PublishedInternal' ||
                  widget.canEvaluate && item['status'] == 'Draft'),
        );
        return gList(
          [
            GCard(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  UiText(
                    textOf(widget.enrollment['player']),
                    style: Theme.of(context).textTheme.titleMedium
                        ?.copyWith(fontWeight: FontWeight.w700),
                  ),
                  if (widget.enrollment['sport'] case final String sport)
                    UiText(sport),
                ],
              ),
            ),
            if (items.isEmpty)
              const EmptyMessage('لا توجد تقييمات لهذا الاشتراك الرياضي.'),
            for (final item in items)
              GLink(
                evaluationDateText(context, item['evaluationDate']) ?? 'تقييم',
                item['status'] == 'Draft'
                    ? Icons.edit_note_outlined
                    : Icons.assignment_outlined,
                () async {
                  await openPage(
                    context,
                    CoachEvaluation(
                      auth: widget.auth,
                      enrollment: widget.enrollment,
                      evaluationId: textOf(item['id']),
                    ),
                  );
                  if (mounted && item['status'] == 'Draft') {
                    setState(() => revision++);
                  }
                },
                key: ValueKey('evaluation-history-${item['id']}'),
                subtitle: [
                  evaluationStatusText(item['status']),
                  if (item['group'] case final String group) group,
                ].join(' · '),
              ),
          ],
          key: PageStorageKey('evaluation-history-${widget.enrollment['id']}'),
        );
      },
    ),
  );
}
