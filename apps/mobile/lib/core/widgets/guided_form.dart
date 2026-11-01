import 'package:flutter/material.dart';

/// Keeps the existing validators, then reveals the first invalid field.
/// It never submits, changes a value, or retries a command.
class GuidedForm extends Form {
  const GuidedForm({
    super.key,
    required super.child,
    super.autovalidateMode,
    super.onChanged,
    super.canPop,
    super.onPopInvokedWithResult,
  });

  @override
  FormState createState() => _GuidedFormState();
}

class _GuidedFormState extends FormState {
  int validationRevision = 0;

  @override
  bool validate() {
    final invalid = super.validateGranularly();
    final revision = ++validationRevision;
    if (invalid.isNotEmpty) {
      final first = invalid.first;
      WidgetsBinding.instance.addPostFrameCallback((_) {
        if (!mounted || !first.mounted || validationRevision != revision) {
          return;
        }
        Scrollable.ensureVisible(
          first.context,
          alignment: .15,
          duration: MediaQuery.disableAnimationsOf(context)
              ? Duration.zero
              : const Duration(milliseconds: 180),
          curve: Curves.easeOut,
        );
      });
    }
    return invalid.isEmpty;
  }
}
