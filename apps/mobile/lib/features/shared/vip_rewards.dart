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

abstract class _VipContextState<T extends StatefulWidget> extends State<T> {
  AuthController get auth;
  late final AuthController watched = auth;
  late final String? opened = followUpContext(auth);
  bool revoked = false;
  bool get current =>
      mounted &&
      !revoked &&
      identical(auth, watched) &&
      opened != null &&
      opened == followUpContext(auth);
  @override
  void initState() {
    super.initState();
    opened;
    watched.addListener(contextChanged);
  }

  void contextChanged() {
    if (opened != followUpContext(watched)) revoked = true;
    if (mounted) setState(() {});
  }

  bool redactFailure(Object failure) {
    if (failure is ApiFailure && [401, 403].contains(failure.status)) {
      revoked = true;
      if (mounted) setState(() {});
      return true;
    }
    return false;
  }

  @override
  void dispose() {
    watched.removeListener(contextChanged);
    super.dispose();
  }
}

class NativeVipRewards extends StatefulWidget {
  const NativeVipRewards({
    super.key,
    required this.auth,
    this.owner = false,
    this.rewardId,
  });
  final AuthController auth;
  final bool owner;
  final String? rewardId;
  @override
  State<NativeVipRewards> createState() => _NativeVipRewardsState();
}

