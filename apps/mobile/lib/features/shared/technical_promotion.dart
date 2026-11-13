import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:intl/intl.dart';

import '../../core/api/api_client.dart';
import '../../core/auth/auth_controller.dart';
import '../../core/theme/appearance.dart';
import '../guardian/guardian_ui.dart';
import 'player_certificates.dart';
import 'player_goals.dart' show goalCommandId;

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
  bool foreground = true, needsRead = false, saved = false, checked = false;
  int revision = 0;
  String? group, command, signature, previewGroup;
  bool? applying;
  String error = '';
  Json? preview;
  bool get locked => applying != null || needsRead || saved;
  String get path =>
      '/api/v1/evaluations/technical-reviews/${widget.requestId}/promotion';
  String l(String ar, String en) => localized(context, ar, en);
  List<Json> items(Object? value) => rows(value ?? const []);
  String displayDate(Object? value) {
    final parsed = DateTime.tryParse(day(value));
    return parsed == null
        ? ''
        : DateFormat.yMMMd(Localizations.localeOf(context).toString())
              .format(parsed);
  }

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
  void didChangeAppLifecycleState(AppLifecycleState state) {
    setState(() {
      foreground = state == AppLifecycleState.resumed;
      revision++;
      preview = null;
      group = null;
    });
  }

  void check() {
    setState(() {
      preview = null;
      error = '';
      if (needsRead && command != null) checked = true;
      needsRead = false;
      revision++;
    });
  }

  Future<void> submit(
    Json options,
    String selected, {
    required bool save,
  }) async {
    if (locked || options['applied'] != null || options['canPreview'] != true) {
      return;
    }
    final current = preview;
    if (save &&
        (current == null ||
            previewGroup != selected ||
            current['enrollmentVersion'] != options['enrollmentVersion'] ||
            current['fingerprint'] is! String ||
            (current['fingerprint'] as String).isEmpty ||
            (obj(current['preview'])['conflicts'] as List).isNotEmpty)) {
      return;
    }
    final startRevision = revision;
    final body = <String, Object?>{
      'groupId': selected,
      'enrollmentVersion': options['enrollmentVersion'],
      'fingerprint': save ? current!['fingerprint'] : null,
    };
    final next = jsonEncode(body);
    if (save && signature != next) {
      signature = next;
      command = goalCommandId();
    }
    setState(() {
      applying = save;
      error = '';
      checked = false;
      if (!save) preview = null;
    });
    try {
      final result = await widget.auth.request(
        'POST',
        path + (save ? '' : '/preview'),
        body: {...body, 'requestId': save ? command : goalCommandId()},
      );
      if (!mounted || identity != original) return;
      if (save) {
        setState(() {
          saved = true;
          preview = null;
          revision++;
          signature = null;
          command = null;
        });
      } else if (foreground && startRevision == revision) {
        final candidate = obj(result);
        final details = obj(candidate['preview']);
        setState(() {
          if (candidate['enrollmentVersion'] != options['enrollmentVersion'] ||
              details['effectiveDate'] != options['effectiveDate'] ||
              (details['targetGroupId'] != null &&
                  details['targetGroupId'] != selected)) {
            needsRead = true;
            error = l(
              'تغيّرت بيانات التسكين. حدّثها قبل إعادة المعاينة.',
              'Placement details changed. Refresh them before previewing again.',
            );
          } else {
            preview = candidate;
            previewGroup = selected;
          }
        });
      }
    } catch (failure) {
      if (mounted &&
          identity == original &&
          (save || startRevision == revision)) {
        final status = failure is ApiFailure ? failure.status : 0;
        setState(() {
          preview = null;
          needsRead = save || [401, 403, 404, 409].contains(status);
          error = save && (status == 0 || status >= 500)
              ? l(
                  'تعذر تأكيد التنفيذ. تحقّق من سجل التنفيذ قبل المحاولة مرة أخرى.',
                  'The transfer could not be confirmed. Check the transfer record before trying again.',
                )
              : failure is ApiFailure
              ? failure.message
              : l(
                  'تعذر تحميل المعاينة. أعد المحاولة.',
                  'The preview could not be loaded. Try again.',
                );
        });
      }
    } finally {
      if (mounted) setState(() => applying = null);
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
        onBack: () => Navigator.of(context).maybePop(),
        child: RemoteBody(
          key: ValueKey('$identity:$revision'),
          auth: widget.auth,
          path: path,
          builder: (context, value) {
            final data = obj(value);
            final applied = data['applied'] == null
                ? null
                : obj(data['applied']);
            final groups = items(data['groups']);
            final selected = groups.any((item) => item['id'] == group)
                ? group
                : null;
            final current =
                selected != null &&
                    previewGroup == selected &&
                    preview?['enrollmentVersion'] == data['enrollmentVersion']
                ? preview
                : null;
            return gList([
              optionCard(data, groups, selected, applied),
              if (data['canPreview'] == true &&
                  applied == null &&
                  current != null)
                previewCard(data, selected!, current),
            ]);
          },
        ),
      );
    },
  );

  Widget optionCard(
    Json data,
    List<Json> groups,
    String? selected,
    Json? applied,
  ) {
    final player = data['player'] as String? ?? '';
    final scope = [
      data['sport'],
      data['branch'],
    ].whereType<String>().where((value) => value.isNotEmpty).join(' · ');
    final label = l(
      'المجموعة في المستوى الموصى به',
      'Group at recommended level',
    );
    return GCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            player.isEmpty ? textOf(data['recommendedLevel']) : player,
            style: Theme.of(context).textTheme.titleLarge,
          ),
          if (player.isNotEmpty && scope.isNotEmpty) Text(scope),
          if (applied != null) ...[
            const SizedBox(height: 12),
            Semantics(
              liveRegion: true,
              child: Text(
                '${l('تم التنفيذ إلى', 'Applied to')} ${applied['toLevelName']}',
                style: Theme.of(context).textTheme.titleMedium,
              ),
            ),
            if (applied['effectiveDate'] != null)
              fact(
                l('بداية النقل', 'Transfer starts'),
                displayDate(applied['effectiveDate']),
              )
            else if (applied['createdAtUtc'] != null)
              fact(
                l('تاريخ التسجيل', 'Recorded on'),
                displayDate(applied['createdAtUtc']),
              ),
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
          ] else ...[
            if (player.isNotEmpty)
              fact(
                l('المستوى الموصى به', 'Recommended level'),
                textOf(data['recommendedLevel']),
              ),
            if (error.isNotEmpty)
              Semantics(liveRegion: true, child: Text(error)),
            if (needsRead || saved)
              OutlinedButton(
                onPressed: check,
                child: Text(
                  l('تحقّق من سجل التنفيذ', 'Check the transfer record'),
                ),
              ),
            if (checked)
              Semantics(
                liveRegion: true,
                child: Text(
                  l(
                    'لا يوجد تنفيذ مسجّل لهذه التوصية.',
                    'No transfer is recorded for this recommendation.',
                  ),
                ),
              ),
            if (saved)
              Text(
                l(
                  'لم يظهر سجل التنفيذ بعد. حدّث السجل للتحقق.',
                  'The transfer record has not appeared. Refresh to check again.',
                ),
              )
            else if (data['canPreview'] != true)
              Text(
                l(
                  'تغيّرت الأدلة أو الخطة. يلزم طلب مراجعة فنية جديد.',
                  'Evidence or the plan changed. Request a new technical review.',
                ),
              )
            else if (groups.isEmpty)
              Text(
                l(
                  'لا توجد مجموعة مطابقة في فرع اللاعب. جهّز المجموعة ومواعيدها أولًا.',
                  'No matching group in this branch. Prepare a group and its sessions first.',
                ),
              )
            else ...[
              const SizedBox(height: 16),
              ExcludeSemantics(child: Text(label)),
              const SizedBox(height: 6),
              Semantics(
                label: label,
                child: DropdownButtonFormField<String>(
                  key: ValueKey('$revision:$selected'),
                  isExpanded: true,
                  isDense: false,
                  initialValue: selected,
                  hint: Text(l('اختر المجموعة', 'Choose group')),
                  items: groups
                      .map(
                        (item) => DropdownMenuItem(
                          value: textOf(item['id']),
                          child: Text(textOf(item['name'])),
                        ),
                      )
                      .toList(),
                  onChanged: locked
                      ? null
                      : (value) => setState(() {
                          group = value;
                          preview = null;
                          error = '';
                        }),
                ),
              ),
              const SizedBox(height: 12),
              OutlinedButton(
                onPressed: locked || selected == null
                    ? null
                    : () => submit(data, selected, save: false),
                child: Text(
                  applying == false
                      ? l('جارٍ تحميل المعاينة…', 'Loading preview…')
                      : l(
                          'معاينة المستوى والمواعيد',
                          'Preview level and schedule',
                        ),
                ),
              ),
              if (data['hasMore'] == true)
                Text(
                  l(
                    'تظهر أول 100 مجموعة مطابقة.',
                    'Showing the first 100 matching groups.',
                  ),
                ),
            ],
          ],
        ],
      ),
    );
  }

  Widget fact(String label, String value) => Padding(
    padding: const EdgeInsets.only(top: 8),
    child: Text('$label · $value'),
  );

  Widget schedule(String title, List<Json> sessions) {
    final dates = sessions
        .where((item) => item['sessionDate'] != null)
        .take(3)
        .toList();
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        const SizedBox(height: 12),
        Text(title, style: Theme.of(context).textTheme.titleSmall),
        Text(
          sessions.isEmpty
              ? l('لا توجد مواعيد مجدولة.', 'No scheduled sessions.')
              : '${l('العدد', 'Total')}: ${sessions.length}',
        ),
        for (final item in dates)
          Text(
            [
              displayDate(item['sessionDate']),
              if (item['startTime'] != null && item['endTime'] != null)
                trainingTimeRange(item['startTime'], item['endTime']),
            ].where((value) => value.isNotEmpty).join(' · '),
          ),
        if (dates.isNotEmpty && sessions.length > dates.length)
          Text(
            l(
              'تظهر أقرب ${dates.length} مواعيد.',
              'Showing the next ${dates.length} sessions.',
            ),
          ),
      ],
    );
  }

  Widget previewCard(Json options, String selected, Json current) {
    final details = obj(current['preview']);
    final conflicts = details['conflicts'] as List;
    return GCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Text(
            l('معاينة التسكين', 'Placement preview'),
            style: Theme.of(context).textTheme.titleMedium,
          ),
          fact(
            l('المجموعة الحالية', 'Current group'),
            textOf(details['sourceGroup']),
          ),
          fact(
            l('المجموعة الجديدة', 'New group'),
            textOf(details['targetGroup']),
          ),
          fact(l('المستوى الجديد', 'New level'), textOf(current['level'])),
          fact(
            l('بداية النقل', 'Transfer starts'),
            displayDate(details['effectiveDate']),
          ),
          if (conflicts.isNotEmpty) ...[
            const SizedBox(height: 12),
            Text(
              l('تعذّر اعتماد هذه المجموعة', 'This group cannot be confirmed'),
              style: TextStyle(
                color: Theme.of(context).colorScheme.error,
                fontWeight: FontWeight.w700,
              ),
            ),
            for (final conflict in conflicts) Text(textOf(conflict)),
          ],
          schedule(
            l(
              'مواعيد تخرج من جدول اللاعب',
              'Sessions removed from the player schedule',
            ),
            items(details['removedSessions']),
          ),
          schedule(
            l(
              'مواعيد تضاف إلى جدول اللاعب',
              'Sessions added to the player schedule',
            ),
            items(details['addedSessions']),
          ),
          const SizedBox(height: 12),
          if (conflicts.isEmpty)
            Text(
              l(
                'تُراجع الأهلية عند الاعتماد. يبقى الاشتراك والتحصيل وسجل الحضور محفوظًا.',
                'Eligibility is rechecked when you confirm. Subscription, payment and attendance records are preserved.',
              ),
            ),
          const SizedBox(height: 8),
          FilledButton(
            onPressed: locked || conflicts.isNotEmpty
                ? null
                : () => submit(options, selected, save: true),
            child: Text(
              applying == true
                  ? l('جارٍ اعتماد التسكين…', 'Confirming placement…')
                  : l(
                      'اعتماد المستوى ونقل المجموعة',
                      'Confirm level and group transfer',
                    ),
            ),
          ),
        ],
      ),
    );
  }
}
