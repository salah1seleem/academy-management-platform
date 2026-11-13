import '../../core/widgets/arabic_number_input.dart';
import '../../core/widgets/guided_form.dart';
import '../../core/widgets/page_status.dart';
import '../shared/operation_design.dart';

import 'package:flutter/material.dart';

import '../../core/api/api_client.dart';
import '../../core/auth/auth_controller.dart';
import '../../core/theme/ui_text.dart';
import '../guardian/guardian_ui.dart';
import '../shared/page_video.dart';
import '../shared/experience_ui.dart';

/// Read-back equality only. Prices and discounts are calculated by the server.
/// Keep decimal strings exact; never round an unrepresentable value into a match.
bool storedAmountMatches(Object? left, Object? right) {
  BigInt? minor(Object? value) {
    final match = RegExp(r'^([0-9]+)(?:\.([0-9]{1,2}))?$').firstMatch('$value');
    return match == null
        ? null
        : BigInt.parse('${match[1]}${(match[2] ?? '').padRight(2, '0')}');
  }

  final a = minor(left), b = minor(right);
  return a != null && b != null && a == b;
}

/// A form belongs to the authorization context that opened it, including drafts.
abstract class NativeOperationState<T extends StatefulWidget> extends State<T> {
  AuthController get auth;
  String? get currentContext => auth.session?.selected == null
      ? null
      : '${auth.session!.selected!.id}:${auth.session!.selected!.contextRevision}';
  late final String? openedContext = currentContext;
  bool get current =>
      mounted && openedContext != null && currentContext == openedContext;
  bool busy = false, accessDenied = false;
  bool blockInteraction = true;
  String? error, message;
  final form = GlobalKey<FormState>();
  final operationScroll = ScrollController();
  @override
  void initState() {
    super.initState();
    openedContext;
    auth.addListener(changedContext);
  }

  void changedContext() {
    if (mounted) setState(() {});
  }

  @override
  void dispose() {
    auth.removeListener(changedContext);
    operationScroll.dispose();
    super.dispose();
  }

  Future<void> run(
    Future<void> Function() action, {
    bool blocking = true,
  }) async {
    if (!current || busy) return;
    if (blocking) FocusManager.instance.primaryFocus?.unfocus();
    setState(() {
      busy = true;
      blockInteraction = blocking;
      error = null;
      message = null;
    });
    try {
      await action();
      accessDenied = false;
    } catch (e) {
      if (e is ApiFailure && [401, 403].contains(e.status)) accessDenied = true;
      if (current) {
        setState(
          () => error = e is ApiFailure
              ? e.message
              : 'تعذر حفظ العملية. راجع الاتصال وحاول مجددًا.',
        );
      }
    } finally {
      if (current) {
        setState(() => busy = false);
        if (blocking && (error != null || message != null)) {
          WidgetsBinding.instance.addPostFrameCallback((_) {
            if (mounted && operationScroll.hasClients) {
              operationScroll.animateTo(
                0,
                duration: const Duration(milliseconds: 200),
                curve: Curves.easeOut,
              );
            }
          });
        }
      }
    }
  }

  VoidCallback? readNextPage;
  void autoPage({
    required bool Function() hasMore,
    required Future<void> Function() next,
  }) {
    readNextPage = () {
      if (current &&
          !busy &&
          error == null &&
          operationScroll.hasClients &&
          operationScroll.position.extentAfter < 240 &&
          hasMore()) {
        next();
      }
    };
    operationScroll.addListener(readNextPage!);
  }

