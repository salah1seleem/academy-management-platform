import 'dart:convert';

import 'package:crypto/crypto.dart';
import 'package:flutter/material.dart';

import '../../core/api/api_client.dart';
import '../../core/auth/auth_controller.dart';
import '../../core/storage/refresh_store.dart';
import '../../core/theme/ui_text.dart';
import '../guardian/guardian_ui.dart';
import '../shared/payment_proof_image.dart';
import '../shared/private_media.dart';
import '../shared/staff_capabilities.dart';
import 'native_operation_form.dart';
import '../owner/inventory_preview.dart';

class NativeProductStockSection extends StatefulWidget {
  const NativeProductStockSection({super.key, required this.auth});
  final AuthController auth;

  @override
  State<NativeProductStockSection> createState() =>
      _NativeProductStockSectionState();
}

class _NativeProductStockSectionState extends State<NativeProductStockSection> {
  late final access = StaffCapabilities.load(widget.auth);

  @override
  Widget build(BuildContext context) => FutureBuilder<StaffCapabilities>(
    future: access,
    builder: (context, snapshot) {
      final rights = snapshot.data;
      if (rights == null ||
          !rights.allows('content.catalog.manage', 'catalog', 'Read') ||
          !rights.explicitAction('catalog', 'Read')) {
        return const SizedBox.shrink();
      }
      return Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          OwnerInventoryPreview(auth: widget.auth),
          NativeProductOrdersEntry(auth: widget.auth),
        ],
      );
    },
  );
}

class NativeProductOrdersEntry extends StatefulWidget {
  const NativeProductOrdersEntry({super.key, required this.auth});
  final AuthController auth;
  @override
  State<NativeProductOrdersEntry> createState() =>
      _NativeProductOrdersEntryState();
}

class _NativeProductOrdersEntryState extends State<NativeProductOrdersEntry> {
  String? get identity => widget.auth.session?.selected == null
      ? null
      : '${widget.auth.session!.selected!.id}:${widget.auth.session!.selected!.contextRevision}';
  late final opened = identity;
  late final access = StaffCapabilities.load(widget.auth);
  @override
  Widget build(BuildContext context) => ListenableBuilder(
    listenable: widget.auth,
    builder: (context, _) => opened == null || identity != opened
        ? const SizedBox.shrink()
        : FutureBuilder<StaffCapabilities>(
            future: access,
            builder: (context, result) =>
                result.data?.canReadProductOrders != true
                ? const SizedBox.shrink()
                : GLink(
                    'طلبات المنتجات',
                    Icons.shopping_bag_outlined,
                    () => openPage(
                      context,
                      NativeProductOrders(auth: widget.auth),
                    ),
                  ),
          ),
  );
}

class NativeProductOrders extends StatefulWidget {
  const NativeProductOrders({
    super.key,
    required this.auth,
    this.filters = const {},
  });
  final AuthController auth;
  final Map<String, String> filters;
  @override
  State<NativeProductOrders> createState() => _NativeProductOrdersState();
}

