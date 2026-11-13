import 'package:flutter_svg/flutter_svg.dart';
import 'package:flutter/material.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/theme/ui_text.dart';
import '../guardian/guardian_ui.dart';
import 'owner_settings_save.dart';
import 'honor_portrait_card.dart';

class OwnerHomeSettingsPage extends StatefulWidget {
  const OwnerHomeSettingsPage({super.key, required this.auth});
  final AuthController auth;
  @override
  State<OwnerHomeSettingsPage> createState() => _OwnerHomeSettingsPageState();
}

class _OwnerHomeSettingsPageState
    extends VerifiedOwnerSettingsState<OwnerHomeSettingsPage> {
  @override
  AuthController get auth => widget.auth;
  final target = TextEditingController();
  bool top = true;
  Json portraits = {
    'coach': 'adult-neutral',
    'member': 'adult-neutral',
    'player': 'child-neutral',
  };
  @override
  void applySettings(Json value, {bool initial = false}) {
    final home = obj(obj(value['settings'])['ownerHome'] ?? <String, dynamic>{});
    top = true;
    final stored = obj(home['highlightPortraits'] ?? <String, dynamic>{});
    portraits = {
      for (final role in honorFallbackAssets.keys)
        role: honorFallbackAssets[role]!.contains(stored[role])
            ? stored[role]
            : honorFallbackAssets[role]!.first,
    };
    target.text = home['coachAssignmentTarget']?.toString() ?? '';
  }

  @override
  void dispose() {
    target.dispose();
    super.dispose();
  }

  Future<void> save() async {
    final text = target.text.trim();
    final value = text.isEmpty ? null : int.tryParse(text);
    if (text.isNotEmpty && (value == null || value < 1 || value > 100)) {
      setState(
        () => error =
            'أدخل هدفًا من مجموعة واحدة إلى 100 مجموعة، أو اتركه فارغًا.',
      );
      return;
    }
    final position = top;
    final selectedPortraits = Map<String, dynamic>.from(portraits);
    await saveSettings(
      OwnerSettingsWrite(
        path: '/api/v1/manage/configuration/owner-home',
        body: {
          'expectedRevision': active!['revision'],
          'quickActionsAtTop': position,
          'coachAssignmentTarget': value,
          'highlightPortraits': selectedPortraits,
        },
        matches: (saved) {
          final home = obj(obj(saved['settings'])['ownerHome'] ?? {});
          return (home['quickActionsAtTop'] == true) == position &&
              home['coachAssignmentTarget'] == value &&
              selectedPortraits.entries.every(
                (e) =>
                    obj(
                      home['highlightPortraits'] ?? <String, dynamic>{},
                    )[e.key] ==
                    e.value,
              );
        },
        failure:
            'لم تتطابق إعدادات الرئيسية المقروءة مع القيم المرسلة. أعد التحقق.',
        success: 'تم حفظ إعدادات الرئيسية.',
      ),
    );
  }

  @override
  Widget build(BuildContext context) => page('المظهر والرئيسية', [
    if (active != null)
      GCard(
        child: Column(
          children: [
            const UiText(
              'الصور الرمزية عند غياب الصورة المرفوعة',
              style: TextStyle(fontWeight: FontWeight.w700),
            ),
            const UiText(
              'رسومات بديلة مشتركة؛ الصورة المرفوعة لها الأولوية.',
              style: TextStyle(fontSize: 11),
            ),
            for (final entry in const [
              ('coach', 'المدرب'),
              ('member', 'العضو المميز'),
              ('player', 'اللاعب'),
            ])
              Padding(
                padding: const EdgeInsets.symmetric(vertical: 8),
                child: DropdownButtonFormField<String>(
                  key: ValueKey('honor-fallback-${entry.$1}'),
                  initialValue: portraits[entry.$1] as String,
                  decoration: InputDecoration(
                    labelText: 'الصورة البديلة · ${entry.$2}',
                  ),
                  items: [
                    for (
                      var i = 0;
                      i < honorFallbackAssets[entry.$1]!.length;
                      i++
                    )
                      DropdownMenuItem(
                        value: honorFallbackAssets[entry.$1]![i],
                        child: Row(
                          children: [
                            if (honorFallback(
                              entry.$1,
                              honorFallbackAssets[entry.$1]![i],
                            ).endsWith('.svg'))
                              SvgPicture.asset(
                                honorFallback(
                                  entry.$1,
                                  honorFallbackAssets[entry.$1]![i],
                                ),
                                width: 32,
                                height: 32,
                              )
                            else
                              Image.asset(
                                honorFallback(
                                  entry.$1,
                                  honorFallbackAssets[entry.$1]![i],
                                ),
                                width: 32,
                                height: 32,
                                fit: BoxFit.cover,
                              ),
                            const SizedBox(width: 10),
                            UiText(
                              i == 0
                                  ? 'رسم محايد'
                                  : i == 1
                                  ? 'رسم ولد / رجل'
                                  : i == 2
                                  ? 'رسم بنت / امرأة'
                                  : 'صورة عرض ${i - 2}',
                            ),
                          ],
                        ),
                      ),
                  ],
                  onChanged: locked
                      ? null
                      : (v) => setState(() => portraits[entry.$1] = v),
                ),
              ),
            field(
              'هدف المجموعات لكل مدرب — اختياري',
              target,
              required: false,
              keyboard: TextInputType.number,
              readOnly: locked,
            ),
            FilledButton(
              onPressed: locked ? null : save,
              child: const UiText('حفظ الإعدادات'),
            ),
          ],
        ),
      ),
    ...recovery,
  ]);
}
