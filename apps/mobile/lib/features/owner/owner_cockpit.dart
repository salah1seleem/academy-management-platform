import 'package:flutter/material.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/theme/ui_text.dart';
import '../guardian/guardian_ui.dart';
import 'owner_operations.dart';
import 'owner_reports.dart';

/// All figures and decision conditions come from the same owner API as the web.
class OwnerCockpit extends StatelessWidget {
  const OwnerCockpit({super.key, required this.auth, this.filters = const {}});
  final AuthController auth;
  final Map<String, String> filters;
  @override
  Widget build(BuildContext context) => OwnerScopedRead(
    auth: auth,
    path:
        '/api/v1/reports/owner-cockpit?${Uri(queryParameters: filters).query}',
    builder: (context, value) => OwnerCockpitContent(
      data: obj(value),
      onReport: (report) => openPage(
        context,
        OwnerReports(
          auth: auth,
          initialReport: report,
          initialFilters: {
            ...filters,
            if (report == 'receivables') 'state': 'overdue',
          },
        ),
      ),
      onCapacity: () =>
          openPage(context, OwnerCapacityPage(auth: auth, filters: filters)),
    ),
  );
}

class OwnerCockpitContent extends StatelessWidget {
  const OwnerCockpitContent({
    super.key,
    required this.data,
    required this.onReport,
    required this.onCapacity,
  });
  final Json data;
  final void Function(String) onReport;
  final VoidCallback onCapacity;
  @override
  Widget build(BuildContext context) {
    final collections = obj(data['collections']),
        target = obj(data['target']),
        overdue = obj(data['overdue']),
        risk = obj(data['risk']),
        attendance = obj(data['attendance']),
        capacity = obj(data['capacity']);
    final change = collections['changePercentage'] as num?,
        progress = target['achievement'] as num?;
    Widget decision(
      IconData icon,
      String title,
      String subtitle,
      VoidCallback action,
    ) => GCard(
      child: ListTile(
        contentPadding: EdgeInsets.zero,
        leading: Icon(icon),
        title: UiText(title),
        subtitle: UiText(subtitle),
        trailing: const Icon(Icons.chevron_left),
        onTap: action,
      ),
    );
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        GCard(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              Row(
                children: [
                  Icon(
                    Icons.insights_rounded,
                    color: Theme.of(context).colorScheme.primary,
                  ),
                  const SizedBox(width: 8),
                  const Heading('ملخص الأكاديمية'),
                ],
              ),
              const UiText('صافي التحصيل المؤكد خلال الفترة'),
              TextButton(
                onPressed: () => onReport('financial'),
                child: UiText(
                  money(collections['net'], data['currency']),
                  style: Theme.of(context).textTheme.headlineMedium,
                ),
              ),
              UiText(
                change == null
                    ? 'لا نسبة مقارنة قابلة للاعتماد لهذه الفترة.'
                    : '${change < 0 ? 'انخفاض' : 'زيادة'} ${change.abs()}% عن الفترة السابقة',
              ),
              UiText(
                '${collections['previousFrom']} — ${collections['previousTo']} · ${money(collections['previous'], data['currency'])}',
                style: Theme.of(context).textTheme.bodySmall,
              ),
              const Divider(height: 24),
              const UiText('هدف التحصيل الشهري'),
              if (target['amount'] != null && progress != null) ...[
                UiText(
                  '${progress.toStringAsFixed(1)}% من ${money(target['amount'], data['currency'])}',
                ),
                const SizedBox(height: 8),
                LinearProgressIndicator(
                  value: (progress / 100).clamp(0.0, 1.0).toDouble(),
                  semanticsLabel: 'تحقيق هدف التحصيل',
                ),
              ] else
                const UiText(
                  'لم يحدد هدف سارٍ لهذه الفترة. يضبطه المالك من إعدادات الأكاديمية في لوحة الويب.',
                ),
            ],
          ),
        ),
        Wrap(
          spacing: 8,
          runSpacing: 8,
          children: [
            ActionChip(
              avatar: const Icon(
                Icons.account_balance_wallet_outlined,
                size: 18,
              ),
              label: UiText(
                'متأخرات · ${money(overdue['amount'], data['currency'])}',
              ),
              onPressed: () => onReport('receivables'),
            ),
            ActionChip(
              avatar: const Icon(Icons.favorite_outline, size: 18),
              label: UiText('متابعة تجديد · ${risk['players']} لاعب'),
              onPressed: () => onReport('renewals'),
            ),
            ActionChip(
              avatar: const Icon(Icons.event_available, size: 18),
              label: UiText(
                'الحضور · ${attendance['percentage'] == null ? 'عينة غير كافية' : '${attendance['percentage']}%'}',
              ),
              onPressed: () => onReport('attendance'),
            ),
            ActionChip(
              avatar: const Icon(Icons.groups_outlined, size: 18),
              label: UiText('أماكن متاحة · ${capacity['seats']}'),
              onPressed: onCapacity,
            ),
          ],
        ),
        const SizedBox(height: 12),
        const Heading('مركز القرارات الذكية'),
        const UiText('قواعد من السجلات؛ لا إرسال رسائل أو خصومات تلقائية.'),
        if ((overdue['amount'] as num? ?? 0) > 0)
          decision(
            Icons.receipt_long_outlined,
            'أولوية 1 · متابعة المستحقات',
            '${overdue['families']} أسر مرتبطة · مراجعة كشف التحصيل للتحضير',
            () => onReport('receivables'),
          ),
        if ((risk['players'] as num? ?? 0) > 0)
          decision(
            Icons.restart_alt,
            'أولوية 2 · متابعة التجديد',
            textOf(risk['basis']),
            () => onReport('renewals'),
          ),
        if ((capacity['seats'] as num? ?? 0) > 0)
          decision(
            Icons.event_seat_outlined,
            'أولوية 3 · مراجعة الأماكن المتاحة',
            'فرصة مشروطة ${money(capacity['conditionalValue'], data['currency'])} قبل الخصومات؛ ليست ربحًا. ${capacity['unpricedGroups']} مجموعات بلا سعر مؤهل.',
            onCapacity,
          ),
        if ((overdue['amount'] as num? ?? 0) <= 0 &&
            (risk['players'] as num? ?? 0) == 0 &&
            (capacity['seats'] as num? ?? 0) == 0)
          const UiText('لا توجد إجراءات وفق هذه القواعد حاليًا.'),
      ],
    );
  }
}

