# Economy (`Assets/_Game/Scripts/Core/Economy`)

Мета-слой: монеты, бустеры в инвентаре, прогресс по уровням, награды, магазин, открытие бустеров, продолжение за монеты и сохранение. Зависит только от `Contracts`.

## Публичный API

| Тип | Назначение |
|---|---|
| `EconomyConfig` | Все числа баланса: `startCoins=100`, `levelCount=20`, `endlessFirstLevel=11`, награды, цены продолжения, уровни открытия бустеров `{3,5,7}`, `freeBoostersOnUnlock=1`, `shopItems`. |
| `Wallet : IWallet` | `Coins`, `Add`, `TrySpend`, событие `Changed(newCoins)`. |
| `BoosterInventory : IBoosterInventory` | `GetCount`, `TryConsume`, `Add`, событие `Changed(type, newCount)`. |
| `PlayerProgress` | `CurrentLevel` (сквозной номер 1, 2, …), `Current : LevelSlot`, `GetSlot(n)`, `CompleteLevel()`, событие `Changed(newLevel)`. |
| `LevelSlot` | `Number` (показывать игроку), `SourceLevel` (какой `Level_XX.json` грузить), `SeedVariant` (0 при первом проходе, 1, 2, … на кругах бесконечного режима), `IsEndless`. |
| `RewardTable` | `GetReward(levelNumber, isHard)`. |
| `BoosterUnlocks` | `IsUnlocked`, `GetUnlockLevel`, `Refresh(currentLevel, inventory, newlyUnlocked)`, событие `Unlocked(type)`. |
| `ShopCatalog` / `ShopItem` / `Shop` | `Catalog.Items`, `Find(id)`; `Shop.CanBuy(id)` и `Shop.TryBuy(id)` возвращают `ShopResult` (`Success`, `UnknownItem`, `NotEnoughCoins`, `Locked`). |
| `ContinueOffer` | `Price`, `CanAfford`, `TryPay()`, `ContinuesUsed`, `ResetForLevel()`. |
| `PlayerState` | Фасад: `Wallet`, `Inventory`, `Progress`, `Unlocks`, `Rewards`, `Continue`, `Shop`, `CompleteLevel(isHard, newlyUnlocked)` возвращает выплаченную награду. `CreateNew(config)`. |
| `SaveService` | `Load()` возвращает `PlayerState`, `Save(state)`, `EnableAutoSave(state)` / `DisableAutoSave()`, `StoredVersion`. |

### Как этим пользуется игровой слой

```csharp
var save = new SaveService(new PlayerPrefsStorage(), config);
var state = save.Load();
save.EnableAutoSave(state);

LevelSlot slot = state.Progress.Current;          // грузим Level_{slot.SourceLevel:00}, seed смешиваем с slot.SeedVariant
state.Continue.ResetForLevel();                   // при старте и рестарте уровня
// победа:
int coins = state.CompleteLevel(settings.isHard, newlyUnlocked);   // newlyUnlocked → попап «новый бустер»
// поражение: state.Continue.TryPay() → session.Collector.AddBufferSlot() + session.Revive()
// магазин:   state.Shop.TryBuy("drill_1")
```

UI подписывается на `Wallet.Changed`, `Inventory.Changed`, `Progress.Changed`, `Unlocks.Unlocked`.

## Ключевые решения

