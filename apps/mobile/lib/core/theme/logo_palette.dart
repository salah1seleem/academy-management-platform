import 'dart:typed_data';
import 'dart:ui' as ui;

import 'package:flutter/material.dart';

/// Display metadata for the one canonical upload. The source bytes stay intact.
class LogoArtwork {
  const LogoArtwork({
    required this.size,
    required this.bounds,
    required this.colors,
    required this.surface,
    this.transparent = false,
    this.ink,
  });
  final Size size;
  final Rect bounds;
  final List<Color> colors;
  final Color surface;
  final bool transparent;
  final Color? ink;
}

Future<LogoArtwork?> inspectLogoArtwork(Uint8List bytes) async {
  ui.Codec? codec;
  ui.Image? image;
  try {
    codec = await ui.instantiateImageCodec(bytes, targetWidth: 96);
    image = (await codec.getNextFrame()).image;
    final data = await image.toByteData(format: ui.ImageByteFormat.rawRgba);
    if (data == null) return null;
    final pixels = data.buffer.asUint8List(), w = image.width, h = image.height;
    final corners = [
      0,
      w - 1,
      (h - 1) * w,
      h * w - 1,
    ].map((p) => p * 4).toList();
    final transparent = corners.every((p) => pixels[p + 3] < 32);
    final background = Color.fromARGB(
      255,
      pixels[corners.first],
      pixels[corners.first + 1],
      pixels[corners.first + 2],
    );
    final uniform =
        !transparent &&
        corners.every(
          (p) =>
              pixels[p + 3] > 240 &&
              (pixels[p] - background.r * 255).abs() < 18 &&
              (pixels[p + 1] - background.g * 255).abs() < 18 &&
              (pixels[p + 2] - background.b * 255).abs() < 18,
        );
    var left = w, top = h, right = -1, bottom = -1;
    for (var y = 0; y < h; y++) {
      for (var x = 0; x < w; x++) {
        final i = (y * w + x) * 4;
        final visible =
            pixels[i + 3] > 48 &&
            (!uniform ||
                (pixels[i] - background.r * 255).abs() +
                        (pixels[i + 1] - background.g * 255).abs() +
                        (pixels[i + 2] - background.b * 255).abs() >
                    90);
        if (visible) {
          if (x < left) left = x;
          if (x > right) right = x;
          if (y < top) top = y;
          if (y > bottom) bottom = y;
        }
      }
    }
    // Trim only a uniform/transparent outer margin, with a one-pixel safety edge.
    final trimLight = uniform && background.computeLuminance() > .8;
    var darkR = 0, darkG = 0, darkB = 0, darkCount = 0;
    for (var i = 0; i + 3 < pixels.length; i += 4) {
      if (pixels[i + 3] < 180) continue;
      final c = HSVColor.fromColor(
        Color.fromARGB(255, pixels[i], pixels[i + 1], pixels[i + 2]),
      );
      if (c.value < .45 && c.saturation < .3) {
        darkR += pixels[i];
        darkG += pixels[i + 1];
        darkB += pixels[i + 2];
        darkCount++;
      }
    }
    final crop = (transparent || trimLight) && right >= left && bottom >= top
        ? Rect.fromLTRB(
            (left - 1).clamp(0, w).toDouble(),
            (top - 1).clamp(0, h).toDouble(),
            (right + 2).clamp(0, w).toDouble(),
            (bottom + 2).clamp(0, h).toDouble(),
          )
        : Rect.fromLTWH(0, 0, w.toDouble(), h.toDouble());
    return LogoArtwork(
      size: Size(w.toDouble(), h.toDouble()),
      bounds: crop,
      colors: paletteFromRgba(pixels),
      surface: uniform ? background : surfaceFromRgba(pixels),
      transparent: transparent,
      ink: darkCount == 0
          ? null
          : Color.fromARGB(
              255,
              (darkR / darkCount).round(),
              (darkG / darkCount).round(),
              (darkB / darkCount).round(),
            ),
    );
  } catch (_) {
    return null;
  } finally {
    image?.dispose();
    codec?.dispose();
  }
}

