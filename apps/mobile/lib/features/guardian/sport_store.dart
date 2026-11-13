import 'package:flutter/material.dart';

import '../../core/api/api_client.dart';
import '../../core/auth/auth_controller.dart';
import '../../core/theme/ui_text.dart';
import '../../core/widgets/page_status.dart';
import '../admin/native_operation_form.dart';
import '../shared/transfer_payment.dart';
import 'guardian_ui.dart';

class ProductPurchase extends StatefulWidget {
  const ProductPurchase({super.key, required this.auth, required this.product});
  final AuthController auth;
  final Json product;
  @override
  State<ProductPurchase> createState() => _ProductPurchaseState();
}

class _ProductPurchaseState extends NativeOperationState<ProductPurchase> {
  @override
  AuthController get auth => widget.auth;
  int quantity = 1, inventoryEpoch = 0;
  String? enrollment, size, variant, color;
  String intent = commandKey();
  List<Json> contexts = [];
  Json? inventory, sale;
  bool numberConfirmed = false,
      uncertain = false,
      inventoryBusy = false,
      refreshRequired = false;
  late Future<void> loaded;
  bool get variantsRequired => inventory?['usesVariants'] == true;
  bool get numbered => inventory?['numberedUniform'] == true;
  List<Json> get variants =>
      rows(inventory?['variants'] ?? [])
          .where((row) => (row['quantity'] as num? ?? 0) > 0)
          .toList();
  String variantColor(Json variant) => textOf(variant['color'] ?? '');
  Set<String> get colors => variants.map(variantColor).toSet();
  bool get chooseColor => colors.any((value) => value.isNotEmpty);
  List<Json> get colorVariants => variants
      .where(
        (row) => !chooseColor || color != null && variantColor(row) == color,
      )
      .toList();
  Set<String> get sizes =>
      colorVariants.map((row) => textOf(row['size'])).toSet();
  List<Json> get sizeVariants =>
      colorVariants.where((row) => row['size'] == size).toList();
  Json? get selectedVariant =>
      variants.where((v) => v['id'] == variant).firstOrNull;
  Json? get selectedChild =>
      contexts.where((e) => e['id'] == enrollment).firstOrNull;
  int get available => variantsRequired
      ? (selectedVariant?['quantity'] as num? ?? 0).toInt()
      : ((inventory?['stockQuantity'] ?? widget.product['stockQuantity'])
                    as num? ??
                0)
            .toInt();
  int? get unitMinor {
    final value = double.tryParse('${inventory?['unitPrice']}');
    return value == null || !value.isFinite || value < 0
        ? null
        : (value * 100).round();
  }

  String? get total {
    final unit = unitMinor;
    if (unit == null) return null;
    final amount = unit * quantity;
    return '${amount ~/ 100}.${(amount % 100).toString().padLeft(2, '0')}';
  }

  Object? get currency => inventory?['currency'] ?? widget.product['currency'];
  bool get inStock =>
      inventory != null &&
      (variantsRequired ? variants.isNotEmpty : available > 0);
  bool get canChange => !busy && !uncertain && !inventoryBusy;

  bool get ready =>
      !busy &&
      !inventoryBusy &&
      !refreshRequired &&
      selectedChild != null &&
      inStock &&
      total != null &&
      quantity <= available &&
      (!variantsRequired ||
          selectedVariant != null &&
              (!chooseColor || color != null) &&
              (!numbered || numberConfirmed));
  @override
  void initState() {
    super.initState();
    loaded = load();
  }

  Future<void> load() async {
    final result = await auth.request(
      'GET',
      '/api/v1/guardian/subscriptions/enrollments',
    );
    if (!current) return;
    contexts = rows(result)
        .where((e) => e['sportId'] == widget.product['sportId'])
        .toList();
    enrollment = contexts.length == 1 ? textOf(contexts.single['id']) : null;
    if (enrollment != null) await loadInventory();
  }

