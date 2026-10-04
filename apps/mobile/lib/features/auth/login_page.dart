import 'package:flutter/material.dart';

import '../../core/api/api_client.dart';
import '../../core/auth/auth_controller.dart';
import '../../core/widgets/app_widgets.dart';

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

  @override
  Widget build(BuildContext context) => AppScaffold(
    title: 'أكاديميتي',
    child: ListView(
      padding: const EdgeInsets.all(20),
      children: [
        const SizedBox(height: 24),
        const Icon(Icons.sports_soccer, size: 56),
        const SizedBox(height: 20),
        Text(
          'كل يوم تدريب.. خطوة للأمام',
          textAlign: TextAlign.center,
          style: Theme.of(context).textTheme.headlineSmall,
        ),
        const SizedBox(height: 12),
        const Text(
          'ادخل برقم هاتف حسابك لمتابعة رحلتك في الأكاديمية.',
          textAlign: TextAlign.center,
        ),
        const SizedBox(height: 24),
        AppCard(
          child: Form(
            key: form,
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                TextFormField(
                  controller: phone,
                  enabled: challenge == null && !busy,
                  keyboardType: TextInputType.phone,
                  textDirection: TextDirection.ltr,
                  autofillHints: const [AutofillHints.telephoneNumber],
                  decoration: const InputDecoration(labelText: 'رقم الهاتف'),
                  validator: (value) =>
                      value == null || value.trim().length < 10
                      ? 'أدخل رقم الهاتف المصري.'
                      : null,
                ),
                if (challenge != null) ...[
                  const SizedBox(height: 16),
                  TextFormField(
                    controller: otp,
                    enabled: !busy,
                    keyboardType: TextInputType.number,
                    textDirection: TextDirection.ltr,
                    maxLength: 6,
                    autofillHints: const [AutofillHints.oneTimeCode],
                    decoration: const InputDecoration(labelText: 'رمز التحقق'),
                    validator: (value) => value?.trim().length == 6
                        ? null
                        : 'أدخل رمز التحقق من ٦ أرقام.',
                  ),
                ],
                if (error ?? widget.auth.error case final String message)
                  ErrorNotice(message),
                const SizedBox(height: 16),
                PrimaryButton(
                  label: challenge == null
                      ? 'إرسال رمز التحقق'
                      : 'تأكيد الدخول',
                  busy: busy,
                  onPressed: submit,
                ),
                if (challenge != null)
                  TextButton(
                    onPressed: busy
                        ? null
                        : () => setState(() {
                            challenge = null;
                            otp.clear();
                            error = null;
                          }),
                    child: const Text('تغيير الرقم أو طلب رمز جديد'),
                  ),
              ],
            ),
          ),
        ),
        if (widget.environment == 'Demo')
          const Padding(
            padding: EdgeInsets.only(top: 16),
            child: Text(
              'بيئة عرض تجريبي فقط — لا تُرسل رسالة SMS حقيقية.',
              textAlign: TextAlign.center,
            ),
          ),
      ],
    ),
  );
}
