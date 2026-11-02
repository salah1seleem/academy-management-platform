import 'dart:convert';

import 'package:flutter/material.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/theme/appearance.dart';
import 'guardian_ui.dart';

class GuardianPlayerCard extends StatelessWidget {
  final AuthController auth;
  final String playerId, name;
  const GuardianPlayerCard({
    super.key,
    required this.auth,
    required this.playerId,
    required this.name,
  });
  @override
  Widget build(BuildContext context) => GuardianPage(
    title: localized(context, 'بطاقة اللاعب', 'Player card'),
    child: RemoteBody(
      auth: auth,
      path: '/api/v1/guardian/children/$playerId/card',
      builder: (context, value) =>
          gList([PlayerCardView(data: obj(value), name: name)]),
    ),
  );
}

class PlayerCardView extends StatelessWidget {
  final Json data;
  final String name;
  const PlayerCardView({super.key, required this.data, required this.name});
  @override
  Widget build(BuildContext context) {
    final encoded = data['pngBase64'];
    Widget? qr;
    if (data['active'] == true && encoded is String) {
      try {
        qr = Image.memory(
          base64Decode(encoded),
          width: 256,
          height: 256,
          fit: BoxFit.contain,
          semanticLabel: localized(
            context,
            'QR بطاقة اللاعب',
            'Player card QR',
          ),
          errorBuilder: (_, _, _) => Text(
            localized(
              context,
              'تعذر عرض البطاقة؛ استخدم كود اللاعب يدويًا.',
              'Card unavailable; use manual player lookup.',
            ),
          ),
        );
      } on FormatException {
        qr = Text(
          localized(
            context,
            'تعذر عرض البطاقة؛ استخدم كود اللاعب يدويًا.',
            'Card unavailable; use manual player lookup.',
          ),
        );
      }
    }
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Container(
          padding: const EdgeInsets.all(20),
          decoration: BoxDecoration(
            color: Colors.white,
            borderRadius: BorderRadius.circular(24),
          ),
          child: Column(
            children: [
              const Icon(Icons.qr_code_2, color: Color(0xFF172238)),
              const SizedBox(height: 12),
              Text(
                name,
                style: const TextStyle(
                  fontWeight: FontWeight.bold,
                  color: Color(0xFF172238),
                ),
              ),
              const SizedBox(height: 16),
              if (qr != null)
                qr
              else
                Text(
                  localized(
                    context,
                    'لا توجد بطاقة فعالة. اطلبها من إدارة الأكاديمية.',
                    'No active card. Request one from academy staff.',
                  ),
                  style: const TextStyle(color: Color(0xFF172238)),
                ),
              if (data['active'] == true && data['expiresAtUtc'] != null)
                Padding(
                  padding: const EdgeInsets.only(top: 12),
                  child: Text(
                    '${localized(context, 'صالحة حتى', 'Valid until')} ${day(data['expiresAtUtc'])}',
                    style: const TextStyle(color: Color(0xFF172238)),
                  ),
                ),
            ],
          ),
        ),
        const SizedBox(height: 16),
        Text(
          localized(
            context,
            'اعرض البطاقة لموظف الحضور. البطاقة ليست إثبات هوية أو دفع؛ البحث اليدوي متاح دائمًا.',
            'Show this card to authorized attendance staff. It is not identity or payment proof; manual lookup remains available.',
          ),
        ),
      ],
    );
  }
}