  Future<void> loadInventory() async {
    if (!current || enrollment == null || uncertain) return;
    final epoch = ++inventoryEpoch;
    final selectedEnrollment = enrollment!;
    setState(() {
      inventoryBusy = true;
      inventory = null;
      variant = null;
      numberConfirmed = false;
      error = null;
    });
    try {
      final data = obj(
        await auth.request(
          'GET',
          '/api/v1/guardian/catalog/${widget.product['id']}/variants?enrollmentId=${Uri.encodeComponent(selectedEnrollment)}',
        ),
      );
      if (!current || epoch != inventoryEpoch) return;
      setState(() {
        inventory = data;
        refreshRequired = false;
        final choices = colors;
        if (!choices.contains(color)) {
          color = choices.length == 1 ? choices.single : null;
        }
        selectSize(sizes.contains(size) ? size : null);
      });
    } catch (e) {
      if (current && epoch == inventoryEpoch) {
        setState(() {
          inventory = null;
          error = e is ApiFailure && [403, 404].contains(e.status)
              ? 'المنتج غير متاح لهذا التسجيل.'
              : 'تعذر تحميل السعر والتوفّر.';
        });
      }
    } finally {
      if (current && epoch == inventoryEpoch) {
        setState(() => inventoryBusy = false);
      }
    }
  }

  void selectSize(String? value) {
    size = value;
    variant = null;
    numberConfirmed = false;
    quantity = 1;
    intent = commandKey();
    final choices = sizeVariants;
    if (!numbered && choices.length == 1) {
      variant = textOf(choices.single['id']);
    }
    if (numbered && inventory?['assignedShirtNumber'] != null) {
      final matches = choices.where(
        (v) => v['shirtNumber'] == inventory!['assignedShirtNumber'],
      );
      if (matches.length == 1) variant = textOf(matches.single['id']);
    }
  }

  Future<void> submit() async {
    if (!current || !ready) return;
    final child = selectedChild!;
    await run(() async {
      try {
        final value = obj(
          await auth.request(
            'POST',
            '/api/v1/guardian/product-sales',
            body: {
              'productId': widget.product['id'],
              'quantity': quantity,
              'expectedAmount': total!,
              'playerId': child['playerId'],
              'branchId': child['branchId'],
              if (variantsRequired) 'variantId': variant,
              if (variantsRequired) 'sportEnrollmentId': child['id'],
            },
            idempotencyKey: intent,
          ),
        );
        if (current) {
          setState(() {
            sale = value;
            uncertain = false;
          });
        }
      } catch (e) {
        if (current) {
          setState(() {
            uncertain = e is! ApiFailure || e.status == 0 || e.status >= 500;
            refreshRequired =
                e is ApiFailure && [404, 409, 422].contains(e.status);
          });
        }
        rethrow;
      }
    });
  }

  Widget selector(
    String label,
    String? value,
    List<DropdownMenuItem<String>> items,
    ValueChanged<String?> change, {
    Key? key,
  }) => DropdownButtonFormField<String>(
    key: key,
    initialValue: value,
    isExpanded: true,
    decoration: InputDecoration(labelText: label),
    items: items,
    onChanged: canChange && items.isNotEmpty ? change : null,
  );

