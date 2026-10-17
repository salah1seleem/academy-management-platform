import 'dart:typed_data';

import 'package:flutter/material.dart';

import '../auth/auth_controller.dart';

class AcademyBrand extends ChangeNotifier {
  final AuthController auth;
  String name = 'The Professional Academy';
  Color primary = const Color(0xFFD71935), secondary = const Color(0xFF171923);
  Uint8List? logo;
  bool signedIn = false, coachAttendance = false;
  bool loading = false;
  String sportTheme = 'general';
  List<String> features = [];
  String? _membership;
  int _epoch = 0;
  AcademyBrand(this.auth) {
    auth.addListener(refresh);
    refresh();
  }
  static Color color(Object? value, Color fallback) {
    if (!RegExp(r'^#[0-9a-fA-F]{6}$').hasMatch('$value')) return fallback;
    return Color(int.parse('FF${value.toString().substring(1)}', radix: 16));
  }

  Future<void> refresh({bool force = false}) async {
    final membership = auth.session?.membershipId == null
        ? null
        : '${auth.session!.membershipId}:${auth.session!.selected?.contextRevision ?? 0}';
    if (!force && _membership == membership) return;
    _membership = membership;
    final epoch = ++_epoch;
    signedIn = membership != null;
    logo = null;
    name = signedIn ? 'الأكاديمية' : 'The Professional Academy';
    primary = const Color(0xFFD71935);
    secondary = const Color(0xFF171923);
    coachAttendance = false;
    sportTheme = 'general';
    features = [];
    loading = signedIn;
    notifyListeners();
    if (!signedIn) return;
    try {
      final data = await auth.request(
        'GET',
        '/api/v1/academy/experience',
      ) as Map<String, dynamic>;
      Uint8List? bytes;
      if (data['logoUrl'] == '/api/v1/academy/branding/logo') {
        bytes = await auth.asset('/api/v1/academy/branding/logo');
      }
      if (epoch != _epoch) return;
      name = data['displayName'] as String? ?? 'الأكاديمية';
      primary = color(data['primaryColor'], primary);
      secondary = color(data['secondaryColor'], secondary);
      logo = bytes;
      coachAttendance = data['coachAttendanceEnabled'] == true;
      final intro = data['onboarding'];
      if (intro is Map) {
        sportTheme = ['football', 'swimming'].contains(intro['sportTheme'])
            ? intro['sportTheme'] as String
            : 'general';
        features = intro['features'] is List
            ? (intro['features'] as List).whereType<String>().toList()
            : [];
      }
      loading = false;
      notifyListeners();
    } catch (_) {
      /* Fail closed for attendance; other private screens remain usable. */
      if (epoch == _epoch) {
        loading = false;
        notifyListeners();
      }
    }
  }

  @override
  void dispose() {
    ++_epoch;
    auth.removeListener(refresh);
    super.dispose();
  }
}

class BrandScope extends InheritedNotifier<AcademyBrand> {
  const BrandScope({
    super.key,
    required AcademyBrand brand,
    required super.child,
  }) : super(notifier: brand);
  static AcademyBrand? maybeOf(BuildContext context) =>
      context.dependOnInheritedWidgetOfExactType<BrandScope>()?.notifier;
}
