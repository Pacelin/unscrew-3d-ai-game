# План разработки Unscrew 3D

## 1. Механика (референс — Screw Master)

- Уровень — 3D-модель из деталей, детали прикручены болтами. Модель можно вращать.
- Сверху `activeBoxes` коробок, у каждой свой цвет и `boxCapacity` (3) слота. Коробки идут очередью `LevelLayout.boxQueue`.
- Тап по болту: болт летит в коробку своего цвета (левую подходящую), иначе в лунку буфера.
- Полная коробка уезжает, её место занимает следующая из очереди, а болты её цвета из буфера автоматически переезжают в неё (каскадно).
- Деталь, у которой не осталось болтов, падает (Rigidbody + `sourceReleaseForce`).
- Болт нельзя открутить, пока его перекрывает неупавшая деталь (`BoltDefinition.blockingPartIds`, граф бейкается в редакторе).
- **Победа** — сняты все болты. **Поражение** — ходов нет: ни один доступный болт не может уйти ни в открытую коробку своего цвета, ни в свободную лунку. Полный буфер сам по себе не поражение, пока какой-то доступный болт подходит к открытой коробке. Продолжить можно бустером ExtraSlot (новая лунка даёт ход) или за монеты.

## 2. Сложность

Ручки (`DifficultySettings`): число цветов, число открытых коробок, размер буфера, `mixWindow` — насколько болты одной коробки разбросаны по порядку разборки.
Цвета раскладывает `LayoutGenerator` так, что уровень **гарантированно проходим** без бустеров. Проверка — бот (`GreedySolver`) проходит все 20 уровней в интеграционном тесте.
Кривая: 1 — обучающий, плавный рост, каждый 5-й — «hard» (меньше буфера, больше смешивание).

## 3. Мета

- Мягкая валюта (монеты). Награда за уровень растёт с номером, hard-уровни дают ×2.
- Бустеры: **ExtraSlot** (+1 лунка / воскрешение), **Drill** (снять любой болт), **Magnet** (добить левую коробку). Открываются на уровнях 3/5/7, при открытии даётся 1 штука бесплатно.
- Магазин: бустеры за монеты, «бесплатные монеты» (заглушка rewarded-рекламы).
- Сохранение: `ISaveStorage` (PlayerPrefs в Unity).
- После 20-го уровня — повтор 11–20 с новым seed (бесконечная игра).

## 4. Архитектура

```
Unscrew.Core (чистый C#, тестируется dotnet-ом за секунды)
  Contracts   интерфейсы и данные (заморожены)
  Board       Board, GameSession            ← IBoard, IGameSession
  Collect     Collector                     ← ICollector
  Boosters    BoosterService                ← IBoosterService
  Economy     Wallet, BoosterInventory, Progress, Shop, Rewards, SaveService
  Generation  DifficultyCurve, LayoutGenerator, GreedySolver
Unscrew.Game (MonoBehaviour): LevelLoader, BoltView, PartView, BoxesView, BufferView, InputController,
                              CameraOrbit, GameFlow (state machine), UI-экраны
Unscrew.Editor: LevelBaker (граф блокировок → Data/Levels), UiBuilder, BuildScript
```

Слой Game только отображает события `CollectEvent` и вызывает `IGameSession`. Вся логика живёт в Core.

## 5. Спецификации Core-модулей (волна 1, параллельно)

У каждого модуля свой агент `core-dev`, своя ветка `core/<module>`, свой worktree. Модуль видит только `Contracts`. Зависимости заменяются фейками в тестах.

### Board — `Scripts/Core/Board`
- `Board : IBoard`, конструктор `(LevelDefinition)`. Использует `BoardRules`.
- `GameSession : IGameSession`, конструктор `(LevelDefinition, LevelLayout, ICollector)`.
  - `Tap`: `CanTap` → `Board.RemoveBolt` → `Collector.Add`. После хода: нет болтов → `Won`, иначе нет ни одного хода (доступный болт, цвет которого `Collector.CanAccept`) → `Lost`. Циклическая блокировка — частный случай.
  - `ForceRemove`: то же, но `ignoreBlocking: true`.
  - `Revive`: `Lost` → `Playing`, если сейчас есть хотя бы один ход (например, ExtraSlot добавил лунку).
