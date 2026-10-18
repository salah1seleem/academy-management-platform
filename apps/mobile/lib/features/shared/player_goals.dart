import 'dart:convert';

import 'package:flutter/material.dart';

import '../../core/api/api_client.dart';
import '../../core/auth/auth_controller.dart';
import '../../core/theme/appearance.dart';
import '../../core/theme/ui_text.dart';
import '../guardian/guardian_ui.dart';

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
  bool busy = false, foreground = true;
  int revision = 0;
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
    setState(() {
      foreground = state == AppLifecycleState.resumed;
      revision++;
      loaded = null;
      title.clear();
      target.clear();
      note.clear();
      reviewOn = null;
    });
  }

  Future<void> navigate(PlayerGoalsPage page) async {
    await openPage(context, page);
    if (mounted && identity == originalIdentity) {
      setState(() => revision++);
    }
  }

  Future<void> save(Json body, {bool transition = false}) async {
    if (busy || identity == null || identity != originalIdentity) {
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
          loaded = null;
          message = 'تم حفظ تحديث الهدف وسجل المراجعة.';
        });
      }
    } catch (e) {
      if (mounted && identity == originalIdentity) {
        setState(
          () => message = e is ApiFailure
              ? e.message
              : 'تعذر الحفظ. لم نسجل نجاحًا.',
        );
      }
    } finally {
      if (mounted) {
        setState(() => busy = false);
      }
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
            if (loaded != '${goal['id']}:${goal['revision']}') {
              loaded = '${goal['id']}:${goal['revision']}';
              title.text = goal['title'] as String? ?? '';
              target.text = goal['target'] as String? ?? '';
              reviewOn =
                  goal['reviewOn'] as String? ?? data['asOfDate'] as String?;
            }
            return gList([
              const UiText(
                'هدف فني نراجعه معًا، وليس ضمانًا زمنيًا أو ترقية تلقائية. لا يغيّر المجموعة أو الاشتراك.',
              ),
              if (message.isNotEmpty) UiText(message),
              if (widget.id == null && !widget.create) ...[
                if (widget.playerId == null)
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
                if (rows(data['items']).isEmpty)
                  const EmptyMessage(
                    'لا توجد أهداف منشورة بعد. يمكنك متابعة التقييمات الحالية من ملف اللاعب.',
                  ),
                for (final row in rows(data['items']))
                  _goalCard(
                    obj(row['goal'] ?? row),
                    sport: row['sport'],
                    reviews: rows(row['reviews'] ?? <Json>[]),
                    reviewsHasMore: row['reviewsHasMore'] == true,
                    open: widget.playerId == null,
                  ),
                if (data['hasMore'] == true) const UiText('تظهر أحدث100 هدف.'),
              ],
              if (widget.id != null) ...[
                Heading('${enrollment['player']} · ${enrollment['sport']}'),
                _goalCard(goal),
              ],
              if (canEdit && (widget.create || goal['state'] == 'Draft'))
                Form(
                  key: form,
                  child: GCard(
                    child: Column(
                      children: [
                        TextFormField(
                          controller: title,
                          enabled: !busy,
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
                          enabled: !busy,
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
                          onPressed: busy
                              ? null
                              : () async {
                                  final first = DateTime.parse(
                                    textOf(data['asOfDate']),
                                  );
                                  final d = await showDatePicker(
                                    context: context,
                                    firstDate: first,
                                    lastDate: first.add(
                                      const Duration(days: 365),
                                    ),
                                    initialDate:
                                        DateTime.tryParse('$reviewOn')
                                                ?.isBefore(first) ==
                                            false
                                        ? DateTime.parse(reviewOn!)
                                        : first,
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
                        const UiText(
                          'المسودة خاصة بفريق التدريب. انشر الهدف للأسرة بعد مراجعته؛ بلا معلومات طبية أو طفل آخر.',
                        ),
                        FilledButton(
                          onPressed: busy
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
                  onPressed: busy
                      ? null
                      : () => save({
                          'action': 'Published',
                          'note': null,
                          'version': goal['version'],
                        }, transition: true),
                  child: const UiText('نشر الهدف للأسرة'),
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
                        onChanged: busy
                            ? null
                            : (v) => setState(() => action = v ?? 'Reviewed'),
                      ),
                      const SizedBox(height: 12),
                      TextField(
                        controller: note,
                        enabled: !busy,
                        maxLength: 500,
                        minLines: 2,
                        maxLines: 4,
                        decoration: const InputDecoration(
                          label: UiText('ملاحظة التقدم للأسرة (اختياري)'),
                        ),
                      ),
                      FilledButton(
                        onPressed: busy
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
              if (canEdit && ['Draft', 'Published'].contains(goal['state']))
                OutlinedButton(
                  onPressed: busy
                      ? null
                      : () async {
                          final confirmed = await showDialog<bool>(
                            context: context,
                            builder: (context) => AlertDialog(
                              title: const UiText('سحب الهدف؟'),
                              content: const UiText(
                                'يختفي الهدف عن الأسرة ويظل سجله محفوظًا. لا تتغير المجموعة أو المستوى.',
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
                          if (confirmed == true && mounted) {
                            save({
                              'action': 'Withdrawn',
                              'note': null,
                              'version': goal['version'],
                            }, transition: true);
                          }
                        },
                  child: const UiText('سحب الهدف مع حفظ السجل'),
                ),
              if (widget.id != null) ...[
                const Heading('سجل فني محفوظ'),
                for (final c in rows(data['history']))
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
          goalState(goal['state']),
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
          ExpansionTile(
            tilePadding: EdgeInsets.zero,
            title: const UiText('سجل المراجعات المنشورة'),
            children: [
              for (final r in reviews)
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
