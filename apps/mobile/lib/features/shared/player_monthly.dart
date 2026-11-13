import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/theme/appearance.dart';
import '../guardian/guardian_ui.dart';
import 'player_skills.dart';
import 'player_badges.dart';
import 'player_certificates.dart';

class PlayerMonthlyPage extends StatefulWidget {
  final AuthController auth;
  final String? playerId, enrollmentId;
  const PlayerMonthlyPage({
    super.key,
    required this.auth,
    this.playerId,
    this.enrollmentId,
  });
  @override
  State<PlayerMonthlyPage> createState() => _MonthlyState();
}

class _MonthlyState extends State<PlayerMonthlyPage>
    with WidgetsBindingObserver {
  String? month, selected;
  int page = 0, revision = 0;
  bool foreground = true, busy = false;
  String message = '';
  String? get identity => widget.auth.session?.selected == null
      ? null
      : '${widget.auth.session!.selected!.academyId}:${widget.auth.session!.selected!.id}:${widget.auth.session!.selected!.contextRevision}';
  late final original = identity;
  String get base => widget.playerId != null
      ? '/api/v1/guardian/children/${widget.playerId}/monthly'
      : '/api/v1/evaluations/monthly/enrollments/${widget.enrollmentId}';
  String path({String? format, Json? report}) {
    final query = Uri(
      queryParameters: {
        if (report == null) 'page': '$page',
        'month': ?(report == null ? month : textOf(report['month'])),
        if (widget.playerId != null)
          'enrollmentId': ?(report == null
              ? selected
              : textOf(obj(report['enrollment'])['id'])),
        'format': ?format,
      },
    ).query;
    return '$base?$query';
  }

  String l(String arabic, String english) =>
      localized(context, arabic, english);

  String shortDate(Object? value) {
    final parsed = DateTime.tryParse(textOf(value));
    return parsed == null
        ? textOf(value)
        : MaterialLocalizations.of(context).formatMediumDate(parsed);
  }

  @override
  void initState() {
    super.initState();
    selected = widget.enrollmentId;
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
      message = '';
    });
  }

  String state(String value) => localized(
    context,
    {
          'Mastered': 'أتقن',
          'Learning': 'قيد التعلم',
          'NotAssessed': 'لم يقيّم',
          'Granted': 'شارة ممنوحة',
          'Revoked': 'شارة مسحوبة',
          'Issued': 'شهادة صادرة',
          'Withdrawn': 'شهادة مسحوبة',
        }[value] ??
        'راجع المصدر',
    {
          'Mastered': 'Mastered',
          'Learning': 'Learning',
          'NotAssessed': 'Not assessed',
          'Granted': 'Badge granted',
          'Revoked': 'Badge revoked',
          'Issued': 'Certificate issued',
          'Withdrawn': 'Certificate withdrawn',
        }[value] ??
        'Review source',
  );
  String unit(String value) => localized(
    context,
    {
          'Checklist': 'قائمة تحقق',
          'Count': 'مرة',
          'Seconds': 'ثانية',
          'Metres': 'متر',
          'Encouragement': 'نقاط تشجيعية غير مالية',
        }[value] ??
        'قياس — راجع المصدر',
    {
          'Checklist': 'Checklist',
          'Count': 'Count',
          'Seconds': 'Seconds',
          'Metres': 'Metres',
          'Encouragement': 'Nonfinancial encouragement points',
        }[value] ??
        'Measurement — review source',
  );
  Future<void> copy(Json d) async {
    if (busy) return;
    final openedRevision = revision;
    final route = ModalRoute.of(context);
    bool stillHere() =>
        mounted &&
        identity == original &&
        foreground &&
        revision == openedRevision &&
        (route?.isCurrent ?? true);
    setState(() {
      busy = true;
      message = '';
    });
    try {
      final r = obj(
        await widget.auth.request('GET', path(format: 'text', report: d)),
      );
      if (!stillHere()) return;
      final text = r['text'];
      if (text is! String || text.isEmpty) {
        throw StateError('Unconfirmed export');
      }
      await Clipboard.setData(ClipboardData(text: text));
      if (stillHere()) {
        setState(
          () => message = localized(
            context,
            'تم نسخ تقرير الشهر إلى الحافظة. احتفظ بالبيانات بشكل خاص.',
            'Month copied to the clipboard. Keep the data private.',
          ),
        );
      }
    } catch (_) {
      if (stillHere()) {
        setState(
          () => message = localized(
            context,
            'تعذر تأكيد التصدير. أعد المحاولة.',
            'Could not confirm the export. Try again.',
          ),
        );
      }
    } finally {
      if (mounted) setState(() => busy = false);
    }
  }

  void showScope(
    Json report, {
    String? nextMonth,
    String? enrollment,
    int nextPage = 0,
  }) {
    setState(() {
      month = nextMonth ?? textOf(report['month']);
      if (widget.playerId != null) {
        selected = enrollment ?? textOf(obj(report['enrollment'])['id']);
      }
      page = nextPage;
      revision++;
      message = '';
    });
  }

  Future<void> openSource(Widget destination) async {
    await openPage(context, destination);
    if (mounted && foreground && identity == original) {
      setState(() {
        revision++;
        message = '';
      });
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
        title: l('تقرير التطور الشهري', 'Monthly development'),
        onBack: () => Navigator.of(context).maybePop(),
        child: RemoteBody(
          key: ValueKey(revision),
          autoPaginate: false,
          auth: widget.auth,
          path: path(),
          builder: (context, value) {
            final report = obj(value);
            final enrollment = obj(report['enrollment']);
            final counts = obj(report['counts']);
            final enrollmentId = textOf(enrollment['id']);
            final options = rows(report['enrollments'] ?? <Json>[]);
            final items = rows(report['items']);
            final monthKey = textOf(report['month']);
            final date = DateTime.parse('$monthKey-01');
            final calendar = MaterialLocalizations.of(context);
            final currentMonth = textOf(report['asOfDate']).substring(0, 7);
            final pageLabel = l('صفحة ${page + 1}', 'Page ${page + 1}');
            final hasMore = report['hasMore'] == true;
            return gList([
              GCard(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      textOf(enrollment['player']),
                      style: Theme.of(context).textTheme.titleLarge,
                    ),
                    Text(
                      "${enrollment['sport']} · ${calendar.formatMonthYear(date)}",
                    ),
                    Text(
                      l(
                        "من ${shortDate(report['from'])} إلى ${shortDate(report['to'])}",
                        "From ${shortDate(report['from'])} to ${shortDate(report['to'])}",
                      ),
                    ),
                    const SizedBox(height: 12),
                    Wrap(
                      spacing: 8,
                      runSpacing: 8,
                      children: [
                        OutlinedButton(
                          onPressed: busy || monthKey == '1900-01'
                              ? null
                              : () => shift(report, date, -1),
                          child: Text(l('الشهر السابق', 'Previous month')),
                        ),
                        OutlinedButton(
                          onPressed: busy || monthKey == currentMonth
                              ? null
                              : () => shift(report, date, 1),
                          child: Text(l('الشهر التالي', 'Next month')),
                        ),
                      ],
                    ),
                    if (widget.playerId != null &&
                        options.any(
                          (option) => option['id'] != enrollmentId,
                        )) ...[
                      const SizedBox(height: 12),
                      DropdownButtonFormField<String>(
                        initialValue: enrollmentId,
                        isExpanded: true,
                        decoration: InputDecoration(
                          labelText: l('التسجيل الرياضي', 'Sport enrollment'),
                        ),
                        items: [
                          if (!options.any(
                            (option) => option['id'] == enrollmentId,
                          ))
                            DropdownMenuItem(
                              value: enrollmentId,
                              child: Text(textOf(enrollment['sport'])),
                            ),
                          for (final option in options)
                            DropdownMenuItem(
                              value: textOf(option['id']),
                              child: Text(
                                "${option['sport']} · ${option['branch']}",
                                overflow: TextOverflow.ellipsis,
                              ),
                            ),
                        ],
                        onChanged: busy
                            ? null
                            : (id) {
                                if (id != null && id != enrollmentId) {
                                  showScope(report, enrollment: id);
                                }
                              },
                      ),
                    ],
                    if (report['enrollmentsHasMore'] == true)
                      Text(
                        l(
                          'تظهر أول 100 تسجيل رياضي.',
                          'Showing the first 100 sport enrollments.',
                        ),
                      ),
                  ],
                ),
              ),
              if (items.isEmpty)
                EmptyMessage(
                  page > 0
                      ? l(
                          'لا توجد نتائج في هذه الصفحة.',
                          'There are no results on this page.',
                        )
                      : l(
                          'لا توجد مراجعات أو شارات أو شهادات مسجلة لهذا الشهر.',
                          'There are no reviews, badges or certificates for this month.',
                        ),
                ),
              if (items.isNotEmpty) ...[
                GCard(
                  child: Wrap(
                    spacing: 18,
                    runSpacing: 10,
                    children: [
                      Text(
                        l(
                          "مراجعات المهارات: ${counts['skills']}",
                          "Skill reviews: ${counts['skills']}",
                        ),
                      ),
                      Text(
                        l(
                          "الشارات: ${counts['badges']}",
                          "Badges: ${counts['badges']}",
                        ),
                      ),
                      Text(
                        l(
                          "الشهادات: ${counts['certificates']}",
                          "Certificates: ${counts['certificates']}",
                        ),
                      ),
                    ],
                  ),
                ),
              ],
              GCard(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    OutlinedButton.icon(
                      onPressed: busy ? null : () => copy(report),
                      icon: const Icon(Icons.copy_all_outlined),
                      label: Text(
                        busy
                            ? l('جارٍ النسخ…', 'Copying…')
                            : l('نسخ تقرير الشهر كاملًا', 'Copy full month'),
                      ),
                    ),
                    const SizedBox(height: 8),
                    Text(
                      l(
                        'احتفظ بالتقرير بشكل خاص. السحب اللاحق لا يغيّر نسخة سبق نسخها.',
                        'Keep the report private. A later withdrawal does not change a copied report.',
                      ),
                    ),
                    if (message.isNotEmpty)
                      Semantics(liveRegion: true, child: Text(message)),
                    ExpansionTile(
                      tilePadding: EdgeInsets.zero,
                      title: Text(l('عن التقرير', 'About the report')),
                      children: [
                        Text(
                          l(
                            'يجمع التقرير مراجعات المهارات والشارات والشهادات لهذا الاشتراك. تظهر السجلات المسحوبة بحالتها الحالية.',
                            'This report shows skill reviews, badges and certificates for this enrollment, including withdrawals.',
                          ),
                        ),
                      ],
                    ),
                  ],
                ),
              ),
              for (final entry in items) entryCard(entry, enrollmentId),
              if (page > 0 || hasMore)
                GCard(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Semantics(liveRegion: true, child: Text(pageLabel)),
                      const SizedBox(height: 8),
                      Wrap(
                        spacing: 8,
                        runSpacing: 8,
                        children: [
                          OutlinedButton(
                            onPressed: busy || page == 0
                                ? null
                                : () => showScope(report, nextPage: page - 1),
                            child: Text(l('الصفحة السابقة', 'Previous page')),
                          ),
                          OutlinedButton(
                            onPressed: busy || !hasMore || page >= 1000
                                ? null
                                : () => showScope(report, nextPage: page + 1),
                            child: Text(l('الصفحة التالية', 'Next page')),
                          ),
                        ],
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

  Widget entryCard(Json entry, String enrollmentId) {
    final (kind, label, destination) = switch (entry['kind']) {
      'Skill' => (
        l('مراجعة مهارة', 'Skill review'),
        l('فتح خطة المهارة', 'Open skill plan'),
        entry['planId'] is String
            ? PlayerSkillsPage(
                auth: widget.auth,
                playerId: widget.playerId,
                planId: textOf(entry['planId']),
              )
            : null,
      ),
      'Certificate' => (
        l('شهادة إنجاز', 'Certificate'),
        l('فتح الشهادة', 'Open certificate'),
        PlayerCertificatesPage(
          auth: widget.auth,
          playerId: widget.playerId,
          id: textOf(entry['id']),
        ),
      ),
      'Badge' => (
        l('شارة مجهود', 'Badge'),
        l('فتح سجل الشارات', 'Open badge history'),
        PlayerBadgesPage(
          auth: widget.auth,
          playerId: widget.playerId,
          enrollmentId: enrollmentId,
        ),
      ),
      _ => (l('سجل تطور', 'Development record'), '', null),
    };
    return GCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            textOf(entry['title']),
            style: Theme.of(context).textTheme.titleMedium,
          ),
          Text("$kind · ${shortDate(entry['on'])}"),
          Text(state(textOf(entry['state']))),
          if (entry['value'] != null)
            Text("${entry['value']} ${unit(textOf(entry['unit']))}"),
          if (entry['note'] is String && textOf(entry['note']).isNotEmpty)
            Text(textOf(entry['note'])),
          if (entry['actor'] is String && textOf(entry['actor']).isNotEmpty)
            Text(l("سجّله ${entry['actor']}", "Recorded by ${entry['actor']}")),
          if (destination != null)
            TextButton(
              onPressed: busy ? null : () => openSource(destination),
              child: Text(label),
            ),
        ],
      ),
    );
  }

  void shift(Json report, DateTime date, int amount) {
    final next = DateTime(date.year, date.month + amount);
    showScope(
      report,
      nextMonth: "${next.year}-${next.month.toString().padLeft(2, '0')}",
    );
  }
}
