import '../../core/widgets/arabic_number_input.dart';
import '../../core/widgets/guided_form.dart';

import 'package:academy_mobile/core/theme/ui_text.dart';
import 'package:flutter/material.dart';

import '../../core/api/api_client.dart';
import '../../core/auth/auth_controller.dart';
import '../../core/widgets/app_widgets.dart';
import 'approved_login_layout.dart';

enum _LoginAction { request, verify, resend }

class LoginPage extends StatefulWidget {
  final AuthController auth;
  final String environment;
  const LoginPage({super.key, required this.auth, required this.environment});
  @override
  State<LoginPage> createState() => _LoginPageState();
}

class _LoginPageState extends State<LoginPage> {
  static final _otpPattern = RegExp(r'^[0-9]{6}$');
  static final _phoneSeparators = RegExp(r'[\s()\-]');
  static final _phonePattern = RegExp(r'^(?:(?:\+|00)?200?|0)1[0-9]{9}$');

  final phone = TextEditingController(), otp = TextEditingController();
  final phoneFocus = FocusNode();
  final form = GlobalKey<FormState>();
  String? challenge, error, notice;
  _LoginAction? pending;
  bool get busy => pending != null;
  @override
  void dispose() {
    phone.dispose();
    otp.dispose();
    phoneFocus.dispose();
    super.dispose();
  }

  Future<void> submit({bool resend = false}) async {
    if (busy || (resend ? challenge == null : !form.currentState!.validate())) {
      return;
    }
    final requesting = challenge == null || resend;
    setState(() {
      pending = resend
          ? _LoginAction.resend
          : requesting
          ? _LoginAction.request
          : _LoginAction.verify;
      error = null;
      notice = null;
    });
    try {
      if (requesting) {
        final id = await widget.auth.requestOtp(phone.text.trim());
        if (mounted) {
          setState(() {
            challenge = id;
            if (resend) {
              otp.clear();
              notice = 'يمكنك إدخال الرمز الجديد.';
            }
          });
        }
      } else {
        await widget.auth.verify(
          phone.text.trim(),
          challenge!,
          otp.text.trim(),
        );
      }
    } on ApiFailure catch (failure) {
      if (mounted) {
        setState(() {
          error = failure.status != 401
              ? failure.message
              : requesting
              ? 'تعذر إرسال الرمز. راجع رقم الهاتف المسجل لدى الأكاديمية.'
              : 'الرمز غير صحيح أو انتهت صلاحيته. يمكنك طلب رمز جديد.';
        });
      }
    } catch (_) {
      if (mounted) {
        setState(
          () => error = 'تعذر إكمال الدخول أو حفظ الجلسة بأمان. أعد المحاولة.',
        );
      }
    } finally {
      if (mounted) setState(() => pending = null);
    }
  }

  void editPhone() {
    setState(() {
      challenge = null;
      otp.clear();
      error = null;
      notice = null;
    });
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (mounted && challenge == null && !busy) phoneFocus.requestFocus();
    });
  }

  String get maskedPhone {
    final value = phone.text.replaceAll(_phoneSeparators, '');
    if (value.length <= 6) return value;
    return '${value.substring(0, 3)} ••••• ${value.substring(value.length - 3)}';
  }

  String submitLabel(BuildContext context) {
    if (challenge != null) return ui(context, 'تأكيد الدخول');
    return Localizations.localeOf(context).languageCode == 'en'
        ? 'Continue'
        : 'متابعة';
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
        autovalidateMode: AutovalidateMode.onUserInteractionIfError,
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            if (challenge == null)
              TextFormField(
                controller: phone,
                focusNode: phoneFocus,
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
                  helperText: ui(
                    context,
                    'مصر (+20)',
                  ).replaceFirst('+20', '\u2066+20\u2069'),
                  errorMaxLines: 3,
                ),
                onChanged: (_) {
                  if (error != null) setState(() => error = null);
                },
                onFieldSubmitted: (_) => submit(),
                validator: (value) {
                  final number = (value ?? '').replaceAll(_phoneSeparators, '');
                  return _phonePattern.hasMatch(number)
                      ? null
                      : ui(context, 'أدخل رقم الهاتف المصري.');
                },
              )
            else ...[
              Stack(
                children: [
                  Semantics(
                    label: ui(context, 'رمز التحقق'),
                    child: TextFormField(
                      key: ValueKey(challenge),
                      controller: otp,
                      enabled: !busy,
                      autofocus: true,
                      keyboardType: TextInputType.number,
                      inputFormatters: const [ArabicNumberInputFormatter()],
                      textInputAction: TextInputAction.done,
                      textDirection: TextDirection.ltr,
                      maxLength: 6,
                      cursorColor: Colors.transparent,
                      cursorErrorColor: Colors.transparent,
                      autofillHints: const [AutofillHints.oneTimeCode],
                      style: const TextStyle(
                        fontSize: 19,
                        color: Colors.transparent,
                      ),
                      decoration: InputDecoration(
                        filled: false,
                        border: InputBorder.none,
                        enabledBorder: InputBorder.none,
                        focusedBorder: InputBorder.none,
                        errorBorder: InputBorder.none,
                        focusedErrorBorder: InputBorder.none,
                        contentPadding: const EdgeInsets.symmetric(
                          vertical: 20,
                        ),
                        counterText: '',
                        errorMaxLines: 3,
                      ),
                      onChanged: (_) => setState(() {
                        error = null;
                        notice = null;
                      }),
                      onFieldSubmitted: (_) => submit(),
                      validator: (value) => _otpPattern.hasMatch(value ?? '')
                          ? null
                          : ui(context, 'أدخل رمز التحقق من ٦ أرقام.'),
                    ),
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
            if (notice case final String message)
              Semantics(
                liveRegion: true,
                child: Padding(
                  padding: const EdgeInsets.only(top: 12),
                  child: UiText(message),
                ),
              ),
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
              child: busy && pending != _LoginAction.resend
                  ? SizedBox.square(
                      dimension: 22,
                      child: CircularProgressIndicator(
                        strokeWidth: 2,
                        semanticsLabel: ui(
                          context,
                          pending == _LoginAction.verify
                              ? 'جارٍ التحقق'
                              : 'جارٍ طلب الرمز',
                        ),
                      ),
                    )
                  : Row(
                      children: [
                        Expanded(
                          child: Text(
                            submitLabel(context),
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
              Wrap(
                alignment: WrapAlignment.center,
                spacing: 8,
                children: [
                  TextButton(
                    onPressed: busy ? null : () => submit(resend: true),
                    child: UiText(
                      pending == _LoginAction.resend
                          ? 'جارٍ طلب الرمز'
                          : 'طلب رمز جديد',
                    ),
                  ),
                  TextButton(
                    onPressed: busy ? null : editPhone,
                    child: const UiText('تعديل الرقم'),
                  ),
                ],
              ),
            ],
          ],
        ),
      ),
    ),
  );
}
