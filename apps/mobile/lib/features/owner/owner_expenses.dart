import 'package:flutter/material.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/theme/ui_text.dart';
import '../guardian/guardian_ui.dart';
import 'owner_operations.dart';

const _expenseRoot = '/api/v1/finance/operating-expenses';
const _categories = {
  'Rent': 'إيجار المنشأة',
  'Electricity': 'الكهرباء',
  'Water': 'المياه',
  'Heating': 'التدفئة',
  'Maintenance': 'الصيانة والتشغيل',
  'CollectionFees': 'رسوم التحصيل الإلكتروني',
  'Supplies': 'المستلزمات',
  'Administration': 'المصروفات الإدارية',
  'Facility': 'تشغيل المرافق',
  'Other': 'مصروفات أخرى',
};
const _states = {
  'Draft': 'مسودة',
  'Approved': 'معتمد للمراجعة',
  'Posted': 'مرحّل',
  'Reversed': 'معكوس',
};

/// Only paid operating-expense lines have a matching native source list.
Map<String, String>? operatingExpenseSource(
  Json line,
  Json statement,
  Map<String, String> scope,
) {
  final source = line['sourceUrl'];
  final url = source is String ? Uri.tryParse(source) : null;
  if (url == null ||
      url.hasScheme ||
      url.hasAuthority ||
      url.path != '/dashboard/finance/operating-expenses' ||
      line['amount'] is! num ||
      line['amount'] == 0) {
    return null;
  }
  final query = url.queryParameters;
  final category = query['category'];
  if (!_categories.containsKey(category) ||
      query['status'] != 'Posted' ||
      query['paid'] != 'true' ||
      query['dateBasis'] != 'payment') {
    return null;
  }
  final allDates = scope['allDates'] == 'true';
  return {
    'category': category!,
    'status': 'Posted',
    'paid': 'true',
    'dateBasis': 'payment',
    if (allDates) 'allDates': 'true',
    if (!allDates)
      'from': (statement['from'] as String?) ?? scope['from'] ?? '',
    if (!allDates) 'to': (statement['to'] as String?) ?? scope['to'] ?? '',
    if (scope['branchId'] case final String id when id.isNotEmpty)
      'branchId': id,
    if (scope['sportId'] case final String id when id.isNotEmpty) 'sportId': id,
  };
}

String _name(Json metadata, String collection, Object? id, String fallback) {
  final list = metadata[collection];
  if (id == null || list is! List) return fallback;
  for (final entry in list) {
    if (entry is Map && entry['id'] == id) {
      return (entry['arabicName'] ?? entry['name'])?.toString() ?? fallback;
    }
  }
  return fallback;
}

String _date(BuildContext context, Object? value) {
  final parsed = DateTime.tryParse(day(value));
  return parsed == null
      ? 'غير محدد'
      : MaterialLocalizations.of(context).formatCompactDate(parsed);
}

String _scope(Json expense, Json metadata) => switch (expense['scope']) {
  'Branch' => 'الفرع كله',
  'Group' => _name(
    metadata,
    'groups',
    expense['trainingGroupId'],
    'مجموعة تدريب',
  ),
  'Resource' => 'مرفق تدريب مشترك',
  'Sport' => _name(metadata, 'sports', expense['sportId'], 'رياضة داخل الفرع'),
  _ => 'غير محدد',
};

class OwnerExpenseList extends StatelessWidget {
  const OwnerExpenseList({
    super.key,
    required this.auth,
    required this.filters,
    required this.metadata,
  });

  final AuthController auth;
  final Map<String, String> filters;
  final Json metadata;

