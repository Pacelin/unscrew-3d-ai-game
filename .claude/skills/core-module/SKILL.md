---
name: core-module
description: Rules and workflow for implementing one Unscrew.Core module (Board, Collect, Boosters, Economy, Generation) in pure C# with NUnit tests run via dotnet outside Unity. Use whenever writing or changing code under Assets/_Game/Scripts/Core or Assets/_Game/Tests/Core.
---

# Core module workflow

## Зона ответственности

Модуль `<M>` из {Board, Collect, Boosters, Economy, Generation} владеет **только**:
- `Assets/_Game/Scripts/Core/<M>/**`
- `Assets/_Game/Tests/Core/<M>/**`
- `Docs/Modules/<M>.md`

Только читать: `Scripts/Core/Contracts/**`, `Tests/Core/Shared/**`, `Docs/Plan.md`, `CLAUDE.md`.
Нельзя: править Contracts, чужие модули, `Assets/UnscrewFirst20`, сцены, `.csproj`, `CoreModule.props`.
Новый хелпер для `Tests/Core/Shared` — положи в свой `Tests/Core/<M>/` и предложи в отчёте.

## Ограничения языка (Unity 2022.3 = C# 9, noEngineReferences)

- Нет `UnityEngine`, `System.Text.Json`, `Newtonsoft`, `record`, `init`, default interface methods, `required`, file-scoped namespace, global using.
- Namespace: `Unscrew.Core` (публичные типы), тесты — `Unscrew.Core.Tests`.
- Случайность — только `Rng` из Contracts. Никакого `DateTime.Now` в логике (время передаётся параметром).
- Горячие пути (`Tap`, `Add`, `GetAccessibleBolts`) без лишних аллокаций: списки результатов передаются снаружи и не создаются внутри.
- Ошибки контракта → `InvalidOperationException` / `ArgumentException`, как описано в xml-доке интерфейса.
- `.meta`-файлы не создавай: Unity сгенерирует их сам после мержа.

## Цикл работы

1. Прочитай интерфейс в `Contracts` и раздел своего модуля в `Docs/Plan.md` §5.
2. Сначала тесты (NUnit 3: `[Test]`, `[TestCase]`, `Assert.AreEqual/IsTrue/Throws`). Не используй `Assert.That` с constraint-ами, которых нет в NUnit 3.5 (версия в Unity).
3. Реализация.
4. `bash Tools/CoreTests/test.sh <M>` — до зелёного. Покрой граничные случаи из xml-доки контракта.
5. Напиши `Docs/Modules/<M>.md`: публичный API, ключевые решения, что покрыто тестами, предложения по Contracts, если есть.
6. Коммиты в свою ветку (`core/<m>` в worktree): небольшие, conventional (`test(core/<m>): ...`, `feat(core/<m>): ...`) с трейлером `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`. `git add` только своих путей.

## Отчёт оркестратору (финальное сообщение)

- Ветка и список коммитов (`git log --oneline master..HEAD`).
- Вывод последнего `test.sh <M>` (строка итогов).
- Публичный API в 5–10 строк.
- Отклонения от плана и предложения по Contracts.
