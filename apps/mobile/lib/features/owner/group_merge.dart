import 'package:flutter/material.dart';

import '../../core/api/api_client.dart';
import '../../core/auth/auth_controller.dart';
import '../../core/theme/ui_text.dart';
import '../guardian/guardian_ui.dart';
import 'owner_followups.dart';

class OwnerGroupMerge extends StatefulWidget {
  const OwnerGroupMerge({
    super.key,
    required this.auth,
    required this.groups,
    required this.today,
  });
  final AuthController auth;
  final List<Json> groups;
  final String today;
  @override
  State<OwnerGroupMerge> createState() => _OwnerGroupMergeState();
}

class _OwnerGroupMergeState extends State<OwnerGroupMerge> {
  late final identity = followUpContext(widget.auth);
  late String effective = widget.today;
  String? source, target, error;
  String command = followUpRequestId();
  Json? preview;
  bool busy = false, uncertainCommit = false;

  bool get current =>
      mounted && identity != null && identity == followUpContext(widget.auth);

  void change(VoidCallback update) => setState(() {
    update();
    preview = null;
    error = null;
    command = followUpRequestId();
  });

  Future<void> submit({required bool commit}) async {
    if (!current ||
        busy ||
        source == null ||
        target == null ||
        commit && preview == null) {
      return;
    }
    setState(() {
      error = null;
      busy = true;
    });
    try {
      final result = obj(
        await widget.auth.request(
          'POST',
          '/api/v1/manage/groups/merge${commit ? '' : '/preview'}',
          body: {
            'sourceGroupId': source,
            'targetGroupId': target,
            'effectiveDate': effective,
            if (commit) 'fingerprint': preview!['fingerprint'],
          },
          idempotencyKey: commit ? command : null,
        ),
      );
      if (!current) return;
      if (commit) {
        if (!mounted) return;
        if (result['id'] is! String || (result['id'] as String).isEmpty) {
          throw const FormatException();
        }
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(content: UiText('تم دمج قوائم اللاعبين.')),
        );
        Navigator.pop(context, true);
      } else {
        if (result['fingerprint'] is! String ||
            (result['fingerprint'] as String).isEmpty ||
            result['conflicts'] is! List ||
            result['count'] is! num) {
          throw const FormatException();
        }
        setState(() => preview = result);
      }
    } catch (e) {
      if (!current) return;
      setState(() {
        final failure = e is ApiFailure ? e : null;
        if (commit && failure?.status == 409) {
          preview = null;
          uncertainCommit = false;
        } else if (commit &&
            (failure == null || failure.status == 0 || failure.status >= 500)) {
          uncertainCommit = true;
        }
        error = uncertainCommit
            ? 'لم يتأكد حفظ الدمج. أعد محاولة الطلب نفسه.'
            : failure?.message ?? 'تعذر تحميل معاينة الدمج. أعد المحاولة.';
      });
    } finally {
      if (current) setState(() => busy = false);
    }
  }

  Widget _pick(
    String label,
    String? selection,
    List<Json> groups,
    ValueChanged<String?> onChanged,
  ) => DropdownButtonFormField<String>(
    key: ValueKey('$label-$selection'),
    initialValue: selection,
    isExpanded: true,
    decoration: InputDecoration(labelText: label),
    items: [
      for (final g in groups)
        DropdownMenuItem(
          value: textOf(g['groupId']),
          child: UiText(
            '${g['name']} · ${g['branch']}',
            maxLines: 1,
            overflow: TextOverflow.ellipsis,
          ),
        ),
    ],
    onChanged: busy || uncertainCommit || groups.isEmpty ? null : onChanged,
  );

  @override
  Widget build(BuildContext context) => ListenableBuilder(
    listenable: widget.auth,
    builder: (context, _) {
      if (!current) return const SizedBox.shrink();
      final from = widget.groups
          .where((g) => (g['occupied'] as num? ?? 0) > 0)
          .toList();
      final chosen = from.where((g) => g['groupId'] == source).firstOrNull;
      final destinations = widget.groups
          .where(
            (g) =>
                chosen != null &&
                g['groupId'] != source &&
                g['branchId'] == chosen['branchId'] &&
                g['sportId'] == chosen['sportId'] &&
                (g['available'] as num? ?? 0) >=
                    (chosen['occupied'] as num? ?? 0),
          )
          .toList();
      final conflicts = (preview?['conflicts'] as List?) ?? [];
      return GuardianPage(
        title: 'دمج مجموعات',
        child: gList([
          const UiText(
            'انقل قائمة اللاعبين إلى مجموعة واحدة بسعتها ومواعيدها. تظل الحصص السابقة محفوظة.',
          ),
          _pick(
            'المجموعة المنقول منها',
            source,
            from,
            (value) => change(() {
              source = value;
              target = null;
            }),
          ),
          _pick(
            'المجموعة المستقبلة',
            target,
            destinations,
            (value) => change(() => target = value),
          ),
          if (source != null && destinations.isEmpty)
            const EmptyMessage(
              'لا توجد مجموعة بسعة كافية في نفس الفرع والرياضة.',
            ),
          OutlinedButton.icon(
            onPressed: busy || uncertainCommit
                ? null
                : () async {
                    final today = DateTime.parse(widget.today);
                    final date = await showDatePicker(
                      context: context,
                      initialDate: DateTime.parse(effective),
                      firstDate: today,
                      lastDate: DateTime(
                        today.year + 1,
                        today.month,
                        today.day,
                      ),
                    );
                    if (current && date != null) {
                      change(
                        () =>
                            effective = date.toIso8601String().substring(0, 10),
                      );
                    }
                  },
            icon: const Icon(Icons.calendar_month_outlined),
            label: UiText('بدء الدمج · $effective'),
          ),
          if (error != null) UiText(error!),
          if (preview != null)
            GCard(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Heading(
                    '${preview!['count']} لاعب إلى ${preview!['target']}',
                  ),
                  if (conflicts.isEmpty)
                    const UiText(
                      'بعد الدمج، راجع مواعيد المجموعة الأصلية من إعدادات المجموعات.',
                    ),
                  for (final issue in conflicts) UiText(textOf(issue)),
                ],
              ),
            ),
          FilledButton.icon(
            onPressed:
                busy || source == null || target == null || conflicts.isNotEmpty
                ? null
                : () => submit(commit: preview != null),
            icon: Icon(
              preview == null
                  ? Icons.visibility_outlined
                  : Icons.merge_type_rounded,
            ),
            label: UiText(
              busy
                  ? 'جارٍ التحقق…'
                  : preview == null
                  ? 'معاينة الدمج'
                  : uncertainCommit
                  ? 'إعادة محاولة طلب الدمج نفسه'
                  : 'تأكيد دمج اللاعبين',
            ),
          ),
        ]),
      );
    },
  );
}