  Widget page(
    String title,
    List<Widget> children, {
    String screen = 'operations',
    ScrollController? scrollController,
    Widget? bottom,
    VoidCallback? onRetry,
    VoidCallback? onBack,
  }) => !current
      ? const SizedBox.shrink()
      : accessDenied
      ? GuardianPage(
          title: title,
          onBack: onBack,
          child: PageStatus(
            child: StatusPanel(
              title: 'لا يمكن عرض هذه الصفحة',
              message: error ?? 'ليست لديك صلاحية هذا الإجراء.',
              icon: Icons.lock_outline_rounded,
              urgent: true,
              onAction: busy ? null : onRetry,
              actionLabel: 'إعادة المحاولة',
            ),
          ),
        )
      : GuardianPage(
          title: title,
          onBack: onBack,
          bottom: bottom == null
              ? null
              : Theme(
                  data: operationTheme(context),
                  child: AbsorbPointer(
                    absorbing: busy && blockInteraction,
                    child: bottom,
                  ),
                ),
          actions: [PageVideoButton(auth: auth, screen: screen)],
          child: Theme(
            data: operationTheme(context),
            child: GuidedForm(
              key: form,
              child: AbsorbPointer(
                absorbing: busy && blockInteraction,
                child: gList(
                  [
                    // Stable slots keep FormFieldState alive when feedback is
                    // inserted. Otherwise index shifts erase validation errors
                    // before the first invalid field can be revealed.
                    busy
                        ? const LinearProgressIndicator()
                        : const SizedBox.shrink(),
                    error != null
                        ? GCard(
                            child: UiText(
                              error!,
                              style: TextStyle(
                                color: Theme.of(context).colorScheme.error,
                              ),
                            ),
                          )
                        : const SizedBox.shrink(),
                    message != null
                        ? GCard(child: UiText(message!))
                        : const SizedBox.shrink(),
                    ...children,
                  ],
                  controller: scrollController ?? operationScroll,
                  onNearEnd: readNextPage,
                ),
              ),
            ),
          ),
        );
  Widget field(
    String label,
    TextEditingController controller, {
    Key? fieldKey,
    FocusNode? focusNode,
    bool required = true,
    bool? markRequired,
    TextInputType? keyboard,
    int? maxLines,
    bool readOnly = false,
    VoidCallback? changed,
    String? Function(String value)? validate,
  }) => Padding(
    key: fieldKey,
    padding: const EdgeInsets.only(bottom: 14),
    child: TextFormField(
      focusNode: focusNode,
      controller: controller,
      readOnly: readOnly,
      textInputAction: (maxLines ?? 1) > 1
          ? TextInputAction.newline
          : TextInputAction.next,
      onTapOutside: (_) => FocusManager.instance.primaryFocus?.unfocus(),
      onChanged: (_) => changed?.call(),
      keyboardType: keyboard,
      inputFormatters: [
        if (keyboard?.index == TextInputType.number.index ||
            keyboard == TextInputType.phone)
          const ArabicNumberInputFormatter(),
      ],
      maxLines: maxLines ?? 1,
      decoration: InputDecoration(
        labelText: (markRequired ?? (required && !readOnly)) ? null : label,
        label: (markRequired ?? (required && !readOnly))
            ? RequiredFieldLabel(label)
            : null,
      ),
      validator: (v) => required && (v == null || v.trim().isEmpty)
          ? 'أدخل $label'
          : validate?.call(v?.trim() ?? ''),
    ),
  );
  Widget dateField(
    String label,
    TextEditingController controller, {
    VoidCallback? changed,
    DateTime? firstDate,
    DateTime? lastDate,
  }) => Padding(
    padding: const EdgeInsets.only(bottom: 14),
    child: TextFormField(
      controller: controller,
      readOnly: true,
      decoration: InputDecoration(
        label: RequiredFieldLabel(label),
        suffixIcon: const Icon(Icons.calendar_today_outlined),
      ),
      validator: (v) =>
          DateTime.tryParse(v ?? '') == null ? 'اختر $label' : null,
      onTap: () async {
        final first = firstDate ?? DateTime(1900);
        final last = lastDate ?? DateTime(2100);
        final initial = DateTime.tryParse(controller.text) ?? DateTime.now();
        final selected = await showDatePicker(
          context: context,
          initialDate: initial.isBefore(first)
              ? first
              : initial.isAfter(last)
              ? last
              : initial,
          firstDate: first,
          lastDate: last,
        );
        if (selected != null && current) {
          setState(() {
            controller.text =
                '${selected.year.toString().padLeft(4, '0')}-${selected.month.toString().padLeft(2, '0')}-${selected.day.toString().padLeft(2, '0')}';
            changed?.call();
          });
        }
      },
    ),
  );
  Widget choice(
    String label,
    String? value,
    List<Json> items,
    ValueChanged<String?>? change, {
    String name = 'arabicName',
    bool required = true,
    bool? markRequired,
  }) => Padding(
    padding: const EdgeInsets.only(bottom: 14),
    child: DropdownButtonFormField<String>(
      key: ValueKey('$label:$value:${items.map((e) => e['id']).join()}'),
      initialValue: items.any((r) => r['id'] == value) ? value : null,
      isExpanded: true,
      decoration: InputDecoration(
        labelText: (markRequired ?? required) ? null : label,
        label: (markRequired ?? required) ? RequiredFieldLabel(label) : null,
      ),
      items: [
        for (final i in items)
          DropdownMenuItem(
            value: textOf(i['id']),
            child: UiText(textOf(i[name]), maxLines: 2),
          ),
      ],
      onChanged: change,
      validator: (v) =>
          required && (v == null || v.isEmpty) ? 'اختر $label' : null,
    ),
  );
}

class OperationPriceSummary extends StatelessWidget {
  const OperationPriceSummary({super.key, required this.quote});
  final Json quote;
  @override
  Widget build(BuildContext context) => Column(
    children: [
      for (final row in [
        ('السعر الأصلي', 'originalAmount'),
        ('الخصم', 'discountAmount'),
        ('المبلغ النهائي', 'finalAmount'),
      ])
        Padding(
          padding: const EdgeInsets.symmetric(vertical: 8),
          child: Row(
            children: [
              Expanded(
                child: UiText(row.$1, style: const TextStyle(fontSize: 12)),
              ),
              MoneyText(
                quote[row.$2],
                quote['currency'],
                size: row.$2 == 'finalAmount' ? 24 : 18,
                color: row.$2 == 'finalAmount'
                    ? ExperienceColors.positive(context)
                    : null,
              ),
            ],
          ),
        ),
    ],
  );
}

class RequiredFieldLabel extends StatelessWidget {
  const RequiredFieldLabel(this.label, {super.key});
  final String label;
  @override
  Widget build(BuildContext context) => Semantics(
    label: '$label، مطلوب',
    excludeSemantics: true,
    child: Row(
      mainAxisSize: MainAxisSize.min,
      children: [
        Flexible(child: UiText(label)),
        Text(
          ' *',
          style: TextStyle(
            color: Theme.of(context).colorScheme.error,
            fontWeight: FontWeight.w800,
          ),
        ),
      ],
    ),
  );
}
