import 'dart:convert';

import 'package:flutter/material.dart';

import '../../core/api/api_client.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/theme/appearance.dart';
import '../guardian/guardian_ui.dart';
import 'player_goals.dart' show goalCommandId;
import 'player_certificates.dart';

class TechnicalPromotionPage extends StatefulWidget {
  final AuthController auth;
  final String requestId;
  const TechnicalPromotionPage({
    super.key,
    required this.auth,
    required this.requestId,
  });
  @override
  State<TechnicalPromotionPage> createState() => _TechnicalPromotionState();
}

class _TechnicalPromotionState extends State<TechnicalPromotionPage>
    with WidgetsBindingObserver {
  String? get identity => widget.auth.session?.selected == null
      ? null
      : '${widget.auth.session!.selected!.academyId}:${widget.auth.session!.selected!.id}:${widget.auth.session!.selected!.contextRevision}';
  late final original = identity;
  bool foreground = true, busy = false;
  int revision = 0;
  String? group, command, signature;
  String message = '', error = '';
  Json? preview;
  String get path =>
      '/api/v1/evaluations/technical-reviews/${widget.requestId}/promotion';
  String l(String ar, String en) => localized(context, ar, en);
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
      preview = null;
      group = null;
      message = '';
      error = '';
    });
  }

  Future<void> submit(Json options, bool save) async {
    if (busy || group == null) return;
    final body = <String, Object?>{
      'groupId': group,
      'enrollmentVersion': options['enrollmentVersion'],
      'fingerprint': save && preview != null ? preview!['fingerprint'] : null,
    };
    final next = jsonEncode({'save': save, ...body});
    if (signature != next) {
      signature = next;
      command = goalCommandId();
    }
    setState(() {
      busy = true;
      error = '';
    });
    try {
      final result = obj(
        await widget.auth.request(
          'POST',
          path + (save ? '' : '/preview'),
          body: {...body, 'requestId': command},
        ),
      );
      if (!mounted || identity != original) return;
      signature = null;
      command = null;
      setState(() {
        if (save) {
          preview = null;
          revision++;
          message = l(
            'تم اعتماد المستوى ونقل المواعيد مع حفظ التاريخ، دون تحصيل جديد.',
            'Level and schedule transfer saved with history; no new collection.',
          );
        } else {
          preview = result;
        }
      });
    } catch (failure) {
      if (mounted && identity == original) {
        setState(() {
          if (!save || failure is ApiFailure && failure.status < 500) {
            preview = null;
          }
          error = l(
            'تعذر الاعتماد. ربما تغير المصدر أو الصلاحية؛ أعد المعاينة. لم نسجل نجاحًا.',
            'Could not confirm. Source or permissions may have changed; preview again. No success recorded.',
          );
        });
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
        title: l('تنفيذ التوصية الفنية', 'Apply technical recommendation'),
        child: RemoteBody(
          key: ValueKey('$identity:$revision'),
          auth: widget.auth,
          path: path,
          builder: (context, value) {
            final d = obj(value),
                applied = d['applied'] == null ? null : obj(d['applied']);
            final current =
                preview?['enrollmentVersion'] == d['enrollmentVersion']
                ? preview
                : null;
            final details = current == null ? null : obj(current['preview']);
            final conflicts = details == null
                ? <Object?>[]
                : (details['conflicts'] as List? ?? []);
            return gList([
              if (message.isNotEmpty) Text(message),
              if (error.isNotEmpty) Text(error),
              GCard(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      textOf(d['recommendedLevel']),
                      style: Theme.of(context).textTheme.titleLarge,
                    ),
                    Text(
                      '${l('التنفيذ من يوم', 'Effective day')}: ${d['effectiveDate']}',
                    ),
                    Text(
                      l(
                        'معاينة ثم اعتماد صريح. يحفظ التاريخ ولا يغير العمرية أو الباقة أو التحصيل أو الحضور المسجل.',
                        'Preview then explicitly confirm. History is preserved; age category, package, money and recorded attendance are unchanged.',
                      ),
                    ),
                    if (applied != null)
                      Text(
                        '${l('تم التنفيذ إلى', 'Applied to')}: ${applied['toLevelName']}',
                      ),
                    if (applied != null)
                      GLink(
                        l(
                          'شهادة إنجاز من هذا المصدر',
                          'Achievement certificate from this source',
                        ),
                        Icons.workspace_premium_outlined,
                        () => openPage(
                          context,
                          PlayerCertificatesPage(
                            auth: widget.auth,
                            sourceId: textOf(applied['id']),
                          ),
                        ),
                      ),
                    if (applied == null && d['canPreview'] != true)
                      Text(
                        l(
                          'تغيرت الأدلة أو الخطة؛ يلزم طلب ومراجعة حديثان.',
                          'Evidence or plan changed; a new request and review are required.',
                        ),
                      ),
                    if (applied == null && d['canPreview'] == true) ...[
                      const SizedBox(height: 24),
                      DropdownButtonFormField<String>(
                        isExpanded: true,
                        initialValue: group,
                        decoration: InputDecoration(
                          labelText: l(
                            'المجموعة في المستوى الموصى به',
                            'Group at recommended level',
                          ),
                        ),
                        items: rows(d['groups'])
                            .map(
                              (g) => DropdownMenuItem(
                                value: textOf(g['id']),
                                child: Text(textOf(g['name']), maxLines: 2),
                              ),
                            )
                            .toList(),
                        onChanged: busy
                            ? null
                            : (v) => setState(() {
                                group = v;
                                preview = null;
                                message = '';
                              }),
                      ),
                      if (rows(d['groups']).isEmpty)
                        Text(
                          l(
                            'لا توجد مجموعة مطابقة؛ جهزها ومواعيدها أولًا.',
                            'No matching group. Prepare a group and its sessions first.',
                          ),
                        ),
                      const SizedBox(height: 12),
                      FilledButton(
                        onPressed: busy || group == null
                            ? null
                            : () => submit(d, false),
                        child: Text(
                          l(
                            'معاينة المستوى والمواعيد',
                            'Preview level and schedule',
                          ),
                        ),
                      ),
                      if (d['hasMore'] == true)
                        Text(
                          l(
                            'تظهر أول100 مجموعة.',
                            'Showing the first100 groups.',
                          ),
                        ),
                    ],
                  ],
                ),
              ),
              if (d['canPreview'] == true && current != null && details != null)
                GCard(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.stretch,
                    children: [
                      Text(
                        l('راجع قبل الاعتماد', 'Review before confirming'),
                        style: Theme.of(context).textTheme.titleMedium,
                      ),
                      Text(
                        '${details['sourceGroup']} ← ${details['targetGroup']}',
                      ),
                      Text('${current['level']} · ${details['effectiveDate']}'),
                      Text(
                        '${l('مواعيد سابقة', 'Previous sessions')}: ${(details['removedSessions'] as List).length} · ${l('جديدة', 'New')}: ${(details['addedSessions'] as List).length}',
                      ),
                      for (final c in conflicts) Text(textOf(c)),
                      if (conflicts.isEmpty)
                        Text(
                          l(
                            'لا تعارض في المعاينة الحالية؛ يعاد الفحص عند الحفظ.',
                            'No conflicts in this preview; checks run again when saving.',
                          ),
                        ),
                      FilledButton(
                        onPressed: busy || conflicts.isNotEmpty
                            ? null
                            : () => submit(d, true),
                        child: Text(
                          l(
                            'اعتماد المستوى ونقل المجموعة',
                            'Confirm level and group transfer',
                          ),
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
}
