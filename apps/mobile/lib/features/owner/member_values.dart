import 'package:flutter/material.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/theme/ui_text.dart';
import '../guardian/guardian_ui.dart';
import '../shared/vip_rewards.dart';

class OwnerVipMembers extends StatelessWidget {
  const OwnerVipMembers({
    super.key,
    required this.auth,
    this.branch = '',
    this.asOf = '',
  });
  final AuthController auth;
  final String branch, asOf;
  @override
  Widget build(BuildContext context) {
    final q = Uri(
      queryParameters: {
        if (branch.isNotEmpty) 'branchId': branch,
        if (asOf.isNotEmpty) 'asOf': asOf,
      },
    ).query;
    return GCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Row(
            children: [
              const Expanded(child: Heading('أعضاء مميزون')),
              TextButton(
                onPressed: () => openPage(
                  context,
                  OwnerGuardianValues(auth: auth, branch: branch, asOf: asOf),
                ),
                child: const UiText('عرض الكل'),
              ),
            ],
          ),
          const UiText('قيمة شهرية مرتبطة بالأبناء، وليست مدفوعات ولي الأمر.'),
          TextButton.icon(
            onPressed: () =>
                openPage(context, NativeVipRewards(auth: auth, owner: true)),
            icon: const Icon(Icons.card_giftcard_rounded),
            label: const UiText('إدارة مكافآت الأعضاء المميزين'),
          ),
          RemoteBody(
            auth: auth,
            path: '/api/v1/reports/vip-members?$q',
            builder: (context, value) {
              final items = rows(obj(value)['items']);
              return Column(
                children: [
                  if (items.isEmpty)
                    const UiText(
                      'لا توجد اشتراكات سارية ذات قيمة شهرية مكتملة.',
                    ),
                  for (final row in items)
                    ListTile(
                      dense: true,
                      contentPadding: EdgeInsets.zero,
                      title: UiText(textOf(obj(row)['displayName'])),
                      trailing: UiText(
                        money(
                          obj(row)['monthlySubscriptionValue'],
                          obj(row)['currency'],
                        ),
                      ),
                      onTap: () => openPage(
                        context,
                        MemberGuardianDetail(
                          auth: auth,
                          id: textOf(obj(row)['id']),
                        ),
                      ),
                    ),
                ],
              );
            },
          ),
        ],
      ),
    );
  }
}

class OwnerGuardianValues extends StatefulWidget {
  const OwnerGuardianValues({
    super.key,
    required this.auth,
    this.branch = '',
    this.asOf = '',
  });
  final AuthController auth;
  final String branch, asOf;
  @override
  State<OwnerGuardianValues> createState() => _OwnerGuardianValuesState();
}

class _OwnerGuardianValuesState extends State<OwnerGuardianValues> {
  String search = '', sort = 'value_desc';
  int page = 0;
  @override
  Widget build(BuildContext context) {
    final q = Uri(
      queryParameters: {
        'search': search,
        'sort': sort,
        'page': '${page + 1}',
        'pageSize': '25',
        if (widget.branch.isNotEmpty) 'branchId': widget.branch,
        if (widget.asOf.isNotEmpty) 'asOf': widget.asOf,
      },
    ).query;
    return GuardianPage(
      title: 'أولياء الأمور',
      child: Column(
        children: [
          Padding(
            padding: const EdgeInsets.all(12),
            child: TextField(
              decoration: const InputDecoration(
                labelText: 'البحث بالاسم أو الهاتف',
                prefixIcon: Icon(Icons.search),
              ),
              onSubmitted: (v) => setState(() {
                search = v;
                page = 0;
              }),
            ),
          ),
          Padding(
            padding: const EdgeInsets.symmetric(horizontal: 12),
            child: DropdownButtonFormField<String>(
              initialValue: sort,
              decoration: const InputDecoration(labelText: 'إجمالي الاشتراكات'),
              items: const [
                DropdownMenuItem(
                  value: 'value_desc',
                  child: UiText('الأعلى أولًا'),
                ),
                DropdownMenuItem(
                  value: 'value_asc',
                  child: UiText('الأقل أولًا'),
                ),
                DropdownMenuItem(value: 'name', child: UiText('بالاسم')),
              ],
              onChanged: (v) => setState(() {
                sort = v ?? 'value_desc';
                page = 0;
              }),
            ),
          ),
          Expanded(
            child: RemoteBody(
              auth: widget.auth,
              path: '/api/v1/people/guardians?$q',
              builder: (context, value) {
                final d = obj(value);
                final items = rows(d['items']);
                final total = d['total'] as int? ?? 0;
                return ListView(
                  padding: const EdgeInsets.all(12),
                  children: [
                    const UiText(
                      'القيمة الشهرية للأبناء المرتبطين؛ ليست سجل مدفوعات الأسرة.',
                    ),
                    if (items.isEmpty) const UiText('لا توجد نتائج مطابقة.'),
                    for (final row in items)
                      GCard(
                        child: ListTile(
                          contentPadding: EdgeInsets.zero,
                          title: UiText(textOf(obj(row)['displayName'])),
                          subtitle: UiText(
                            '${obj(row)['contactPhone']}\n${obj(row)['linkedChildren']} أبناء',
                          ),
                          trailing: Column(
                            mainAxisSize: MainAxisSize.min,
                            children: [
                              UiText(
                                money(
                                  obj(row)['monthlySubscriptionValue'],
                                  obj(row)['currency'],
                                ),
                              ),
                              if ((obj(row)['unpricedSubscriptions'] as num? ??
                                      0) >
                                  0)
                                const UiText('قيمة غير مكتملة'),
                            ],
                          ),
                          onTap: () => openPage(
                            context,
                            MemberGuardianDetail(
                              auth: widget.auth,
                              id: textOf(obj(row)['id']),
                            ),
                          ),
                        ),
                      ),
                    Row(
                      mainAxisAlignment: MainAxisAlignment.spaceBetween,
                      children: [
                        TextButton(
                          onPressed: page == 0
                              ? null
                              : () => setState(() => page--),
                          child: const UiText('السابق'),
                        ),
                        UiText(
                          '${page + 1} / ${((total + 24) ~/ 25).clamp(1, 1000000)}',
                        ),
                        TextButton(
                          onPressed: (page + 1) * 25 >= total
                              ? null
                              : () => setState(() => page++),
                          child: const UiText('التالي'),
                        ),
                      ],
                    ),
                  ],
                );
              },
            ),
          ),
        ],
      ),
    );
  }
}

class MemberGuardianDetail extends StatelessWidget {
  const MemberGuardianDetail({super.key, required this.auth, required this.id});
  final AuthController auth;
  final String id;
  @override
  Widget build(BuildContext context) => GuardianPage(
    title: 'ولي الأمر',
    child: RemoteBody(
      auth: auth,
      path: '/api/v1/people/guardians/$id',
      builder: (context, value) {
        final d = obj(value);
        return ListView(
          padding: const EdgeInsets.all(16),
          children: [
            Heading(textOf(d['displayName'])),
            UiText(textOf(d['contactPhone'])),
            const Heading('الأبناء المرتبطون'),
            for (final row in rows(d['children']))
              GCard(
                child: ListTile(
                  title: UiText(textOf(obj(row)['arabicName'])),
                  subtitle: UiText(textOf(obj(row)['playerCode'])),
                ),
              ),
          ],
        );
      },
    ),
  );
}