class _NativeProductOrdersState
    extends NativeOperationState<NativeProductOrders> {
  @override
  AuthController get auth => widget.auth;
  final search = TextEditingController();
  String status = 'Pending', appliedSearch = '';
  int number = 1, total = 0;
  List<Json> items = [];
  @override
  void initState() {
    super.initState();
    autoPage(hasMore: () => items.length < total, next: () => load(more: true));
    load();
  }

  @override
  void dispose() {
    search.dispose();
    super.dispose();
  }

  Future<void> load({bool more = false}) => run(() async {
    final target = more ? number + 1 : 1;
    final access = await StaffCapabilities.load(auth);
    if (!access.canReadProductOrders) {
      throw const ApiFailure(403, 'ليست لديك صلاحية عرض طلبات المنتجات.');
    }
    final result = obj(
      await auth.request(
        'GET',
        '/api/v1/manage/product-sales?${Uri(queryParameters: {
          for (final k in ['from', 'to', 'branchId'])
            if (widget.filters[k]?.isNotEmpty == true) k: widget.filters[k]!,
          'page': '$target',
          'pageSize': '25',
          if (status.isNotEmpty) 'status': status,
          if (appliedSearch.isNotEmpty) 'search': appliedSearch,
        }).query}',
      ),
    );
    if (current) {
      setState(() {
        items = [if (more) ...items, ...rows(result['items'])];
        number = target;
        total = (result['totalCount'] as num).toInt();
      });
    }
  });
  Future<void> reload() async {
    setState(() {
      items = [];
      total = 0;
    });
    await load();
  }

  void applySearch(String value) {
    search.text = value;
    setState(() {
      appliedSearch = value.trim();
      number = 1;
    });
    reload();
  }

  @override
  Widget build(BuildContext context) => page(
    'طلبات المنتجات',
    [
      choice(
        'حالة الطلب',
        status,
        const [
          {'id': '', 'arabicName': 'كل الطلبات'},
          {'id': 'Pending', 'arabicName': 'بانتظار المراجعة'},
          {'id': 'Confirmed', 'arabicName': 'مقبولة'},
          {'id': 'Rejected', 'arabicName': 'مرفوضة'},
        ],
        (value) {
          setState(() {
            status = value ?? '';
            number = 1;
          });
          reload();
        },
        required: false,
      ),
      field('المنتج أو اللاعب أو رقم الطلب', search, required: false),
      OutlinedButton.icon(
        onPressed: () => applySearch(search.text),
        icon: const Icon(Icons.search),
        label: const UiText('بحث'),
      ),
      if (appliedSearch.isNotEmpty)
        TextButton(
          onPressed: () => applySearch(''),
          child: const UiText('مسح البحث'),
        ),
      if (!busy && error != null)
        OutlinedButton(
          onPressed: () => load(more: items.isNotEmpty),
          child: const UiText('إعادة المحاولة'),
        ),
      if (!busy && error == null && items.isEmpty)
        const EmptyMessage('لا توجد طلبات مطابقة.'),
      for (final row in items)
        GCard(
          key: ValueKey('staff-product-order:${row['id']}'),
          onTap: () async {
            await openPage(
              context,
              NativeProductOrderReview(auth: auth, id: textOf(row['id'])),
            );
            if (current) await reload();
          },
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Heading(textOf(row['productName'])),
              StatusPill(row['status']),
              PaymentRequestReference(order: row),
              UiText(
                [
                  row['player'] ?? 'منتج للأسرة',
                  if (row['branchName'] != null) row['branchName'],
                ].join(' · '),
              ),
              UiText(
                [
                  if (textOf(row['colorSnapshot'] ?? '').isNotEmpty)
                    row['colorSnapshot'],
                  if (textOf(row['sizeSnapshot'] ?? '').isNotEmpty)
                    row['sizeSnapshot'],
                  if (row['shirtNumberSnapshot'] != null)
                    'رقم ${row['shirtNumberSnapshot']}',
                  'الكمية ${row['quantity']}',
                ].join(' · '),
              ),
              MoneyText(row['amount'], row['currency'], size: 22),
              if (row['status'] == 'Pending')
                UiText(
                  row['hasProof'] == true
                      ? 'وصل إثبات الدفع'
                      : 'بانتظار إثبات الدفع',
                ),
              const UiText('عرض الطلب'),
            ],
          ),
        ),
      const SizedBox.shrink(),
    ],
    screen: 'payments',
    onRetry: reload,
  );
}

class NativeProductOrderReview extends StatefulWidget {
  const NativeProductOrderReview({
    super.key,
    required this.auth,
    required this.id,
    this.attemptStore,
  });
  final AuthController auth;
  final String id;
  final RefreshStore? attemptStore;
  @override
  State<NativeProductOrderReview> createState() =>
      _NativeProductOrderReviewState();
}

