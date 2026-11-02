import 'package:flutter/material.dart';

import '../../core/api/api_client.dart';
import '../../core/auth/auth_controller.dart';
import '../../core/theme/ui_text.dart';
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
  bool numberConfirmed = false, uncertain = false, inventoryBusy = false;
  late Future<void> loaded;
  bool get variantsRequired => widget.product['usesVariants'] == true;
  bool get numbered => inventory?['numberedUniform'] == true;
  List<Json> get variants => rows(inventory?['variants'] ?? []);
  Json? get selectedVariant =>
      variants.where((v) => v['id'] == variant).firstOrNull;
  Json? get selectedChild =>
      contexts.where((e) => e['id'] == enrollment).firstOrNull;
  int get available => variantsRequired
      ? (selectedVariant?['quantity'] as num? ?? 0).toInt()
      : (widget.product['stockQuantity'] as num? ?? 0).toInt();
  double? get unit {
    if (variantsRequired) return double.tryParse('${inventory?['unitPrice']}');
    final price = double.tryParse('${widget.product['displayPrice']}');
    final discount =
        double.tryParse('${widget.product['discountPercentage']}') ?? 0;
    return price == null ? null : (price * (100 - discount)).round() / 100;
  }

  bool get ready =>
      !busy &&
      !inventoryBusy &&
      enrollment != null &&
      unit != null &&
      quantity <= available &&
      (!variantsRequired ||
          selectedVariant != null &&
              (!variants.any((v) => textOf(v['color'] ?? '').isNotEmpty) ||
                  color != null) &&
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
    if (contexts.length == 1) enrollment = textOf(contexts.single['id']);
    if (variantsRequired) await loadInventory();
  }

  Future<void> loadInventory() async {
    final epoch = ++inventoryEpoch;
    if (current) {
      setState(() {
        inventoryBusy = true;
        variant = null;
        numberConfirmed = false;
        error = null;
      });
    }
    try {
      final data = obj(
        await auth.request(
          'GET',
          '/api/v1/guardian/catalog/${widget.product['id']}/variants${enrollment == null ? '' : '?enrollmentId=${Uri.encodeComponent(enrollment!)}'}',
        ),
      );
      if (!current || epoch != inventoryEpoch) return;
      setState(() {
        inventory = data;
        if (!variants.any((v) => textOf(v['color'] ?? '') == color)) {
          color = null;
        }
        final colors = variants.map((v) => textOf(v['color'] ?? '')).toSet();
        if (color == null && colors.length == 1) {
          color = colors.single;
        }
        selectSize(size);
      });
    } catch (e) {
      if (current && epoch == inventoryEpoch) {
        setState(() {
          inventory = null;
          error = e is ApiFailure ? e.message : 'تعذر تحديث المقاسات والأرقام.';
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
    final choices = variants
        .where(
          (v) =>
              v['size'] == size &&
              (color == null || textOf(v['color'] ?? '') == color),
        )
        .toList();
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
              'expectedAmount': (unit! * quantity).toStringAsFixed(2),
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
          setState(
            () => uncertain =
                e is! ApiFailure || e.status == 0 || e.status >= 500,
          );
        }
        rethrow;
      }
    });
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
              if (data.hasError) {
                return Center(
                  child: TextButton(
                    onPressed: () => setState(() => loaded = load()),
                    child: const UiText('إعادة المحاولة'),
                  ),
                );
              }
              if (data.connectionState != ConnectionState.done) {
                return const Center(child: CircularProgressIndicator());
              }
              return gList([
                DemoImage(widget.product['imageReference'], height: 190),
                Heading(textOf(widget.product['arabicName'])),
                if ((widget.product['arabicDescription']?.toString().trim() ??
                        '')
                    .isNotEmpty)
                  UiText(textOf(widget.product['arabicDescription'])),
                Heading(
                  money(
                    unit == null ? null : unit! * quantity,
                    widget.product['currency'],
                  ),
                ),
                if (sale == null) ...[
                  if (contexts.isEmpty)
                    const EmptyMessage('لا توجد تسجيلات متاحة لهذه الرياضة.'),
                  DropdownButtonFormField<String>(
                    initialValue: enrollment,
                    isExpanded: true,
                    decoration: const InputDecoration(
                      labelText: 'الطفل والفرع',
                    ),
                    items: [
                      for (final e in contexts)
                        DropdownMenuItem(
                          value: textOf(e['id']),
                          child: UiText(
                            '${e['player']} · ${e['branch']}',
                            overflow: TextOverflow.ellipsis,
                          ),
                        ),
                    ],
                    onChanged: busy || uncertain
                        ? null
                        : (v) {
                            setState(() {
                              enrollment = v;
                              intent = commandKey();
                            });
                            if (variantsRequired) loadInventory();
                          },
                  ),
                  if (variantsRequired) ...[
                    if (inventoryBusy) const LinearProgressIndicator(),
                    OutlinedButton(
                      onPressed: busy || uncertain || inventoryBusy
                          ? null
                          : loadInventory,
                      child: const UiText('تحديث المقاسات والأرقام'),
                    ),
                    if (variants.any(
                      (v) => textOf(v['color'] ?? '').isNotEmpty,
                    ))
                      DropdownButtonFormField<String>(
                        key: ValueKey('color:$color:$inventoryEpoch'),
                        initialValue: color,
                        isExpanded: true,
                        decoration: const InputDecoration(labelText: 'اللون'),
                        items: [
                          for (final value
                              in variants
                                  .map((v) => textOf(v['color'] ?? ''))
                                  .toSet())
                            DropdownMenuItem(
                              value: value,
                              child: UiText(
                                value.isEmpty ? 'اللون الأساسي' : value,
                              ),
                            ),
                        ],
                        onChanged: busy || uncertain || inventoryBusy
                            ? null
                            : (v) => setState(() {
                                color = v;
                                selectSize(null);
                              }),
                      ),
                    DropdownButtonFormField<String>(
                      key: ValueKey('size:$size:$inventoryEpoch'),
                      initialValue: variants.any((v) => v['size'] == size)
                          ? size
                          : null,
                      isExpanded: true,
                      decoration: const InputDecoration(labelText: 'المقاس'),
                      items: [
                        for (final value
                            in variants
                                .where(
                                  (v) =>
                                      color == null ||
                                      textOf(v['color'] ?? '') == color,
                                )
                                .map((v) => textOf(v['size']))
                                .toSet())
                          DropdownMenuItem(value: value, child: UiText(value)),
                      ],
                      onChanged: busy || uncertain || inventoryBusy
                          ? null
                          : (v) => setState(() => selectSize(v)),
                    ),
                    if (numbered && size != null) ...[
                      if (inventory?['assignedShirtNumber'] != null &&
                          !variants.any(
                            (v) =>
                                v['size'] == size &&
                                (color == null ||
                                    textOf(v['color'] ?? '') == color) &&
                                v['shirtNumber'] ==
                                    inventory!['assignedShirtNumber'],
                          ))
                        const UiText(
                          'رقم الطفل غير متاح بهذا المقاس. اختر رقمًا متاحًا وراجعه قبل المتابعة.',
                        ),
                      DropdownButtonFormField<String>(
                        key: ValueKey('number:$size:$variant:$inventoryEpoch'),
                        initialValue: variant,
                        isExpanded: true,
                        decoration: const InputDecoration(
                          labelText: 'رقم القميص المتاح',
                        ),
                        items: [
                          for (final v in variants.where(
                            (v) =>
                                v['size'] == size &&
                                (color == null ||
                                    textOf(v['color'] ?? '') == color),
                          ))
                            DropdownMenuItem(
                              value: textOf(v['id']),
                              child: UiText(
                                '${v['shirtNumber']} · المتاح ${v['quantity']}',
                              ),
                            ),
                        ],
                        onChanged: busy || uncertain || inventoryBusy
                            ? null
                            : (v) => setState(() {
                                variant = v;
                                numberConfirmed = false;
                                quantity = 1;
                                intent = commandKey();
                              }),
                      ),
                      CheckboxListTile(
                        value: numberConfirmed,
                        title: UiText(
                          'أؤكد رقم القميص المختار: ${selectedVariant?['shirtNumber'] ?? 'اختر رقمًا'}',
                        ),
                        onChanged: busy || uncertain || variant == null
                            ? null
                            : (v) =>
                                  setState(() => numberConfirmed = v == true),
                      ),
                    ],
                  ],
                  UiText(
                    variantsRequired && selectedVariant == null
                        ? 'اختر المقاس${numbered ? ' ورقم القميص' : ''} لعرض الرصيد.'
                        : 'المتاح للاختيار: $available',
                  ),
                  Row(
                    mainAxisAlignment: MainAxisAlignment.center,
                    children: [
                      IconButton(
                        tooltip: 'تقليل الكمية',
                        onPressed: busy || uncertain || quantity <= 1
                            ? null
                            : () => setState(() {
                                quantity--;
                                intent = commandKey();
                              }),
                        icon: const Icon(Icons.remove_circle_outline),
                      ),
                      UiText('$quantity'),
                      IconButton(
                        tooltip: 'زيادة الكمية',
                        onPressed:
                            busy ||
                                uncertain ||
                                quantity >= available ||
                                quantity >= 100
                            ? null
                            : () => setState(() {
                                quantity++;
                                intent = commandKey();
                              }),
                        icon: const Icon(Icons.add_circle_outline),
                      ),
                    ],
                  ),
                  GCard(
                    child: UiText(
                      'مراجعة الطلب: ${selectedChild?['player'] ?? 'اختر الطفل'} · ${selectedChild?['branch'] ?? ''}\n${color == null || color!.isEmpty ? '' : '$color · '}${size ?? 'مقاس عام'}${selectedVariant?['shirtNumber'] == null ? '' : ' · رقم ${selectedVariant!['shirtNumber']}'} · الكمية $quantity\n${money(unit == null ? null : unit! * quantity, widget.product['currency'])}',
                    ),
                  ),
                  if (uncertain)
                    const UiText(
                      'لم يتأكد إنشاء الطلب. أعد نفس الطلب دون تغيير الاختيار أو راجع طلباتك.',
                    ),
                  FilledButton(
                    onPressed: ready ? submit : null,
                    child: UiText(
                      uncertain ? 'إعادة تأكيد الطلب نفسه' : 'متابعة إلى الدفع',
                    ),
                  ),
                ] else ...[
                  Heading(money(sale!['amount'], sale!['currency'])),
                  GCard(
                    child: UiText(
                      '${selectedChild?['player'] ?? ''} · ${color == null || color!.isEmpty ? '' : '$color · '}${size ?? 'مقاس عام'}${selectedVariant?['shirtNumber'] == null ? '' : ' · رقم ${selectedVariant!['shirtNumber']}'} · الكمية $quantity',
                    ),
                  ),
                  TransferPayment(
                    auth: auth,
                    proofPath:
                        '/api/v1/guardian/product-sales/${sale!['id']}/proof',
                    subscription: false,
                  ),
                  const UiText(
                    'تُراجع الإدارة الدفع وتوافر المقاس والرقم قبل التأكيد. الاستلام من الأكاديمية.',
                  ),
                ],
                if (error != null) UiText(error!),
                if (busy) const Center(child: CircularProgressIndicator()),
              ]);
            },
          ),
        );
}