  List<Widget> variantControls() => [
    if (chooseColor)
      selector(
        'اللون',
        color,
        [
          for (final value in colors)
            DropdownMenuItem(
              value: value,
              child: UiText(value.isEmpty ? 'اللون الأساسي' : value),
            ),
        ],
        (value) => setState(() {
          color = value;
          selectSize(null);
        }),
        key: ValueKey('color:$color:$inventoryEpoch'),
      ),
    if (sizes.isNotEmpty)
      selector(
        'المقاس',
        size,
        [
          for (final value in sizes)
            DropdownMenuItem(value: value, child: UiText(value)),
        ],
        (value) => setState(() => selectSize(value)),
        key: ValueKey('size:$size:$inventoryEpoch'),
      ),
    if (numbered && size != null) ...[
      if (inventory?['assignedShirtNumber'] != null &&
          !sizeVariants.any(
            (row) => row['shirtNumber'] == inventory!['assignedShirtNumber'],
          ))
        const UiText('رقم الطفل غير متاح بهذا المقاس. اختر رقمًا متاحًا.'),
      selector(
        'رقم القميص المتاح',
        variant,
        [
          for (final row in sizeVariants)
            DropdownMenuItem(
              value: textOf(row['id']),
              child: UiText(
                '${row['shirtNumber']} · المتاح ${row['quantity']}',
              ),
            ),
        ],
        (value) => setState(() {
          variant = value;
          numberConfirmed = false;
          quantity = 1;
          intent = commandKey();
        }),
        key: ValueKey('number:$size:$variant:$inventoryEpoch'),
      ),
      if (selectedVariant case final row?)
        CheckboxListTile(
          contentPadding: EdgeInsets.zero,
          value: numberConfirmed,
          title: UiText('أؤكد رقم القميص المختار: ${row['shirtNumber']}'),
          onChanged: canChange
              ? (value) => setState(() => numberConfirmed = value == true)
              : null,
        ),
    ],
  ];

