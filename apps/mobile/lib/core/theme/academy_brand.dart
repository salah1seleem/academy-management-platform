import 'dart:typed_data';
import 'dart:async';

import 'package:flutter/material.dart';

import '../auth/auth_controller.dart';
import 'logo_palette.dart';

class AcademyBrand extends ChangeNotifier {
  final AuthController auth;
  String name = 'iAcademy One';
  Color primary = const Color(0xFF087E9D), secondary = const Color(0xFF0A1F44);
  Uint8List? logo;
  LogoArtwork? artwork;
  String? profileName, profilePhoto;
  Object? profileGender;
  Future<void> loadProfile() async {
    final epoch = _epoch;
    if (auth.session?.selected == null) return;
    try {
      final value = await auth.request('GET', '/api/v1/me/profile');
      if (epoch != _epoch || value is! Map<String, dynamic>) return;
      profileName = value['displayName'] as String?;
      profilePhoto = value['photoReference'] as String?;
      profileGender = value['gender'];
      notifyListeners();
    } catch (_) {
      // Optional portrait never blocks Home or changes the authenticated name.
    }
  }

  Color? logoColor;
  Color logoBackground = Colors.white;
  Color get headerColor => headerTone(logoColor ?? primary);
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
    artwork = null;
    profileName = null;
    profilePhoto = null;
    profileGender = null;
    logoColor = null;
    logoBackground = Colors.white;
    name = signedIn
        ? auth.session!.selected?.academyName ?? 'الأكاديمية'
        : 'iAcademy One';
    primary = const Color(0xFF087E9D);
    secondary = const Color(0xFF0A1F44);
    coachAttendance = false;
    sportTheme = 'general';
    features = [];
    loading = signedIn;
    notifyListeners();
    if (!signedIn) return;
    unawaited(loadProfile());
    try {
      final data =
          await auth
                  .request('GET', '/api/v1/academy/experience')
                  .timeout(const Duration(seconds: 8))
              as Map<String, dynamic>;
      if (epoch != _epoch) return;
      name = data['displayName'] as String? ?? 'الأكاديمية';
      primary = color(data['primaryColor'], primary);
      secondary = color(data['secondaryColor'], secondary);
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
      // Optional artwork cannot hold onboarding/Home or discard valid metadata.
      if (data['logoUrl'] == '/api/v1/academy/branding/logo') {
        try {
          final bytes = await auth
              .asset('/api/v1/academy/branding/logo')
              .timeout(const Duration(seconds: 5));
          final inspected = await inspectLogoArtwork(bytes);
          if (epoch != _epoch) return;
          logo = bytes;
          artwork = inspected;
          logoColor = inspected?.colors.firstOrNull;
          logoBackground = inspected?.surface ?? Colors.white;
          notifyListeners();
        } catch (_) {
          /* Neutral fallback; never reuse another context's logo. */
        }
      }
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