List<Color> paletteFromRgba(Uint8List pixels) {
  final buckets = <int, ({double weight, double r, double g, double b})>{};
  for (var i = 0; i + 3 < pixels.length; i += 4) {
    if (pixels[i + 3] < 180) continue;
    final hsv = HSVColor.fromColor(
      Color.fromARGB(255, pixels[i], pixels[i + 1], pixels[i + 2]),
    );
    if (hsv.saturation < .28 || hsv.value < .22) continue;
    final key = (((hsv.hue + 10) % 360) / 20).floor(),
        weight = hsv.saturation * hsv.value;
    final old = buckets[key] ?? (weight: 0.0, r: 0.0, g: 0.0, b: 0.0);
    buckets[key] = (
      weight: old.weight + weight,
      r: old.r + pixels[i] * weight,
      g: old.g + pixels[i + 1] * weight,
      b: old.b + pixels[i + 2] * weight,
    );
  }
  final sorted = buckets.entries.toList()
    ..sort((a, b) => b.value.weight.compareTo(a.value.weight));
  final chosen = <Color>[];
  for (final entry in sorted) {
    if (chosen.length == 3 ||
        entry.value.weight < sorted.first.value.weight * .12) {
      break;
    }
    final v = entry.value,
        c = Color.fromARGB(
          255,
          (v.r / v.weight).round(),
          (v.g / v.weight).round(),
          (v.b / v.weight).round(),
        );
    final hue = HSVColor.fromColor(c).hue;
    if (chosen.every((old) {
      final distance = (HSVColor.fromColor(old).hue - hue).abs();
      return distance > 40 && distance < 320;
    })) {
      chosen.add(c);
    }
  }
  return chosen;
}

/// Neutral surface from visible pixels, independent of the accent suggestion.
/// Transparent light marks need a dark plate; mixed artwork uses a mid neutral.
Future<Color> logoSurface(Uint8List bytes) async {
  ui.Codec? codec;
  ui.Image? image;
  try {
    codec = await ui.instantiateImageCodec(
      bytes,
      targetWidth: 48,
      targetHeight: 48,
    );
    image = (await codec.getNextFrame()).image;
    final data = await image.toByteData(format: ui.ImageByteFormat.rawRgba);
    if (data == null) return Colors.white;
    return surfaceFromRgba(data.buffer.asUint8List());
  } catch (_) {
    return Colors.white;
  } finally {
    image?.dispose();
    codec?.dispose();
  }
}

Color surfaceFromRgba(Uint8List pixels) {
  var light = 0, dark = 0, total = 0;
  for (var i = 0; i + 3 < pixels.length; i += 4) {
    if (pixels[i + 3] < 64) continue;
    total++;
    final luminance = Color.fromARGB(
      255,
      pixels[i],
      pixels[i + 1],
      pixels[i + 2],
    ).computeLuminance();
    if (luminance > .65) light++;
    if (luminance < .15) dark++;
  }
  if (total == 0) return Colors.white;
  if (light / total > .18 && dark / total > .18) return const Color(0xFF8B97A8);
  return light / total > .55 ? const Color(0xFF202B3D) : Colors.white;
}

/// Small local sample; white backgrounds and black outlines do not become accents.
Future<Color?> logoAccent(Uint8List bytes) async {
  ui.Codec? codec;
  ui.Image? image;
  try {
    codec = await ui.instantiateImageCodec(
      bytes,
      targetWidth: 64,
      targetHeight: 64,
    );
    image = (await codec.getNextFrame()).image;
    final pixels = await image.toByteData(format: ui.ImageByteFormat.rawRgba);
    if (pixels == null) return null;
    return accentFromRgba(pixels.buffer.asUint8List());
  } catch (_) {
    return null;
  } finally {
    image?.dispose();
    codec?.dispose();
  }
}

Color? accentFromRgba(Uint8List pixels) {
  final buckets = <int, ({double weight, double r, double g, double b})>{};
  for (var i = 0; i + 3 < pixels.length; i += 4) {
    if (pixels[i + 3] < 180) continue;
    final color = Color.fromARGB(255, pixels[i], pixels[i + 1], pixels[i + 2]);
    final hsv = HSVColor.fromColor(color);
    if (hsv.saturation < .28 ||
        hsv.value < .22 ||
        hsv.value > .98 && hsv.saturation < .4) {
      continue;
    }
    final key = (((hsv.hue + 10) % 360) / 20).floor();
    final weight = hsv.saturation * hsv.value;
    final old = buckets[key] ?? (weight: 0.0, r: 0.0, g: 0.0, b: 0.0);
    buckets[key] = (
      weight: old.weight + weight,
      r: old.r + pixels[i] * weight,
      g: old.g + pixels[i + 1] * weight,
      b: old.b + pixels[i + 2] * weight,
    );
  }
  if (buckets.isEmpty) return null;
  final winner = buckets.values.reduce((a, b) => a.weight >= b.weight ? a : b);
  return Color.fromARGB(
    255,
    (winner.r / winner.weight).round(),
    (winner.g / winner.weight).round(),
    (winner.b / winner.weight).round(),
  );
}

/// Keep white header controls readable even for yellow or very light logos.
Color headerTone(Color accent) {
  var hsv = HSVColor.fromColor(accent);
  var result = accent;
  while (1.05 / (result.computeLuminance() + .05) < 5 && hsv.value > .15) {
    hsv = hsv.withValue((hsv.value - .025).clamp(0, 1));
    result = hsv.toColor();
  }
  return result;
}
