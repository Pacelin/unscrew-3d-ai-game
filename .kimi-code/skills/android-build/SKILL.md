---
name: android-build
description: Build the Android APK of Unscrew - check Android Build Support/SDK/JDK, configure player settings, run BuildScript via UnityMCP or Unity batchmode, verify the output. Use when asked to build, ship, or test on a device.
---

# Android build

## Предусловия (проверить один раз)
1. Путь к редактору: `mcpforunity://project/info` или `EditorApplication.applicationPath` через `execute_code`.
2. Модуль: `<Editor>/Data/PlaybackEngines/AndroidPlayer` существует, внутри есть `SDK`, `NDK`, `OpenJDK`. Если нет, попроси человека поставить **Android Build Support** через Unity Hub (это его действие, агент не ставит).
3. `manage_build` / `execute_code`: `BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android, BuildTarget.Android)`.

## Настройки (BuildScript применяет их сам)
- Package: `com.novatest.unscrew`, Product Name `Unscrew 3D`, Company `NovaTest`.
- Portrait only, Min API 24, Target API — highest installed, IL2CPP, ARM64 (+ARMv7 по желанию), .NET Standard 2.1.
- Сцены: `Assets/Scenes/Main.unity` (+ другие из `EditorBuildSettings`).
- Подпись: debug keystore (для тестового APK достаточно).
- Bundle version = `git rev-list --count HEAD`.

## Сборка
- Скрипт: `Assets/_Game/Scripts/Editor/Build/BuildScript.cs`, `public static void BuildAndroid()` → `Builds/Unscrew_<version>.apk`, меню **Unscrew/Build Android APK**.
- Через MCP: `execute_menu_item("Unscrew/Build Android APK")` или `execute_code("Unscrew.Editor.BuildScript.BuildAndroid()")`. Потом `read_console` и найти `BuildReport`.
- Batchmode (Unity с этим проектом должен быть закрыт):
  `"<Editor>/Unity.exe" -batchmode -quit -projectPath K:/_NOVA_TEST -buildTarget Android -executeMethod Unscrew.Editor.BuildScript.BuildAndroid -logFile Logs/build.log`

## Проверка
- APK существует, размер разумный (< 150 МБ), в логе `Build succeeded`.
- Если есть `adb`: `adb install -r Builds/...apk`, `adb logcat -s Unity` на исключения при старте.
- APK не коммитится (`*.apk` в .gitignore), его прикладывают в GitHub Release.
