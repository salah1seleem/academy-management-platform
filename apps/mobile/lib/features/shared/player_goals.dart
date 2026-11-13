import '../../core/widgets/guided_form.dart';

import 'dart:convert';

import 'package:flutter/material.dart';

import '../../core/api/api_client.dart';
import '../../core/auth/auth_controller.dart';
import '../../core/theme/appearance.dart';
import '../../core/theme/ui_text.dart';
import '../guardian/guardian_ui.dart';
import 'player_monthly.dart';

String goalCommandId() {
  final h = commandKey().substring(0, 32);
  return '${h.substring(0, 8)}-${h.substring(8, 12)}-${h.substring(12, 16)}-${h.substring(16, 20)}-${h.substring(20)}';
}

String goalState(Object? state) =>
    const {
      'Draft': 'مسودة خاصة بفريق التدريب',
      'Published': 'قيد العمل · منشور للأسرة',
      'Achieved': 'تحقق الهدف بمراجعة فنية',
      'Withdrawn': 'هدف مسحوب',
    }[state] ??
    'غير محدد';
String goalAction(Object? action) =>
    const {
      'Created': 'إنشاء مسودة',
      'Edited': 'تعديل المسودة',
      'Published': 'نشر للأسرة',
      'Reviewed': 'مراجعة التقدم',
      'Achieved': 'تحقق الهدف',
      'Withdrawn': 'سحب الهدف',
    }[action] ??
    'تحديث';

class PlayerGoalsPage extends StatefulWidget {
  final AuthController auth;
  final String? enrollmentId, playerId, id;
  final bool create;
  const PlayerGoalsPage({
    super.key,
    required this.auth,
    this.enrollmentId,
    this.playerId,
    this.id,
    this.create = false,
  });
  @override
  State<PlayerGoalsPage> createState() => _PlayerGoalsPageState();
}

