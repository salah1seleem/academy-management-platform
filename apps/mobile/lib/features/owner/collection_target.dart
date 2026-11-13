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
    this.usesMonthlyDefault = false,
  });
  final AuthController auth;
  final String? from, to, branchId, branchName;
  final bool usesMonthlyDefault;
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
    amount.text = storedTarget(value)?.toString() ?? '';
  }

  @override
  void confirmed() => Navigator.pop(context, true);
  @override
  void dispose() {
    amount.dispose();
    super.dispose();
  }

  Object? storedTarget(Json value) {
    if (widget.from == null) {
      return obj(value['settings'])['monthlyCollectionTarget'];
    }
    final matches = targets(value);
    return matches.length == 1 ? matches.single['amount'] : null;
  }

  Future<void> saveAmount() async {
    final text = amount.text.trim();
    final value = num.tryParse(text);
    if (value == null ||
        value <= 0 ||
        value > 1000000000 ||
        !storedAmountMatches(text, value)) {
      setState(
        () =>
            error = 'أدخل مبلغًا موجبًا حتى مليار، بمنزلتين عشريتين كحد أقصى.',
      );
      return;
    }
    await save(value);
  }

  Future<void> removeTarget(Object stored, bool hasMonthlyDefault) async {
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: const UiText('إزالة الهدف؟'),
        content: UiText(
          'سيُزال الهدف البالغ ${money(stored, academyCurrency)}.'
          '${hasMonthlyDefault ? ' سيعود عرض الهدف الشهري الافتراضي.' : ''}',
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(context, false),
            child: const UiText('إلغاء'),
          ),
          FilledButton(
            onPressed: () => Navigator.pop(context, true),
            child: const UiText('تأكيد إزالة الهدف'),
          ),
        ],
      ),
    );
    if (current && confirmed == true) await save(null);
  }

  Future<void> save(num? value) async {
    final revision = active?['revision'];
    if (revision == null || locked) return;
    await saveSettings(
      OwnerSettingsWrite(
        path: '/api/v1/manage/configuration/collection-target',
        body: {
          'amount': value,
          'expectedRevision': revision,
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
        failure: 'لم يتطابق هدف الفترة المقروء مع التغيير المطلوب.',
        success: value == null ? 'تمت إزالة الهدف.' : 'تم حفظ هدف التحصيل.',
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    final settings = active == null ? null : obj(active!['settings']);
    final stored = active == null ? null : storedTarget(active!);
    final monthly = widget.usesMonthlyDefault && settings != null
        ? settings['monthlyCollectionTarget']
        : null;
    final format = MaterialLocalizations.of(context).formatShortDate;
    final from = DateTime.tryParse(widget.from ?? '');
    final to = DateTime.tryParse(widget.to ?? '');
    final range = from == null || to == null
        ? 'الفترة المحددة'
        : from == to
        ? format(to)
        : 'من ${format(from)} إلى ${format(to)}';
    return page(widget.from == null ? 'هدف التحصيل الشهري' : 'ضبط هدف التحصيل', [
      if (active != null) ...[
        UiText(
          widget.from == null
              ? 'هدف لشهر الأكاديمية الحالي ولكل الفروع.'
              : '${widget.branchName ?? 'كل الفروع'} · $range',
        ),
        const SizedBox(height: 16),
        field(
          'الهدف (${academyCurrency == 'EGP' ? 'ج.م' : academyCurrency ?? '—'})',
          amount,
          required: false,
          keyboard: const TextInputType.numberWithOptions(decimal: true),
          readOnly: locked,
          changed: () => setState(() => error = null),
        ),
        if (monthly != null)
          UiText('الهدف الشهري الافتراضي ${money(monthly, academyCurrency)}'),
        if (pending == null && !settingsChanged)
          FilledButton(
            onPressed: locked || amount.text.trim().isEmpty ? null : saveAmount,
            child: UiText(
              widget.from == null ? 'حفظ الهدف وعرض الشهر الحالي' : 'حفظ الهدف',
            ),
          ),
        if (!locked && stored != null)
          OutlinedButton(
            onPressed: () => removeTarget(stored, monthly != null),
            child: UiText(
              widget.from == null ? 'إزالة الهدف الشهري' : 'إزالة هدف الفترة',
            ),
          ),
      ],
      ...recovery,
    ], onRetry: () => loadSettings());
  }
}
