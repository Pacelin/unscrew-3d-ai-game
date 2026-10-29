---
name: unity-verify
description: Definition-of-done loop for any change that Unity compiles or renders - refresh, check console for compile errors, run EditMode tests, optionally enter Play Mode and capture a screenshot. Use after editing scripts/scenes/prefabs under Assets, before reporting work as done.
---

# Unity verify

Только один агент за раз управляет редактором. Если `editor/state` показывает Play Mode или компиляцию, которые запустил не ты, подожди.

## 1. Компиляция
1. `refresh_unity(compile="request", mode="force", wait_for_ready=true)`.
2. `read_console(types=["error","warning"], count=30)`.
   - Ошибки `CS####` → исправить и повторить с шага 1.
   - Игнорировать шум `MCP-FOR-UNITY ... WebSocket` — это сам MCP.
3. Если что-то похоже на зависание, прочитай ресурс `mcpforunity://editor/state` → `data.advice.ready_for_tools`.

## 2. Тесты
- Быстро (Core без Unity): `bash Tools/CoreTests/test.sh All`.
- В Unity: `run_tests(mode="EditMode", assembly_names=["Unscrew.Core.Tests", ...])`, затем `get_test_job(job_id, wait_timeout=60, include_failed_tests=true)`.
- PlayMode-тесты (если есть): `mode="PlayMode", init_timeout=120000`.

## 3. Визуальная проверка (для изменений в view/UI/сцене)
1. Сохранить сцену (`manage_scene` save) до входа в Play Mode.
2. `manage_editor` → play; дождаться готовности; при необходимости смоделировать ход через `execute_code` (например, `GameFlow` / `IGameSession.Tap`).
3. Скриншот Game view (`manage_camera`/`manage_editor` screenshot) → сохранить в `Docs/Screens/<этап>_<что>.png`, посмотреть его (Read).
4. `read_console` на runtime-ошибки (`NullReference`, `MissingReference`).
5. `manage_editor` → stop. Не оставлять редактор в Play Mode.

## 4. Отчёт
Пиши фактами: «компиляция чистая, EditMode N/N, скриншот Docs/Screens/...». Если что-то не проверено, так и напиши.
