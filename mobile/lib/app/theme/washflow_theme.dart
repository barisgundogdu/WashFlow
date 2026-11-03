import 'package:flutter/material.dart';

abstract final class WashFlowSpacing {
  static const xs = 4.0;
  static const sm = 8.0;
  static const md = 12.0;
  static const lg = 16.0;
  static const xl = 24.0;
  static const xxl = 32.0;
}

abstract final class WashFlowTheme {
  static const ink = Color(0xFF10242C);
  static const primary = Color(0xFF006B68);
  static const selected = Color(0xFFDDF3EF);
  static const background = Color(0xFFF4F7F8);
  static const muted = Color(0xFF51666E);
  static const outline = Color(0xFF788B91);
  static const controlRadius = BorderRadius.all(Radius.circular(12));
  static const cardRadius = BorderRadius.all(Radius.circular(16));

  static final light = _buildLight();

  static ThemeData _buildLight() {
    final colors = ColorScheme.fromSeed(seedColor: primary).copyWith(
      primary: primary,
      onPrimary: Colors.white,
      primaryContainer: selected,
      onPrimaryContainer: ink,
      secondaryContainer: selected,
      onSecondaryContainer: ink,
      surface: Colors.white,
      onSurface: ink,
      onSurfaceVariant: muted,
      outline: outline,
      outlineVariant: const Color(0xFFD5E0E3),
      error: const Color(0xFFB3261E),
      onError: Colors.white,
      errorContainer: const Color(0xFFFFEDEA),
      onErrorContainer: const Color(0xFF78201A),
    );
    final base = ThemeData(
      useMaterial3: true,
      colorScheme: colors,
      scaffoldBackgroundColor: background,
      materialTapTargetSize: MaterialTapTargetSize.padded,
      visualDensity: VisualDensity.standard,
    );
    final type = base.textTheme;
    const controlShape = RoundedRectangleBorder(borderRadius: controlRadius);
    return base.copyWith(
      textTheme: type.copyWith(
        headlineLarge: type.headlineLarge?.copyWith(
          fontSize: 28,
          height: 34 / 28,
          fontWeight: FontWeight.w700,
        ),
        titleMedium: type.titleMedium?.copyWith(fontWeight: FontWeight.w600),
        bodyLarge: type.bodyLarge?.copyWith(height: 1.5),
      ),
      appBarTheme: const AppBarTheme(
        backgroundColor: background,
        foregroundColor: ink,
        surfaceTintColor: Colors.transparent,
        centerTitle: true,
      ),
      cardTheme: const CardThemeData(
        color: Colors.white,
        surfaceTintColor: Colors.transparent,
        elevation: 0,
        shape: RoundedRectangleBorder(
          borderRadius: cardRadius,
          side: BorderSide(color: Color(0xFFD5E0E3)),
        ),
      ),
      inputDecorationTheme: InputDecorationTheme(
        filled: true,
        fillColor: colors.surface,
        contentPadding: const EdgeInsets.all(WashFlowSpacing.lg),
        border: const OutlineInputBorder(borderRadius: controlRadius),
        enabledBorder: const OutlineInputBorder(
          borderRadius: controlRadius,
          borderSide: BorderSide(color: outline),
        ),
        focusedBorder: const OutlineInputBorder(
          borderRadius: controlRadius,
          borderSide: BorderSide(color: primary, width: 2),
        ),
        errorMaxLines: 3,
      ),
      filledButtonTheme: FilledButtonThemeData(
        style: FilledButton.styleFrom(
          minimumSize: const Size(48, 56),
          padding: const EdgeInsets.symmetric(
            horizontal: WashFlowSpacing.xl,
            vertical: WashFlowSpacing.lg,
          ),
          shape: controlShape,
        ),
      ),
      outlinedButtonTheme: OutlinedButtonThemeData(
        style: OutlinedButton.styleFrom(
          minimumSize: const Size(48, 48),
          shape: controlShape,
        ),
      ),
      textButtonTheme: TextButtonThemeData(
        style: TextButton.styleFrom(
          minimumSize: const Size(48, 48),
          shape: controlShape,
        ),
      ),
    );
  }
}