- **Бесконечный режим.** Номер уровня сквозной и не ограничен. Для `n > 20`: `k = n - 21`, `SourceLevel = 11 + k % 10`, `SeedVariant = 1 + k / 10`. Номер 21 соответствует уровню 11 в варианте 1, номер 31 — уровню 11 в варианте 2. Как построить seed из `SeedVariant`, решает Generation или склейка (Economy не знает про `DifficultySettings.seed`).
- **Награда.** `20 + 2·(min(n, 20) − 1)`, на hard-уровне ×2. В бесконечном режиме считается по `SourceLevel`, поэтому повтор уровня 15 платит как уровень 15. Признак hard не вычисляется здесь: его передаёт вызывающий из `DifficultySettings.isHard`, чтобы Economy не зависел от Generation.
- **Открытие бустеров.** Бустер открывается, когда игрок **доходит** до уровня 3/5/7, то есть после победы на 2/4/6. Так в Screw Master бустер показывают с туториалом на этом уровне. 1 штука выдаётся один раз, флаг хранится в сохранении. Если пропустить сразу несколько порогов, откроются все, в порядке `BoosterType`.
- **Магазин.** Закрытые бустеры купить нельзя (`ShopResult.Locked`). Есть паки ×1 и ×3 со скидкой. «Бесплатные монеты» — это обычный `ShopItem` вида `Coins` с ценой 0 (+25), а показ рекламы остаётся на стороне UI. Числа лежат в `EconomyConfig.shopItems`.
- **Продолжение за монеты.** Цена `100 + 50·(число продолжений на этом уровне)`. `ContinueOffer` только списывает монеты, а возрождение (`AddBufferSlot` + `Revive`, как ExtraSlot) делает вызывающий. `PlayerState.CompleteLevel` сбрасывает счётчик сам. При старте или рестарте уровня нужно вызвать `ResetForLevel()`.
- **События** вызываются только при реальном изменении: `Add(0)` и неудачный `TrySpend` ничего не шлют. Отрицательные суммы и неизвестный `BoosterType` дают `ArgumentException`. Сложение насыщается на `int.MaxValue`.
- **Формат сохранения.** Одно значение на ключ, целые числа в invariant culture, флаги хранятся как `0`/`1`. Ключи: `unscrew.version` (=1), `unscrew.coins`, `unscrew.level`, `unscrew.booster.{extraslot|drill|magnet}`, `unscrew.unlocked.{…}`. Имена ключей бустеров заданы явно и не берутся из `enum.ToString()`. Каждый ключ читается отдельно: если значение отсутствует, не парсится, отрицательное или уровень `< 1`, берётся дефолт только этого ключа. Исключение из `ISaveStorage.Load` тоже ведёт к дефолту. Версия пишется при каждом `Save`. Сохранение без версии или с более новой версией читается по ключам, а точка для будущих миграций отмечена в `Load`.
- **Автосохранение.** При каждом событии переписывается всё состояние, около 8 ключей. Это дёшево, и сохранение не может оказаться частично рассинхронизированным. Сбрасывать на диск (`PlayerPrefs.Save()`) — забота Unity-реализации `ISaveStorage`.

## Тесты (`Assets/_Game/Tests/Core/Economy`, 47 шт.)

- Wallet: старт, `Add`/`TrySpend`, точный баланс, нули без событий, отрицательные суммы, насыщение.
- BoosterInventory: счётчики по типам, пустой `TryConsume`, содержимое `Changed`, неверные аргументы.
- PlayerProgress: соответствие номер → (source, variant) для 1, 20, 21, 30, 31, 45; переход 20 → 21; невалидный номер.
- RewardTable: рост, hard ×2, формула из конфига, ограничение сверху для бесконечного режима.
- ContinueOffer: рост цены, сброс, нехватка монет.
- BoosterUnlocks: уровни 3/5/7, бесплатная штука, однократность, пропуск нескольких порогов, `null`-список.
- Shop: состав каталога, покупка, `Locked`, `NotEnoughCoins` без побочных эффектов, бесплатные монеты, неизвестный id.
- SaveService: пустое хранилище, round-trip, ключ версии, повреждённые и отрицательные значения (по ключу), сохранение без версии, автосохранение и его отключение.
- PlayerState: выплата награды, переход на следующий уровень, открытие бустера, награда в бесконечном режиме по `SourceLevel`.

Тестовый хелпер `MemorySaveStorage` (in-memory `ISaveStorage`) лежит в `Tests/Core/Economy`. Его стоит перенести в `Tests/Core/Shared`.

## Предложения по Contracts

Не обязательны, модуль работает с текущими контрактами.
- `BoosterInventory.TypeCount = 3` дублирует число значений `BoosterType`. Можно добавить в Contracts константу `BoosterTypes.Count` (или `Enum.GetValues`).
