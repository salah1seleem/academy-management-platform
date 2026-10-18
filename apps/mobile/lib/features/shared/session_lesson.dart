import 'dart:convert';

import 'package:flutter/material.dart';

import '../../core/api/api_client.dart';
import '../../core/auth/auth_controller.dart';
import '../../core/theme/appearance.dart';
import '../guardian/guardian_ui.dart';
import 'player_goals.dart' show goalCommandId;

/// Staff-only session preparation; never acquires player attendance permission.
class SessionLessonPage extends StatefulWidget {
  final AuthController auth;
  final String sessionId;
  const SessionLessonPage({
    super.key,
    required this.auth,
    required this.sessionId,
  });
  @override
  State<SessionLessonPage> createState() => _SessionLessonState();
}

class _SessionLessonState extends State<SessionLessonPage>
    with WidgetsBindingObserver {
  String? get identity => widget.auth.session?.selected == null
      ? null
      : '${widget.auth.session!.selected!.academyId}:${widget.auth.session!.selected!.id}:${widget.auth.session!.selected!.contextRevision}';
  late final original = identity;
  int revision = 0;
  bool busy = false, foreground = true;
  String? choice, signature, requestId;
  String message = '';
  String l(String ar, String en) => localized(context, ar, en);
  Json map(Object? x) => obj(x ?? <String, dynamic>{});
  List<Json> list(Object? x) => rows(x ?? <Json>[]);
  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    super.dispose();
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState s) {
    setState(() {
      foreground = s == AppLifecycleState.resumed;
      revision++;
      choice = null;
      message = '';
    });
  }

  Future<void> assign(Json d) async {
    if (busy || choice == null) return;
    final body = {'templateId': choice, 'previousRevision': d['revision']};
    final encoded = jsonEncode(body);
    if (signature != encoded) {
      signature = encoded;
      requestId = goalCommandId();
    }
    setState(() {
      busy = true;
      message = '';
    });
    try {
      await widget.auth.request(
        'POST',
        '/api/v1/evaluations/lessons/sessions/${widget.sessionId}',
        body: {...body, 'requestId': requestId},
      );
      if (!mounted || identity != original) return;
      signature = null;
      requestId = null;
      setState(() {
        revision++;
        choice = null;
        message = l(
          'تم حفظ خطة الحصة مع تاريخ الإسناد.',
          'Session plan saved with assignment history.',
        );
      });
    } catch (e) {
      if (mounted && identity == original) {
        setState(
          () => message = e is ApiFailure
              ? e.message
              : l(
                  'تعذر الحفظ؛ أعد فتح الحصة.',
                  'Could not save; reopen the session.',
                ),
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
      if (!foreground || identity == null || identity != original) {
        return const SizedBox.shrink();
      }
      return GuardianPage(
        title: l('خطة الحصة', 'Session plan'),
        child: RemoteBody(
          key: ValueKey('$identity:$revision'),
          auth: widget.auth,
          path: '/api/v1/evaluations/lessons/sessions/${widget.sessionId}',
          builder: (context, v) {
            final d = map(v),
                s = map(d['session']),
                active = map(d['active']),
                item = map(active['item']);
            final choices = list(d['available']);
            final chosen = choices.any((x) => x['id'] == choice)
                ? choice
                : null;
            return gList([
              GCard(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      textOf(s['group']),
                      style: Theme.of(context).textTheme.titleLarge,
                    ),
                    const SizedBox(height: 8),
                    Text(day(s['sessionDate'])),
                    Text(
                      '${textOf(s['startTime']).split(':').take(2).join(':')} — ${textOf(s['endTime']).split(':').take(2).join(':')}',
                      textDirection: TextDirection.ltr,
                    ),
                    Text(
                      '${d['sessionMinutes']} ${l('دقيقة · الحصة الفعلية', 'minutes · actual session')}',
                    ),
                  ],
                ),
              ),
              if (message.isNotEmpty) Text(message),
              if (d['contextChanged'] == true)
                Text(
                  l(
                    'تغير سياق الحصة. هذه خطة تاريخية لا تطابق الموعد أو المستوى الحالي.',
                    'Session context changed. This historical plan no longer matches the current time or level.',
                  ),
                ),
              if (d['canAssign'] == true)
                GCard(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.stretch,
                    children: [
                      Text(
                        l(
                          'قالب معتمد لنفس المستوى والمدة',
                          'Reviewed template for this level and duration',
                        ),
                      ),
                      DropdownButtonFormField<String>(
                        key: ValueKey('$revision:$chosen'),
                        initialValue: chosen,
                        isExpanded: true,
                        decoration: InputDecoration(
                          labelText: l('اختر خطة', 'Choose a plan'),
                        ),
                        items: choices
                            .map(
                              (x) => DropdownMenuItem(
                                value: textOf(x['id']),
                                child: Text(
                                  textOf(x['title']),
                                  overflow: TextOverflow.ellipsis,
                                ),
                              ),
                            )
                            .toList(),
                        onChanged: busy
                            ? null
                            : (v) => setState(() => choice = v),
                      ),
                      const SizedBox(height: 12),
                      FilledButton.icon(
                        onPressed: busy || chosen == null
                            ? null
                            : () => assign(d),
                        icon: const Icon(Icons.playlist_add_check),
                        label: Text(
                          busy
                              ? l('جارٍ الحفظ…', 'Saving…')
                              : active.isEmpty
                              ? l('إسناد خطة الحصة', 'Assign session plan')
                              : l(
                                  'استبدال مع حفظ السابق',
                                  'Replace and preserve history',
                                ),
                        ),
                      ),
                      if (choices.isEmpty)
                        Text(
                          l(
                            'جهز قالبًا معتمدًا يطابق مستوى المجموعة ومدة الحصة من لوحة الإدارة.',
                            'Prepare a reviewed template matching the group level and session duration on the dashboard.',
                          ),
                        ),
                    ],
                  ),
                ),
              if (!busy && active.isNotEmpty) ...[
                GCard(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(
                        textOf(item['title']),
                        style: Theme.of(context).textTheme.titleLarge,
                      ),
                      const SizedBox(height: 8),
                      Text(
                        '${active['computedMinutes']} ${l('دقيقة محسوبة', 'computed minutes')} · ${l('المعلن', 'declared')} ${item['declaredMinutes']}',
                        style: const TextStyle(fontWeight: FontWeight.bold),
                      ),
                      if (active['durationMatches'] != true)
                        Text(
                          l(
                            'تعارض في المدة؛ لا تستخدم هذه النسخة.',
                            'Duration mismatch; do not use this version.',
                          ),
                        ),
                    ],
                  ),
                ),
                for (final step in list(active['steps'])) stepCard(step),
              ],
              if (!busy && active.isEmpty)
                Text(
                  l(
                    'لم تُسند خطة لهذه الحصة بعد.',
                    'No plan assigned to this session yet.',
                  ),
                ),
              GCard(
                child: ExpansionTile(
                  title: Text(l('سجل إسناد الخطة', 'Plan assignment history')),
                  children: [
                    for (final a in list(d['history']))
                      ListTile(
                        leading: const Icon(Icons.history),
                        title: Text(
                          '${l('مراجعة', 'Revision')} ${a['revision']} · ${textOf(a['actorName'])}',
                        ),
                        subtitle: Text(day(a['createdAtUtc'])),
                      ),
                    if (d['historyHasMore'] == true)
                      Text(
                        l(
                          'تظهر أحدث20 مراجعة.',
                          'Showing the latest20 revisions.',
                        ),
                      ),
                  ],
                ),
              ),
            ]);
          },
        ),
      );
    },
  );
  Widget stepCard(Json step) {
    final e = map(step['exercise']);
    return GCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              const Icon(Icons.fitness_center_outlined),
              const SizedBox(width: 12),
              Expanded(
                child: Text(
                  textOf(e['title']),
                  style: Theme.of(context).textTheme.titleMedium,
                ),
              ),
              const SizedBox(width: 8),
              Text(
                '${step['minutes']} ${l('د', 'min')}',
                style: const TextStyle(fontWeight: FontWeight.bold),
              ),
            ],
          ),
          const SizedBox(height: 12),
          Text('${textOf(e['skill'])} · ${textOf(e['equipment'])}'),
          const SizedBox(height: 8),
          Text(textOf(e['instructions'])),
          const SizedBox(height: 8),
          Text(
            '${e['rightsKind'] == 'Original' ? l('أصلي', 'Original') : l('مرخص', 'Licensed')} · ${textOf(e['rightsReference'])}',
            style: const TextStyle(fontSize: 14),
          ),
        ],
      ),
    );
  }
}
