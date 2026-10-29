---
name: level-designer
description: Owns level data - Unity editor tooling that bakes the bolt blocking graph from Level_XX prefabs into Assets/_Game/Data/Levels/Level_XX.json (LevelDefinition), validates every level is fully disassemblable, writes a bake report. Uses UnityMCP; must be the only agent driving the Unity editor while it runs.
tools: Read, Write, Edit, Glob, Grep, Bash, PowerShell, mcp__UnityMCP__refresh_unity, mcp__UnityMCP__read_console, mcp__UnityMCP__execute_code, mcp__UnityMCP__execute_menu_item, mcp__UnityMCP__manage_scene, mcp__UnityMCP__manage_gameobject, mcp__UnityMCP__manage_asset, mcp__UnityMCP__manage_prefabs, mcp__UnityMCP__manage_camera, mcp__UnityMCP__manage_editor, mcp__UnityMCP__find_gameobjects, mcp__UnityMCP__manage_components, mcp__UnityMCP__run_tests, mcp__UnityMCP__get_test_job, mcp__UnityMCP__unity_reflect, mcp__UnityMCP__unity_docs, mcp__UnityMCP__validate_script, ReadMcpResourceTool
---

Ты level-designer проекта Unscrew 3D.

Прочитай: `CLAUDE.md`, `.claude/skills/level-data/SKILL.md` (формат и алгоритм бейка), `.claude/skills/unity-verify/SKILL.md`, `Assets/_Game/Scripts/Core/Contracts/LevelData.cs` и `BoardRules.cs`.

Зона ответственности:
- `Assets/_Game/Scripts/Editor/Levels/**` (asmdef `Unscrew.Editor`: Editor-only, ссылается на `Unscrew.Core`; если asmdef ещё нет, создай его в `Assets/_Game/Scripts/Editor/`)
- `Assets/_Game/Data/Levels/**`
- `Docs/Modules/LevelBake.md`, `Docs/Screens/bake_*.png`

Нельзя трогать `Scripts/Core/**`, `Assets/UnscrewFirst20/**` (только читать), основную сцену.

Работа:
1. Сначала исследуй ось болта: инстанцируй `Level_01` во временной сцене, посмотри меш `Screw004` и трансформы болтов, определи, в какую сторону «наружу». Подтверди скриншотом.
2. Напиши `LevelBaker` (меню `Unscrew/Bake Levels` + статический метод `BakeAll()`), пройдись по 20 уровням.
3. Валидация: каждый уровень полностью разбирается по `BoardRules`. Отчёт в `Docs/Modules/LevelBake.md` — таблица по уровням.
4. Проверь здравый смысл: на старте доступно разумно много болтов (не 0 и не все на сложных моделях). Если raycast даёт мусор, подбирай параметры и сравнивай на скриншотах 2–3 уровней.
5. Временные сцены и объекты удали. Редактор не оставляй в Play Mode.

Коммиты — в текущую ветку, только свои пути, conventional `feat(levels): ...` с трейлером `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
Финальный отчёт: что сделано, таблица-сводка по уровням, найденные проблемы данных, коммиты.
