import 'package:flutter/material.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/theme/ui_text.dart';
import '../guardian/guardian_ui.dart';
import '../shared/operation_design.dart';

class OwnerInventoryPreview extends StatelessWidget {
  const OwnerInventoryPreview({super.key, required this.auth});
  final AuthController auth;
  @override
  Widget build(BuildContext context) => RemoteBody(
    auth: auth,
    path: '/api/v1/content/catalog/?active=true',
    builder: (context, value) {
      final items = rows(value);
      if (items.isEmpty) return const SizedBox.shrink();
      return Theme(
        data: operationTheme(context),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            const Heading('المنتجات الرياضية'),
            SingleChildScrollView(
              scrollDirection: Axis.horizontal,
              child: IntrinsicHeight(
                child: Row(
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                    for (final item in items)
                      Padding(
                        padding: const EdgeInsetsDirectional.only(end: 12),
                        child: SizedBox(
                          width: 244,
                          child: GCard(
                            onTap: () => openPage(
                              context,
                              OwnerInventoryRead(auth: auth, product: item),
                            ),
                            child: Column(
                              crossAxisAlignment: CrossAxisAlignment.stretch,
                              children: [
                                DemoImage(item['imageReference'], height: 180),
                                const SizedBox(height: 12),
                                UiText(
                                  textOf(item['arabicName']),
                                  style: const TextStyle(
                                    fontWeight: FontWeight.w700,
                                    fontSize: 15,
                                  ),
                                ),
                                const SizedBox(height: 6),
                                UiText(
                                  'المتبقي ${item['stockQuantity'] ?? 0}',
                                  style: TextStyle(
                                    fontSize: 22,
                                    fontWeight: FontWeight.w800,
                                    color: Theme.of(context)
                                        .colorScheme
                                        .primary,
                                  ),
                                ),
                                const SizedBox(height: 4),
                                UiText(
                                  item['usesVariants'] == true
                                      ? 'المخزون حسب المقاس'
                                      : 'المخزون الإجمالي',
                                  style: const TextStyle(fontSize: 12),
                                ),
                              ],
                            ),
                          ),
                        ),
                      ),
                  ],
                ),
              ),
            ),
          ],
        ),
      );
    },
  );
}

class OwnerInventoryRead extends StatelessWidget {
  const OwnerInventoryRead({
    super.key,
    required this.auth,
    required this.product,
  });
  final AuthController auth;
  final Json product;
  @override
  Widget build(BuildContext context) => GuardianPage(
    operation: true,
    title: textOf(product['arabicName']),
    child: RemoteBody(
      auth: auth,
      path: '/api/v1/content/catalog/${product['id']}/inventory',
      builder: (context, value) {
        final inventory = obj(value);
        return gList([
          DemoImage(product['imageReference'], height: 230),
          const SizedBox(height: 16),
          Heading('إجمالي المتبقي ${inventory['stockQuantity']}'),
          for (final variant in rows(inventory['variants']))
            GCard(
              child: Row(
                children: [
                  Expanded(
                    child: UiText(
                      '${(variant['color']?.toString() ?? '').isEmpty ? '' : '${variant['color']} · '}مقاس ${variant['size']}${variant['shirtNumber'] == null ? '' : ' · رقم ${variant['shirtNumber']}'}',
                    ),
                  ),
                  UiText(
                    '${variant['quantity']}',
                    style: const TextStyle(
                      fontSize: 24,
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
