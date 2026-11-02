import 'package:flutter/material.dart';

import '../../core/api/api_client.dart';
import '../../core/auth/auth_controller.dart';
import '../../core/theme/ui_text.dart';
import '../guardian/guardian_ui.dart';
import '../shared/private_media.dart';
import '../shared/staff_capabilities.dart';
import 'native_operation_form.dart';

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
                result.data?.allows('payment.read', 'catalog', 'Read') != true
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
    if (!access.allows('payment.read', 'catalog', 'Read')) {
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

  @override
  Widget build(BuildContext context) => page('طلبات المنتجات', [
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
    field('البحث بالمنتج أو اللاعب', search, required: false),
    OutlinedButton.icon(
      onPressed: () {
        setState(() {
          appliedSearch = search.text.trim();
          number = 1;
        });
        reload();
      },
      icon: const Icon(Icons.search),
      label: const UiText('بحث'),
    ),
    if (!busy && items.isEmpty) const EmptyMessage('لا توجد طلبات مطابقة.'),
    for (final row in items)
      GCard(
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
            UiText(
              '${row['player'] ?? 'منتج للأسرة'} · ${row['branchName'] ?? ''}',
            ),
            UiText(
              '${textOf(row['colorSnapshot'] ?? '').isEmpty ? '' : '${row['colorSnapshot']} · '}${row['sizeSnapshot'] ?? 'مقاس عام'}${row['shirtNumberSnapshot'] == null ? '' : ' · رقم ${row['shirtNumberSnapshot']}'} · الكمية ${row['quantity']}',
            ),
            MoneyText(row['amount'], row['currency'], size: 22),
            UiText(
              row['hasProof'] == true
                  ? 'وصل إثبات الدفع'
                  : 'بانتظار إثبات الدفع',
            ),
            const UiText('عرض الطلب ومراجعته'),
          ],
        ),
      ),
    const SizedBox.shrink(),
  ], screen: 'payments');
}

class NativeProductOrderReview extends StatefulWidget {
  const NativeProductOrderReview({
    super.key,
    required this.auth,
    required this.id,
  });
  final AuthController auth;
  final String id;
  @override
  State<NativeProductOrderReview> createState() =>
      _NativeProductOrderReviewState();
}

class _NativeProductOrderReviewState
    extends NativeOperationState<NativeProductOrderReview> {
  @override
  AuthController get auth => widget.auth;
  final reason = TextEditingController();
  Json? data, frozen, original;
  StaffCapabilities? access;
  bool recorded = false;
  String get path => '/api/v1/manage/product-sales/${widget.id}';
  bool get canReview =>
      access?.allows('content.catalog.manage', 'catalog', 'Edit') == true;
  bool get canAccept =>
      canReview &&
      (access!.owner ||
          access!.explicitAction('catalog', 'Edit') &&
              access!.explicitAction('finance', 'Add'));
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
    if (current) {
      setState(() {
        access = rights;
        data = value;
      });
    }
  });
  Future<void> review(bool accept) async {
    if (!current ||
        busy ||
        frozen != null ||
        !canReview ||
        accept && !canAccept ||
        data?['status'] != 'Pending' ||
        !isPrivateAsset(data?['proofUrl'])) {
      return;
    }
    if (reason.text.trim().length > 500) {
      setState(() => error = 'الملاحظة لا تتجاوز500حرف.');
      return;
    }
    final decision = await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: UiText(accept ? 'قبول طلب المنتج' : 'رفض طلب المنتج'),
        content: UiText(
          accept
              ? 'أكد وصول التحويل ومراجعة الإثبات. سيعتمد المبلغ المسجل ويُخصم المقاس والرقم المختاران من المخزون.'
              : 'سيُحفظ رفض الطلب وملاحظتك لولي الأمر.',
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
      ),
    );
    if (decision != true || !current) return;
    setState(() {
      original = Map<String, dynamic>.from(data!);
      frozen = {
        'accept': accept,
        'reason': reason.text.trim().isEmpty ? null : reason.text.trim(),
      };
    });
    await finish();
  }

  Future<void> finish() => run(() async {
    try {
      if (!recorded) {
        try {
          await auth.request('POST', '$path/review', body: frozen);
          recorded = true;
        } on ApiFailure catch (e) {
          if (e.status >= 400 && e.status < 500 && e.status != 408) {
            frozen = null;
            original = null;
          }
          rethrow;
        }
      }
      final saved = await read();
      final source = original!;
      final desired = frozen!['accept'] == true ? 'Confirmed' : 'Rejected';
      final same = [
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
        'currency',
        'proofUrl',
      ].every((k) => saved[k] == source[k]);
      if (!same ||
          !storedAmountMatches(saved['amount'], source['amount']) ||
          !storedAmountMatches(saved['unitPrice'], source['unitPrice']) ||
          saved['status'] != desired ||
          saved['reviewReason'] != frozen!['reason']) {
        throw const ApiFailure(
          0,
          'لم تتأكد مطابقة قرار المراجعة ومصدره. تحقق من الطلب نفسه قبل أي إجراء آخر.',
        );
      }
      if (current) {
        setState(() {
          data = saved;
          frozen = null;
          original = null;
          recorded = false;
          message = 'حُفظت المراجعة وأُعيدت قراءة الطلب نفسه.';
        });
      }
    } on ApiFailure catch (e) {
      if (current && [401, 403, 404].contains(e.status)) {
        setState(() {
          data = null;
          access = null;
        });
      }
      rethrow;
    }
  });
  @override
  Widget build(BuildContext context) {
    final value = data;
    return page('طلب منتج رياضي', [
      if (value == null && !busy)
        OutlinedButton(
          onPressed: load,
          child: const UiText('إعادة تحميل الطلب'),
        ),
      if (value != null) ...[
        Heading(textOf(value['productName'])),
        StatusPill(value['status']),
        GCard(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              UiText('اللاعب: ${value['player'] ?? 'منتج للأسرة'}'),
              UiText('المقاس: ${value['sizeSnapshot'] ?? 'مقاس عام'}'),
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
        if (isPrivateAsset(value['proofUrl'])) ...[
          const Heading('إثبات الدفع'),
          PrivateImage(
            auth: auth,
            path: textOf(value['proofUrl']),
            height: 240,
            fit: BoxFit.contain,
          ),
        ] else
          const UiText('بانتظار وصول إثبات الدفع من ولي الأمر.'),
        if (frozen != null) ...[
          const UiText('قرار المراجعة ثابت حتى التأكد من حفظه.'),
          FilledButton(
            onPressed: finish,
            child: UiText(
              recorded
                  ? 'إعادة التحقق من الطلب نفسه'
                  : 'استكمال مراجعة الطلب نفسه',
            ),
          ),
        ] else if (value['status'] == 'Pending' &&
            canReview &&
            isPrivateAsset(value['proofUrl'])) ...[
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
