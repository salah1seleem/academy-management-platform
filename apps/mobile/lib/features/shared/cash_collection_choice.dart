import 'package:flutter/material.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/theme/ui_text.dart';
import '../guardian/guardian_ui.dart';
import '../owner/cash_registers.dart';

/// Cash readiness is a business state, not a failed page load. The server
/// still rechecks configuration, scope, price and the personal shift on collect.
class CashCollectionChoice extends StatefulWidget {
  const CashCollectionChoice({
    super.key,
    required this.auth,
    required this.path,
    required this.value,
    required this.onChange,
  });
  final AuthController auth;
  final String path;
  final String? value;
  final ValueChanged<String?>? onChange;
  @override
  State<CashCollectionChoice> createState() => _CashCollectionChoiceState();
}

class _CashCollectionChoiceState extends State<CashCollectionChoice> {
  int revision = 0;
  void reload() {
    widget.onChange?.call(null);
    setState(() => revision++);
  }

  Future<void> registers() async {
    await showModalBottomSheet<void>(
      context: context,
      isScrollControlled: true,
      useSafeArea: true,
      builder: (sheet) => SizedBox(
        height: MediaQuery.sizeOf(sheet).height * .8,
        child: Column(
          children: [
            Padding(
              padding: const EdgeInsets.all(16),
              child: Row(
                children: [
                  const Expanded(child: UiText('الصناديق والعهد')),
                  IconButton(
                    tooltip: 'إغلاق',
                    onPressed: () => Navigator.pop(sheet),
                    icon: const Icon(Icons.close),
                  ),
                ],
              ),
            ),
            Expanded(child: NativeCashRegisters(auth: widget.auth)),
          ],
        ),
      ),
    );
    if (mounted) reload();
  }

  @override
  Widget build(BuildContext context) => RemoteBody(
    key: ValueKey('${widget.path}:$revision'),
    auth: widget.auth,
    path: widget.path,
    builder: (context, result) {
      final data = obj(result), sessions = rows(data['sessions']);
      if (data['cashEnabled'] == false || sessions.isEmpty) {
        final disabled = data['cashEnabled'] == false;
        return Padding(
          padding: const EdgeInsets.symmetric(vertical: 16),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Row(
                children: [
                  const Icon(Icons.account_balance_wallet_outlined),
                  const SizedBox(width: 10),
                  Expanded(
                    child: UiText(
                      disabled
                          ? 'التحصيل النقدي غير مفعّل'
                          : 'لا توجد وردية مفتوحة لك',
                      style: Theme.of(context).textTheme.titleMedium,
                    ),
                  ),
                ],
              ),
              const SizedBox(height: 10),
              UiText(
                disabled
                    ? data['canPrepare'] == true
                          ? 'فعّل النقدي من إعدادات الأكاديمية، ثم افتح ورديتك من الصناديق والعهد في لوحة الإدارة.'
                          : 'اطلب من المالك تفعيل النقدي وتجهيز وردية باسمك لهذا الفرع.'
                    : 'افتح وردية مخولة باسمك لهذا الفرع وعملة الباقة من الصناديق والعهد في لوحة الإدارة.',
              ),
              const SizedBox(height: 12),
              Wrap(
                spacing: 8,
                runSpacing: 8,
                children: [
                  OutlinedButton.icon(
                    onPressed: widget.onChange == null ? null : registers,
                    icon: const Icon(Icons.account_balance_outlined),
                    label: const UiText('حالة الصناديق'),
                  ),
                  TextButton.icon(
                    onPressed: widget.onChange == null ? null : reload,
                    icon: const Icon(Icons.refresh),
                    label: const UiText('تحديث حالة النقدي'),
                  ),
                ],
              ),
            ],
          ),
        );
      }
      return DropdownButtonFormField<String>(
        key: ValueKey('cash-shift-$revision'),
        initialValue: sessions.any((s) => s['id'] == widget.value)
            ? widget.value
            : null,
        isExpanded: true,
        decoration: const InputDecoration(labelText: 'وردية التحصيل'),
        items: [
          for (final s in sessions)
            DropdownMenuItem(
              value: textOf(s['id']),
              child: UiText('${s['name']} · ${s['branch']}'),
            ),
        ],
        onChanged: widget.onChange,
      );
    },
  );
}
