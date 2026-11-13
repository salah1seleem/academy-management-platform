import 'package:flutter/material.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/theme/ui_text.dart';
import '../../core/widgets/page_status.dart';
import '../guardian/guardian_ui.dart';
import '../shared/operation_design.dart';

int? _stockCount(Object? value) => value is int && value >= 0 ? value : null;

class OwnerInventoryPreview extends StatefulWidget {
  const OwnerInventoryPreview({super.key, required this.auth});
  final AuthController auth;

  @override
  State<OwnerInventoryPreview> createState() => _OwnerInventoryPreviewState();
}

class _OwnerInventoryPreviewState extends State<OwnerInventoryPreview> {
  int _revision = 0;

  void _refresh() => setState(() => _revision++);

  Future<void> _open(Json item) async {
    await openPage<void>(
      context,
      OwnerInventoryRead(auth: widget.auth, product: item),
    );
    if (mounted) _refresh();
  }

  @override
  Widget build(BuildContext context) => Theme(
    data: operationTheme(context),
    child: Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Row(
          children: [
            const Expanded(child: Heading('المنتجات الرياضية')),
            IconButton(
              tooltip: 'تحديث المنتجات',
              onPressed: _refresh,
              icon: const Icon(Icons.refresh_rounded),
            ),
          ],
        ),
        RemoteBody(
          key: ValueKey(_revision),
          auth: widget.auth,
          path: '/api/v1/content/catalog/?active=true',
          builder: (context, value) {
            if (value is! List ||
                value.any(
                  (row) =>
                      row is! Json ||
                      row['id'] is! String ||
                      (row['id'] as String).isEmpty ||
                      row['arabicName'] is! String ||
                      (row['arabicName'] as String).trim().isEmpty,
                )) {
              return const EmptyMessage('تعذر قراءة المنتجات. حدّث القائمة.');
            }
            final items = value.cast<Json>();
            if (items.isEmpty) {
              return const EmptyMessage('لا توجد منتجات رياضية نشطة.');
            }
            return SingleChildScrollView(
              key: const PageStorageKey('owner-inventory-products'),
              scrollDirection: Axis.horizontal,
              child: IntrinsicHeight(
                child: Row(
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                    for (final item in items)
                      Padding(
                        padding: const EdgeInsetsDirectional.only(end: 12),
                        child: SizedBox(
                          width: 224,
                          child: GCard(
                            padding: const EdgeInsets.all(14),
                            onTap: () => _open(item),
                            child: Column(
                              crossAxisAlignment: CrossAxisAlignment.stretch,
                              children: [
                                UiText(
                                  item['arabicName'] as String,
                                  style: const TextStyle(
                                    fontWeight: FontWeight.w800,
                                  ),
                                ),
                                const SizedBox(height: 6),
                                UiText(
                                  switch (_stockCount(item['stockQuantity'])) {
                                    final count? => 'المتبقي $count',
                                    null => 'الرصيد غير متاح',
                                  },
                                  style: TextStyle(
                                    fontSize: 19,
                                    fontWeight: FontWeight.w800,
                                    color: Theme.of(context)
                                        .colorScheme
                                        .primary,
                                  ),
                                ),
                                if (item['usesVariants'] == true)
                                  const UiText('المخزون حسب المقاس'),
                                const SizedBox(height: 10),
                                DemoImage(item['imageReference'], height: 92),
                              ],
                            ),
                          ),
                        ),
                      ),
                  ],
                ),
              ),
            );
          },
        ),
      ],
    ),
  );
}

class OwnerInventoryRead extends StatefulWidget {
  const OwnerInventoryRead({
    super.key,
    required this.auth,
    required this.product,
  });
  final AuthController auth;
  final Json product;

  @override
  State<OwnerInventoryRead> createState() => _OwnerInventoryReadState();
}

class _OwnerInventoryReadState extends State<OwnerInventoryRead> {
  int _revision = 0;

  void _refresh() => setState(() => _revision++);

  @override
  Widget build(BuildContext context) {
    final id = textOf(widget.product['id']);
    return GuardianPage(
      operation: true,
      title: 'مخزون المنتج',
      actions: [
        IconButton(
          tooltip: 'تحديث المخزون',
          onPressed: _refresh,
          icon: const Icon(Icons.refresh_rounded),
        ),
      ],
      child: RemoteBody(
        key: ValueKey(_revision),
        auth: widget.auth,
        path: '/api/v1/content/catalog/${Uri.encodeComponent(id)}/inventory',
        notFound: const PageStatus(
          child: StatusPanel(message: 'مخزون هذا المنتج غير متاح.'),
        ),
        builder: (context, value) {
          final total = value is Json
              ? _stockCount(value['stockQuantity'])
              : null;
          final variants = value is Json
              ? _inventoryRows(value, id, total)
              : null;
          if (total == null || variants == null) {
            return PageStatus(
              child: StatusPanel(
                message: 'تعذر التحقق من رصيد المنتج.',
                icon: Icons.warning_amber_rounded,
                urgent: true,
                actionLabel: 'إعادة المحاولة',
                onAction: _refresh,
              ),
            );
          }
          return gList([
            GCard(
              child: Row(
                children: [
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        UiText(textOf(widget.product['arabicName'])),
                        const SizedBox(height: 4),
                        Heading('إجمالي المتبقي $total'),
                      ],
                    ),
                  ),
                  const SizedBox(width: 12),
                  SizedBox(
                    width: 68,
                    child: DemoImage(
                      widget.product['imageReference'],
                      height: 68,
                    ),
                  ),
                ],
              ),
            ),
            if (variants.isNotEmpty) const Heading('المقاسات'),
            for (final variant in variants)
              GCard(
                padding: const EdgeInsets.symmetric(
                  horizontal: 16,
                  vertical: 12,
                ),
                child: Row(
                  children: [
                    Expanded(child: UiText(_variantLabel(variant))),
                    const SizedBox(width: 12),
                    UiText(
                      '${variant['quantity']}',
                      style: const TextStyle(
                        fontSize: 22,
                        fontWeight: FontWeight.w800,
                      ),
                    ),
                  ],
                ),
              ),
          ]);
        },
      ),
    );
  }
}

List<Json>? _inventoryRows(Json value, String id, int? total) {
  final rows = value['variants'];
  if (value['id'] != id || value['usesVariants'] is! bool || rows is! List) {
    return null;
  }
  if (value['usesVariants'] == false) return rows.isEmpty ? [] : null;
  if (rows.any(
    (row) =>
        row is! Json ||
        row['size'] is! String ||
        (row['size'] as String).trim().isEmpty ||
        _stockCount(row['quantity']) == null ||
        (row['color'] != null && row['color'] is! String) ||
        (row['shirtNumber'] != null && _stockCount(row['shirtNumber']) == null),
  )) {
    return null;
  }
  final variants = rows.cast<Json>();
  return variants.fold<int>(0, (sum, row) => sum + (row['quantity'] as int)) ==
          total
      ? variants
      : null;
}

String _variantLabel(Json row) {
  final color = (row['color'] as String?)?.trim();
  return [
    if (color != null && color.isNotEmpty) color,
    'مقاس ${row['size']}',
    if (row['shirtNumber'] != null) 'رقم ${row['shirtNumber']}',
  ].join(' · ');
}
