import 'package:flutter/material.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/theme/ui_text.dart';
import '../admin/native_operation_form.dart';
import '../guardian/guardian_ui.dart';
import 'private_media.dart';

/// Select an authorized group without opening academy/staff management data.
class NativeMediaGroups extends StatefulWidget {
  const NativeMediaGroups({super.key, required this.auth});
  final AuthController auth;
  @override
  State<NativeMediaGroups> createState() => _NativeMediaGroupsState();
}

class _NativeMediaGroupsState extends NativeOperationState<NativeMediaGroups> {
  @override
  AuthController get auth => widget.auth;
  final search = TextEditingController();
  String applied = '';
  int index = 1;
  Json? data;
  @override
  void initState() {
    super.initState();
    autoPage(
      hasMore: () =>
          data?['hasMore'] == true ||
          rows(data?['items'] ?? []).length < (data?['total'] as num? ?? 0),
      next: () => load(more: true),
    );
    load();
  }

  @override
  void dispose() {
    search.dispose();
    super.dispose();
  }

  Future<void> load({bool more = false}) => run(() async {
    final target = more ? index + 1 : 1;
    final query = Uri(
      queryParameters: {'search': applied, 'page': '$target', 'pageSize': '25'},
    ).query;
    final result = obj(
      await auth.request('GET', '/api/v1/assets/media-groups?$query'),
    );
    if (current) {
      setState(() {
        data = {
          ...result,
          'items': [
            if (more) ...rows(data?['items'] ?? []),
            ...rows(result['items']),
          ],
        };
        index = target;
      });
    }
  });

  void changePage(int next) {
    setState(() {
      index = next;
      data = null;
    });
    load();
  }

  @override
  Widget build(BuildContext context) => page(
    'رفع صور وفيديو',
    [
      TextField(
        controller: search,
        maxLength: 100,
        decoration: const InputDecoration(
          labelText: 'بحث باسم المجموعة',
          counterText: '',
        ),
        onSubmitted: (_) {
          applied = search.text.trim();
          changePage(1);
        },
      ),
      OutlinedButton(
        onPressed: () {
          applied = search.text.trim();
          changePage(1);
        },
        child: const UiText('بحث'),
      ),
      if (data != null) ...[
        if (rows(data!['items']).isEmpty)
          const EmptyMessage('لا توجد مجموعات متاحة ضمن هذا البحث وصلاحياتك.'),
        for (final group in rows(data!['items']))
          GCard(
            onTap: () => openPage(
              context,
              UploadSessionMedia(auth: auth, groupId: textOf(group['id'])),
            ),
            child: ListTile(
              title: UiText(textOf(group['arabicName'])),
              subtitle: UiText(
                '${group['branchName']} · ${group['sportName']}',
              ),
              trailing: const Icon(Icons.chevron_right),
            ),
          ),
        const SizedBox.shrink(),
      ],
      if (error != null)
        OutlinedButton(onPressed: load, child: const UiText('إعادة المحاولة')),
    ],
    screen: 'media',
    onRetry: load,
  );
}
