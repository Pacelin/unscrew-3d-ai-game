---
name: level-bake
description: Rebake level data (bolt blocking graph / availability) from the source configs UnscrewFirst20 into Assets/_Game/Data/Levels. Use when asked to rebake, re-bake, or resync levels after changing source data, bake rules or level geometry.
---

# Запекание уровней (level bake)

Пересчитывает `Assets/_Game/Data/Levels/Level_XX.json` (формат `Unscrew.Core.LevelDefinition`) из исходников
`Assets/UnscrewFirst20/Levels/Level_XX.prefab` + `Assets/UnscrewFirst20/Data/Level_XX.json`. Позиции болтов — **строго
из конфига** (headLift не применяется, см. флаги ниже). Трогать только `Assets/_Game/Data/Levels/**`.

## Что запекается

- `blockingPartIds` (доступность): свип сферы радиуса шляпки от исходной позиции вдоль оси болта × 1.15 длины;
  каждая деталь ≠ владельца, задетая свипом, — блокер. Начальные пересечения — с глубиной.
- Циклы разрываются (выбрасывается самое дальнее ребро), всё валидируется волнами `BoardRules.IsAccessible`.
- Формат/правила: `Docs/Modules/LevelBake.md` (там же история бейков 1–6), формат данных — скилл `level-data`.

## Как запустить

В редакторе Unity:

- **Unscrew → Bake Levels** — все 20 уровней, запись на диск (`LevelBaker.BakeAll`).
- **Unscrew → Validate Levels** — отчёт без записи (сравнение с файлами на диске).

Через MCP (`execute_code`), со **стандартными флагами проекта**:

```csharp
// Стандартный бейк: позиции из конфига (headLift=0), доступность, запись.
Unscrew.Editor.Levels.LevelBaker.ApplyHeadLift = false;      // true = посадка ComputeSeat (подъём/опускание шляпки)
Unscrew.Editor.Levels.LevelBaker.ApplyVisibilityRule = false; // true = аналитическая тапаемость (бейк 5, см. LevelBake.md)
var ov = Unscrew.Editor.Levels.LevelBaker.LoadOverrides();
for (int n = FROM; n <= TO; n++)
    Unscrew.Editor.Levels.LevelBaker.BakeLevel(n, ov, true);  // write=true
return "baked";
```

После записи — `refresh_unity` (Assets), затем EditMode-тесты (`Unscrew.Core.Tests` читают эти JSON).

## Флаги (`LevelBaker` / `LevelValidator`)

| Флаг | true | false (стандартно) |
| --- | --- | --- |
| `ApplyHeadLift` | `ComputeSeat`: утопленные шляпки выкручиваются, висящие закручиваются (знаковый headLift) | headLift = 0, позиции из конфига; правило 1 только отчёт |
| `ApplyVisibilityRule` | аналитический зонд тапаемости + печатники (бейк 5; ненадёжен, см. LevelBake.md §Правило 3) | доступность только |

## Контроль после бейка

- Все `headLift == 0` (если ApplyHeadLift=false): проверить скриптом/глазами.
- `BakeResult.valid == true` для всех уровней; валидатор меню печатает таблицу.
- Проходимость через реальный ввод — input-кампания `DebugAutoplay.RunCampaignViaInput` (см. `qa-tester`, п. 3).
  Внимание: с headLift=0 часть шляпок утоплена/заподлицо — тапаемость зависит от запаса тап-бокса (1.15×);
  дедлоки, если появятся, лечатся только осознанными правками данных/мешей, не автолифтами.
