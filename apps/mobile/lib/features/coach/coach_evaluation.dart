import '../shared/operation_design.dart';
import '../shared/staff_capabilities.dart';

import 'package:academy_mobile/core/theme/ui_text.dart';
import 'package:flutter/material.dart';

import '../../core/api/api_client.dart';
import '../../core/auth/auth_controller.dart';
import '../../core/widgets/app_widgets.dart';
import '../guardian/guardian_ui.dart';

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
  bool missingCriteria = false, canSetupCriteria = false;

  final notes = TextEditingController();
  final scores = <String, double>{};
  final assessed = <String>{};
  String? createdId;
  bool uncertainCreate = false, writeIssued = false, publishIssued = false;
  bool wantsPublish = false, shareWithGuardian = true;
  Json? pendingSave;
  String? get contextId => widget.auth.session?.selected == null
      ? null
      : '${widget.auth.session!.selected!.academyId}:${widget.auth.session!.selected!.id}:${widget.auth.session!.selected!.contextRevision}';
  late final openedContext = contextId;
  bool get current => openedContext != null && openedContext == contextId;
  bool get published =>
      ['Published', 'PublishedInternal'].contains(evaluation?['status']);
  bool get locked => busy || pendingSave != null || published;

  @override
  void initState() {
    super.initState();
    openedContext;
    missingCriteria = widget.enrollment['criteriaReady'] == false;
    canSetupCriteria = widget.auth.session?.selected?.role == 'AcademyOwner';
    if (missingCriteria &&
        !canSetupCriteria &&
        widget.auth.session?.selected?.role == 'AcademyAdmin') {
      StaffCapabilities.load(widget.auth).then((a) {
        if (mounted && current) {
          setState(
            () => canSetupCriteria = a.allows(
              'evaluation.criteria.manage',
              'evaluations',
              'Add',
            ),
          );
        }
      });
    }
    createdId = widget.evaluationId;
    widget.auth.addListener(contextChanged);
    if (createdId != null || !missingCriteria) {
      WidgetsBinding.instance.addPostFrameCallback((_) {
        if (mounted) create();
      });
    }
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

  Future<void> create() async {
    if (busy || !current || uncertainCreate) return;
    setState(() {
      busy = true;
      error = null;
    });
    try {
      if (createdId == null) {
        final created = obj(
          await widget.auth.request(
            'POST',
            '/api/v1/evaluations',
            body: {
              'sportEnrollmentId': widget.enrollment['id'],
              'evaluationDate':
                  widget.enrollment['evaluationDate'] ??
                  DateTime.now().toIso8601String().split('T').first,
              if (widget.enrollment['sessionId'] != null)
                'trainingSessionId': widget.enrollment['sessionId'],
              'reportingPeriod': 'تقييم دوري',
              'generalNotes': null,
            },
            idempotencyKey: commandKey(),
          ),
        );
        createdId = textOf(created['id']);
        if (createdId!.isEmpty) {
          uncertainCreate = true;
          throw const ApiFailure(
            0,
            'لم يصل معرف المسودة. راجع التقييمات قبل إنشاء تقييم آخر.',
          );
        }
      }
      final next = obj(
        await widget.auth.request('GET', '/api/v1/evaluations/$createdId'),
      );
      if (next['id'] != createdId ||
          next['sportEnrollmentId'] != widget.enrollment['id'] ||
          ![
            'Draft',
            'Published',
            'PublishedInternal',
          ].contains(next['status'])) {
        throw const ApiFailure(
          0,
          'لم تتأكد مطابقة مسودة اللاعب. أعد التحقق من المسودة نفسها.',
        );
      }
      for (final item in rows(next['scores'])) {
        scores[textOf(item['criterionId'])] = ((item['score'] as num?) ?? 50)
            .toDouble();
        if (item['score'] != null) assessed.add(textOf(item['criterionId']));
      }
      notes.text = textOf(next['generalNotes'] ?? '');
      if (mounted && current) setState(() => evaluation = next);
    } on ApiFailure catch (e) {
      if (createdId == null && (e.status == 0 || e.status >= 500)) {
        uncertainCreate = true;
      }
      if (mounted && current) {
        setState(() {
          error = e.message;
          if (e.message ==
              'جهّز معايير تقييم الرياضة أولًا، ثم ابدأ تقييم اللاعب.') {
            missingCriteria = true;
          }
        });
      }
    } catch (_) {
      if (createdId == null) uncertainCreate = true;
      if (mounted && current) {
        setState(
          () => error = 'تعذر تأكيد المسودة. راجع التقييمات قبل إنشاء أخرى.',
        );
      }
    } finally {
      if (mounted) setState(() => busy = false);
    }
  }

  Future<void> setupCriteria() async {
    if (busy || !current) return;
    setState(() {
      busy = true;
      error = null;
    });
    var startAfter = false;
    try {
      final sport = widget.enrollment['sportId'];
      if (sport == null) {
        throw const ApiFailure(422, 'أعد فتح اللاعب لاختيار رياضة التقييم.');
      }
      if (!canSetupCriteria) {
        final list = await widget.auth.request(
          'GET',
          '/api/v1/evaluations/criteria?sportId=$sport',
        );
        if (rows(list).any((c) => c['isActive'] == true) &&
            mounted &&
            current) {
          setState(() {
            missingCriteria = false;
            busy = false;
          });
          await create();
        }
        return;
      }
      final proposal = obj(
        await widget.auth.request(
          'GET',
          '/api/v1/evaluations/criteria/defaults?sportId=$sport',
        ),
      );
      if (!mounted || !current) return;
      setState(() => criteriaDialogOpen = true);
      final accepted = await showDialog<bool>(
        context: context,
        builder: (c) => Theme(
          data: operationTheme(c),
          child: AlertDialog(
            title: const UiText('معايير تقييم الرياضة'),
            content: SingleChildScrollView(
              child: Column(
                mainAxisSize: MainAxisSize.min,
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  UiText(
                    textOf(proposal['sport']),
                    style: const TextStyle(fontWeight: FontWeight.w700),
                  ),
                  const SizedBox(height: 12),
                  const UiText(
                    'راجع المعايير المقترحة. يمكنك تعديلها لاحقًا من إعدادات التقييم.',
                  ),
                  for (final item in rows(proposal['criteria']))
                    Padding(
                      padding: const EdgeInsets.only(top: 10),
                      child: UiText('• ${item['arabicName']}'),
                    ),
                ],
              ),
            ),
            actions: [
              TextButton(
                onPressed: () => Navigator.pop(c, false),
                child: const UiText('رجوع'),
              ),
              FilledButton(
                onPressed: () => Navigator.pop(c, true),
                child: const UiText('اعتماد المعايير وبدء التقييم'),
              ),
            ],
          ),
        ),
      );
      if (mounted) setState(() => criteriaDialogOpen = false);
      if (accepted != true || !current) return;
      await widget.auth.request(
        'POST',
        '/api/v1/evaluations/criteria/defaults',
        body: {'sportId': sport},
        idempotencyKey: commandKey(),
      );
      if (mounted && current) setState(() => missingCriteria = false);
      startAfter = true;
    } on ApiFailure catch (e) {
      if (mounted && current) setState(() => error = e.message);
    } finally {
      if (mounted) {
        setState(() {
          busy = false;
          criteriaDialogOpen = false;
        });
      }
    }
    if (startAfter && current) await create();
  }

  Future<void> save({required bool publish, bool share = true}) async {
    if (evaluation == null || busy || !current || published) return;
    if (pendingSave == null && publish && assessed.isEmpty) {
      setState(
        () => error = 'قيّم معيارًا واحدًا على الأقل أو أكد درجته قبل النشر.',
      );
      return;
    }
    setState(() {
      busy = true;
      error = null;
      message = null;
    });
    try {
      if (pendingSave == null) {
        wantsPublish = publish;
        shareWithGuardian = share;
        pendingSave = {
          'reportingPeriod': evaluation!['reportingPeriod'],
          'generalNotes': notes.text.trim(),
          'version': evaluation!['version'],
          'scores': rows(evaluation!['scores'])
              .map(
                (item) => {
                  'criterionId': item['criterionId'],
                  'score': assessed.contains(textOf(item['criterionId']))
                      ? scores[textOf(item['criterionId'])]!.round()
                      : null,
                  'notes': null,
                },
              )
              .toList(),
        };
      }
      // Once sent, an uncertain write is reconciled by GET, never by another PUT.
      if (!writeIssued) {
        writeIssued = true;
        await widget.auth.request(
          'PUT',
          '/api/v1/evaluations/${evaluation!['id']}',
          body: pendingSave,
        );
      }
      var stored = await verifyStored();
      if (wantsPublish && stored['status'] == 'Draft' && !publishIssued) {
        publishIssued = true;
        await widget.auth.request(
          'POST',
          '/api/v1/evaluations/${evaluation!['id']}/publish',
          body: {
            'version': stored['version'],
            'shareWithGuardian': shareWithGuardian,
          },
          idempotencyKey: commandKey(),
        );
        stored = await verifyStored();
      }
      if (stored['status'] !=
          (wantsPublish
              ? (shareWithGuardian ? 'Published' : 'PublishedInternal')
              : 'Draft')) {
        throw const ApiFailure(
          0,
          'لم يتأكد النشر بعد. أعد التحقق من التقييم نفسه.',
        );
      }
      if (mounted && current) {
        setState(() {
          evaluation = stored;
          message = wantsPublish
              ? (shareWithGuardian
                    ? 'تم نشر التقييم لولي الأمر.'
                    : 'حُفظ التقييم للاستخدام الداخلي.')
              : 'تم حفظ المسودة.';
          pendingSave = null;
          writeIssued = false;
          publishIssued = false;
        });
      }
    } on ApiFailure catch (e) {
      if (mounted && current) setState(() => error = e.message);
    } catch (_) {
      if (mounted && current) {
        setState(() => error = 'تعذر تأكيد الحفظ. أعد التحقق من التقييم نفسه.');
      }
    } finally {
      if (mounted) setState(() => busy = false);
    }
  }

  Future<Json> verifyStored() async {
    final stored = obj(
      await widget.auth.request(
        'GET',
        '/api/v1/evaluations/${evaluation!['id']}',
      ),
    );
    final expected = rows(pendingSave!['scores']);
    final actual = rows(stored['scores']);
    if (stored['id'] != evaluation!['id'] ||
        stored['sportEnrollmentId'] != widget.enrollment['id'] ||
        stored['reportingPeriod'] != pendingSave!['reportingPeriod'] ||
        textOf(stored['generalNotes'] ?? '').trim() !=
            textOf(pendingSave!['generalNotes'] ?? '').trim() ||
        actual.length != expected.length ||
        expected.any(
          (e) => !actual.any(
            (a) =>
                a['criterionId'] == e['criterionId'] &&
                a['score'] == e['score'],
          ),
        )) {
      throw const ApiFailure(
        0,
        'لم تتأكد مطابقة الدرجات المحفوظة. راجع التقييم نفسه قبل إجراء تعديل آخر.',
      );
    }
    return stored;
  }

  Widget evaluationActions(BuildContext context) => DecoratedBox(
    decoration: BoxDecoration(
      color: Theme.of(context).scaffoldBackgroundColor,
      border: Border(
        top: BorderSide(color: Theme.of(context).colorScheme.outlineVariant),
      ),
    ),
    child: Padding(
      padding: const EdgeInsets.fromLTRB(16, 12, 16, 12),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          if (message != null)
            UiText(
              message!,
              style: TextStyle(color: Theme.of(context).colorScheme.primary),
            ),
          if (error != null) ErrorNotice(error!),
          if (pendingSave == null) ...[
            FilledButton(
              style: FilledButton.styleFrom(
                minimumSize: const Size.fromHeight(52),
              ),
              onPressed: busy ? null : () => save(publish: true),
              child: const UiText(
                'نشر التقييم لولي الأمر',
                style: TextStyle(fontSize: 15, fontWeight: FontWeight.w700),
              ),
            ),
            const SizedBox(height: 8),
            OutlinedButton(
              onPressed: busy ? null : () => save(publish: true, share: false),
              child: const UiText('نشر التقييم بدون ولي الأمر'),
            ),
          ],
          if (pendingSave != null)
            FilledButton(
              onPressed: busy
                  ? null
                  : () => save(publish: wantsPublish, share: shareWithGuardian),
              child: const UiText('إعادة التحقق من التقييم نفسه'),
            ),
        ],
      ),
    ),
  );

  @override
  Widget build(BuildContext context) => !current
      ? const SizedBox.shrink()
      : Theme(
          data: operationTheme(context),
          child: AppScaffold(
            title: 'تقييم اللاعب',
            bottomBar: evaluation == null || published
                ? null
                : evaluationActions(context),
            child: evaluation == null
                ? ListView(
                    padding: const EdgeInsets.all(20),
                    children: [
                      AppCard(
                        child: UiText(
                          '${widget.enrollment['player']}\n${widget.enrollment['group']}',
                        ),
                      ),
                      if (missingCriteria)
                        OperationSection(
                          title: 'معايير الرياضة لم تُجهّز بعد',
                          icon: Icons.tune,
                          children: [
                            UiText(
                              canSetupCriteria
                                  ? 'جهّز المعايير مرة واحدة، ثم ابدأ تقييم اللاعب.'
                                  : 'اطلب من إدارة الأكاديمية تجهيز المعايير لهذه الرياضة.',
                            ),
                            const SizedBox(height: 12),
                            FilledButton.icon(
                              onPressed: busy ? null : setupCriteria,
                              icon: const Icon(Icons.playlist_add_check),
                              label: UiText(
                                canSetupCriteria
                                    ? 'تجهيز معايير التقييم'
                                    : 'إعادة التحقق من المعايير',
                              ),
                            ),
                          ],
                        ),
                      if (busy && !criteriaDialogOpen)
                        const Center(child: CircularProgressIndicator()),
                      if (error != null) ErrorNotice(error!),
                      if (error != null && !missingCriteria)
                        FilledButton(
                          onPressed: busy || uncertainCreate || missingCriteria
                              ? null
                              : create,
                          child: UiText(
                            createdId == null
                                ? 'بدء تقييم جديد'
                                : 'إعادة التحقق من المسودة نفسها',
                          ),
                        ),
                      if (uncertainCreate)
                        const UiText(
                          'راجع قائمة التقييمات للتأكد من المسودة قبل إنشاء أخرى.',
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
                                  UiText(
                                    '${published ? 'تقييم منشور · ' : ''}${assessed.length} من ${scores.length} معايير مُقيّمة',
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
                                          ? Theme.of(context).colorScheme.error
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
                                : Theme.of(context).colorScheme.outlineVariant,
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
                        maxLines: 3,
                        decoration: InputDecoration(
                          labelText: ui(context, 'ملاحظات المدرب'),
                        ),
                      ),
                      if (published && message != null)
                        UiText(
                          message!,
                          style: TextStyle(
                            color: Theme.of(context).colorScheme.primary,
                          ),
                        ),
                      if (published && error != null) ErrorNotice(error!),
                    ],
                  ),
          ),
        );
}

