---
name: level-data
description: Format of the source level assets (Level_XX.prefab, Level_XX.json, bolt prefabs and palette) and of the baked LevelDefinition files in Assets/_Game/Data/Levels, plus how the blocking graph is baked. Use when loading levels, mapping bolts/parts to GameObjects, coloring bolts, or (re)baking level data.
---

# Level data

## Исходные ассеты (`Assets/UnscrewFirst20`, только чтение)

- `Levels/Level_01..20.prefab`: `Level_XX/Assembly/{Model/<детали>, Bolts/Bolt_NNN_Owner_MMM}`.
  Префабы содержат только Transform + MeshFilter + MeshRenderer. Коллайдеры и физику добавляем сами в рантайме или при бейке.
- `Data/Level_XX.json`:
  - `parts[]`: `id` (== индекс), `childIndex`, `boltIds`, `sourceReleaseForce` (vec3), `sourceSpin`, `name`, `nodePath`;
  - `bolts[]`: `id` (== индекс), `ownerId`, `position`, `eulerDegrees` (относительно `Assembly`), `nodePath`;
  - `allowedBoltPaletteIndices`: 0..11; `rootEulerDegrees`, `rootScale`, `modelPosition`, `modelEulerDegrees`, `modelScale`.
  Проверено для всех 20 уровней: id подряд, у каждой детали есть болты, болтов кратно 3. `sourceLayer` = 0 везде, кроме Level_01 (1 и 2), — порядка разборки в данных фактически нет.
- Палитра: индекс `i` → `Bolts/Prefabs/Bolt_{i:00}.prefab`. Материалы `Bolts/Materials/{i+1}_<color>_1|2.mat` (0 red, 1 yellow, 2 blue, 3 green, 4 purple, 5 orange, 6 pink, 7 white, 8 lightblue, 9 brown, 10 bluegray, 11 gray). В готовых уровнях все болты белые (`Bolt_07`), в рантайме их нужно перекрасить (свапнуть `sharedMaterials` с `Bolt_XX`).
- Связь с GameObject: деталь `parts[i]` = `Assembly/Model` child с индексом `childIndex` (или по `nodePath`), болт `bolts[i]` = `Assembly/Bolts` по `nodePath`.

## Бейк (`Assets/_Game/Data/Levels/Level_XX.json`)

Формат — `Unscrew.Core.LevelDefinition` (JsonUtility, camelCase-поля):
```json
{ "number": 1, "allowedColors": [1,2],
  "parts": [ { "id": 0, "boltIds": [0,1,2] } ],
  "bolts": [ { "id": 0, "ownerPartId": 0, "blockingPartIds": [] } ] }
```
Генерирует editor-скрипт `Assets/_Game/Scripts/Editor/Levels/LevelBaker.cs`, меню **Unscrew/Bake Levels** (а также статический метод для `execute_code` и batchmode).

Алгоритм `blockingPartIds` (реализован в `LevelBaker`, детали и параметры — `Docs/Modules/LevelBake.md`):
1. Префаб инстанцируется в preview-сцене, на детали вешаются `MeshCollider` (non-convex).
2. Ось болта: длинная ось меша `Screw004` — локальная +Y, «наружу» = `transform.forward` болта (проверено на всех болтах Level_01).
3. **Короткий коридор** перед шляпкой (4 радиуса шляпки в длину, 0.6 радиуса в ширину, касания ближе 0.5 радиуса игнорируются) → детали ≠ owner в коридоре = блокеры. Бесконечный луч не подходит: через полости моделей он даёт ложные блокировки.
4. **Видимость**: если шляпку не видно ни из одного из 400 направлений в конусе 75°, блокеры берутся вдоль наименее закрытого направления.
5. Циклы разрываются (выкидывается самый дальний блокер), всё логируется. Итог валидируется по `BoardRules`.
6. Ручные исправления — `Assets/_Game/Data/Levels/overrides.json` (`{level, boltId, add:[], remove:[]}`).

**Следствие для игры:** из-за разорванных циклов около 7% болтов формально доступны, хотя их шляпка визуально прикрыта. Тап — raycast **только по слою болтов**, доступность — из `IBoard.IsAccessible`, а не по видимости.

Отчёт бейка: `Docs/Modules/LevelBake.md` — по уровням: болтов, заблокированных, доступных на старте, глубина графа, разорванные циклы.

## Чтение в рантайме и тестах

- Unity: `JsonUtility.FromJson<LevelDefinition>(textAsset.text)`.
- dotnet-тесты (`#if CORE_DOTNET_TESTS`): `System.Text.Json` с `IncludeFields = true`, путь `Assets/_Game/Data/Levels`.
