import 'package:flutter/material.dart';

import '../../core/auth/auth_controller.dart';
import '../guardian/guardian_ui.dart';
import '../../core/theme/appearance.dart';

class MediaStorageNotice extends StatelessWidget {
  final AuthController auth;
  const MediaStorageNotice({super.key, required this.auth});
  @override
  Widget build(BuildContext context) => RemoteBody(
    auth: auth,
    path: '/api/v1/assets/storage-policy',
    builder: (context, value) {
      if (value is! Map || value['notice'] is! String) {
        return const SizedBox.shrink();
      }
      final data = Map<String, dynamic>.from(value);
      String mb(Object? n) => ((n as num) / 1048576).toStringAsFixed(1);
      return GCard(
        child: ExpansionTile(
          title: Text(
            localized(
              context,
              'الاحتفاظ وحدود الوسائط',
              'Media retention and limits',
            ),
          ),
          children: [
            Text(textOf(data['notice'])),
            if (data['storedBytes'] is num)
              Text(
                localized(
                  context,
                  'تخزين الأكاديمية: ${mb(data['storedBytes'])} / ${mb(data['storageLimitBytes'])} ميجابايت',
                  'Academy storage: ${mb(data['storedBytes'])} / ${mb(data['storageLimitBytes'])} MB',
                ),
              ),
            if (data['workerEnabled'] != true)
              Text(
                localized(
                  context,
                  'عامل التنظيف الآلي غير مشغّل؛ لا نفترض إزالة الملفات المنتهية.',
                  'Automatic cleanup is not running; expired files are not assumed removed.',
                ),
              ),
          ],
        ),
      );
    },
  );
}