class OwnerCapacityPage extends StatelessWidget {
  const OwnerCapacityPage({
    super.key,
    required this.auth,
    this.filters = const {},
  });
  final AuthController auth;
  final Map<String, String> filters;
  @override
  Widget build(BuildContext context) => GuardianPage(
    title: 'أماكن التدريب المتاحة',
    child: OwnerScopedRead(
      auth: auth,
      path:
          '/api/v1/reports/owner-cockpit?${Uri(queryParameters: filters).query}',
      builder: (context, value) {
        final d = obj(value), capacity = obj(d['capacity']);
        final items = rows(capacity['items'])
            .where((g) => (g['available'] as num? ?? 0) > 0)
            .toList();
        return gList([
          UiText(textOf(capacity['basis'])),
          UiText(
            '${capacity['unknownGroups']} مجموعات بلا سعة موثقة · ${capacity['unpricedGroups']} بلا سعر مؤهل',
          ),
          if (items.isEmpty)
            const UiText('لا توجد أماكن موثقة متاحة ضمن النطاق.'),
          for (final g in items)
            GCard(
              child: ListTile(
                contentPadding: EdgeInsets.zero,
                title: UiText('${g['name']} · ${g['available']} مكان'),
                subtitle: UiText(
                  '${g['branch']} · ${g['sport']}\nالإشغال ${g['occupied']} / ${g['capacity']}\n${g['conditionalValue'] == null ? 'لا باقة شهرية مؤهلة' : 'قيمة مشروطة ${money(g['conditionalValue'], d['currency'])}'}',
                ),
                trailing: const Icon(Icons.chevron_left),
                onTap: () => openPage(
                  context,
                  OwnerGroupOperations(auth: auth, id: textOf(g['groupId'])),
                ),
              ),
            ),
        ]);
      },
    ),
  );
}

class NativeOperatingStatement extends StatelessWidget {
  const NativeOperatingStatement({
    super.key,
    required this.auth,
    this.filters = const {},
  });
  final AuthController auth;
  final Map<String, String> filters;
  @override
  Widget build(BuildContext context) => OwnerScopedRead(
    auth: auth,
    path:
        '/api/v1/reports/operating-statement?${Uri(queryParameters: filters).query}',
    builder: (context, value) {
      final d = obj(value);
      return gList([
        const Heading('الفائض والعجز التشغيلي'),
        const UiText('أساس نقدي؛ ليس ربحًا نهائيًا على أساس الاستحقاق.'),
        UiText('${d['from']} — ${d['to']}'),
        Facts([
          ('المقبوض بعد الاسترداد', money(d['totalIncome'], d['currency'])),
          ('المدفوعات التشغيلية', money(d['totalExpenses'], d['currency'])),
          ('الفائض / العجز النقدي', money(d['surplus'], d['currency'])),
        ]),
        for (final section in ['income', 'expenses']) ...[
          Heading(section == 'income' ? 'المقبوضات' : 'المدفوعات'),
          for (final line in rows(d[section]))
            Facts([
              (textOf(line['label']), money(line['amount'], d['currency'])),
            ]),
        ],
        const Heading('أرصدة منفصلة لا تضاف إلى المقبوض'),
        Facts([
          ('مستحقات العملاء', money(d['receivables'], d['currency'])),
          ('رواتب مستحقة', money(d['approvedPayrollPayable'], d['currency'])),
          (
            'مصروفات غير مسددة',
            money(d['unpaidOperatingExpenses'], d['currency']),
          ),
        ]),
        for (final warning in (d['warnings'] as List? ?? const []))
          UiText('• $warning'),
      ]);
    },
  );
}