class _PlayerGoalsPageState extends State<PlayerGoalsPage>
    with WidgetsBindingObserver {
  String? get identity => widget.auth.session?.selected == null
      ? null
      : '${widget.auth.session!.selected!.academyId}:${widget.auth.session!.selected!.id}:${widget.auth.session!.selected!.contextRevision}';
  late final originalIdentity = identity;
  final title = TextEditingController(),
      target = TextEditingController(),
      note = TextEditingController();
  final form = GlobalKey<FormState>();
  String? reviewOn, loaded, commandBody, requestId;
  String message = '', action = 'Reviewed';
  bool busy = false, foreground = true, conflict = false;
  int revision = 0;
  DialogRoute<bool>? publicationPreview;
  static const root = '/api/v1/evaluations/development-goals';
  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    title.dispose();
    target.dispose();
    note.dispose();
    super.dispose();
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    final preview = publicationPreview;
    if (state != AppLifecycleState.resumed &&
        preview != null &&
        preview.isActive) {
      preview.navigator?.removeRoute(preview, false);
    }
    setState(() {
      foreground = state == AppLifecycleState.resumed;
      revision++;
    });
  }

  Future<void> navigate(PlayerGoalsPage page) async {
    await openPage(context, page);
    if (mounted && identity == originalIdentity) {
      setState(() => revision++);
    }
  }

  Future<void> save(Json body, {bool transition = false}) async {
    if (busy ||
        conflict ||
        !foreground ||
        identity == null ||
        identity != originalIdentity) {
      return;
    }
    final encoded = jsonEncode(body);
    if (commandBody != encoded) {
      commandBody = encoded;
      requestId = goalCommandId();
    }
    setState(() {
      busy = true;
      message = '';
    });
    try {
      final value = obj(
        await widget.auth.request(
          widget.id != null && !transition ? 'PUT' : 'POST',
          widget.id == null
              ? '$root/enrollments/${widget.enrollmentId}'
              : '$root/${widget.id}${transition ? '/transition' : ''}',
          body: {...body, 'requestId': requestId},
        ),
      );
      if (!mounted || identity != originalIdentity) {
        return;
      }
      commandBody = null;
      requestId = null;
      if (widget.id == null) {
        Navigator.of(context).pushReplacement(
          MaterialPageRoute<void>(
            builder: (_) =>
                PlayerGoalsPage(auth: widget.auth, id: textOf(value['id'])),
          ),
        );
      } else {
        setState(() {
          revision++;
          note.clear();
          action = 'Reviewed';
          loaded = null;
          message = 'تم حفظ تحديث الهدف وسجل المراجعة.';
        });
      }
    } catch (e) {
      if (mounted && identity == originalIdentity) {
        setState(() {
          conflict = e is ApiFailure && e.status == 409;
          message = e is ApiFailure ? e.message : 'تعذر تأكيد الحفظ.';
        });
      }
    } finally {
      if (mounted) {
        setState(() => busy = false);
      }
    }
  }

  Future<void> publish(Json goal) async {
    if (publicationPreview != null) return;
    final navigator = Navigator.of(context, rootNavigator: true);
    final openedRevision = revision;
    final route = DialogRoute<bool>(
      context: context,
      themes: InheritedTheme.capture(from: context, to: navigator.context),
      builder: (context) => ListenableBuilder(
        listenable: widget.auth,
        builder: (context, _) =>
            identity == null || identity != originalIdentity
            ? const SizedBox.shrink()
            : AlertDialog(
                insetPadding: const EdgeInsets.symmetric(
                  horizontal: 20,
                  vertical: 24,
                ),
                title: UiText(
                  'نشر الهدف للأسرة؟',
                  style: Theme.of(context).textTheme.titleLarge,
                ),
                content: SingleChildScrollView(
                  child: Column(
                    mainAxisSize: MainAxisSize.min,
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      const UiText(
                        'راجع النص وتأكد من خلوه من البيانات الطبية أو بيانات لاعب آخر.',
                      ),
                      const SizedBox(height: 16),
                      UiText(
                        textOf(goal['title']),
                        style: Theme.of(context).textTheme.titleMedium,
                      ),
                      const SizedBox(height: 6),
                      UiText(textOf(goal['target'])),
                      UiText(
                        "${localized(context, 'موعد المراجعة', 'Review on')}: ${day(goal['reviewOn'])}",
                      ),
                    ],
                  ),
                ),
                actions: [
                  TextButton(
                    onPressed: () => Navigator.pop(context, false),
                    child: const UiText('إلغاء'),
                  ),
                  FilledButton(
                    onPressed: () => Navigator.pop(context, true),
                    child: const UiText('نشر الهدف للأسرة'),
                  ),
                ],
              ),
      ),
    );
    publicationPreview = route;
    final confirmed = await navigator.push(route);
    publicationPreview = null;
    if (confirmed == true && mounted && revision == openedRevision) {
      await save({
        'action': 'Published',
        'note': null,
        'version': goal['version'],
      }, transition: true);
    }
  }

  @override
  Widget build(BuildContext context) => ListenableBuilder(
    listenable: widget.auth,
    builder: (context, _) {
      if (!foreground || identity == null || identity != originalIdentity) {
        return const SizedBox.shrink();
      }
      return GuardianPage(
        title: localized(
          context,
          'أهداف تطور اللاعب',
          'Player development goals',
        ),
        child: RemoteBody(
          key: ValueKey(revision),
          auth: widget.auth,
          path: widget.playerId != null
              ? '/api/v1/guardian/children/${widget.playerId}/development-goals'
              : widget.id != null
              ? '$root/${widget.id}'
              : '$root/enrollments/${widget.enrollmentId}',
          builder: (context, value) {
            final data = obj(value),
                goal = obj(data['item'] ?? <String, dynamic>{}),
                enrollment = obj(data['enrollment'] ?? <String, dynamic>{});
            final canEdit = widget.create
                ? data['canCreate'] == true
                : data['canEdit'] == true;
            final loadedVersion =
                '${widget.id ?? widget.enrollmentId}:${goal['version']}';
            if (loaded != loadedVersion) {
              if (loaded != null) {
                note.clear();
                action = 'Reviewed';
              }
              loaded = loadedVersion;
              title.text = goal['title'] as String? ?? '';
              target.text = goal['target'] as String? ?? '';
              reviewOn =
                  goal['reviewOn'] as String? ?? data['asOfDate'] as String?;
            }
            final savedDate = textOf(goal['reviewOn']);
            final today = textOf(data['asOfDate']);
            final expired =
                goal['state'] == 'Draft' && savedDate.compareTo(today) < 0;
            final dirty =
                goal['state'] == 'Draft' &&
                (title.text != goal['title'] ||
                    target.text != goal['target'] ||
                    reviewOn != savedDate);
            final locked = busy || conflict;
            final family = widget.playerId != null;
            final items = rows(data['items'] ?? <Json>[])
                .where(
                  (row) =>
                      !family ||
                      const [
                        'Published',
                        'Achieved',
                      ].contains(obj(row['goal'] ?? row)['state']),
                )
                .toList();
            return gList([
              if (message.isNotEmpty)
                Semantics(liveRegion: true, child: UiText(message)),
              if (conflict)
                OutlinedButton(
                  onPressed: () => setState(() {
                    conflict = false;
                    message = '';
                    revision++;
                  }),
                  child: const UiText('قراءة أحدث نسخة'),
                ),
              if (widget.id == null && !widget.create) ...[
                if (!family)
                  Heading('${enrollment['player']} · ${enrollment['sport']}'),
                if (widget.playerId == null && data['canCreate'] == true)
                  FilledButton.icon(
                    icon: const Icon(Icons.flag_outlined),
                    label: const UiText('هدف جديد'),
                    onPressed: () => navigate(
                      PlayerGoalsPage(
                        auth: widget.auth,
                        enrollmentId: widget.enrollmentId,
                        create: true,
                      ),
                    ),
                  ),
                if (items.isEmpty)
                  EmptyMessage(
                    family
                        ? 'لم ينشر فريق التدريب أهدافًا لهذا اللاعب بعد.'
                        : 'لا توجد أهداف مسجلة لهذا الاشتراك الرياضي.',
                  ),
                for (final row in items)
                  _goalCard(
                    obj(row['goal'] ?? row),
                    sport: row['sport'],
                    reviews: rows(row['reviews'] ?? <Json>[])
                        .where(
                          (review) => const [
                            'Published',
                            'Reviewed',
                            'Achieved',
                          ].contains(review['action']),
                        )
                        .toList(),
                    reviewsHasMore: row['reviewsHasMore'] == true,
                    open: widget.playerId == null,
                  ),
                if (data['hasMore'] == true) const UiText('تظهر أحدث100 هدف.'),
                GLink(
                  'تقرير التطور الشهري',
                  Icons.auto_graph_rounded,
                  () => openPage(
                    context,
                    PlayerMonthlyPage(
                      auth: widget.auth,
                      playerId: widget.playerId,
                      enrollmentId: widget.enrollmentId,
                    ),
                  ),
                ),
              ],
              if (widget.id != null || widget.create)
                Heading('${enrollment['player']} · ${enrollment['sport']}'),
              if (widget.id != null && (goal['state'] != 'Draft' || !canEdit))
                _goalCard(goal),
              if (goal['state'] == 'Draft' && canEdit)
                UiText(goalState('Draft')),
              if (widget.create && !canEdit)
                const EmptyMessage('إضافة هدف تحتاج صلاحية على هذا الاشتراك.'),
              if (canEdit && (widget.create || goal['state'] == 'Draft'))
                GuidedForm(
                  key: form,
                  child: GCard(
                    child: Column(
                      children: [
                        TextFormField(
                          controller: title,
                          enabled: !locked,
                          onChanged: (_) => setState(() {}),
                          maxLength: 100,
                          decoration: const InputDecoration(
                            label: UiText('عنوان الهدف'),
                          ),
                          validator: (v) => v == null || v.trim().isEmpty
                              ? 'أدخل عنوان الهدف.'
                              : null,
                        ),
                        const SizedBox(height: 12),
                        TextFormField(
                          controller: target,
                          enabled: !locked,
                          onChanged: (_) => setState(() {}),
                          maxLength: 500,
                          minLines: 2,
                          maxLines: 4,
                          decoration: const InputDecoration(
                            label: UiText('ما الذي نعمل عليه؟'),
                          ),
                          validator: (v) => v == null || v.trim().isEmpty
                              ? 'وضح خطوة التطور المطلوبة.'
                              : null,
                        ),
                        OutlinedButton.icon(
                          icon: const Icon(Icons.calendar_month_outlined),
                          label: UiText(
                            '${localized(context, 'موعد المراجعة', 'Review on')}: ${day(reviewOn)}',
                          ),
                          onPressed: locked
                              ? null
                              : () async {
                                  final first = DateTime.parse(
                                    textOf(data['asOfDate']),
                                  );
                                  final nextYear = first.year + 1;
                                  final lastDay = DateTime(
                                    nextYear,
                                    first.month + 1,
                                    0,
                                  ).day;
                                  final last = DateTime(
                                    nextYear,
                                    first.month,
                                    first.day.clamp(1, lastDay),
                                  );
                                  final selected = DateTime.tryParse(
                                    '$reviewOn',
                                  );
                                  final initial =
                                      selected == null ||
                                          selected.isBefore(first) ||
                                          selected.isAfter(last)
                                      ? first
                                      : selected;
                                  final d = await showDatePicker(
                                    context: context,
                                    firstDate: first,
                                    lastDate: last,
                                    initialDate: initial,
                                  );
                                  if (d != null && mounted) {
                                    setState(
                                      () => reviewOn = d
                                          .toIso8601String()
                                          .substring(0, 10),
                                    );
                                  }
                                },
                        ),
                        if (expired)
                          const UiText(
                            'موعد المراجعة المحفوظ فات. اختر موعدًا جديدًا واحفظه قبل النشر.',
                          ),
                        if (dirty)
                          const UiText(
                            'احفظ التعديلات لمراجعة النص قبل النشر.',
                          ),
                        const SizedBox(height: 12),
                        FilledButton(
                          onPressed: locked || (!widget.create && !dirty)
                              ? null
                              : () {
                                  if (form.currentState!.validate()) {
                                    save({
                                      'title': title.text.trim(),
                                      'target': target.text.trim(),
                                      'reviewOn': reviewOn,
                                      'version': goal['version'] ?? 0,
                                    });
                                  }
                                },
                          child: const UiText('حفظ المسودة'),
                        ),
                      ],
                    ),
                  ),
                ),
              if (canEdit && goal['state'] == 'Draft')
                FilledButton(
                  onPressed: locked || dirty || expired
                      ? null
                      : () => publish(goal),
                  child: const UiText('مراجعة النشر'),
                ),
              if (canEdit && goal['state'] == 'Published')
                GCard(
                  child: Column(
                    children: [
                      DropdownButtonFormField<String>(
                        initialValue: action,
                        isExpanded: true,
                        decoration: const InputDecoration(
                          label: UiText('نتيجة المراجعة'),
                        ),
                        items: const [
                          DropdownMenuItem(
                            value: 'Reviewed',
                            child: UiText('ما زال قيد العمل'),
                          ),
                          DropdownMenuItem(
                            value: 'Achieved',
                            child: UiText('تحقق الهدف بمراجعة فنية'),
                          ),
                        ],
                        onChanged: locked
                            ? null
                            : (v) => setState(() => action = v ?? 'Reviewed'),
                      ),
                      const SizedBox(height: 12),
                      TextField(
                        controller: note,
                        enabled: !locked,
                        maxLength: 500,
                        minLines: 2,
                        maxLines: 4,
                        decoration: const InputDecoration(
                          label: UiText('ملاحظة التقدم للأسرة (اختياري)'),
                        ),
                      ),
                      const UiText(
                        'تظهر نتيجة المراجعة للأسرة. اعتماد تحقق الهدف لا يغيّر المستوى أو المجموعة أو الاشتراك.',
                      ),
                      const SizedBox(height: 12),
                      FilledButton(
                        onPressed: locked
                            ? null
                            : () => save({
                                'action': action,
                                'note': note.text.trim(),
                                'version': goal['version'],
                              }, transition: true),
                        child: const UiText('حفظ المراجعة'),
                      ),
                    ],
                  ),
                ),
              if (widget.id != null &&
                  rows(data['history'] ?? <Json>[]).isNotEmpty) ...[
                const Heading('سجل فني محفوظ'),
                for (final c in rows(data['history'] ?? <Json>[]))
                  GCard(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        UiText(
                          '${ui(context, goalAction(c['action']))} · ${c['actorName']}',
                        ),
                        UiText(
                          '${day(c['createdAtUtc'])} · ${localized(context, 'نسخة', 'version ')}${c['revision']}',
                        ),
                        if (c['note'] is String) UiText(textOf(c['note'])),
                      ],
                    ),
                  ),
                if (data['historyHasMore'] == true)
                  const UiText('تظهر أحدث100 مراجعة.'),
              ],
              if (canEdit && ['Draft', 'Published'].contains(goal['state']))
                TextButton(
                  style: TextButton.styleFrom(
                    foregroundColor: Theme.of(context).colorScheme.error,
                  ),
                  onPressed: locked
                      ? null
                      : () async {
                          final openedRevision = revision;
                          final confirmed = await showDialog<bool>(
                            context: context,
                            builder: (context) => AlertDialog(
                              title: const UiText('سحب الهدف؟'),
                              content: const UiText(
                                'يُخفى الهدف عن الأسرة ويظل سجله متاحًا لفريق التدريب.',
                              ),
                              actions: [
                                TextButton(
                                  onPressed: () =>
                                      Navigator.pop(context, false),
                                  child: const UiText('إلغاء'),
                                ),
                                TextButton(
                                  onPressed: () => Navigator.pop(context, true),
                                  child: const UiText('سحب الهدف'),
                                ),
                              ],
                            ),
                          );
                          if (confirmed == true &&
                              mounted &&
                              revision == openedRevision) {
                            save({
                              'action': 'Withdrawn',
                              'note': null,
                              'version': goal['version'],
                            }, transition: true);
                          }
                        },
                  child: const UiText('سحب الهدف مع حفظ السجل'),
                ),
            ]);
          },
        ),
      );
    },
  );
  Widget _goalCard(
    Json goal, {
    Object? sport,
    List<Json> reviews = const [],
    bool reviewsHasMore = false,
    bool open = false,
  }) => GCard(
    child: Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        if (sport is String) UiText(sport),
        Row(
          children: [
            Icon(
              goal['state'] == 'Achieved'
                  ? Icons.check_circle_outline
                  : Icons.flag_outlined,
              color: Theme.of(context).colorScheme.primary,
            ),
            const SizedBox(width: 10),
            Expanded(child: Heading(textOf(goal['title']))),
          ],
        ),
        UiText(
          widget.playerId == null
              ? goalState(goal['state'])
              : goal['state'] == 'Achieved'
              ? 'تحقق الهدف'
              : 'قيد العمل',
          style: Theme.of(context).textTheme.labelLarge,
        ),
        const SizedBox(height: 12),
        UiText(textOf(goal['target'])),
        UiText(
          '${localized(context, 'موعد المراجعة', 'Review on')}: ${day(goal['reviewOn'])}',
        ),
        if (reviews.isNotEmpty) ...[
          const SizedBox(height: 12),
          UiText(
            '${localized(context, 'آخر تحديث', 'Latest update')} · ${reviews.first['actorName']} · ${day(reviews.first['createdAtUtc'])}',
            style: Theme.of(context).textTheme.labelMedium,
          ),
          UiText(
            reviews.first['note'] is String
                ? textOf(reviews.first['note'])
                : goalAction(reviews.first['action']),
          ),
          if (reviews.length > 1)
            ExpansionTile(
              tilePadding: EdgeInsets.zero,
              title: const UiText('سجل المراجعات المنشورة'),
              children: [
                for (final r in reviews.skip(1))
                  ListTile(
                    contentPadding: EdgeInsets.zero,
                    title: UiText(
                      '${ui(context, goalAction(r['action']))} · ${r['actorName']}',
                    ),
                    subtitle: UiText(
                      '${day(r['createdAtUtc'])}${r['note'] is String ? ' · ${r['note']}' : ''}',
                    ),
                  ),
                if (reviewsHasMore)
                  const UiText(
                    'تظهر أحدث20 مراجعة منشورة؛ يمكن طلب سجل أقدم من الإدارة.',
                  ),
              ],
            ),
        ],
        if (open)
          TextButton(
            onPressed: () => navigate(
              PlayerGoalsPage(auth: widget.auth, id: textOf(goal['id'])),
            ),
            child: const UiText('فتح الهدف وسجل المراجعة'),
          ),
      ],
    ),
  );
}
