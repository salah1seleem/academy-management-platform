import 'package:flutter/material.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/theme/ui_text.dart';
import '../guardian/guardian_ui.dart';
import 'owner_operations.dart';

class NativeExternalAgreements extends StatefulWidget {
  const NativeExternalAgreements({
    super.key,
    required this.auth,
    this.branch = '',
  });
  final AuthController auth;
  final String branch;
  @override
  State<NativeExternalAgreements> createState() =>
      _NativeExternalAgreementsState();
}

class _NativeExternalAgreementsState extends State<NativeExternalAgreements> {
  int page = 1;
  String search = '';
  final field = TextEditingController();
  @override
  void dispose() {
    field.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => OwnerScopedRead(
    key: ValueKey('$page:$search'),
    auth: widget.auth,
    path:
        '/api/v1/finance/external-agreements?${Uri(queryParameters: {'allDates': 'true', 'page': '$page', 'pageSize': '25', if (search.isNotEmpty) 'search': search, if (widget.branch.isNotEmpty) 'branchId': widget.branch}).query}',
    builder: (context, value) {
      final d = obj(value);
      return gList([
        const Heading('الاتفاقيات والتأجير'),
        const UiText('حساب التعامل الخارجي مستقل عن الرواتب؛ لا خصم تلقائي.'),
        TextField(
          controller: field,
          decoration: const InputDecoration(
            labelText: 'البحث بالطرف أو الاتفاق',
          ),
          onSubmitted: (v) => setState(() {
            search = v.trim();
            page = 1;
          }),
        ),
        Row(
          children: [
            TextButton(
              onPressed: () => setState(() {
                search = field.text.trim();
                page = 1;
              }),
              child: const UiText('بحث'),
            ),
            TextButton(
              onPressed: () => setState(() {
                field.clear();
                search = '';
                page = 1;
              }),
              child: const UiText('مسح'),
            ),
          ],
        ),
        if (rows(d['items']).isEmpty)
          const UiText('لا اتفاقيات محفوظة في النطاق.'),
        for (final a in rows(d['items']))
          GCard(
            child: ListTile(
              contentPadding: EdgeInsets.zero,
              title: UiText(textOf(a['title'])),
              subtitle: UiText(
                '${a['counterpartyName']} · ${a['branch']}\nالمتبقي ${money(a['remaining'], a['currency'])}',
              ),
              trailing: const Icon(Icons.chevron_left),
              onTap: () => openPage(
                context,
                NativeExternalAgreementDetail(
                  auth: widget.auth,
                  id: textOf(a['id']),
                ),
              ),
            ),
          ),
        Row(
          mainAxisAlignment: MainAxisAlignment.spaceBetween,
          children: [
            TextButton(
              onPressed: page > 1 ? () => setState(() => page--) : null,
              child: const UiText('السابق'),
            ),
            UiText('صفحة $page · ${d['count']} اتفاقات'),
            TextButton(
              onPressed: page * 25 < (d['count'] as num? ?? 0)
                  ? () => setState(() => page++)
                  : null,
              child: const UiText('التالي'),
            ),
          ],
        ),
      ]);
    },
  );
}

class NativeExternalAgreementDetail extends StatelessWidget {
  const NativeExternalAgreementDetail({
    super.key,
    required this.auth,
    required this.id,
  });
  final AuthController auth;
  final String id;
  @override
  Widget build(BuildContext context) => GuardianPage(
    title: 'ملف الاتفاق الخارجي',
    child: OwnerScopedRead(
      auth: auth,
      path: '/api/v1/finance/external-agreements/$id',
      builder: (context, value) {
        final d = obj(value), a = obj(d['agreement']);
        return gList([
          Heading(textOf(a['title'])),
          UiText(textOf(a['counterpartyName'])),
          Facts([
            ('الفترة', '${day(a['fromDate'])} — ${day(a['toDate'])}'),
            ('الاستحقاق', day(a['dueOn'])),
            ('قيمة الاتفاق', money(a['amount'], a['currency'])),
            ('المحصل', money(d['paid'], a['currency'])),
            ('المتبقي', money(d['remaining'], a['currency'])),
          ]),
          const UiText('حساب خارجي مستقل؛ لا يخصم من راتب الموظف أو عمولته.'),
          if (a['cancelledAtUtc'] != null)
            const UiText('ملغى دون تحصيل؛ تاريخه محفوظ.'),
          const Heading('إيصالات التحصيل'),
          for (final r in rows(d['receipts']))
            GCard(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  UiText(money(r['amount'], r['currency'])),
                  UiText(textOf(r['receiptNumber'])),
                  UiText(
                    '${r['paymentMethod'] == 'Cash' ? 'نقدي' : 'إلكتروني مثبت يدويًا'} · ${day(r['receivedOn'] ?? r['receivedAtUtc'])}',
                  ),
                  if (r['paymentReference'] != null)
                    UiText(textOf(r['paymentReference'])),
                ],
              ),
            ),
          if (rows(d['bookings']).isNotEmpty)
            const Heading('مواعيد الموارد المرتبطة'),
          for (final b in rows(d['bookings']))
            GCard(
              child: UiText(
                '${b['resource']} · ${day(b['date'])}\n${textOf(b['startTime']).substring(0, 5)} — ${textOf(b['endTime']).substring(0, 5)} · ${b['state'] == 'Cancelled' ? 'ملغى' : 'محجوز'}',
              ),
            ),
          const UiText(
            'إنشاء الاتفاق وتسجيل التحصيل والطباعة من لوحة الويب للمخولين.',
          ),
        ]);
      },
    ),
  );
}
