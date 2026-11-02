import 'package:flutter/material.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/theme/ui_text.dart';
import '../admin/native_operation_form.dart';
import '../guardian/guardian_ui.dart';
import 'owner_settings_save.dart';

class CollectionTargetPage extends StatefulWidget {
  const CollectionTargetPage({
    super.key,
    required this.auth,
    this.from,
    this.to,
    this.branchId,
    this.branchName,
    this.initialAmount,
  });
  final AuthController auth;
  final String? from, to, branchId, branchName;
  final Object? initialAmount;
  @override
  State<CollectionTargetPage> createState() => _CollectionTargetPageState();
}

class _CollectionTargetPageState
    extends VerifiedOwnerSettingsState<CollectionTargetPage> {
  @override
  AuthController get auth => widget.auth;
  final amount = TextEditingController();
  List<Json> targets(Json active) =>
      rows(
            obj(
                  obj(active['settings'])['ownerHome'] ?? <String, dynamic>{},
                )['targets'] ??
                [],
          )
          .where(
            (t) =>
                t['from'] == widget.from &&
                t['to'] == widget.to &&
                (t['branchId'] ?? '') == (widget.branchId ?? ''),
          )
          .toList();
  @override
  void applySettings(Json value, {bool initial = false}) {
    final matches = targets(value);
    final stored = widget.from == null
        ? obj(value['settings'])['monthlyCollectionTarget']
        : matches.length == 1
        ? matches.single['amount']
        : null;
    amount.text =
        (initial && widget.from != null ? widget.initialAmount : stored)
            ?.toString() ??
        '';
  }

  @override
  void confirmed() => Navigator.pop(context, true);
  @override
  void dispose() {
    amount.dispose();
    super.dispose();
  }

  Future<void> save() async {
    final text = amount.text.trim();
    final value = text.isEmpty ? null : num.tryParse(text);
    if (text.isNotEmpty &&
        (value == null ||
            value <= 0 ||
            value > 1000000000 ||
            !storedAmountMatches(text, value))) {
      setState(
        () => error = 'أدخل هدفًا موجبًا بدقتين عشريتين أو اتركه فارغًا.',
      );
      return;
    }
    await saveSettings(
      OwnerSettingsWrite(
        path: '/api/v1/manage/configuration/collection-target',
        body: {
          'amount': value,
          'expectedRevision': active!['revision'],
          if (widget.from != null) 'from': widget.from,
          if (widget.to != null) 'to': widget.to,
          if (widget.branchId?.isNotEmpty == true) 'branchId': widget.branchId,
        },
        matches: (saved) {
          if (widget.from == null) {
            final actual = obj(saved['settings'])['monthlyCollectionTarget'];
            return value == null
                ? actual == null
                : storedAmountMatches(actual, value);
          }
          final matches = targets(saved);
          return value == null
              ? matches.isEmpty
              : matches.length == 1 &&
                    storedAmountMatches(matches.single['amount'], value);
        },
        failure: 'لم يتطابق هدف الفترة المقروء مع الفرع والفترة والقيمة المرسلة. أعد التحقق.',
        success: 'تم حفظ هدف التحصيل.',
      ),
    );
  }

  @override
  Widget build(
    BuildContext context,
  ) => page(widget.from == null ? 'هدف التحصيل الشهري' : 'ضبط هدف التحصيل', [
    if (active != null) ...[
      UiText(
        widget.from == null
            ? 'هدف لشهر الأكاديمية الحالي ولكل الفروع.'
            : '${widget.branchName ?? 'كل الفروع'} · ${widget.from} — ${widget.to}',
      ),
      const SizedBox(height: 20),
      field(
        'الهدف (${academyCurrency ?? '—'})',
        amount,
        required: false,
        keyboard: const TextInputType.numberWithOptions(decimal: true),
        readOnly: locked,
      ),
      FilledButton(
        onPressed: locked ? null : save,
        child: UiText(
          widget.from == null ? 'حفظ الهدف وعرض الشهر الحالي' : 'حفظ الهدف',
        ),
      ),
    ],
    ...recovery,
  ]);
}