  Widget orderSummary({required bool confirmed}) {
    final shirt = selectedVariant?['shirtNumber'];
    final choice = [
      if (color != null && color!.isNotEmpty) color!,
      if (size != null) 'مقاس $size',
      if (shirt != null) 'رقم $shirt',
      'الكمية $quantity',
    ].join(' · ');
    return GCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Heading(confirmed ? 'بيانات الطلب' : 'مراجعة الطلب'),
          if (selectedChild case final child?)
            UiText('${child['player']} · ${child['branch']}'),
          UiText(choice),
          MoneyText(
            confirmed ? sale!['amount'] : total,
            confirmed ? sale!['currency'] : currency,
            size: 22,
          ),
        ],
      ),
    );
  }

  @override
  Widget build(BuildContext context) => !current
      ? const SizedBox.shrink()
      : GuardianPage(
          operation: true,
          title: textOf(widget.product['arabicName']),
          child: FutureBuilder<void>(
            future: loaded,
            builder: (context, data) {
              if (data.connectionState != ConnectionState.done) {
                return const Center(
                  child: CircularProgressIndicator(
                    semanticsLabel: 'جارٍ تحميل بيانات الطلب',
                  ),
                );
              }
              if (data.hasError) {
                return gList([
                  const EmptyMessage('تعذر تحميل تسجيلات الأبناء.'),
                  OutlinedButton(
                    onPressed: () => setState(() => loaded = load()),
                    child: const UiText('إعادة المحاولة'),
                  ),
                ]);
              }
              if (sale != null) {
                return gList([
                  orderSummary(confirmed: true),
                  _ProductProof(auth: auth, id: textOf(sale!['id'])),
                ]);
              }
              final selectionComplete =
                  inStock && (!variantsRequired || selectedVariant != null);
              final canReview = selectionComplete && total != null;
              return gList([
                if ((widget.product['imageReference']?.toString().trim() ?? '')
                    .isNotEmpty)
                  DemoImage(widget.product['imageReference'], height: 160),
                if ((widget.product['arabicDescription']?.toString().trim() ??
                        '')
                    .isNotEmpty)
                  UiText(textOf(widget.product['arabicDescription'])),
                if (contexts.isEmpty)
                  const EmptyMessage('لا توجد تسجيلات للأبناء في هذه الرياضة.')
                else ...[
                  selector(
                    'الطفل والفرع',
                    enrollment,
                    [
                      for (final child in contexts)
                        DropdownMenuItem(
                          value: textOf(child['id']),
                          child: UiText(
                            '${child['player']} · ${child['branch']}',
                            overflow: TextOverflow.ellipsis,
                          ),
                        ),
                    ],
                    (value) {
                      setState(() {
                        enrollment = value;
                        intent = commandKey();
                      });
                      loadInventory();
                    },
                  ),
                  if (inventoryBusy)
                    const LinearProgressIndicator(
                      semanticsLabel: 'جارٍ تحميل السعر والتوفّر',
                    ),
                  if (!canReview && error != null)
                    Semantics(liveRegion: true, child: UiText(error!)),
                  if (enrollment != null &&
                      !inventoryBusy &&
                      !canReview &&
                      (inventory == null || !inStock || total == null))
                    OutlinedButton(
                      onPressed: canChange ? loadInventory : null,
                      child: const UiText('تحديث السعر والتوفّر'),
                    ),
                  if (inventory != null && !inStock)
                    const EmptyMessage('المنتج غير متاح حاليًا.'),
                  if (inventory != null && inStock && total == null)
                    const EmptyMessage('السعر غير متاح حاليًا.'),
                  if (inStock && variantsRequired) ...variantControls(),
                  if (canReview) ...[
                    UiText('المتاح: $available قطعة'),
                    Row(
                      mainAxisAlignment: MainAxisAlignment.center,
                      children: [
                        IconButton(
                          tooltip: 'تقليل الكمية',
                          onPressed: canChange && quantity > 1
                              ? () => setState(() {
                                  quantity--;
                                  intent = commandKey();
                                })
                              : null,
                          icon: const Icon(Icons.remove_circle_outline),
                        ),
                        Semantics(label: 'الكمية', child: UiText('$quantity')),
                        IconButton(
                          tooltip: 'زيادة الكمية',
                          onPressed:
                              canChange &&
                                  quantity < available &&
                                  quantity < 100
                              ? () => setState(() {
                                  quantity++;
                                  intent = commandKey();
                                })
                              : null,
                          icon: const Icon(Icons.add_circle_outline),
                        ),
                      ],
                    ),
                    orderSummary(confirmed: false),
                    if (error != null)
                      Semantics(liveRegion: true, child: UiText(error!)),
                    if (refreshRequired)
                      OutlinedButton(
                        onPressed: canChange ? loadInventory : null,
                        child: const UiText('تحديث السعر والتوفّر'),
                      ),
                    if (uncertain)
                      const UiText(
                        'لم يتأكد إنشاء الطلب. أعد إرسال نفس الاختيارات أو راجع طلباتك.',
                      ),
                    FilledButton(
                      onPressed: ready ? submit : null,
                      child: UiText(
                        uncertain
                            ? 'إعادة تأكيد الطلب نفسه'
                            : 'متابعة إلى الدفع',
                      ),
                    ),
                  ],
                  if (busy)
                    const Center(
                      child: CircularProgressIndicator(
                        semanticsLabel: 'جارٍ فتح الطلب',
                      ),
                    ),
                ],
              ]);
            },
          ),
        );
}

String? _productStatusLabel(Object? status) => switch (status) {
  'Confirmed' => 'تم تأكيد الدفع',
  'Rejected' => 'الطلب مرفوض',
  _ => null,
};

String _productVariant(Json order) => [
  if (textOf(order['colorSnapshot'] ?? '').trim().isNotEmpty)
    textOf(order['colorSnapshot']),
  if (textOf(order['sizeSnapshot'] ?? '').trim().isNotEmpty)
    textOf(order['sizeSnapshot']),
  if (order['shirtNumberSnapshot'] != null)
    'رقم ${order['shirtNumberSnapshot']}',
].join(' · ');

class GuardianProductOrders extends StatefulWidget {
  const GuardianProductOrders({super.key, required this.auth});
  final AuthController auth;
  @override
  State<GuardianProductOrders> createState() => _GuardianProductOrdersState();
}

class _GuardianProductOrdersState extends State<GuardianProductOrders> {
  int revision = 0, page = 1;

  void reload() => setState(() => revision++);
  void changePage(int number) => setState(() => page = number);

