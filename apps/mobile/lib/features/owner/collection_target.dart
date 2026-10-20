import 'package:flutter/material.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/theme/ui_text.dart';
import '../guardian/guardian_ui.dart';

class CollectionTargetPage extends StatefulWidget {
  const CollectionTargetPage({super.key, required this.auth});
  final AuthController auth;
  @override
  State<CollectionTargetPage> createState() => _CollectionTargetPageState();
}

class _CollectionTargetPageState extends State<CollectionTargetPage> {
  final amount = TextEditingController();
  bool initialized = false, busy = false;
  String? error;
  @override
  void dispose() {
    amount.dispose();
    super.dispose();
  }

  Future<void> save(Json active) async {
    final value = amount.text.trim().isEmpty ? null : num.tryParse(amount.text);
    if (amount.text.trim().isNotEmpty && (value == null || value <= 0)) {
      setState(() => error = 'أدخل هدف تحصيل صحيحًا.');
      return;
    }
    setState(() {
      busy = true;
      error = null;
    });
    try {
      await widget.auth.request(
        'PUT',
        '/api/v1/manage/configuration/collection-target',
        body: {'amount': value, 'expectedRevision': active['revision']},
      );
      if (mounted) Navigator.pop(context, true);
    } catch (_) {
      if (mounted) {
        setState(() => error = 'تعذر حفظ الهدف؛ حدّث الإعدادات وحاول مجددًا.');
      }
    } finally {
      if (mounted) setState(() => busy = false);
    }
  }

  @override
  Widget build(BuildContext context) => GuardianPage(
    title: 'هدف التحصيل الشهري',
    child: RemoteBody(
      auth: widget.auth,
      path: '/api/v1/manage/configuration',
      builder: (context, value) {
        final data = obj(value),
            active = rows(data['versions'])
                .where((v) => v['state'] == 'Active')
                .firstOrNull;
        if (active == null) {
          return const EmptyMessage('فعّل إعدادات الأكاديمية أولًا.');
        }
        if (!initialized) {
          amount.text =
              active['settings']?['monthlyCollectionTarget']?.toString() ?? '';
          initialized = true;
        }
        return gList([
          const UiText(
            'هدف لشهر الأكاديمية الحالي ولكل الفروع. يظهر مع نطاق الشهر الحالي فقط، وليس وعدًا بالتحصيل.',
          ),
          const SizedBox(height: 20),
          TextField(
            controller: amount,
            keyboardType: const TextInputType.numberWithOptions(decimal: true),
            decoration: InputDecoration(
              labelText: 'الهدف (${data['academyCurrency']})',
            ),
            enabled: !busy,
          ),
          const SizedBox(height: 16),
          if (error != null) UiText(error!),
          FilledButton(
            onPressed: busy ? null : () => save(active),
            child: const UiText('حفظ الهدف وعرض الشهر الحالي'),
          ),
        ]);
      },
    ),
  );
}
