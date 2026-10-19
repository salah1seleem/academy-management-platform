import 'package:flutter/material.dart';

import '../shared/feedback_state.dart';
import '../shared/feedback_incident.dart';

import '../../core/api/api_client.dart';
import '../../core/auth/auth_controller.dart';
import '../../core/theme/ui_text.dart';
import 'guardian_ui.dart';

class CoachFeedbackPage extends StatefulWidget {
  final AuthController auth;
  final String playerId;
  const CoachFeedbackPage({
    super.key,
    required this.auth,
    required this.playerId,
  });
  @override
  State<CoachFeedbackPage> createState() => _CoachFeedbackPageState();
}

class _CoachFeedbackPageState extends State<CoachFeedbackPage>
    with WidgetsBindingObserver {
  String? get current => widget.auth.session?.selected == null
      ? null
      : '${widget.auth.session!.selected!.id}:${widget.auth.session!.selected!.contextRevision}';
  late final identity = current;
  bool foreground = true;
  final form = GlobalKey<FormState>();
  final message = TextEditingController();
  String? groupId, coachId;
  String kind = 'Rating', result = '', requestKey = commandKey();
  String subject = 'Other', urgency = 'Routine';
  String? incidentOn;
  Json? incidentSession;
  void changeIncident(Json? s) => setState(() {
    if (s != null) {
      groupId = textOf(s['groupId']);
    } else if (incidentSession != null) {
      groupId = null;
    }
    incidentSession = s;
    coachId = null;
    requestKey = commandKey();
  });
  int score = 5, revision = 0;
  bool busy = false;
  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    setState(() {
      foreground = state == AppLifecycleState.resumed;
      revision++;
      message.clear();
      result = '';
      incidentSession = null;
      coachId = null;
      groupId = null;
      requestKey = commandKey();
    });
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    message.dispose();
    super.dispose();
  }

  Future<void> submit() async {
    if (busy || !form.currentState!.validate()) return;
    setState(() {
      busy = true;
      result = '';
    });
    try {
      await widget.auth.request(
        'POST',
        '/api/v1/guardian/children/${widget.playerId}/feedback',
        body: {
          'trainingGroupId': groupId,
          'coachMembershipId': coachId,
          'kind': kind,
          'score': kind == 'Rating' ? score : null,
          'message': message.text.trim(),
          if (kind != 'Rating') ...{
            'subject': subject,
            'urgency': urgency,
            'incidentOn': incidentOn,
            'incidentSessionId': incidentSession?['id'],
          },
        },
        idempotencyKey: requestKey,
      );
      if (mounted) {
        setState(() {
          result = kind != 'Rating' && urgency == 'Safety'
              ? 'وصل بلاغ السلامة إلى قائمة المراجعة البشرية للمالك. هذا ليس تأكيد استجابة فورية.'
              : 'وصلت ملاحظتك لإدارة الأكاديمية.';
          requestKey = commandKey();
          message.clear();
          revision++;
        });
      }
    } catch (error) {
      if (mounted) {
        setState(
          () => result = error is ApiFailure
              ? error.message
              : 'تعذر إرسال الملاحظة. حاول مرة أخرى.',
        );
      }
    } finally {
      if (mounted) setState(() => busy = false);
    }
  }

  @override
  Widget build(BuildContext context) => ListenableBuilder(
    listenable: widget.auth,
    builder: (context, _) {
      if (!foreground || identity == null || identity != current) {
        return const SizedBox.shrink();
      }
      return GuardianPage(
        title: 'رأيك في التدريب',
        child: RemoteBody(
          key: ValueKey(revision),
          auth: widget.auth,
          path: '/api/v1/guardian/children/${widget.playerId}/feedback-options',
          builder: (context, value) {
            final data = obj(value), groups = [...rows(data['groups'])];
            if (incidentSession != null &&
                !groups.any((g) => g['id'] == incidentSession!['groupId'])) {
              groups.add({
                'id': incidentSession!['groupId'],
                'name': incidentSession!['groupName'],
                'sport': 'وقت الواقعة',
              });
            }
            final selected = groups
                .where((g) => textOf(g['id']) == groupId)
                .toList();
            final coaches = incidentSession != null
                ? rows(incidentSession!['coaches'])
                : selected.isEmpty
                ? <Json>[]
                : rows(selected.first['coaches']);
            return gList([
              if (result.isNotEmpty) UiText(result),
              const UiText(
                'تقييم المدرب مرة واحدة شهريًا لكل طفل. الشكاوى والاقتراحات تصل للمالك فقط ولا تظهر للمدرب.',
              ),
              if (groups.isEmpty)
                const EmptyMessage('لا توجد مجموعة نشطة لهذا الطفل حاليًا.'),
              GCard(
                child: Form(
                  key: form,
                  child: Column(
                    children: [
                      DropdownButtonFormField<String>(
                        key: ValueKey(
                          'group/$groupId/${incidentSession?['id']}',
                        ),
                        initialValue: groupId,
                        isExpanded: true,
                        decoration: const InputDecoration(
                          labelText: 'المجموعة',
                        ),
                        items: [
                          for (final group in groups)
                            DropdownMenuItem(
                              value: textOf(group['id']),
                              child: UiText(
                                '${group['name']} · ${group['sport']}',
                              ),
                            ),
                        ],
                        onChanged: busy || incidentSession != null
                            ? null
                            : (value) => setState(() {
                                groupId = value;
                                coachId = null;
                                requestKey = commandKey();
                              }),
                        validator: (value) =>
                            value == null ? 'اختر مجموعة الطفل.' : null,
                      ),
                      const SizedBox(height: 12),
                      DropdownButtonFormField<String>(
                        initialValue: kind,
                        decoration: const InputDecoration(
                          labelText: 'نوع الملاحظة',
                        ),
                        items: const [
                          DropdownMenuItem(
                            value: 'Rating',
                            child: UiText('تقييم المدرب'),
                          ),
                          DropdownMenuItem(
                            value: 'Complaint',
                            child: UiText('شكوى'),
                          ),
                          DropdownMenuItem(
                            value: 'Suggestion',
                            child: UiText('اقتراح'),
                          ),
                        ],
                        onChanged: busy
                            ? null
                            : (value) => setState(() {
                                kind = value ?? 'Rating';
                                if (incidentSession != null) groupId = null;
                                incidentSession = null;
                                coachId = null;
                                requestKey = commandKey();
                              }),
                      ),
                      const SizedBox(height: 12),
                      DropdownButtonFormField<String>(
                        key: ValueKey(
                          '$groupId/$kind/${incidentSession?['id']}',
                        ),
                        initialValue: coachId,
                        isExpanded: true,
                        decoration: InputDecoration(
                          labelText: kind == 'Rating'
                              ? 'المدرب'
                              : 'المدرب المعني (اختياري)',
                        ),
                        items: [
                          if (kind != 'Rating')
                            const DropdownMenuItem(
                              value: '',
                              child: UiText('ملاحظة عامة'),
                            ),
                          for (final coach in coaches)
                            DropdownMenuItem(
                              value: textOf(coach['id']),
                              child: UiText(textOf(coach['name'])),
                            ),
                        ],
                        onChanged: busy
                            ? null
                            : (value) => setState(() {
                                coachId = value == '' ? null : value;
                                requestKey = commandKey();
                              }),
                        validator: (value) =>
                            kind == 'Rating' && (value == null || value.isEmpty)
                            ? 'اختر المدرب.'
                            : null,
                      ),
                      if (kind == 'Rating') ...[
                        const SizedBox(height: 12),
                        DropdownButtonFormField<int>(
                          initialValue: score,
                          decoration: const InputDecoration(
                            labelText: 'تقييم ولي الأمر',
                          ),
                          items: [
                            for (var i = 1; i <= 5; i++)
                              DropdownMenuItem(
                                value: i,
                                child: UiText('$i من 5'),
                              ),
                          ],
                          onChanged: busy
                              ? null
                              : (value) => setState(() {
                                  score = value ?? 5;
                                  requestKey = commandKey();
                                }),
                        ),
                      ],
                      if (kind != 'Rating') ...[
                        const SizedBox(height: 12),
                        DropdownButtonFormField<String>(
                          initialValue: subject,
                          isExpanded: true,
                          decoration: const InputDecoration(
                            labelText: 'موضوع الملاحظة',
                          ),
                          items: feedbackSubjects.entries
                              .map(
                                (e) => DropdownMenuItem(
                                  value: e.key,
                                  child: UiText(e.value),
                                ),
                              )
                              .toList(),
                          onChanged: busy
                              ? null
                              : (v) => setState(() {
                                  subject = v ?? 'Other';
                                  requestKey = commandKey();
                                }),
                        ),
                        const SizedBox(height: 12),
                        DropdownButtonFormField<String>(
                          initialValue: urgency,
                          isExpanded: true,
                          decoration: const InputDecoration(
                            labelText: 'درجة الاستعجال',
                          ),
                          items: feedbackUrgencies.entries
                              .map(
                                (e) => DropdownMenuItem(
                                  value: e.key,
                                  child: UiText(e.value),
                                ),
                              )
                              .toList(),
                          onChanged: busy
                              ? null
                              : (v) => setState(() {
                                  urgency = v ?? 'Routine';
                                  requestKey = commandKey();
                                }),
                        ),
                        TextButton(
                          onPressed: busy
                              ? null
                              : () async {
                                  final today =
                                      DateTime.tryParse(
                                        data['asOfDate'] as String? ?? '',
                                      ) ??
                                      DateTime.now();
                                  final value = await showDatePicker(
                                    context: context,
                                    initialDate:
                                        DateTime.tryParse(incidentOn ?? '') ??
                                        today,
                                    firstDate: DateTime(2000),
                                    lastDate: today,
                                  );
                                  if (value != null && mounted) {
                                    changeIncident(null);
                                    setState(() {
                                      incidentOn = value
                                          .toIso8601String()
                                          .split('T')
                                          .first;
                                      requestKey = commandKey();
                                    });
                                  }
                                },
                          child: UiText(
                            'تاريخ الواقعة (اختياري): ${incidentOn ?? 'غير محدد'}',
                          ),
                        ),
                        if (incidentOn != null)
                          TextButton(
                            onPressed: busy
                                ? null
                                : () => setState(() {
                                    incidentOn = null;
                                    if (incidentSession != null) {
                                      groupId = null;
                                    }
                                    incidentSession = null;
                                    coachId = null;
                                    requestKey = commandKey();
                                  }),
                            child: const UiText('مسح تاريخ الواقعة'),
                          ),
                        if (incidentOn != null)
                          IgnorePointer(
                            ignoring: busy,
                            child: FeedbackIncidentPicker(
                              key: ValueKey(incidentOn),
                              auth: widget.auth,
                              playerId: widget.playerId,
                              day: incidentOn!,
                              selected: incidentSession,
                              changed: changeIncident,
                            ),
                          ),
                        const UiText(
                          'قائمة المدربين للإسناد الحالي، وليست إثباتًا لمدرب واقعة قديمة. يمكنك اختيار ملاحظة عامة لتراجع الإدارة الواقعة.',
                        ),
                        if (urgency == 'Safety')
                          const UiText(
                            'بلاغ السلامة يحتاج مراجعة بشرية عاجلة. هذه الخدمة ليست طوارئ أو مراقبة لحظية؛ عند خطر فوري تواصل مباشرة مع خدمات الطوارئ المحلية أو مسؤول الأكاديمية.',
                          ),
                      ],
                      const SizedBox(height: 12),
                      TextFormField(
                        controller: message,
                        maxLength: 500,
                        maxLines: 3,
                        enabled: !busy,
                        decoration: InputDecoration(
                          labelText: kind == 'Rating'
                              ? 'تعليق (اختياري)'
                              : 'اكتب الملاحظة',
                        ),
                        onChanged: (_) => requestKey = commandKey(),
                        validator: (value) =>
                            kind != 'Rating' && (value?.trim().length ?? 0) < 5
                            ? 'اكتب خمسة أحرف على الأقل.'
                            : null,
                      ),
                      FilledButton(
                        onPressed: busy ? null : submit,
                        child: const UiText('إرسال للإدارة'),
                      ),
                    ],
                  ),
                ),
              ),
              if (rows(data['mine']).isNotEmpty) ...[
                const Heading('ملاحظاتي السابقة'),
                for (final item in rows(data['mine']))
                  GCard(
                    child: ListTile(
                      title: UiText(switch (item['kind']) {
                        'Rating' => 'تقييم المدرب · ${item['score']}/5',
                        'Complaint' => 'شكوى',
                        _ => 'اقتراح',
                      }),
                      subtitle: UiText(
                        '${item['message']?.toString().isEmpty == true ? 'بدون تعليق' : item['message']} · ${item['status'] == 'Open' ? 'بانتظار المراجعة' : feedbackStates[item['status']] ?? 'غير متاحة'}${(item['ownerResponse'] is String && (item['ownerResponse'] as String).isNotEmpty) ? '\nرد الإدارة: ${item['ownerResponse']}' : ''}',
                      ),
                    ),
                  ),
              ],
            ]);
          },
        ),
      );
    },
  );
}
