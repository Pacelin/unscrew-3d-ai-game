---
name: bolt-seat
description: On-demand audit of bolt seating on all 20 levels — the head must rest on the owner surface (no mesh intersection) and the thread must be maximally inside the owner. Run when asked to check bolt placement / floating or buried bolts.
---

# Bolt seating check (посадка болтов)

Проверяет ПОЗИЦИИ болтов относительно детали-владельца по финальным данным `Assets/_Game/Data/Levels/Level_XX.json`
(исходные позиции из `Assets/UnscrewFirst20/Data` + запечённый `headLift`). Read-only, ничего не перезаписывает.

## Что проверяется (head и резьба отдельно)

1. **Шляпка не пересекается с мешем владельца** — 19 точек шляпки, тест чётности лучей (правило 1).
2. **Резьба утоплена по максимуму** — стержень (seat→tip) внутри владельца: середина вала в меши ИЛИ
   сегмент seat→tip пересекает поверхность; плюс «остаточный потенциал закручивания» ≤ 0.05
   (можно ли закрутить глубже, не задев шляпку). Потенциал > 0.05 = «болт висит в воздухе».
3. Нарушения печатаются списком `b<bolt> (владелец N, headLift X): ...` по уровням.

## Как запустить

В редакторе Unity (меню):

- **Unscrew → Check Bolt Seating** — отчёт по всем 20 уровням в консоль.

Через MCP (`execute_code`):

```csharp
// сводка по уровням
var sb = new System.Text.StringBuilder();
int tB=0,tE=0,tU=0,tL=0;
for (int n = 1; n <= 20; n++) {
    string rep = Unscrew.Editor.Levels.LevelValidator.CheckSeating(n, 0.05f, out int b, out int e, out int u, out int lo);
    tB+=b; tE+=e; tU+=u; tL+=lo;
    sb.AppendLine($"L{n} bolts={b} embedded={e} unanchored={u} loose={lo}");
    if (rep.Length > 0) sb.Append(rep); // детальный список нарушений уровня
}
sb.AppendLine($"TOTAL bolts={tB} embedded={tE} unanchored={tU} loose={tL}");
return sb.ToString();
```

## Интерпретация

- **embedded** (шляпка в меши) — ошибка бейка: должно быть 0; лечится перебейком
  (`Unscrew → Validate And Rebake Levels` или `LevelBaker.BakeAllResults(true)`).
- **loose** («можно закрутить ещё на X») — болт висит в воздухе; ComputeSeat должен был закрутить его
  при бейке. > 0 → проверить `LevelValidator.ComputeSeat`.
- **unanchored** (резьба не во владельце) — типовые причины:
  1. минимальный лифт видимости (болт внутри чашки-владельца: без выкручивения не тапается) — задокументирован
     в отчёте бейка (`visibilityLifts`), осознанный компромисс;
  2. толстый бобышка-владелец + короткий вал (геометрия исходников) — одновременно «шляпка снаружи» и
     «резьба внутри» невозможны; требует правки исходных ассетов, код не лечит.

## Связанное

- Посадка считается при бейке: `LevelValidator.ComputeSeat` (по умолчанию `ApplyHeadLift = true`).
- Видимость/тапаемость — отдельное правило 3 (`Docs/Modules/LevelBake.md` §«Правило 3»).