class NativeEvaluationDrafts extends StatefulWidget {
  const NativeEvaluationDrafts({
    super.key,
    required this.auth,
    required this.enrollment,
  });
  final AuthController auth;
  final Json enrollment;
  @override
  State<NativeEvaluationDrafts> createState() => _NativeEvaluationDraftsState();
}

class _NativeEvaluationDraftsState extends State<NativeEvaluationDrafts> {
  int revision = 0;
  @override
  Widget build(BuildContext context) => AppScaffold(
    title: 'مسودات اللاعب',
    child: RemoteBody(
      key: ValueKey(revision),
      auth: widget.auth,
      path:
          '/api/v1/evaluations?${Uri(queryParameters: {'playerId': textOf(widget.enrollment['playerId']), 'status': 'Draft'}).query}',
      builder: (context, value) {
        final drafts = rows(value)
            .where(
              (e) =>
                  e['sportEnrollmentId'] == widget.enrollment['id'] &&
                  e['status'] == 'Draft',
            )
            .toList();
        return gList([
          if (drafts.isEmpty)
            const EmptyMessage('لا توجد مسودات لهذا الاشتراك الرياضي.'),
          for (final draft in drafts)
            GCard(
              child: ListTile(
                key: ValueKey('evaluation-draft-${draft['id']}'),
                title: UiText(
                  '${day(draft['evaluationDate'])} · ${draft['sport']}',
                ),
                subtitle: UiText(
                  '${draft['group']} · ${draft['scoredCriteria']} معايير مُقيّمة',
                ),
                trailing: const Icon(Icons.chevron_right),
                onTap: () async {
                  await openPage(
                    context,
                    CoachEvaluation(
                      auth: widget.auth,
                      enrollment: widget.enrollment,
                      evaluationId: textOf(draft['id']),
                    ),
                  );
                  if (mounted) setState(() => revision++);
                },
              ),
            ),
        ]);
      },
    ),
  );
}
