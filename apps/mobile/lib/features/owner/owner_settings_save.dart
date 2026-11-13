import 'package:flutter/material.dart';

import '../../core/api/api_client.dart';
import '../../core/theme/ui_text.dart';
import '../admin/native_operation_form.dart';
import '../guardian/guardian_ui.dart';

class OwnerSettingsWrite {
  OwnerSettingsWrite({
    required this.path,
    required Map<String, dynamic> body,
    required this.matches,
    required this.failure,
    required this.success,
  }) : body = Map.unmodifiable(body);
  final String path, failure, success;
  final Json body;
  final bool Function(Json active) matches;
}

/// Confirm configuration writes against the active version in the same session.
abstract class VerifiedOwnerSettingsState<T extends StatefulWidget>
    extends NativeOperationState<T> {
  Json? active;
  String? academyCurrency;
  OwnerSettingsWrite? pending;
  bool settingsChanged = false;
  bool get locked => busy || pending != null || settingsChanged;
  void applySettings(Json value, {bool initial = false});
  void confirmed() {}

  @override
  void initState() {
    super.initState();
    loadSettings(initial: true);
  }

  Future<Json> readSettings() async {
    try {
      final data = obj(
        await auth.request('GET', '/api/v1/manage/configuration'),
      );
      if (current) academyCurrency = data['academyCurrency']?.toString();
      final value = rows(data['versions'])
          .where((v) => v['state'] == 'Active')
          .firstOrNull;
      if (value == null ||
          value['revision'] is! num ||
          (value['revision'] as num) <= 0 ||
          value['settings'] is! Map) {
        throw const ApiFailure(0, 'إعدادات الأكاديمية الفعالة غير متاحة.');
      }
      return value;
    } on ApiFailure catch (e) {
      if (current && (e.status == 401 || e.status == 403)) {
        setState(() {
          active = null;
          pending = null;
        });
      }
      rethrow;
    }
  }

  Future<void> loadSettings({bool initial = false}) => run(() async {
    final value = await readSettings();
    if (!current) return;
    setState(() {
      active = value;
      pending = null;
      settingsChanged = false;
      applySettings(value, initial: initial);
    });
  });

  Future<void> verify(OwnerSettingsWrite change) async {
    final value = await readSettings();
    if (!current) return;
    if (!change.matches(value)) throw ApiFailure(0, change.failure);
    setState(() {
      active = value;
      pending = null;
      applySettings(value);
      message = change.success;
    });
    confirmed();
  }

  Future<void> saveSettings(OwnerSettingsWrite change) async {
    if (locked || active == null) return;
    await run(() async {
      setState(() => pending = change);
      try {
        await auth.request('PUT', change.path, body: change.body);
      } on ApiFailure catch (e) {
        if (current && e.status >= 400 && e.status < 500 && e.status != 408) {
          setState(() {
            pending = null;
            settingsChanged = e.status == 409;
          });
        }
        rethrow;
      }
      if (current) await verify(change);
    });
  }

  List<Widget> get recovery => [
    if (pending != null) ...[
      const UiText(
        'القيم المرسلة مقفلة حتى التحقق من الحفظ أو إعادة قراءة الوضع الحالي.',
      ),
      OutlinedButton(
        onPressed: busy ? null : () => run(() => verify(pending!)),
        child: const UiText('إعادة التحقق من الحفظ'),
      ),
    ],
    if (pending != null || error != null)
      TextButton(
        onPressed: busy ? null : () => loadSettings(),
        child: const UiText('إعادة تحميل الإعدادات الحالية'),
      ),
  ];
}
