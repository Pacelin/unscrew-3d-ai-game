# Модуль Board

Код: `Assets/_Game/Scripts/Core/Board`, тесты: `Assets/_Game/Tests/Core/Board`.
Проверка: `bash Tools/CoreTests/test.sh Board`.

## Публичный API

```csharp
public sealed class Board : IBoard
{
    Board(LevelDefinition definition);           // валидирует определение, ArgumentException при битых данных
    LevelDefinition Definition; int RemainingBolts;
    bool IsRemoved(int boltId);                  // вне диапазона -> false
    bool IsPartReleased(int partId);             // вне диапазона -> ArgumentOutOfRangeException
    bool IsAccessible(int boltId);               // вне диапазона -> false
    void GetAccessibleBolts(List<int> result);   // очищает и заполняет по возрастанию
    void RemoveBolt(int boltId, bool ignoreBlocking, List<int> releasedParts);
    bool HasAccessibleBolts();                   // доп. метод (не в IBoard), без аллокаций
}

public sealed class GameSession : IGameSession
{
    GameSession(LevelDefinition, LevelLayout, ICollector);
    IBoard Board; ICollector Collector; LevelLayout Layout; SessionState State; int MovesMade;
    int GetBoltColor(int boltId);
    bool CanTap(int boltId);
    bool Tap(int boltId, List<CollectEvent> events, List<int> releasedParts);
    bool ForceRemove(int boltId, List<CollectEvent> events, List<int> releasedParts);
    bool Revive();
    bool HasAvailableMove();                     // доп. метод: есть ли ход (доступный болт, который примет коллектор)
}
```

## Ключевые решения

- **Board** хранит `bool[] removed` и кэш `bool[] partReleased`. Деталь может освободиться только при снятии
  своего болта, поэтому после `RemoveBolt` проверяется лишь владелец. Семантика совпадает с `BoardRules`
  (это проверяет тест `Board_MatchesBoardRules`); деталь без болтов считается освобождённой с начала.
- `RemoveBolt`: снятый болт → `InvalidOperationException`, заблокированный без `ignoreBlocking` →
  `InvalidOperationException`, id вне диапазона → `ArgumentOutOfRangeException`. При исключении состояние не меняется.
- **Выходные списки** (`events`, `releasedParts`) в `Board` и `GameSession` только дополняются, не очищаются —
  так бустер Magnet может накопить события нескольких `ForceRemove` в один список. Очищает вызывающий.
  `null` вместо списка → `ArgumentNullException`.
- **Порядок хода**: `Board.RemoveBolt` → `Collector.Add` → пересчёт состояния. Приоритет проверок:
  нет болтов → `Won` (даже если последний болт лёг в буфер и заполнил его); иначе нет ни одного хода → `Lost`.
  Эта же проверка делается в конструкторе (пустой уровень → сразу `Won`, нет ходов с начала → сразу `Lost`).
- **Правило поражения — «ходов нет» (решение геймдизайнера)**: `HasAvailableMove()` = существует доступный болт
  (`IsAccessible`), чей цвет `Collector.CanAccept` — т.е. есть открытая коробка этого цвета с местом или свободная лунка.
  **Полный буфер сам по себе не поражение**: пока какой-то доступный болт подходит к открытой коробке, игра идёт.
  Циклическая блокировка (болты есть, доступных нет) покрывается этим же правилом.
  Проверка без аллокаций: проход по `boltColors` с `IsAccessible` + `CanAccept`, выход на первом найденном ходе.
- В `Lost` ходы и `ForceRemove` запрещены, продолжить можно только через `Revive`.
- `Revive`: только из `Lost` и только если ход есть прямо сейчас (`HasAvailableMove`), например ExtraSlot добавил лунку.
  Дедлок без доступных болтов воскресить нельзя (лунка не помогает, а Drill в `Lost` не работает).
- `MovesMade` считает только успешные `Tap`; снятия бустерами (`ForceRemove`) ходами не считаются.
- `CanTap`/`Tap`/`ForceRemove` с id вне диапазона возвращают `false`, а не бросают (тап по «чужому» объекту безопасен).
  `GetBoltColor` вне диапазона → `ArgumentOutOfRangeException`.
- Конструктор `GameSession` требует `layout.boltColors.Length == bolts.Length`. `boxQueue` не проверяется —
  он целиком принадлежит коллектору.

## Тесты (43)

- `BoardTests`: начальное состояние, валидация, порядок и очистка `GetAccessibleBolts`, освобождение детали и
  разблокировка, болт под двумя деталями, `ignoreBlocking`, повторное снятие, id вне диапазона, сверка с `BoardRules`.
- `GameSessionTests` (на `FakeCollector`): Tap/ForceRemove — успех и отказы (блок, снят, вне диапазона, коллектор не принимает),
  события и releasedParts, победа, приоритет победы над полным буфером; полный буфер + доступный болт цвета
  открытой коробки → `Playing` и этот ход работает; полный буфер без подходящих болтов → `Lost`; запрет ходов в Lost;
  Revive (нет хода / после AddBufferSlot / после появления коробки / в Playing / после Won), циклическая блокировка
  в процессе и с начала (не воскрешается), «нет ходов» в конструкторе, пустой уровень, null-списки.

`FakeCollector` (`Tests/Core/Board/FakeCollector.cs`): цвета из `BoxColors` уходят «в коробку» без ограничений,
остальные — в буфер заданной ёмкости; `AcceptOverride = false` эмулирует отказ.

## Предложения по Contracts

- Добавить в `IBoard` метод `bool HasAccessibleBolts()` (без аллокаций) — нужен сессии и пригодится боту/Generation.
  Сейчас он есть только у класса `Board`.
- Явно указать в xml-доке `IGameSession.Tap/ForceRemove`, что списки дополняются, а не очищаются.
