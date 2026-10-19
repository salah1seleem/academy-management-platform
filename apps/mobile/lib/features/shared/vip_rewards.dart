import 'package:flutter/material.dart';

import '../../core/api/api_client.dart';
import '../../core/auth/auth_controller.dart';
import '../../core/theme/ui_text.dart';
import '../guardian/guardian_ui.dart';
import '../owner/owner_followups.dart';

const vipStates = {
  'Available': 'متاحة',
  'Reserved': 'محجوزة لطلب',
  'Used': 'استخدمت',
  'Expired': 'انتهت',
  'Cancelled': 'ألغيت',
};

class NativeVipRewards extends StatefulWidget {
  const NativeVipRewards({super.key, required this.auth, this.owner = false});
  final AuthController auth;
  final bool owner;
  @override
  State<NativeVipRewards> createState() => _NativeVipRewardsState();
}

class _NativeVipRewardsState extends State<NativeVipRewards> {
  int page = 1, version = 0;
  bool busy = false;
  String error = '';
  String get root => widget.owner
      ? '/api/v1/manage/vip-rewards'
      : '/api/v1/guardian/vip-rewards';
  Future<void> cancel(String id) async {
    final ok = await showDialog<bool>(
      context: context,
      builder: (c) => AlertDialog(
        title: const UiText('إلغاء المكافأة؟'),
        content: const UiText('لن يتغير أي اشتراك مدفوع.'),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(c, false),
            child: const UiText('رجوع'),
          ),
          TextButton(
            onPressed: () => Navigator.pop(c, true),
            child: const UiText('تأكيد الإلغاء'),
          ),
        ],
      ),
    );
    if (ok != true || !mounted) return;
    setState(() {
      busy = true;
      error = '';
    });
    try {
      await widget.auth.request('POST', '$root/$id/cancel', body: {});
      if (mounted) setState(() => version++);
    } catch (e) {
      if (mounted) {
        setState(() => error = e is ApiFailure ? e.message : 'تعذر الإلغاء.');
      }
    } finally {
      if (mounted) setState(() => busy = false);
    }
  }

  @override
  Widget build(BuildContext context) => GuardianPage(
    title: 'مكافآت الأعضاء المميزين',
    child: RemoteBody(
      key: ValueKey('$page:$version'),
      auth: widget.auth,
      path: '$root?page=$page',
      builder: (context, value) {
        final d = obj(value);
        return ListView(
          padding: const EdgeInsets.all(16),
          children: [
            const UiText(
              'استحقاق خصم محفوظ، لا رصيد نقدي. يظهر عند اختيار الباقة إذا كان أفضل من العرض الحالي.',
            ),
            if (widget.owner)
              FilledButton.icon(
                icon: const Icon(Icons.card_giftcard_rounded),
                label: const UiText('إصدار مكافأة'),
                onPressed: busy
                    ? null
                    : () async {
                        await Navigator.of(context).push(
                          MaterialPageRoute(
                            builder: (_) => NativeVipIssue(auth: widget.auth),
                          ),
                        );
                        if (mounted) setState(() => version++);
                      },
              ),
            if (error.isNotEmpty) UiText(error),
            UiText(textOf(d['conditions'])),
            if (rows(d['items']).isEmpty)
              const GCard(child: UiText('لا توجد مكافآت مسجلة بعد.')),
            for (final r in rows(d['items']))
              GCard(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                    ListTile(
                      contentPadding: EdgeInsets.zero,
                      leading: const Icon(Icons.workspace_premium_rounded),
                      title: UiText(
                        widget.owner ? textOf(r['guardian']) : 'مكافأة أسرتك',
                      ),
                      trailing: UiText(
                        '${r['percentage']}%',
                        style: const TextStyle(
                          fontSize: 24,
                          fontWeight: FontWeight.w800,
                        ),
                      ),
                    ),
                    UiText(textOf(r['message'])),
                    const SizedBox(height: 10),
                    UiText(
                      '${vipStates[r['state']] ?? 'غير متاحة'} · حتى ${r['expiresOn']}',
                    ),
                    UiText(
                      'إشعار داخلي محفوظ · ${r['read'] == true ? 'قرأته الأسرة' : 'لم يقرأ بعد'}',
                    ),
                    for (final h in rows(r['history']))
                      UiText(
                        'حجز ${textOf(h['createdAtUtc']).split('T').first}${h['usedAtUtc'] != null
                            ? ' · استخدمت'
                            : h['releasedAtUtc'] != null
                            ? ' · حرر الحجز'
                            : ''}',
                      ),
                    if (widget.owner &&
                        ['Available', 'Expired'].contains(r['state']))
                      TextButton(
                        onPressed: busy ? null : () => cancel(textOf(r['id'])),
                        child: const UiText('إلغاء المكافأة'),
                      ),
                  ],
                ),
              ),
            Row(
              mainAxisAlignment: MainAxisAlignment.spaceBetween,
              children: [
                TextButton(
                  onPressed: page == 1 ? null : () => setState(() => page--),
                  child: const UiText('السابق'),
                ),
                UiText('$page'),
                TextButton(
                  onPressed: page * 25 >= (d['count'] as num? ?? 0)
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
  );
}

class NativeVipIssue extends StatefulWidget {
  const NativeVipIssue({super.key, required this.auth});
  final AuthController auth;
  @override
  State<NativeVipIssue> createState() => _NativeVipIssueState();
}

class _NativeVipIssueState extends State<NativeVipIssue> {
  final percentage = TextEditingController();
  final message = TextEditingController(
    text: 'شكرًا لثقتكم بأكاديميتنا. يسعدنا تقديم مكافأة خاصة لدعم رحلة أبنائكم الرياضية.',
  );
  final selected = <String>{};
  final requestId = followUpRequestId();
  late final identity = followUpContext(widget.auth);
  Json? preview;
  String expires = '', error = '';
  bool busy = false;
  @override
  void dispose() {
    percentage.dispose();
    message.dispose();
    super.dispose();
  }

  void change() {
    setState(() {
      preview = null;
      error = '';
    });
  }

  Future<void> send(bool confirm) async {
    if (busy || identity != followUpContext(widget.auth)) return;
    final pct = double.tryParse(percentage.text.replaceAll(',', '.'));
    if (selected.isEmpty ||
        pct == null ||
        pct <= 0 ||
        pct >= 100 ||
        message.text.trim().isEmpty ||
        expires.isEmpty) {
      setState(
        () => error =
            'اختر الأسرة، ونسبة أكبر من0وأقل من100، والرسالة وتاريخ الانتهاء.',
      );
      return;
    }
    setState(() {
      busy = true;
      error = '';
    });
    try {
      final result = await widget.auth.request(
        'POST',
        confirm
            ? '/api/v1/manage/vip-rewards/'
            : '/api/v1/manage/vip-rewards/preview',
        body: {
          'requestId': requestId,
          'guardianIds': selected.toList(),
          'percentage': pct,
          'message': message.text.trim(),
          'expiresOn': expires,
          if (confirm) 'quoteHash': preview?['quoteHash'],
        },
      );
      if (mounted && identity == followUpContext(widget.auth)) {
        if (confirm) {
          Navigator.of(context).pop(true);
        } else {
          setState(() => preview = obj(result));
        }
      }
    } catch (e) {
      if (mounted) {
        setState(
          () => error = e is ApiFailure
              ? e.message
              : 'تعذر الحفظ. أعد المحاولة بنفس البيانات دون تكرار المكافأة.',
        );
      }
    } finally {
      if (mounted) setState(() => busy = false);
    }
  }

  @override
  Widget build(BuildContext context) => GuardianPage(
    title: 'إصدار مكافأة',
    child: RemoteBody(
      auth: widget.auth,
      path: '/api/v1/manage/vip-rewards/options',
      builder: (context, value) {
        final d = obj(value);
        final today = DateTime.tryParse(textOf(d['today']));
        return ListView(
          padding: const EdgeInsets.all(16),
          children: [
            const Heading('مكافأة تستحقها الأسرة'),
            const UiText(
              'اختر من أعلى10أسر اليوم. المعاينة لا تنشئ استحقاقًا ولا إشعارًا.',
            ),
            if (error.isNotEmpty) UiText(error),
            GCard(
              child: Column(
                children: [
                  for (final g in rows(d['items']))
                    CheckboxListTile(
                      dense: true,
                      contentPadding: EdgeInsets.zero,
                      title: UiText(textOf(g['name'])),
                      subtitle: UiText(
                        '${money(g['monthlyValue'], d['currency'])} شهريًا',
                      ),
                      value: selected.contains(g['id']),
                      onChanged: busy
                          ? null
                          : (v) {
                              change();
                              setState(() {
                                if (v == true) {
                                  selected.add(textOf(g['id']));
                                } else {
                                  selected.remove(g['id']);
                                }
                              });
                            },
                    ),
                ],
              ),
            ),
            TextField(
              controller: percentage,
              enabled: !busy,
              keyboardType: const TextInputType.numberWithOptions(
                decimal: true,
              ),
              decoration: const InputDecoration(labelText: 'نسبة الخصم %'),
              onChanged: (_) => change(),
            ),
            const SizedBox(height: 12),
            TextField(
              controller: message,
              enabled: !busy,
              maxLength: 500,
              maxLines: 3,
              decoration: const InputDecoration(labelText: 'رسالة الأسرة'),
              onChanged: (_) => change(),
            ),
            OutlinedButton.icon(
              icon: const Icon(Icons.event),
              label: UiText(expires.isEmpty ? 'صالحة حتى' : expires),
              onPressed: busy || today == null
                  ? null
                  : () async {
                      final date = await showDatePicker(
                        context: context,
                        initialDate: DateTime.tryParse(expires) ?? today,
                        firstDate: today,
                        lastDate: today.add(const Duration(days: 366)),
                      );
                      if (date != null && mounted) {
                        change();
                        setState(
                          () =>
                              expires = date.toIso8601String().split('T').first,
                        );
                      }
                    },
            ),
            UiText(textOf(d['conditions'])),
            const SizedBox(height: 12),
            OutlinedButton(
              onPressed: busy ? null : () => send(false),
              child: const UiText('معاينة المكافأة'),
            ),
            if (preview != null)
              GCard(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                    const Heading('تأكيد الإصدار'),
                    UiText(
                      '${rows(preview!['recipients']).map((g) => g['name']).join('، ')} · ${preview!['percentage']}%',
                    ),
                    UiText(textOf(preview!['message'])),
                    UiText('حتى ${preview!['expiresOn']}'),
                    UiText(textOf(preview!['delivery'])),
                    FilledButton(
                      onPressed: busy ? null : () => send(true),
                      child: const UiText('تأكيد الإصدار وحفظ الإشعار الداخلي'),
                    ),
                  ],
                ),
              ),
          ],
        );
      },
    ),
  );
}