  Future<void> open(String id) async {
    final session = widget.auth.session;
    await openPage(context, GuardianProductOrder(auth: widget.auth, id: id));
    if (mounted &&
        widget.auth.session?.selected?.id == session?.selected?.id &&
        widget.auth.session?.selected?.contextRevision ==
            session?.selected?.contextRevision &&
        widget.auth.session?.selected?.academyId ==
            session?.selected?.academyId &&
        widget.auth.session?.userId == session?.userId) {
      reload();
    }
  }

  @override
  Widget build(BuildContext context) => GuardianPage(
    operation: true,
    title: 'طلبات المنتجات',
    actions: [
      IconButton(
        tooltip: ui(context, 'تحديث الطلبات'),
        onPressed: reload,
        icon: const Icon(Icons.refresh_rounded),
      ),
    ],
    child: RemoteBody(
      key: ValueKey((page, revision)),
      auth: widget.auth,
      path: '/api/v1/guardian/product-sales?page=$page&pageSize=25',
      autoPaginate: false,
      builder: (context, value) {
        final items = rows(value is List ? value : obj(value)['items']);
        final hasMore = value is Map && value['hasMore'] == true;
        final list = gList([
          if (items.isEmpty)
            EmptyMessage(
              page == 1
                  ? 'لا توجد طلبات منتجات بعد.'
                  : 'لا توجد طلبات في هذه الصفحة.',
            ),
          for (final row in items)
            GCard(
              key: ValueKey('product-order:${row['id']}'),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  Heading(textOf(row['productName'])),
                  StatusPill(
                    row['status'],
                    displayLabel: _productStatusLabel(row['status']),
                  ),
                  UiText(
                    '${row['player'] ?? 'طلب للأسرة'} · الكمية ${row['quantity']}',
                  ),
                  if (_productVariant(row).isNotEmpty)
                    UiText(_productVariant(row)),
                  PaymentRequestReference(order: row),
                  MoneyText(row['amount'], row['currency'], size: 22),
                  TextButton(
                    onPressed: () => open(textOf(row['id'])),
                    child: UiText(
                      row['status'] == 'Pending' ? 'متابعة الطلب' : 'عرض الطلب',
                    ),
                  ),
                ],
              ),
            ),
        ], key: PageStorageKey('guardian-product-orders-$page'));
        if (page == 1 && !hasMore) return list;
        return Column(
          children: [
            Expanded(child: list),
            PageActionBar(
              child: Row(
                children: [
                  if (page > 1)
                    IconButton(
                      tooltip: ui(context, 'الصفحة السابقة'),
                      onPressed: () => changePage(page - 1),
                      icon: const Icon(Icons.chevron_left),
                    )
                  else
                    const SizedBox(width: 48),
                  Expanded(
                    child: UiText(
                      '${ui(context, 'الصفحة')} $page',
                      textAlign: TextAlign.center,
                    ),
                  ),
                  if (hasMore)
                    IconButton(
                      tooltip: ui(context, 'الصفحة التالية'),
                      onPressed: () => changePage(page + 1),
                      icon: const Icon(Icons.chevron_right),
                    )
                  else
                    const SizedBox(width: 48),
                ],
              ),
            ),
          ],
        );
      },
    ),
  );
}

class GuardianProductOrder extends StatefulWidget {
  const GuardianProductOrder({super.key, required this.auth, required this.id});
  final AuthController auth;
  final String id;

  @override
  State<GuardianProductOrder> createState() => _GuardianProductOrderState();
}

class _GuardianProductOrderState extends State<GuardianProductOrder> {
  int revision = 0;