- Тесты: фейковый ICollector в `Tests/Core/Board`.

### Collect — `Scripts/Core/Collect`
- `Collector : ICollector`, конструктор `(int[] boxQueue, int activeBoxes, int boxCapacity, int bufferSlots)`.
- Правила — в xml-доке `ICollector`. Порядок событий = порядок анимаций: `BoltToBox|BoltToBuffer`, затем `BoxCompleted`, `BoxActivated`, `BufferToBox`… (каскад).
- Тесты: каскады, исчерпание очереди, несколько коробок одного цвета, `AddBufferSlot`.

### Boosters — `Scripts/Core/Boosters`
- `BoosterService : IBoosterService`, конструктор `(IBoosterInventory)`.
- ExtraSlot: можно в `Playing` и `Lost`; `Collector.AddBufferSlot`, затем `Revive`, если `Lost`.
- Drill: `Playing`, цель не снята → `ForceRemove(target)`.
- Magnet: `Playing`, левая непустая коробка цвета C, нужно `capacity - filled` болтов цвета C на доске (сначала доступные, потом любые) → `ForceRemove` каждого.
- Бустер списывается из инвентаря только при успехе. Тесты на фейках IGameSession/ICollector/IBoard/IBoosterInventory.

### Economy — `Scripts/Core/Economy`
- `Wallet : IWallet`, `BoosterInventory : IBoosterInventory`, `PlayerProgress` (текущий уровень, бесконечный режим после 20), `RewardTable`, `ShopCatalog` + `Shop.TryBuy`, `BoosterUnlocks`, цена продолжения за монеты.
- `SaveService` — сохраняет и грузит всё через `ISaveStorage` (без JsonUtility и System.Text.Json: свой простой формат или по ключу на значение).
- Стартовый баланс: 100 монет. Все числа баланса в одном классе `EconomyConfig`.

### Generation — `Scripts/Core/Generation`
- `DifficultyCurve : IDifficultyCurve` — по `def.number` (1..20, дальше бесконечный режим) и размеру уровня. Seed детерминирован.
- `LayoutGenerator : ILayoutGenerator` — случайный допустимый порядок разборки (по `BoardRules`) → группы по `boxCapacity` → цвета групп → `boxQueue`. `mixWindow` перемешивает болты между соседними группами, сохраняя проходимость в пределах `bufferSlots` (самопроверка простой симуляцией).
- `GreedySolver : ISolver` — бот на `IGameSession`: сначала болты в открытые коробки, потом освобождающие детали, потом в буфер цвет, который раньше всех появится в очереди.
- Тесты: проходимость на синтетических уровнях, детерминизм по seed, монотонность кривой.

## 6. Этапы

| # | Этап | Кто | Проверка |
|---|---|---|---|
| 1 | Каркас: контракты, asmdef, dotnet-проекты, CLAUDE.md, скиллы, агенты | оркестратор | `test.sh All` + Unity EditMode |
| 2a | Core-модули ×5 параллельно (worktree) | core-dev ×5 | `test.sh <Module>` |
| 2b | Бейк уровней: граф блокировок → `Data/Levels` | level-designer (Unity) | отчёт + скриншоты |
| 3 | Мерж + интеграционные тесты: бот проходит 20 уровней | оркестратор / core-dev | `test.sh All`, Unity EditMode |
| 4 | Игровой слой: загрузка уровня, тапы, анимации, коробки и буфер, победа и поражение | unity-integrator | Play Mode + скриншоты, qa-tester |
| 5 | Мета-UI: меню, магазин, HUD бустеров, экраны победы/поражения | unity-integrator | qa-tester |
| 6 | Полировка: звук, VFX, haptics, туториал | unity-integrator | qa-tester |
| 7 | APK | android-build | установка на устройство |
| 8 | README (запуск, архитектура, процесс с ИИ) | оркестратор | — |
| 9* | Ивенты / доп. уровни | по времени | — |

## 7. Риски

- Неточный граф блокировок из raycast'ов → страховка в виде проверки на циклы при бейке, ручных override'ов в JSON и бота.
- Один редактор Unity на всех → MCP-агенты строго последовательно.
- Android Build Support / SDK — проверить до этапа 7.
