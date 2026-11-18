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
    this.playerName,
    this.playerCode,
  });
  final AuthController auth;
  final String playerId;
  final String? playerName, playerCode;
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
  BigInt? minor(Object? value) {
    final match = RegExp(r'^(\d+)(?:\.(\d{1,2}))?$').firstMatch('$value');
    return match == null
        ? null
        : BigInt.parse('${match[1]}${(match[2] ?? '').padRight(2, '0')}');
  }

  bool currency(Object? value) =>
      value is String && RegExp(r'^[A-Z]{3}$').hasMatch(value);
  bool named(Object? value) => value is String && value.trim().isNotEmpty;
  bool validSource(Json value) {
    if (value['collections'] is! List ||
        value['refunds'] is! List ||
        value['canRecord'] is! bool) {
      return false;
    }
    final ids = <String>{};
    for (final row in value['collections'] as List) {
      if (row is! Map ||
          !named(row['id']) ||
          !ids.add(row['id'] as String) ||
          minor(row['available']) == null ||
          !currency(row['currency']) ||
          !named(row['paymentMethod'])) {
        return false;
      }
    }
    final refundIds = <String>{};
    for (final row in value['refunds'] as List) {
      if (row is! Map ||
          !named(row['id']) ||
          !refundIds.add(row['id'] as String) ||
          !named(row['collectionId']) ||
          minor(row['amount']) == null ||
          !currency(row['currency']) ||
          (row['payoutMethod'] != null && !named(row['payoutMethod']))) {
        return false;
      }
    }
    return true;
  }

  bool validQuote(Json value) {
    final requested = minor(amount.text.trim()),
        available = minor(value['available']);
    if (requested == null ||
        available == null ||
        available < requested ||
        !storedAmountMatches(value['amount'], amount.text.trim()) ||
        value['currency'] != source?['currency'] ||
        !currency(value['currency']) ||
        value['sessions'] is! List) {
      return false;
    }
    final ids = <String>{};
    for (final row in value['sessions'] as List) {
      if (row is! Map ||
          !named(row['id']) ||
          !named(row['name']) ||
          !ids.add(row['id'] as String)) {
        return false;
      }
    }
    return method != 'Cash' || value['cashEnabled'] is bool;
  }

  List<Json> get eligible =>
      rows(data?['collections'] ?? [])
          .where((c) => minor(c['available'])! > BigInt.zero)
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
    if (!validSource(result)) {
      throw const ApiFailure(
        0,
        'تعذر التحقق من سجل الاستردادات. أعد تحميل السجل.',
      );
    }
    if (current) setState(() => data = result);
  });
  void invalidate() {
    quote = null;
    confirmed = false;
    frozen = null;
    intent = commandKey();
  }

  bool get validAmount {
    final requested = minor(amount.text.trim()),
        available = minor(source?['available']);
    return requested != null &&
        available != null &&
        requested > BigInt.zero &&
        requested <= available;
  }

  Json body() => {
    'collectionId': collection,
    'amount': amount.text.trim(),
    'reason': reason.text.trim().isEmpty ? null : reason.text.trim(),
    if (method == 'Cash' && session != null) 'cashSessionId': session,
  };
  Future<void> review() => run(() async {
    setState(invalidate);
    if (data?['canRecord'] != true) return;
    if (reason.text.trim().length > 500) {
      throw const ApiFailure(422, 'السبب لا يتجاوز 500 حرف.');
    }
    if (source == null || !validAmount) {
      setState(
        () => error = 'أدخل مبلغًا صحيحًا ضمن المتاح للاسترداد، بحد أقصى منزلتين عشريتين.',
      );
      return;
    }
    final q = obj(await auth.request('POST', '$root/quote', body: body()));
    if (!validQuote(q)) {
      throw const ApiFailure(
        0,
        'تعذر التحقق من مراجعة الاسترداد. أعد المراجعة قبل الصرف.',
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
    if (data?['canRecord'] != true ||
        !confirmed ||
        quote == null ||
        !validAmount ||
        method == 'Cash' &&
            (quote!['cashEnabled'] != true ||
                session == null ||
                quote!['quoteHash'] == null)) {
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
      if (!validSource(read)) {
        throw const ApiFailure(
          0,
          'تعذر التحقق من سجل الاستردادات. أعد تحميل السجل.',
        );
      }
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
      final pending =
          written || e is! ApiFailure || e.status == 0 || e.status >= 500;
      if (current) {
        setState(() {
          uncertain = pending;
          if (!uncertain) invalidate();
        });
      }
      if (!pending && current) {
        try {
          final refreshed = obj(await auth.request('GET', root));
          if (current) {
            setState(() {
              data = validSource(refreshed) ? refreshed : null;
              if (!eligible.any((row) => row['id'] == collection)) {
                collection = null;
                session = null;
              }
            });
          }
        } catch (_) {
          if (current) setState(() => data = null);
        }
      }
      rethrow;
    }
  });
  @override
  Widget build(BuildContext context) => page(
    'استرداد مبلغ للاعب',
    [
      if (named(widget.playerName))
        UiText(
          '${widget.playerName}${named(widget.playerCode) ? ' · ${widget.playerCode}' : ''}',
          style: const TextStyle(fontWeight: FontWeight.w700),
        ),
      const UiText(
        'راجع التحصيل والمبلغ وطريقة الرد. الاسترداد لا يلغي الاشتراك أو يعيد فتح الدين تلقائيًا.',
      ),
      if (data == null && error != null)
        OutlinedButton(
          onPressed: busy ? null : load,
          child: const UiText('إعادة تحميل السجل'),
        ),
      if (data != null) ...[
        if (data!['canRecord'] != true)
          const UiText('هذا السجل للقراءة فقط.')
        else if (eligible.isEmpty)
          const EmptyMessage('لا يوجد مبلغ متاح للاسترداد المباشر.')
        else ...[
          Padding(
            padding: const EdgeInsets.only(bottom: 14),
            child: DropdownButtonFormField<String>(
              key: ValueKey('refund-source:$collection'),
              initialValue: collection,
              isExpanded: true,
              isDense: false,
              itemHeight: null,
              decoration: const InputDecoration(
                label: RequiredFieldLabel('التحصيل'),
              ),
              items: [
                for (final c in eligible)
                  DropdownMenuItem<String>(
                    value: c['id'] as String,
                    child: Padding(
                      padding: const EdgeInsets.symmetric(vertical: 8),
                      child: UiText(
                        '${named(c['receiptNumber']) ? '${c['receiptNumber']} · ' : ''}${day(c['confirmedAtUtc'])} · المتاح ${money(c['available'], c['currency'])}',
                      ),
                    ),
                  ),
              ],
              onChanged: uncertain
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
          ),
          AbsorbPointer(
            absorbing: uncertain,
            child: Column(
              children: [
                field(
                  'قيمة الاسترداد',
                  amount,
                  keyboard: const TextInputType.numberWithOptions(
                    decimal: true,
                  ),
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
                  minLines: 1,
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
                        for (final s in sessions)
                          {...s, 'arabicName': s['name']},
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
                          (quote!['cashEnabled'] != true ||
                              session == null ||
                              quote!['quoteHash'] == null)
                  ? null
                  : (v) => setState(() => confirmed = v == true),
            ),
            if (uncertain)
              UiText(
                recorded != null
                    ? 'وصل تأكيد الطلب ولم تتأكد قراءة السجل. تحقق من تسجيله دون صرف المبلغ مرة أخرى.'
                    : 'لم تتأكد نتيجة الطلب. أعد تأكيد الطلب نفسه دون صرف المبلغ مرة أخرى.',
              ),
            FilledButton(
              onPressed: confirmed ? commit : null,
              child: UiText(
                uncertain
                    ? recorded != null
                          ? 'التحقق من تسجيل الاسترداد'
                          : 'إعادة تأكيد الاسترداد نفسه'
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
    ],
    screen: 'players',
    onRetry: load,
  );
}
