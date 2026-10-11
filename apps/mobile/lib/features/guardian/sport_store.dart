import 'package:academy_mobile/core/theme/ui_text.dart';
import 'package:flutter/material.dart';

import '../../core/api/api_client.dart';
import '../../core/auth/auth_controller.dart';
import '../../core/theme/appearance.dart';
import 'guardian_ui.dart';
import '../shared/transfer_payment.dart';

class ProductPurchase extends StatefulWidget {
  final AuthController auth;
  final Json product;
  const ProductPurchase({super.key, required this.auth, required this.product});
  @override
  State<ProductPurchase> createState() => _ProductPurchaseState();
}

class _ProductPurchaseState extends State<ProductPurchase> {
  int quantity = 1;
  bool busy = false;
  String? error, enrollment;
  List<Json> contexts = [];
  Json? sale;
  String key = commandKey();
  late Future<void> loaded;
  @override
  void initState() {
    super.initState();
    loaded = load();
  }

  Future<void> load() async {
    final result = await widget.auth.request(
      'GET',
      '/api/v1/guardian/subscriptions/enrollments',
    );
    contexts = rows(result)
        .where((e) => e['sportId'] == widget.product['sportId'])
        .toList();
    if (contexts.length == 1) enrollment = contexts.first['id'];
  }

  double? get unit => widget.product['displayPrice'] == null
      ? null
      : (widget.product['displayPrice'] as num).toDouble() *
            (100 - (widget.product['discountPercentage'] as num? ?? 0)) /
            100;
  Future<void> submit() async {
    if (busy || enrollment == null) return;
    final selected = contexts.firstWhere((e) => e['id'] == enrollment);
    setState(() {
      busy = true;
      error = null;
    });
    try {
      final result = await widget.auth.request(
        'POST',
        '/api/v1/guardian/product-sales',
        body: {
          'productId': widget.product['id'],
          'quantity': quantity,
          'playerId': selected['playerId'],
          'branchId': selected['branchId'],
        },
        idempotencyKey: key,
      );
      if (mounted) setState(() => sale = obj(result));
    } catch (e) {
      if (mounted) {
        setState(
          () => error = e is ApiFailure ? e.message : 'تعذر إرسال طلب المنتج.',
        );
      }
    } finally {
      if (mounted) setState(() => busy = false);
    }
  }

  @override
  Widget build(BuildContext context) => GuardianPage(
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
          UiText(textOf(widget.product['arabicDescription'])),
          Heading(
            money(
              unit == null ? null : unit! * quantity,
              widget.product['currency'],
            ),
          ),
          UiText(
            localized(
              context,
              'المتاح: ${widget.product['stockQuantity'] ?? 0}',
              'Available: ${widget.product['stockQuantity'] ?? 0}',
            ),
          ),
          if (sale == null) ...[
            if (contexts.isEmpty)
              const EmptyMessage('لا توجد تسجيلات متاحة لهذه الرياضة.'),
            DropdownButtonFormField<String>(
              initialValue: enrollment,
              isExpanded: true,
              decoration: InputDecoration(
                labelText: localized(context, 'الطفل والفرع', 'Child & branch'),
              ),
              items: [
                for (final e in contexts)
                  DropdownMenuItem(
                    value: e['id'],
                    child: UiText(
                      '${e['player']} · ${e['branch']}',
                      overflow: TextOverflow.ellipsis,
                    ),
                  ),
              ],
              onChanged: busy
                  ? null
                  : (v) => setState(() {
                      enrollment = v;
                      key = commandKey();
                    }),
            ),
            Row(
              mainAxisAlignment: MainAxisAlignment.center,
              children: [
                IconButton(
                  tooltip: localized(
                    context,
                    'تقليل الكمية',
                    'Decrease quantity',
                  ),
                  onPressed: busy || quantity == 1
                      ? null
                      : () => setState(() {
                          quantity--;
                          key = commandKey();
                        }),
                  icon: const Icon(Icons.remove_circle_outline),
                ),
                UiText('$quantity'),
                IconButton(
                  tooltip: localized(
                    context,
                    'زيادة الكمية',
                    'Increase quantity',
                  ),
                  onPressed:
                      busy ||
                          quantity >=
                              (widget.product['stockQuantity'] as num? ?? 0) ||
                          quantity >= 100
                      ? null
                      : () => setState(() {
                          quantity++;
                          key = commandKey();
                        }),
                  icon: const Icon(Icons.add_circle_outline),
                ),
              ],
            ),
            FilledButton(
              onPressed:
                  busy ||
                      enrollment == null ||
                      unit == null ||
                      (widget.product['stockQuantity'] as num? ?? 0) < 1
                  ? null
                  : submit,
              child: UiText(
                localized(context, 'متابعة إلى الدفع', 'Continue to payment'),
              ),
            ),
          ] else ...[
            Heading(money(sale!['amount'], sale!['currency'])),
            TransferPayment(
              auth: widget.auth,
              proofPath: '/api/v1/guardian/product-sales/${sale!['id']}/proof',
              subscription: false,
            ),
            UiText(
              localized(
                context,
                'تُراجع الإدارة الدفع وتوافر المخزون قبل التأكيد. الاستلام من الأكاديمية.',
                'Academy staff review payment and stock before confirmation. Collect from the academy.',
              ),
            ),
          ],
          if (error != null) UiText(error!),
        ]);
      },
    ),
  );
}
