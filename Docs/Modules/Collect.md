# Модуль Collect

`Assets/_Game/Scripts/Core/Collect/Collector.cs` — `Collector : ICollector`: коробки сверху и буфер лунок снизу.
Тесты: `Assets/_Game/Tests/Core/Collect/CollectorTests.cs`, прогон `bash Tools/CoreTests/test.sh Collect`.

## Публичный API

```csharp
new Collector(int[] boxQueue, int activeBoxes, int boxCapacity, int bufferSlots)
int BoxCapacity, ActiveBoxCount;  BoxView GetBox(slot);  IReadOnlyList<int> UpcomingColors;  bool AllBoxesDone
int BufferCapacity, BufferUsed;   bool IsBufferFull;     int GetBufferBolt(hole), GetBufferColor(hole)
bool CanAccept(color)
void Add(boltId, color, List<CollectEvent> events)      // дописывает события в конец списка
void AddBufferSlot(List<CollectEvent> events)
```

## Правила и решения

- **Старт.** Первые `min(activeBoxes, queue.Length)` цветов очереди сразу встают в слоты 0..N-1, остальные — в `UpcomingColors`. Конструктор событий не генерирует (стартовую раскладку view берёт из `GetBox`). Очередь копируется.
- **Add.** Болт идёт в самую левую неполную коробку своего цвета (`BoltToBox`), иначе в самую левую свободную лунку (`BoltToBuffer`). Если не может ни то, ни другое — `InvalidOperationException`, состояние и список событий не меняются.
- **Каскад** (только в слоте, куда попал болт, — другие слоты не меняются):
  `BoltToBox` → `BoxCompleted(color, slot)` → `BoxActivated(next, slot)` → `BufferToBox` для болтов цвета `next` слева направо, пока коробка не заполнится → если заполнилась, снова `BoxCompleted` → `BoxActivated` → … Цикл, а не рекурсия.
- **Лунки не сдвигаются** (как в Screw Master): болт остаётся в своей лунке, пока не уедет в коробку; освободившаяся лунка переиспользуется следующим болтом (самая левая свободная).
- **Исчерпание очереди.** Завершённая коробка, которой нечем заменить, оставляет слот пустым (`color = -1`, `filled = 0`), события `BoxActivated` нет — view понимает, что слот опустел, по `BoxCompleted` без последующего `BoxActivated` того же слота. Пустые слоты больше не заполняются. `AllBoxesDone` = очередь пуста и все слоты пусты.
- **Несколько коробок одного цвета** — болт идёт в самую левую неполную. При активации буферные болты тянутся только в активированную коробку; если их больше ёмкости, остаток остаётся в буфере и уедет в следующую коробку этого цвета при её активации (каскад).
- **AddBufferSlot** добавляет лунку в конец: `BufferSlotAdded(bufferSlot = индекс новой лунки)`. Перераспределения болтов нет — новая лунка пустая.
- Неиспользуемые поля событий = `-1` (`boltId` у событий коробок, `boxSlot` у `BoltToBuffer`, `bufferSlot` у `BoltToBox` и т.п.).
- **Валидация:** `boxQueue == null` → `ArgumentNullException`; `activeBoxes < 1`, `boxCapacity < 1`, `bufferSlots < 0` → `ArgumentOutOfRangeException`; отрицательный цвет в очереди или в `Add` → `ArgumentException` (`CanAccept(-1) == false`); `events == null` → `ArgumentNullException`; индекс слота/лунки вне диапазона → `ArgumentOutOfRangeException`.
- Горячий путь `Add` без аллокаций (кроме роста списка событий у вызывающего). `UpcomingColors` — живой read-only view, создаётся один раз.

## Покрыто тестами (28)

Старт (полная/короткая/пустая очередь, копия очереди, 0 лунок), выход за диапазон, маршрутизация в коробку/буфер, дописывание событий без очистки, переполнение буфера (исключение без побочных эффектов), null/отрицательные аргументы, завершение и активация в том же слоте, подтягивание из буфера слева направо без сдвига лунок, переиспользование освободившейся лунки, каскад «цвет → другой цвет», каскад двух коробок одного цвета при избытке болтов в буфере, каскад до исчерпания очереди, пустой слот после исчерпания, `AllBoxesDone`, несколько коробок одного цвета (левая первая), активация не трогает другие слоты, `AddBufferSlot` (из полного буфера, из нуля, повторно, новая лунка участвует в подтягивании), полный сценарий из 18 болтов с проверкой инвариантов после каждого хода.

## Предложения по Contracts

Нет обязательных. Опционально: явно задокументировать в xml-доке `CollectEventType.BoxCompleted`, что при исчерпании очереди за ним не следует `BoxActivated` (слот пустеет).