class _NativeVipRewardsState extends _VipContextState<NativeVipRewards> {
  @override
  AuthController get auth => widget.auth;
  int page = 1, version = 0;
  bool busy = false;
  String error = '';
  String? pendingCancel;
  String message = '';
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
    if (ok != true || !current) return;
    setState(() {
      busy = true;
      error = '';
      message = '';
      pendingCancel = id;
    });
    var accepted = false;
    try {
      await widget.auth.request('POST', '$root/$id/cancel', body: {});
      accepted = true;
      await verifyCancellation();
    } catch (e) {
      if (redactFailure(e)) return;
      if (current) {
        setState(() {
          if (!accepted &&
              e is ApiFailure &&
              e.status >= 400 &&
              e.status < 500) {
            pendingCancel = null;
            version++;
            error = RegExp(r'[\u0600-\u06ff]').hasMatch(e.message)
                ? e.message
                : 'رُفض الإلغاء. راجع حالة المكافأة الحالية.';
          } else {
            error =
                'لم يتأكد الإلغاء. أعد التحقق من السجل دون إرسال إلغاء آخر.';
          }
        });
      }
    } finally {
      if (current) setState(() => busy = false);
    }
  }

  Future<void> verifyCancellation() async {
    if (!current || pendingCancel == null) return;
    final id = pendingCancel!;
    final data = obj(await auth.request('GET', '$root/$id'));
    if (!current) return;
    final item = rows(data['items']);
    if (item.length != 1 ||
        item.single['id'] != id ||
        item.single['state'] != 'Cancelled') {
      throw StateError('Cancellation was not read back.');
    }
    setState(() {
      pendingCancel = null;
      error = '';
      message = 'أُلغي الاستحقاق وأُعيد التحقق من سجله.';
      version++;
    });
  }

  Future<void> recheckCancellation() async {
    if (!current || busy) return;
    setState(() => busy = true);
    try {
      await verifyCancellation();
    } catch (e) {
      if (!redactFailure(e) && current) {
        setState(() => error = 'تعذر التحقق من الإلغاء. حاول القراءة مجددًا.');
      }
    } finally {
      if (current) setState(() => busy = false);
    }
  }

  @override
  Widget build(BuildContext context) => !current
      ? const SizedBox.shrink()
      : GuardianPage(
          title: 'مكافآت الأعضاء المميزين',
          child: RemoteBody(
            key: ValueKey('$page:$version'),
            auth: widget.auth,
            path: widget.rewardId == null
                ? '$root?page=$page'
                : '$root/${widget.rewardId}',
            builder: (context, value) {
              final d = obj(value);
              if (widget.rewardId != null &&
                  (rows(d['items']).length != 1 ||
                      rows(d['items']).single['id'] != widget.rewardId)) {
                return const GCard(
                  child: UiText(
                    'تعذر التحقق من المكافأة المحددة. أعد فتحها من التنبيه.',
                  ),
                );
              }
              return ListView(
                padding: const EdgeInsets.all(16),
                children: [
                  const UiText(
                    'استحقاق خصم محفوظ، لا رصيد نقدي. يظهر عند اختيار الباقة إذا كان أفضل من العرض الحالي.',
                  ),
                  if (widget.owner && widget.rewardId == null)
                    FilledButton.icon(
                      icon: const Icon(Icons.card_giftcard_rounded),
                      label: const UiText('إصدار مكافأة'),
                      onPressed: busy || pendingCancel != null
                          ? null
                          : () async {
                              await Navigator.of(context).push(
                                MaterialPageRoute(
                                  builder: (_) =>
                                      NativeVipIssue(auth: widget.auth),
                                ),
                              );
                              if (current) setState(() => version++);
                            },
                    ),
                  if (error.isNotEmpty) UiText(error),
                  if (message.isNotEmpty) UiText(message),
                  if (pendingCancel != null)
                    OutlinedButton(
                      onPressed: busy ? null : recheckCancellation,
                      child: const UiText('إعادة التحقق من الإلغاء'),
                    ),
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
                            leading: const Icon(
                              Icons.workspace_premium_rounded,
                            ),
                            title: UiText(
                              widget.owner
                                  ? textOf(r['guardian'])
                                  : 'مكافأة أسرتك',
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
                            r['delivery'] == 'Disabled'
                                ? 'التنبيه معطل؛ المكافأة محفوظة حسب شروطها.'
                                : 'إشعار داخلي محفوظ · ${r['read'] == true ? 'قرأته الأسرة' : 'لم يقرأ بعد'}',
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
                              onPressed: busy || pendingCancel != null
                                  ? null
                                  : () => cancel(textOf(r['id'])),
                              child: const UiText('إلغاء المكافأة'),
                            ),
                        ],
                      ),
                    ),
                  if (widget.rewardId == null) const SizedBox.shrink(),
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

class _NativeVipIssueState extends _VipContextState<NativeVipIssue> {
  @override
  AuthController get auth => widget.auth;
  final percentage = TextEditingController();
  final message = TextEditingController(
    text: 'لديكم مكافأة من الأكاديمية. راجعوا نسبة الخصم وموعد الصلاحية.',
  );
  final selected = <String>{};
  final requestId = followUpRequestId();
  Json? preview;
  Json? intent;
  List<String>? issuedIds;
  bool canRetrySame = false;
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

  Future<void> send(bool confirm, {bool retrySame = false}) async {
    if (busy || !current) return;
    final pct = double.tryParse(percentage.text.replaceAll(',', '.'));
    if (intent == null &&
        (selected.isEmpty ||
            pct == null ||
            pct <= 0 ||
            pct >= 100 ||
            message.text.trim().isEmpty ||
            expires.isEmpty)) {
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
    final alreadyFrozen = intent != null;
    var posted = false;
    var accepted = false;
    try {
      if (!confirm) {
        final result = await auth.request(
          'POST',
          '/api/v1/manage/vip-rewards/preview',
          body: {
            'requestId': requestId,
            'guardianIds': selected.toList(),
            'percentage': pct,
            'message': message.text.trim(),
            'expiresOn': expires,
          },
        );
        if (current) setState(() => preview = obj(result));
      } else {
        if (intent == null) {
          if (preview == null) return;
          intent = Map<String, dynamic>.unmodifiable({
            'requestId': requestId,
            'guardianIds': List<String>.unmodifiable(selected.toList()..sort()),
            'percentage': pct,
            'message': message.text.trim(),
            'expiresOn': expires,
            'quoteHash': preview!['quoteHash'],
          });
        }
        if (!alreadyFrozen || retrySame && canRetrySame) {
          canRetrySame = false;
          posted = true;
          final result = obj(
            await auth.request(
              'POST',
              '/api/v1/manage/vip-rewards/',
              body: intent,
            ),
          );
          accepted = true;
          if (!current) return;
          final ids = (result['ids'] as List? ?? []).map(textOf).toList();
          issuedIds =
              ids.length == selected.length && ids.toSet().length == ids.length
              ? ids
              : null;
        }
        await verifyIssue();
      }
    } catch (e) {
      if (redactFailure(e)) return;
      if (current) {
        if (posted &&
            !accepted &&
            !alreadyFrozen &&
            e is ApiFailure &&
            e.status >= 400 &&
            e.status < 500) {
          intent = null;
          preview = null;
          issuedIds = null;
        }
        setState(
          () => error = intent != null
              ? 'لم يتأكد الإصدار. أعد التحقق من النتيجة المحفوظة؛ البيانات مقفلة لتجنب التكرار.'
              : e is ApiFailure &&
                    RegExp(r'[\u0600-\u06ff]').hasMatch(e.message)
              ? e.message
              : 'تعذر إتمام الطلب. راجع البيانات وحاول مجددًا.',
        );
      }
    } finally {
      if (current) setState(() => busy = false);
    }
  }

  Future<void> verifyIssue() async {
    if (!current || intent == null) return;
    if (issuedIds == null) {
      try {
        final result = obj(
          await auth.request(
            'GET',
            '/api/v1/manage/vip-rewards/requests/$requestId',
          ),
        );
        if (!current) return;
        if (result['requestId'] != requestId) {
          throw StateError('Different issuance attempt.');
        }
        issuedIds = (result['ids'] as List? ?? []).map(textOf).toList();
        canRetrySame = false;
      } on ApiFailure catch (e) {
        if (e.status == 404 && current) canRetrySame = true;
        rethrow;
      }
    }
    final expected = (intent!['guardianIds'] as List).cast<String>().toSet();
    final ids = issuedIds!;
    if (ids.length != expected.length || ids.toSet().length != ids.length) {
      throw StateError('Invalid issuance result.');
    }
    final found = <String>{};
    for (final id in ids) {
      if (!current) return;
      final detail = obj(
        await auth.request('GET', '/api/v1/manage/vip-rewards/$id'),
      );
      if (!current) return;
      final items = rows(detail['items']);
      if (items.length != 1) throw StateError('Missing issued reward.');
      final r = items.single;
      if (r['id'] != id ||
          r['requestId'] != requestId ||
          !expected.contains(r['guardianId']) ||
          !found.add(textOf(r['guardianId'])) ||
          num.tryParse('${r['percentage']}') != intent!['percentage'] ||
          r['message'] != intent!['message'] ||
          r['expiresOn'] != intent!['expiresOn'] ||
          preview?['currency'] != null &&
              r['currency'] != preview!['currency']) {
        throw StateError('Issued reward differs from the frozen preview.');
      }
    }
    if (mounted && current) Navigator.of(context).pop(true);
  }

  @override
  Widget build(BuildContext context) => !current
      ? const SizedBox.shrink()
      : GuardianPage(
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
                  const Heading('اختر الأسر'),
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
                            onChanged: busy || intent != null
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
                    enabled: !busy && intent == null,
                    keyboardType: const TextInputType.numberWithOptions(
                      decimal: true,
                    ),
                    decoration: const InputDecoration(
                      labelText: 'نسبة الخصم %',
                    ),
                    onChanged: (_) => change(),
                  ),
                  const SizedBox(height: 12),
                  TextField(
                    controller: message,
                    enabled: !busy && intent == null,
                    maxLength: 500,
                    maxLines: 3,
                    decoration: const InputDecoration(
                      labelText: 'رسالة الأسرة',
                    ),
                    onChanged: (_) => change(),
                  ),
                  OutlinedButton.icon(
                    icon: const Icon(Icons.event),
                    label: UiText(expires.isEmpty ? 'صالحة حتى' : expires),
                    onPressed: busy || intent != null || today == null
                        ? null
                        : () async {
                            final date = await showDatePicker(
                              context: context,
                              initialDate: DateTime.tryParse(expires) ?? today,
                              firstDate: today,
                              lastDate: today.add(const Duration(days: 366)),
                            );
                            if (date != null && current && intent == null) {
                              change();
                              setState(
                                () => expires = date
                                    .toIso8601String()
                                    .split('T')
                                    .first,
                              );
                            }
                          },
                  ),
                  UiText(textOf(d['conditions'])),
                  const SizedBox(height: 12),
                  OutlinedButton(
                    onPressed: busy || intent != null
                        ? null
                        : () => send(false),
                    child: const UiText('معاينة المكافأة'),
                  ),
                  if (intent != null) ...[
                    const UiText(
                      'الطلب مقفل حتى التحقق من الإصدار. إعادة التحقق تقرأ السجل فقط.',
                    ),
                    OutlinedButton(
                      onPressed: busy ? null : () => send(true),
                      child: const UiText('إعادة التحقق من الإصدار'),
                    ),
                    if (canRetrySame)
                      OutlinedButton(
                        onPressed: busy
                            ? null
                            : () => send(true, retrySame: true),
                        child: const UiText('إعادة تأكيد الطلب نفسه'),
                      ),
                  ],
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
                            onPressed: busy || intent != null
                                ? null
                                : () => send(true),
                            child: const UiText(
                              'تأكيد الإصدار وحفظ الإشعار الداخلي',
                            ),
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
