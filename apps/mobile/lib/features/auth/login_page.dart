import '../../core/widgets/arabic_number_input.dart';
import '../../core/widgets/guided_form.dart';

import 'package:academy_mobile/core/theme/ui_text.dart';
import 'package:flutter/material.dart';

import '../../core/api/api_client.dart';
import '../../core/auth/auth_controller.dart';
import '../../core/widgets/app_widgets.dart';
import 'approved_login_layout.dart';

class LoginPage extends StatefulWidget {
  final AuthController auth;
  final String environment;
  const LoginPage({super.key, required this.auth, required this.environment});
  @override
  State<LoginPage> createState() => _LoginPageState();
}

class _LoginPageState extends State<LoginPage> {
  final phone = TextEditingController(), otp = TextEditingController();
  final form = GlobalKey<FormState>();
  String? challenge, error;
  bool busy = false;
  @override
  void dispose() {
    phone.dispose();
    otp.dispose();
    super.dispose();
  }

  Future<void> submit() async {
    if (busy || !form.currentState!.validate()) return;
    setState(() {
      busy = true;
      error = null;
    });
    try {
      if (challenge == null) {
        final id = await widget.auth.requestOtp(phone.text.trim());
        if (mounted) setState(() => challenge = id);
      } else {
        await widget.auth.verify(
          phone.text.trim(),
          challenge!,
          otp.text.trim(),
        );
      }
    } on ApiFailure catch (failure) {
      if (mounted) setState(() => error = failure.message);
    } catch (_) {
      if (mounted) {
        setState(
          () => error = 'تعذر إكمال الدخول أو حفظ الجلسة بأمان. أعد المحاولة.',
        );
      }
    } finally {
      if (mounted) setState(() => busy = false);
    }
  }

  String get maskedPhone {
    final value = phone.text.trim().replaceAll(RegExp(r'\s+'), '');
    if (value.length <= 6) return value;
    return '${value.substring(0, 3)} ••••• ${value.substring(value.length - 3)}';
  }

  Widget otpPreview(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final digits = otp.text.characters.toList();
    return ExcludeSemantics(
      child: Directionality(
        textDirection: TextDirection.ltr,
        child: Row(
          key: const ValueKey('login-six-code-boxes'),
          children: [
            for (var i = 0; i < 6; i++) ...[
              if (i > 0) const SizedBox(width: 8),
              Expanded(
                child: Container(
                  height: 62,
                  alignment: Alignment.center,
                  decoration: BoxDecoration(
                    color: scheme.surface,
                    borderRadius: BorderRadius.circular(12),
                    border: Border.all(
                      color: i == digits.length
                          ? scheme.primary
                          : scheme.outline,
                    ),
                  ),
                  child: Text(
                    i < digits.length ? digits[i] : '',
                    style: TextStyle(
                      color: scheme.onSurface,
                      fontSize: 26,
                      height: 1,
                      fontWeight: FontWeight.w600,
                    ),
                  ),
                ),
              ),
            ],
          ],
        ),
      ),
    );
  }

  @override
  Widget build(BuildContext context) => ApprovedLoginLayout(
    verifying: challenge != null,
    demo: widget.environment == 'Demo',
    form: AutofillGroup(
      child: GuidedForm(
        key: form,
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            if (challenge == null)
              TextFormField(
                controller: phone,
                enabled: !busy,
                keyboardType: TextInputType.phone,
                inputFormatters: const [ArabicNumberInputFormatter()],
                textInputAction: TextInputAction.done,
                textDirection: TextDirection.ltr,
                autofillHints: const [AutofillHints.telephoneNumber],
                style: const TextStyle(fontSize: 19),
                decoration: InputDecoration(
                  labelText: ui(context, 'رقم الهاتف'),
                  hintText: '01xxxxxxxxx',
                  errorMaxLines: 3,
                ),
                onFieldSubmitted: (_) => submit(),
                validator: (value) => value == null || value.trim().length < 10
                    ? ui(context, 'أدخل رقم الهاتف المصري.')
                    : null,
              )
            else ...[
              Stack(
                children: [
                  TextFormField(
                    controller: otp,
                    enabled: !busy,
                    autofocus: true,
                    keyboardType: TextInputType.number,
                    inputFormatters: const [ArabicNumberInputFormatter()],
                    textInputAction: TextInputAction.done,
                    textDirection: TextDirection.ltr,
                    maxLength: 6,
                    cursorColor: Colors.transparent,
                    autofillHints: const [AutofillHints.oneTimeCode],
                    style: const TextStyle(
                      fontSize: 19,
                      color: Colors.transparent,
                    ),
                    decoration: InputDecoration(
                      labelText: ui(context, 'رمز التحقق'),
                      labelStyle: const TextStyle(
                        color: Colors.transparent,
                        fontSize: 1,
                      ),
                      floatingLabelBehavior: FloatingLabelBehavior.never,
                      filled: false,
                      border: InputBorder.none,
                      enabledBorder: InputBorder.none,
                      focusedBorder: InputBorder.none,
                      contentPadding: const EdgeInsets.symmetric(vertical: 20),
                      counterText: '',
                      errorMaxLines: 3,
                    ),
                    onChanged: (_) => setState(() {}),
                    onFieldSubmitted: (_) => submit(),
                    validator: (value) => value?.trim().length == 6
                        ? null
                        : ui(context, 'أدخل رمز التحقق من ٦ أرقام.'),
                  ),
                  Positioned(
                    top: 0,
                    left: 0,
                    right: 0,
                    child: IgnorePointer(child: otpPreview(context)),
                  ),
                ],
              ),
              const SizedBox(height: 10),
              Text(
                maskedPhone,
                key: const ValueKey('login-masked-phone'),
                textDirection: TextDirection.ltr,
                style: TextStyle(
                  fontSize: 12,
                  color: Theme.of(context).colorScheme.onSurfaceVariant,
                ),
              ),
            ],
            if (error ?? widget.auth.error case final String message)
              ErrorNotice(message),
            const SizedBox(height: 20),
            FilledButton(
              key: const ValueKey('login-submit'),
              style: FilledButton.styleFrom(
                minimumSize: const Size.fromHeight(52),
                padding: const EdgeInsets.symmetric(
                  horizontal: 18,
                  vertical: 14,
                ),
              ),
              onPressed: busy ? null : submit,
              child: busy
                  ? const SizedBox.square(
                      dimension: 22,
                      child: CircularProgressIndicator(
                        strokeWidth: 2,
                        semanticsLabel: 'جارٍ التنفيذ',
                      ),
                    )
                  : Row(
                      children: [
                        Expanded(
                          child: UiText(
                            challenge == null ? 'متابعة' : 'تأكيد الدخول',
                            style: const TextStyle(
                              fontSize: 15,
                              fontWeight: FontWeight.w700,
                            ),
                          ),
                        ),
                        const SizedBox(width: 12),
                        Icon(
                          challenge == null
                              ? Icons.arrow_forward_rounded
                              : Icons.check_rounded,
                          size: 18,
                        ),
                      ],
                    ),
            ),
            if (challenge != null) ...[
              const SizedBox(height: 8),
              TextButton(
                onPressed: busy
                    ? null
                    : () => setState(() {
                        challenge = null;
                        otp.clear();
                        error = null;
                      }),
                child: const UiText('تغيير الرقم أو طلب رمز جديد'),
              ),
            ],
          ],
        ),
      ),
    ),
  );
}
