import 'package:flutter/material.dart';

import '../../core/api/api_client.dart';
import '../../core/auth/auth_controller.dart';
import '../../core/theme/ui_text.dart';
import '../admin/native_operation_form.dart';
import 'guardian_ui.dart';

/// Uses the existing atomic family review; never collects a line separately.
class NativeFamilyRenewalReview extends StatefulWidget {
  const NativeFamilyRenewalReview({
    super.key,
    required this.auth,
    required this.id,
    required this.data,
    required this.onConfirmed,
  });
  final AuthController auth;
  final String id;
  final Json data;
  final VoidCallback onConfirmed;
  @override
  State<NativeFamilyRenewalReview> createState() =>
      _NativeFamilyRenewalReviewState();
}

class _NativeFamilyRenewalReviewState
    extends NativeOperationState<NativeFamilyRenewalReview>
    with AutomaticKeepAliveClientMixin<NativeFamilyRenewalReview> {
  @override
  bool get wantKeepAlive => true;
  @override
  AuthController get auth => widget.auth;
  final reason = TextEditingController();
  late final groups = <String, String>{
    for (final line in rows(widget.data['items']))
      textOf(line['id']): line['groupId'] == null
          ? ''
          : textOf(line['groupId']),
  };
  Json? intent;
  bool acknowledged = false, mustReload = false;
  String get root => '/api/v1/manage/family-renewals/${widget.id}';
  @override
  void dispose() {
    reason.dispose();
    super.dispose();
  }

  bool get eligible =>
      widget.data['proofUrl'] != null &&
      rows(widget.data['items']).isNotEmpty &&
      rows(widget.data['items']).every(
        (l) =>
            rows(l['groups'] ?? <Json>[])
                .any((g) => g['id'] == groups[textOf(l['id'])]),
      );

  Future<void> decide(bool accept) async {
    if (!current ||
        busy ||
        mustReload ||
        widget.data['canReview'] != true ||
        widget.data['status'] != 'Pending') {
      return;
    }
    if (intent == null) {
      if (accept && !eligible) return;
      final confirmed = await showDialog<bool>(
        context: context,
        builder: (c) => AlertDialog(
          title: UiText(accept ? 'تأكيد تجديد الأسرة' : 'رفض طلب الأسرة'),
          content: UiText(
            accept
                ? 'سيُعتمد كل التسجيلات معًا بقيمة ${money(widget.data['totalAmount'], widget.data['currency'])}. راجع الإثبات والمجموعات؛ لن يُعتمد جزء من الطلب.'
                : 'سيُرفض الطلب كاملًا دون إنشاء تحصيل. لا تعني هذه الخطوة رد تحويل أرسلته الأسرة.',
          ),
          actions: [
            TextButton(
              onPressed: () => Navigator.pop(c, false),
              child: const UiText('رجوع'),
            ),
            FilledButton(
              onPressed: () => Navigator.pop(c, true),
              child: UiText(accept ? 'تأكيد قبول الأسرة' : 'تأكيد رفض الأسرة'),
            ),
          ],
        ),
      );
      if (!current || confirmed != true) return;
      intent = <String, dynamic>{
        'accept': accept,
        'revision': widget.data['revision'],
        'reason': reason.text.trim(),
        'placements': accept
            ? [
                for (final line in rows(widget.data['items']))
                  {'lineId': line['id'], 'groupId': groups[textOf(line['id'])]},
              ]
            : null,
      };
    }
    await run(() async {
      final expected = intent!['accept'] == true ? 'Approved' : 'Rejected';
      if (!acknowledged) {
        try {
          final response = obj(
            await auth.request('POST', '$root/review', body: intent),
          );
          if (response['id'] != widget.id || response['status'] != expected) {
            throw const ApiFailure(
              0,
              'لم تتأكد نتيجة قرار الأسرة. راجع الطلب نفسه دون تحصيل جديد.',
            );
          }
          acknowledged = true;
        } on ApiFailure catch (failure) {
          if (failure.status >= 400 && failure.status < 500) {
            intent = null;
            mustReload = true;
          }
          rethrow;
        }
      }
      final read = obj(await auth.request('GET', root));
      final wanted = rows(widget.data['items']), actual = rows(read['items']);
      if (read['id'] != widget.id ||
          read['status'] != expected ||
          read['currency'] != widget.data['currency'] ||
          !storedAmountMatches(
            read['totalAmount'],
            widget.data['totalAmount'],
          ) ||
          actual.length != wanted.length ||
          actual.map((l) => l['id']).toSet().length != wanted.length) {
        throw const ApiFailure(
          0,
          'لم تتأكد قراءة قرار الأسرة كاملًا. أعد تأكيد الحالة نفسها.',
        );
      }
      final receiptIds = <String>{};
      for (final line in wanted) {
        final saved = actual.where((l) => l['id'] == line['id']).firstOrNull;
        if (saved == null ||
            saved['sportEnrollmentId'] != line['sportEnrollmentId'] ||
            saved['currency'] != line['currency'] ||
            !storedAmountMatches(saved['finalAmount'], line['finalAmount'])) {
          throw const ApiFailure(
            0,
            'لم تتطابق تسجيلات الأسرة المقروءة مع الطلب المراجع.',
          );
        }
        if (expected == 'Approved') {
          final id = textOf(saved['receiptId']);
          if (saved['receiptId'] == null || !receiptIds.add(id)) {
            throw const ApiFailure(0, 'لم تتأكد إيصالات جميع تسجيلات الأسرة.');
          }
          final receipt = obj(
            await auth.request('GET', '/api/v1/subscriptions/receipts/$id'),
          );
          if (receipt['id'] != id ||
              receipt['isVoided'] != false ||
              receipt['playerNameSnapshot'] != line['player'] ||
              receipt['currency'] != line['currency'] ||
              !storedAmountMatches(receipt['amount'], line['finalAmount']) ||
              [
                'originalAmount',
                'discountAmount',
                'finalAmount',
              ].any((k) => !storedAmountMatches(receipt[k], line[k]))) {
            throw const ApiFailure(
              0,
              'لم تتأكد مطابقة إيصال أحد الأبناء. راجع الحالة نفسها دون تحصيل آخر.',
            );
          }
        } else if (saved['receiptId'] != null) {
          throw const ApiFailure(
            0,
            'ظهر إيصال غير متوقع في طلب مرفوض. راجع السجل قبل المتابعة.',
          );
        }
      }
      if (current) widget.onConfirmed();
    });
  }

  @override
  Widget build(BuildContext context) {
    super.build(context);
    return ListenableBuilder(
      listenable: auth,
      builder: (context, _) {
        if (!current) return const SizedBox.shrink();
        if (widget.data['status'] != 'Pending') return const SizedBox.shrink();
        if (widget.data['canReview'] != true) {
          return const UiText(
            'هذا الطلب للقراءة فقط؛ المراجعة تحتاج إذن الإدارة المخوّل.',
          );
        }
        final locked = busy || intent != null || mustReload;
        return GCard(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              const Heading('مراجعة تجديد الأسرة'),
              const UiText(
                'قارن الإثبات مع الإجمالي. القبول يجدد كل التسجيلات معًا، ولا يسمح باعتماد جزئي.',
              ),
              const SizedBox(height: 16),
              for (final line in rows(widget.data['items'])) ...[
                DropdownButtonFormField<String>(
                  key: ValueKey('family-group-${line['id']}'),
                  initialValue: groups[textOf(line['id'])] == ''
                      ? null
                      : groups[textOf(line['id'])],
                  isExpanded: true,
                  itemHeight: null,
                  decoration: InputDecoration(
                    labelText: 'مجموعة ${line['player']}',
                  ),
                  items: [
                    for (final g in rows(line['groups'] ?? <Json>[]))
                      DropdownMenuItem(
                        value: textOf(g['id']),
                        child: UiText(textOf(g['name'])),
                      ),
                    if (line['groupId'] != null &&
                        !rows(line['groups'] ?? <Json>[])
                            .any((g) => g['id'] == line['groupId']))
                      DropdownMenuItem(
                        value: textOf(line['groupId']),
                        child: const UiText(
                          'المجموعة الحالية غير متاحة؛ راجع التسجيل',
                        ),
                      ),
                  ],
                  onChanged: locked || line['groupId'] != null
                      ? null
                      : (v) => setState(
                          () => groups[textOf(line['id'])] = v ?? '',
                        ),
                ),
                if (line['groupId'] != null)
                  const UiText(
                    'المجموعة الحالية محفوظة؛ نقل اللاعب عملية منفصلة.',
                    style: TextStyle(fontSize: 12),
                  ),
                const SizedBox(height: 16),
              ],
              TextField(
                controller: reason,
                enabled: !locked,
                maxLength: 500,
                maxLines: 3,
                decoration: const InputDecoration(
                  labelText: 'ملاحظة أو سبب الرفض (اختياري)',
                ),
              ),
              if (widget.data['proofUrl'] == null)
                const UiText(
                  'لم يصل إثبات الأسرة بعد؛ لا يمكن اعتماد التجديد.',
                ),
              if (error != null)
                UiText(
                  error!,
                  style: TextStyle(color: Theme.of(context).colorScheme.error),
                ),
              if (mustReload)
                OutlinedButton(
                  onPressed: busy ? null : widget.onConfirmed,
                  child: const UiText('تحديث الطلب'),
                )
              else if (intent != null) ...[
                const UiText(
                  'لم نؤكد اكتمال قراءة القرار. الاختيارات محفوظة؛ لا تنشئ تحصيلًا آخر.',
                ),
                FilledButton(
                  onPressed: busy
                      ? null
                      : () => decide(intent!['accept'] == true),
                  child: UiText(
                    acknowledged
                        ? 'إعادة قراءة القرار والإيصالات'
                        : 'إعادة تأكيد نفس القرار',
                  ),
                ),
              ] else ...[
                FilledButton(
                  onPressed: eligible && !busy ? () => decide(true) : null,
                  child: const UiText('قبول وتجديد كل التسجيلات'),
                ),
                OutlinedButton(
                  onPressed: busy ? null : () => decide(false),
                  child: const UiText('رفض الطلب كاملًا'),
                ),
              ],
              if (busy) const Center(child: CircularProgressIndicator()),
            ],
          ),
        );
      },
    );
  }
}