class GuardianProductOrders extends StatelessWidget {
  const GuardianProductOrders({super.key, required this.auth});
  final AuthController auth;
  @override
  Widget build(BuildContext context) => GuardianPage(
    operation: true,
    title: 'طلباتي من المنتجات الرياضية',
    child: RemoteBody(
      auth: auth,
      path: '/api/v1/guardian/product-sales',
      builder: (context, value) {
        final items = rows(value);
        return gList([
          if (items.isEmpty) const EmptyMessage('لا توجد طلبات منتجات بعد.'),
          for (final row in items)
            GCard(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  Heading(textOf(row['productName'])),
                  StatusPill(row['status']),
                  UiText(
                    '${row['player'] ?? 'منتج للأسرة'} · الكمية ${row['quantity']}\n${row['colorSnapshot'] == null || textOf(row['colorSnapshot'] ?? '').isEmpty ? '' : '${row['colorSnapshot']} · '}${row['sizeSnapshot'] ?? 'مقاس عام'}${row['shirtNumberSnapshot'] == null ? '' : ' · رقم ${row['shirtNumberSnapshot']}'}\n${money(row['amount'], row['currency'])}',
                  ),
                  TextButton(
                    onPressed: () => openPage(
                      context,
                      GuardianProductOrder(auth: auth, id: textOf(row['id'])),
                    ),
                    child: const UiText('عرض الطلب والدفع'),
                  ),
                ],
              ),
            ),
        ]);
      },
    ),
  );
}

