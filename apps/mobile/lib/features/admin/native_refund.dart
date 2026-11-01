import 'package:flutter/material.dart';

import '../../core/api/api_client.dart';
import '../../core/auth/auth_controller.dart';
import '../../core/theme/ui_text.dart';
import '../guardian/guardian_ui.dart';
import 'native_operation_form.dart';

class NativePlayerRefund extends StatefulWidget {
  const NativePlayerRefund({
    super.key,
    required this.auth,
    required this.playerId,
  });
  final AuthController auth;
  final String playerId;
  @override
  State<NativePlayerRefund> createState() => _NativePlayerRefundState();
}

class _NativePlayerRefundState
    extends NativeOperationState<NativePlayerRefund> {
  @override
  AuthController get auth => widget.auth;
  Json? data, quote, frozen, recorded;
  List<Json> sessions = [];
  String? collection, session;
  String method = 'Online', intent = commandKey();
  bool confirmed = false, uncertain = false;
  final amount = TextEditingController(), reason = TextEditingController();
  List<Json> get eligible =>
      rows(data?['collections'] ?? [])
          .where((c) => (double.tryParse('${c['available']}') ?? 0) > 0)
          .toList();
  Json? get source => eligible.where((c) => c['id'] == collection).firstOrNull;
  String get root => '/api/v1/people/players/${widget.playerId}/refunds';
  @override
  void initState() {
    super.initState();
    load();
  }

  @override
  void dispose() {
    amount.dispose();
    reason.dispose();
    super.dispose();
  }

  Future<void> load() => run(() async {
    final result = obj(await auth.request('GET', root));
    if (current) setState(() => data = result);
  });
  void invalidate() {
    quote = null;
    confirmed = false;
    frozen = null;
    intent = commandKey();
  }

  bool get validAmount =>
      RegExp(r'^\d+(\.\d{1,2})?$').hasMatch(amount.text.trim()) &&
      (double.tryParse(amount.text) ?? 0) > 0 &&
      (double.tryParse(amount.text) ?? 0) <=
          (double.tryParse('${source?['available']}') ?? 0);
  Json body() => {
    'collectionId': collection,
    'amount': amount.text.trim(),
    'reason': reason.text.trim().isEmpty ? null : reason.text.trim(),
    if (method == 'Cash' && session != null) 'cashSessionId': session,
  };
  Future<void> review() => run(() async {
    if (source == null || !validAmount) {
      setState(
        () => error = 'أدخل مبلغًا صحيحًا ضمن المتاح للاسترداد، بحد أقصى منزلتين عشريتين.',
      );
      return;
    }
    final q = obj(await auth.request('POST', '$root/quote', body: body()));
    if ((double.tryParse('${q['amount']}') ?? 0) >
        (double.tryParse('${q['available']}') ?? 0)) {
      throw const ApiFailure(
        409,
        'المبلغ يتجاوز المتاح الحالي. حدّث التحصيل قبل المراجعة.',
      );
    }
    if (current) {
      setState(() {
        quote = q;
        sessions = rows(q['sessions'] ?? []);
        confirmed = false;
        frozen = null;
        intent = commandKey();
      });
    }
  });
  Future<void> commit() => run(() async {
    if (!confirmed ||
        quote == null ||
        !validAmount ||
        method == 'Cash' && (session == null || quote!['quoteHash'] == null)) {
      return;
    }
    frozen ??= {
      ...body(),
      if (method == 'Cash') 'quoteHash': quote!['quoteHash'],
    };
    var written = recorded != null;
    try {
      recorded ??= obj(
        await auth.request('POST', root, body: frozen, idempotencyKey: intent),
      );
      written = true;
      final read = obj(await auth.request('GET', root));
      final saved = rows(read['refunds'] ?? [])
          .where((r) => r['id'] == recorded!['id'])
          .firstOrNull;
      if (saved == null ||
          saved['collectionId'] != frozen!['collectionId'] ||
          !storedAmountMatches(saved['amount'], frozen!['amount']) ||
          saved['currency'] != quote!['currency'] ||
          saved['payoutMethod'] != method ||
          saved['cashSessionId'] != frozen!['cashSessionId'] ||
          (textOf(saved['reason']).trim()) !=
              (textOf(frozen!['reason']).trim())) {
        throw const ApiFailure(
          0,
          'سُجل الطلب ولم تتأكد قراءة نتيجته. راجع السجل دون صرف المبلغ مرة أخرى.',
        );
      }
      if (current) {
        setState(() {
          data = read;
          message = 'سُجل الاسترداد وظهر في السجل. لا تصرف المبلغ مرة أخرى.';
          uncertain = false;
          frozen = null;
          recorded = null;
          quote = null;
          confirmed = false;
          collection = null;
          session = null;
          sessions = [];
          amount.clear();
          reason.clear();
          intent = commandKey();
        });
      }
    } catch (e) {
      if (current) {
        setState(
          () => uncertain =
              written || e is! ApiFailure || e.status == 0 || e.status >= 500,
        );
      }
      rethrow;
    }
  });
  @override
  Widget build(BuildContext context) => page('استرداد مبلغ للاعب', [
    const UiText(
      'راجع التحصيل والمبلغ وطريقة الرد. الاسترداد لا يلغي الاشتراك أو يعيد فتح الدين تلقائيًا.',
    ),
    if (data != null) ...[
      if (eligible.isEmpty)
        const EmptyMessage('لا يوجد مبلغ متاح للاسترداد المباشر.')
      else ...[
        choice(
          'التحصيل',
          collection,
          [
            for (final c in eligible)
              {
                ...c,
                'arabicName':
                    '${day(c['confirmedAtUtc'])} · المتاح ${money(c['available'], c['currency'])}',
              },
          ],
          uncertain
              ? null
              : (v) => setState(() {
                  collection = v;
                  method = source?['paymentMethod'] == 'Cash'
                      ? 'Cash'
                      : 'Online';
                  session = null;
                  sessions = [];
                  amount.clear();
                  invalidate();
                }),
        ),
        AbsorbPointer(
          absorbing: uncertain,
          child: Column(
            children: [
              field(
                'قيمة الاسترداد',
                amount,
                keyboard: const TextInputType.numberWithOptions(decimal: true),
                changed: () => setState(invalidate),
              ),
              choice(
                'طريقة رد المبلغ',
                method,
                [
                  {'id': 'Online', 'arabicName': 'تحويل مُنفذ خارج النظام'},
                  {'id': 'Cash', 'arabicName': 'صرف نقدي من وردية مخولة'},
                ],
                source?['paymentMethod'] == 'Cash'
                    ? null
                    : (v) => setState(() {
                        method = v ?? 'Online';
                        session = null;
                        sessions = [];
                        invalidate();
                      }),
              ),
              field(
                'سبب الاسترداد',
                reason,
                required: false,
                maxLines: 3,
                changed: () => setState(invalidate),
              ),
              if (method == 'Cash') ...[
                if (sessions.isEmpty)
                  const UiText(
                    'اضغط مراجعة المبلغ لتحميل ورديات الصرف المتاحة في فرع التحصيل وعملته.',
                  ),
                if (sessions.isNotEmpty)
                  choice(
                    'وردية الصرف',
                    session,
                    [
                      for (final s in sessions) {...s, 'arabicName': s['name']},
                    ],
                    (v) => setState(() {
                      session = v;
                      invalidate();
                    }),
                  ),
              ],
            ],
          ),
        ),
        if (!uncertain)
          OutlinedButton(
            onPressed: source == null ? null : review,
            child: const UiText('مراجعة مبلغ الاسترداد'),
          ),
        if (quote != null) ...[
          GCard(
            child: UiText(
              'المبلغ ${money(quote!['amount'], quote!['currency'])}\nالمتاح ${money(quote!['available'], quote!['currency'])}${quote!['balance'] == null ? '' : '\nرصيد الوردية ${money(quote!['balance'], quote!['currency'])}'}',
            ),
          ),
          if (method == 'Cash' && quote!['cashEnabled'] != true)
            const UiText('النقدي غير مفعل لهذه الأكاديمية.'),
          CheckboxListTile(
            value: confirmed,
            title: UiText(
              method == 'Cash'
                  ? 'راجعت الوردية والمبلغ وأؤكد توثيق صرف الاسترداد مرة واحدة.'
                  : 'أؤكد أن التحويل تم خارج النظام وأوثق رده مرة واحدة.',
            ),
            onChanged:
                uncertain ||
                    method == 'Cash' &&
                        (session == null || quote!['quoteHash'] == null)
                ? null
                : (v) => setState(() => confirmed = v == true),
          ),
          if (uncertain)
            const UiText(
              'لم تتأكد نتيجة الطلب. أعد تأكيد الطلب نفسه دون صرف المبلغ مرة أخرى.',
            ),
          FilledButton(
            onPressed: confirmed ? commit : null,
            child: UiText(
              uncertain
                  ? 'إعادة تأكيد الاسترداد نفسه'
                  : 'تأكيد تسجيل الاسترداد',
            ),
          ),
        ],
      ],
      const Heading('سجل الاستردادات'),
      for (final r in rows(data!['refunds'] ?? []))
        GCard(
          child: UiText(
            '${money(r['amount'], r['currency'])} · ${day(r['createdAtUtc'])}\n${r['reason'] ?? 'لا توجد ملاحظة'}',
          ),
        ),
    ],
  ], screen: 'players');
}