class _NativeProductOrderReviewState
    extends NativeOperationState<NativeProductOrderReview> {
  @override
  AuthController get auth => widget.auth;
  @override
  String? get currentContext {
    final membership = super.currentContext;
    return membership == null ? null : '${auth.session?.userId}:$membership';
  }

  final reason = TextEditingController();
  Json? data, frozen;
  StaffCapabilities? access;
  bool recorded = false, retryAllowed = false, verifiedPending = false;
  bool unavailable = false;
  String? fingerprint;
  String? visibleProof;
  bool get proofReady =>
      visibleProof != null && visibleProof == data?['proofUrl'];
  late final RefreshStore? attemptStore = widget.attemptStore ?? _store();
  String get path => '/api/v1/manage/product-sales/${widget.id}';
  bool get canReview =>
      data?['canReview'] as bool? ??
      access?.allows('content.catalog.manage', 'catalog', 'Edit') == true;
  bool get canAccept =>
      canReview &&
      (data?['canAccept'] as bool? ??
          (access!.owner ||
              access!.explicitAction('catalog', 'Edit') &&
                  access!.explicitAction('finance', 'Add')));

  RefreshStore? _store() {
    final transport = auth.transport;
    final member = auth.session?.selected;
    if (transport is! HttpApiTransport || member == null) return null;
    return SecureRefreshStore(
      '${transport.config.storageKey}.product-review.'
      '${member.academyId}.${member.id}.${auth.session?.userId ?? member.id}.'
      '${widget.id}',
    );
  }

  String _fingerprint(Json order, String status, Object? note) {
    const fields = [
      'id',
      'productId',
      'playerId',
      'sportEnrollmentId',
      'variantId',
      'branchId',
      'productName',
      'sizeSnapshot',
      'colorSnapshot',
      'shirtNumberSnapshot',
      'quantity',
      'amount',
      'unitPrice',
      'currency',
      'proofUrl',
    ];
    return sha256
        .convert(
          utf8.encode(
            jsonEncode([
              for (final field in fields) order[field],
              status,
              note,
            ]),
          ),
        )
        .toString();
  }

  @override
  void initState() {
    super.initState();
    load();
  }

  @override
  void dispose() {
    reason.dispose();
    super.dispose();
  }

  Future<Json> read() async {
    final value = obj(await auth.request('GET', path));
    if (value['id'] != widget.id) {
      throw const ApiFailure(
        0,
        'لم تتأكد هوية طلب المنتج. أعد فتحه من القائمة.',
      );
    }
    return value;
  }

  Future<void> load() => run(() async {
    final rights = await StaffCapabilities.load(auth);
    if (!rights.allows('payment.read', 'catalog', 'Read')) {
      throw const ApiFailure(403, 'ليست لديك صلاحية عرض هذا الطلب.');
    }
    final value = await read();
    final saved = await attemptStore?.read();
    if (current) {
      setState(() {
        access = rights;
        data = value;
        fingerprint = saved != null && RegExp(r'^[a-f0-9]{64}$').hasMatch(saved)
            ? saved
            : null;
      });
    }
  });
  Future<void> review(bool accept) async {
    if (!current ||
        busy ||
        fingerprint != null ||
        !canReview ||
        accept && !canAccept ||
        data?['status'] != 'Pending' ||
        !proofReady) {
      return;
    }
    if (reason.text.trim().length > 500) {
      setState(() => error = 'الملاحظة لا تتجاوز500حرف.');
      return;
    }
    final source = Map<String, dynamic>.from(data!);
    final note = reason.text.trim();
    final decision = await showDialog<bool>(
      context: context,
      builder: (context) => ListenableBuilder(
        listenable: auth,
        builder: (context, _) {
          if (!current) {
            return AlertDialog(
              content: const UiText('انتهت جلسة المراجعة.'),
              actions: [
                TextButton(
                  onPressed: () => Navigator.pop(context, false),
                  child: const UiText('إغلاق'),
                ),
              ],
            );
          }
          return AlertDialog(
            title: UiText(accept ? 'قبول طلب المنتج' : 'رفض طلب المنتج'),
            content: UiText(
              '${source['productName']} · ${money(source['amount'], source['currency'])}\n'
              '${accept ? 'أكد وصول التحويل. سيُخصم الصنف المطلوب من المخزون.' : 'سيظهر الرفض وملاحظتك لولي الأمر.'}',
            ),
            actions: [
              TextButton(
                onPressed: () => Navigator.pop(context, false),
                child: const UiText('رجوع'),
              ),
              FilledButton(
                onPressed: () => Navigator.pop(context, true),
                child: const UiText('تأكيد المراجعة'),
              ),
            ],
          );
        },
      ),
    );
    if (decision != true ||
        !current ||
        !proofReady ||
        visibleProof != source['proofUrl']) {
      return;
    }
    await run(() async {
      final body = <String, dynamic>{
        'accept': accept,
        'reason': note.isEmpty ? null : note,
        'expectedProofAssetId': Uri.parse(source['proofUrl'] as String)
            .pathSegments
            .last,
      };
      final value = _fingerprint(
        source,
        accept ? 'Confirmed' : 'Rejected',
        body['reason'],
      );
      try {
        await attemptStore?.write(value);
      } catch (_) {
        throw const ApiFailure(0, 'تعذر حفظ مرجع المراجعة. حاول مجددًا.');
      }
      if (!current) return;
      if (!proofReady || visibleProof != source['proofUrl']) {
        await attemptStore?.clear();
        return;
      }
      setState(() {
        frozen = body;
        fingerprint = value;
      });
      await finish();
    });
  }

  Future<void> _clear() async {
    await attemptStore?.clear();
    if (!current) return;
    setState(() {
      fingerprint = null;
      frozen = null;
      recorded = false;
      retryAllowed = false;
      verifiedPending = false;
      reason.clear();
    });
  }

  Future<void> verify() => run(() => finish(readOnly: true));

  Future<void> finish({bool readOnly = false}) async {
    try {
      if (!readOnly && !recorded) {
        if (!proofReady ||
            visibleProof?.split('/').last != frozen?['expectedProofAssetId']) {
          throw const ApiFailure(0, 'أعد فتح صورة الإثبات قبل إرسال القرار.');
        }
        try {
          await auth.request('POST', '$path/review', body: frozen);
          recorded = true;
        } on ApiFailure catch (e) {
          if (e.status == 409) {
            final previousProof = frozen?['expectedProofAssetId'];
            final Json updated;
            try {
              updated = await read();
              if (current) setState(() => data = updated);
              await _clear();
            } catch (_) {
              if (current) {
                setState(() {
                  data = null;
                  visibleProof = null;
                });
              }
              rethrow;
            }
            if (updated['status'] == 'Pending' &&
                updated['proofUrl'] is String &&
                (updated['proofUrl'] as String).split('/').last !=
                    previousProof) {
              throw const ApiFailure(
                409,
                'تغيّر إثبات الدفع. راجع الصورة الحالية.',
              );
            }
          } else if ([400, 422].contains(e.status)) {
            await _clear();
          }
          rethrow;
        }
      }
      final saved = await read();
      if (!current) return;
      final status = saved['status'];
      final sameDecision =
          status is String &&
          ['Confirmed', 'Rejected'].contains(status) &&
          _fingerprint(saved, status, saved['reviewReason']) == fingerprint;
      if (sameDecision) {
        await _clear();
        if (current) {
          setState(() {
            data = saved;
            message = 'حُفظت المراجعة وأُعيدت قراءة الطلب نفسه.';
          });
        }
        return;
      }
      if (readOnly && !recorded && status == 'Pending') {
        final decision = frozen;
        final sameSource =
            decision != null &&
            _fingerprint(
                  saved,
                  decision['accept'] == true ? 'Confirmed' : 'Rejected',
                  decision['reason'],
                ) ==
                fingerprint;
        setState(() {
          data = saved;
          retryAllowed = sameSource;
          verifiedPending = !sameSource;
        });
        return;
      }
      if (readOnly && !recorded && ['Confirmed', 'Rejected'].contains(status)) {
        await _clear();
        if (current) setState(() => data = saved);
        throw const ApiFailure(
          0,
          'ظهر قرار مختلف على الطلب. راجع الحالة المحفوظة.',
        );
      }
      throw const ApiFailure(
        0,
        'لم تتأكد مطابقة قرار المراجعة ومصدره. تحقق من الطلب نفسه قبل أي إجراء آخر.',
      );
    } on ApiFailure catch (e) {
      if (current && [401, 403, 404].contains(e.status)) {
        setState(() {
          data = null;
          access = null;
          visibleProof = null;
          unavailable = true;
        });
      }
      rethrow;
    }
  }

  @override
  Widget build(BuildContext context) {
    final value = data;
    return page('طلب منتج رياضي', [
      if (value == null && !busy && !unavailable)
        OutlinedButton(
          onPressed: load,
          child: const UiText('إعادة تحميل الطلب'),
        ),
      if (value != null) ...[
        Heading(textOf(value['productName'])),
        StatusPill(value['status']),
        PaymentRequestReference(order: value),
        GCard(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              if (value['player'] != null) UiText('اللاعب: ${value['player']}'),
              if (textOf(value['sizeSnapshot'] ?? '').isNotEmpty)
                UiText('المقاس: ${value['sizeSnapshot']}'),
              if (textOf(value['colorSnapshot'] ?? '').isNotEmpty)
                UiText('اللون: ${value['colorSnapshot']}'),
              if (value['shirtNumberSnapshot'] != null)
                UiText('رقم القميص: ${value['shirtNumberSnapshot']}'),
              UiText('الكمية: ${value['quantity']}'),
              MoneyText(value['amount'], value['currency'], size: 26),
              if (value['reviewReason'] != null)
                UiText('ملاحظة المراجعة: ${value['reviewReason']}'),
            ],
          ),
        ),
        if (value['proofUrl'] case final String proof
            when isPrivateAsset(proof))
          PaymentProofImage(
            key: ValueKey(proof),
            auth: auth,
            path: proof,
            onAvailabilityChanged: (available) {
              if (current && data?['proofUrl'] == proof) {
                setState(() => visibleProof = available ? proof : null);
              }
            },
          )
        else if (value['status'] == 'Pending')
          const UiText('بانتظار وصول إثبات الدفع من ولي الأمر.'),
        if (fingerprint != null) ...[
          UiText(
            verifiedPending
                ? 'الطلب ما زال معلقًا. راجع الإثبات الحالي.'
                : 'قرار المراجعة بانتظار التحقق.',
          ),
          FilledButton(
            onPressed: verify,
            child: const UiText('التحقق من القرار'),
          ),
          if (retryAllowed &&
              proofReady &&
              canReview &&
              (frozen?['accept'] != true || canAccept))
            OutlinedButton(
              onPressed: () => run(() async {
                setState(() => retryAllowed = false);
                await finish();
              }),
              child: const UiText('إعادة إرسال نفس القرار'),
            ),
          if (verifiedPending && canReview)
            OutlinedButton(
              onPressed: () => run(_clear),
              child: const UiText('فتح مراجعة جديدة'),
            ),
        ] else if (value['status'] == 'Pending' && canReview && proofReady) ...[
          field('ملاحظة لولي الأمر', reason, required: false, maxLines: 3),
          if (canAccept)
            FilledButton(
              onPressed: () => review(true),
              child: const UiText('قبول وخصم المخزون'),
            ),
          OutlinedButton(
            onPressed: () => review(false),
            child: const UiText('رفض الطلب'),
          ),
        ],
      ],
    ], screen: 'payments');
  }
}