  @override
  Widget build(BuildContext context) {
    final paid = filters['dateBasis'] == 'payment';
    final unpaid = filters['unpaid'] == 'true';
    return OwnerScopedRead(
      auth: auth,
      path: Uri(
        path: _expenseRoot,
        queryParameters: {
          for (final key in [
            'from',
            'to',
            'branchId',
            'sportId',
            'category',
            'status',
            'dateBasis',
            'paid',
            'unpaid',
          ])
            if (filters[key] case final String value when value.isNotEmpty)
              key: value,
          'page': '1',
          'pageSize': '25',
        },
      ).toString(),
      builder: (context, value) {
        final items = rows(obj(value)['items']);
        return gList([
          if (paid)
            Heading(_categories[filters['category']] ?? 'المصروفات المسددة'),
          if (unpaid) const Heading('المصروفات غير المسددة'),
          UiText(
            [
              _name(metadata, 'branches', filters['branchId'], 'كل الفروع'),
              if (filters['sportId'] != null)
                _name(
                  metadata,
                  'sports',
                  filters['sportId'],
                  'الرياضة المحددة',
                ),
              if (paid) 'مسددة ومرحّلة',
              if (unpaid && filters['status'] == 'Posted') 'مرحّلة',
            ].join(' · '),
          ),
          const SizedBox(height: 12),
          if (items.isEmpty)
            EmptyMessage(
              paid
                  ? 'لا توجد مصروفات مسددة ضمن النطاق المحدد.'
                  : unpaid
                  ? 'لا توجد مصروفات غير مسددة ضمن النطاق المحدد.'
                  : 'لا توجد مصروفات مستحقة ضمن النطاق المحدد.',
            ),
          for (final row in items)
            if (row['id'] case final String id when id.isNotEmpty)
              GLink(
                paid
                    ? money(row['amount'], row['currency'])
                    : _categories[row['category']] ?? 'مصروف تشغيلي',
                Icons.payments_outlined,
                () => openPage(
                  context,
                  OwnerExpenseDetail(auth: auth, id: id, metadata: metadata),
                ),
                compact: true,
                subtitle: [
                  if (!paid) ...[
                    money(row['amount'], row['currency']),
                    _states[row['status']] ?? 'حالة غير متاحة',
                  ],
                  _date(context, row[paid ? 'paidOn' : 'periodFrom']),
                  if ((filters['branchId'] ?? '').isEmpty)
                    _name(
                      metadata,
                      'branches',
                      row['branchId'],
                      'الفرع المسجّل',
                    ),
                ].join(' · '),
              )
            else
              const UiText('تعذر فتح سجل مصروف.'),
        ]);
      },
    );
  }
}

class OwnerExpenseDetail extends StatelessWidget {
  const OwnerExpenseDetail({
    super.key,
    required this.auth,
    required this.id,
    required this.metadata,
  });

  final AuthController auth;
  final String id;
  final Json metadata;

  String _shareLabel(Json share) {
    if (share['trainingGroupId'] != null) {
      return _name(
        metadata,
        'groups',
        share['trainingGroupId'],
        'مجموعة محفوظة',
      );
    }
    return switch (share['serviceKind']) {
      'Training' || 'Unallocated' => 'غير موزع على المجموعات',
      'DayPass' => 'زيارة يومية',
      'OpenSwim' => 'سباحة حرة',
      'FacilityBooking' => 'حجز مرفق',
      'Event' => 'فعالية',
      _ => 'خدمة تشغيلية',
    };
  }

  @override
  Widget build(BuildContext context) => GuardianPage(
    title: 'تفاصيل المصروف',
    onBack: () => Navigator.of(context).pop(),
    child: OwnerScopedRead(
      auth: auth,
      path: '$_expenseRoot/${Uri.encodeComponent(id)}',
      builder: (context, value) {
        final data = obj(value);
        final expense = data['expense'];
        if (expense is! Json || expense['id'] != id) {
          return const EmptyMessage('تعذر التحقق من تفاصيل المصروف.');
        }
        final currency = expense['currency'];
        final from = _date(context, expense['periodFrom']);
        final to = _date(context, expense['periodTo']);
        final allocations = data['allocations'];
        return gList([
          Heading(_categories[expense['category']] ?? 'مصروف تشغيلي'),
          GCard(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                MoneyText(expense['amount'], currency, size: 25),
                const SizedBox(height: 12),
                for (final field in [
                  (
                    'الفرع',
                    _name(
                      metadata,
                      'branches',
                      expense['branchId'],
                      'الفرع المسجّل',
                    ),
                  ),
                  ('نطاق التكلفة', _scope(expense, metadata)),
                  ('الحالة', _states[expense['status']] ?? 'حالة غير متاحة'),
                  ('الاستحقاق', from == to ? from : '$from — $to'),
                  (
                    'السداد',
                    expense['paidOn'] == null
                        ? 'غير مسدد'
                        : _date(context, expense['paidOn']),
                  ),
                  if (expense['note'] case final String note
                      when note.trim().isNotEmpty)
                    ('الملاحظة', note),
                ])
                  Padding(
                    padding: const EdgeInsets.symmetric(vertical: 6),
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        UiText(
                          field.$1,
                          style: Theme.of(context).textTheme.labelMedium,
                        ),
                        const SizedBox(height: 3),
                        UiText(field.$2),
                      ],
                    ),
                  ),
              ],
            ),
          ),
          if (expense['status'] == 'Posted') ...[
            const Heading('الأنصبة المحفوظة'),
            if (allocations is List && data['allocationTotal'] is num) ...[
              UiText(
                'إجمالي التوزيع ${money(data['allocationTotal'], currency)}',
              ),
              for (final share in rows(allocations))
                ListTile(
                  title: UiText(_shareLabel(share)),
                  subtitle: MoneyText(
                    share['amount'],
                    share['currency'] ?? currency,
                    size: 15,
                  ),
                ),
            ] else
              const UiText('تعذر عرض الأنصبة المحفوظة.'),
          ],
        ]);
      },
    ),
  );
}