  @override
  Widget build(BuildContext context) => GuardianPage(
    operation: true,
    title: 'طلب المنتج',
    child: RemoteBody(
      key: ValueKey(revision),
      auth: widget.auth,
      path: '/api/v1/guardian/product-sales/${widget.id}',
      notFound: const PageStatus(
        child: StatusPanel(message: 'طلب المنتج غير متاح لحسابك.'),
      ),
      builder: (context, value) {
        final r = obj(value);
        if (r['id'] != widget.id) {
          return PageStatus(
            child: StatusPanel(
              message: 'تعذر قراءة طلب المنتج.',
              onAction: () => setState(() => revision++),
              actionLabel: 'إعادة المحاولة',
            ),
          );
        }
        return gList([
          Heading(textOf(r['productName'])),
          StatusPill(
            r['status'],
            displayLabel: _productStatusLabel(r['status']),
          ),
          PaymentRequestReference(order: r),
          GCard(
            child: Column(
              children:
                  [
                        if (textOf(r['player'] ?? '').trim().isNotEmpty)
                          ('الطفل', textOf(r['player'])),
                        if (textOf(r['sizeSnapshot'] ?? '').trim().isNotEmpty)
                          ('المقاس', textOf(r['sizeSnapshot'])),
                        if (textOf(r['colorSnapshot'] ?? '').isNotEmpty)
                          ('اللون', textOf(r['colorSnapshot'] ?? '')),
                        if (r['shirtNumberSnapshot'] != null)
                          ('رقم القميص', textOf(r['shirtNumberSnapshot'])),
                        ('الكمية', textOf(r['quantity'])),
                        (
                          r['status'] == 'Confirmed'
                              ? 'المبلغ المدفوع'
                              : 'مبلغ الطلب',
                          money(r['amount'], r['currency']),
                        ),
                        if (textOf(r['reviewReason'] ?? '').trim().isNotEmpty)
                          ('رد الإدارة', textOf(r['reviewReason'])),
                      ]
                      .map(
                        (item) => Padding(
                          padding: const EdgeInsets.symmetric(vertical: 10),
                          child: Row(
                            crossAxisAlignment: CrossAxisAlignment.start,
                            children: [
                              Expanded(
                                flex: 2,
                                child: UiText(
                                  item.$1,
                                  style: TextStyle(
                                    fontSize: 12,
                                    color: Theme.of(context)
                                        .colorScheme
                                        .onSurfaceVariant,
                                  ),
                                ),
                              ),
                              Expanded(
                                flex: 3,
                                child: UiText(
                                  item.$2,
                                  style: const TextStyle(
                                    fontSize: 15,
                                    fontWeight: FontWeight.w600,
                                  ),
                                ),
                              ),
                            ],
                          ),
                        ),
                      )
                      .toList(),
            ),
          ),
          if (r['status'] == 'Confirmed')
            const UiText('تواصل مع الأكاديمية لترتيب استلام المنتج.'),
          if (r['status'] == 'Pending')
            _ProductProof(
              auth: widget.auth,
              id: widget.id,
              received: r['hasProof'] == true,
            ),
          OutlinedButton(
            onPressed: () => setState(() => revision++),
            child: const UiText('تحديث حالة الطلب'),
          ),
        ]);
      },
    ),
  );
}

class _ProductProof extends StatefulWidget {
  const _ProductProof({
    required this.auth,
    required this.id,
    this.received = false,
  });
  final AuthController auth;
  final String id;
  final bool received;

  @override
  State<_ProductProof> createState() => _ProductProofState();
}

class _ProductProofState extends State<_ProductProof> {
  bool submitted = false, replacing = false;

  @override
  Widget build(BuildContext context) {
    if ((widget.received || submitted) && !replacing) {
      return GCard(
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            const Heading('إثبات الدفع قيد المراجعة'),
            TextButton(
              onPressed: () => setState(() => replacing = true),
              child: const UiText('استبدال إثبات الدفع'),
            ),
          ],
        ),
      );
    }
    return TransferPayment(
      auth: widget.auth,
      proofPath: '/api/v1/guardian/product-sales/${widget.id}/proof',
      subscription: false,
      replacement: replacing,
      onSubmitted: () {
        if (mounted) {
          setState(() {
            submitted = true;
            replacing = false;
          });
        }
      },
    );
  }
}
