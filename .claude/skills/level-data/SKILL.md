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
  "bolts": [ { "id": 0, "ownerPartId": 0, "blockingPartIds": [], "headLift": 0.0 } ] }
```
Генерирует editor-скрипт `Assets/_Game/Scripts/Editor/Levels/LevelBaker.cs`, меню **Unscrew/Bake Levels** (а также статический метод для `execute_code` и batchmode).

Алгоритм `blockingPartIds` (реализован в `LevelBaker`, детали, параметры и таблицы — `Docs/Modules/LevelBake.md`):
1. Префаб инстанцируется в preview-сцене, на детали вешаются `MeshCollider` (non-convex), `queriesHitBackfaces = true`.
2. Геометрия болта — из меша `Screw004` (`LevelBaker.GetGeometry`): ось = локальная +Y меша = `transform.forward` болта («наружу»);
   в мире радиус шляпки 0.31–0.48, высота шляпки 0.18–0.28, длина болта 0.46–0.72 (масштаб зависит от уровня).
3. **Правило «путь выкручивания свободен»**: `SphereCast` (все попадания) радиусом = радиус шляпки, старт — центр сферы на R выше
   посадочной плоскости шляпки, вдоль оси наружу, дистанция = длина болта × 1.15. Любая деталь ≠ owner, задетая свипом, — блокер.
   Начальные пересечения (distance 0) — тоже блокеры, плюс отдельный список с глубиной проникновения.
4. `overrides.json` (`{level, boltId, add:[], remove:[]}`) — ручные исправления.
5. Циклы разрываются (выкидывается ребро с самым дальним попаданием; начальные пересечения весят −глубина, т. е. сначала
   самые неглубокие касания), всё логируется. Итог валидируется по `BoardRules`.
6. **Правило «шляпка не внутри владельца»** (`LevelValidator.CheckHead`): 19 точек шляпки, тест чётности лучей по 7 направлениям против
   MeshCollider владельца. Исправление — `BoltDefinition.headLift` (`LevelValidator.ComputeHeadLift`): минимальный сдвиг наружу вдоль оси,
   при котором все точки шляпки вне владельца (шаг + бисекция) + 0.015, округление вверх до 0.001. Применяется в бейке **до** свипа
   (шаг 3 считается от поднятой позиции). Поиск чистой позиции — до 10× длины болта. **Откатов нет** (решение геймдизайнера, бейк 4):
   подъёмы длиннее болта / со стержнем, не достающим до владельца / найденные дальше 3× длины применяются и только помечаются
   (`LiftResult.forcedReason`, `BakeResult.forcedLifts`; > 2× длины — «extreme»). Откат `headLift = 0` — только если чистой позиции нет
   в пределах 10× длины (сейчас 0). Итог: 161 нарушение → 161 поднято (0.026–2.628), правило 1 — 0 нарушений на всех 20 уровнях;
   32 принудительных (L05 — 11, L18 — 14, L12 — 4, L13 — 2, L16 — 1; 13 extreme), список — в LevelBake.md.

Меню: **Unscrew/Validate Levels** (`LevelValidator.ValidateAll(false)` — отчёт без записи, сравнение с файлами на диске),
**Unscrew/Validate And Rebake Levels** (`ValidateAll(true)`), **Unscrew/Bake Levels** (`LevelBaker.BakeAll()`).

**`headLift` обязателен для игры:** view при спавне болта делает `boltTransform.position += boltTransform.forward * headLift`
(мировые единицы уровня при корне префаба в масштабе 1; если корень уровня масштабируется — умножить на `levelRoot.lossyScale.x`).
Без этого у 161 болта шляпка останется утопленной, а граф блокировок (посчитанный от поднятых шляпок) не будет соответствовать картинке.

**Следствие для игры:** при разрыве циклов выкидывается 139 рёбер (10 уровней), и правило не учитывает видимость: часть болтов формально
доступна, хотя шляпка визуально прикрыта (или утоплена во владельце). Тап — raycast **только по слою болтов**, доступность — из
`IBoard.IsAccessible`, а не по видимости.

Отчёт бейка: `Docs/Modules/LevelBake.md` — по уровням: заблокированные, доступные на старте, глубина графа, разорванные циклы (было → стало), начальные пересечения свипа, нарушения правила 1.

## Чтение в рантайме и тестах

- Unity: `JsonUtility.FromJson<LevelDefinition>(textAsset.text)`.
- dotnet-тесты (`#if CORE_DOTNET_TESTS`): `System.Text.Json` с `IncludeFields = true`, путь `Assets/_Game/Data/Levels`.