class GuardianProductOrder extends StatelessWidget {
  const GuardianProductOrder({super.key, required this.auth, required this.id});
  final AuthController auth;
  final String id;
  @override
  Widget build(BuildContext context) => GuardianPage(
    operation: true,
    title: 'طلب المنتج الرياضي',
    child: RemoteBody(
      auth: auth,
      path: '/api/v1/guardian/product-sales/$id',
      builder: (context, value) {
        final r = obj(value);
        return gList([
          Heading(textOf(r['productName'])),
          StatusPill(r['status']),
          GCard(
            child: Column(
              children:
                  [
                        ('الطفل', textOf(r['player'] ?? 'منتج للأسرة')),
                        ('المقاس', textOf(r['sizeSnapshot'] ?? 'مقاس عام')),
                        if (textOf(r['colorSnapshot'] ?? '').isNotEmpty)
                          ('اللون', textOf(r['colorSnapshot'] ?? '')),
                        if (r['shirtNumberSnapshot'] != null)
                          ('رقم القميص', textOf(r['shirtNumberSnapshot'])),
                        ('الكمية', textOf(r['quantity'])),
                        ('المبلغ', money(r['amount'], r['currency'])),
                        (
                          'رد الإدارة',
                          textOf(r['reviewReason'] ?? 'لا توجد ملاحظة'),
                        ),
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
          if (r['status'] == 'Pending')
            TransferPayment(
              auth: auth,
              proofPath: '/api/v1/guardian/product-sales/$id/proof',
              subscription: false,
            ),
        ]);
      },
    ),
  );
}
