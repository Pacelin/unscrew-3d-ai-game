# Unscrew 3D — тестовое задание (AI-first)

Screw-пазл на Unity 2022.3.62f2 + URP 14 по референсу Screw Master. Цель — APK, 20 уровней, бустеры, экономика, меню и магазин.
Полный план и спецификации модулей: `Docs/Plan.md`. Журнал работы с ИИ: `Docs/AI_LOG.md`.

## Структура

```
Assets/UnscrewFirst20/          ассеты из ТЗ — НЕ РЕДАКТИРОВАТЬ (только читать)
Assets/_Game/
  Scripts/Core/                 чистый C#, asmdef Unscrew.Core (noEngineReferences)
    Contracts/                  общие интерфейсы и данные — ЗАМОРОЖЕНЫ, меняет только оркестратор
    Board/ Collect/ Boosters/ Economy/ Generation/   по одному модулю на core-dev агента
  Scripts/Game/                 MonoBehaviour-слой (view, input, UI) — unity-integrator
  Scripts/Editor/               editor-инструменты (бейк уровней, генерация UI, билд)
  Tests/Core/<Module>/          NUnit-тесты модулей (asmdef Unscrew.Core.Tests)
  Tests/Core/Shared/            общие тестовые хелперы (TestLevels) — только дополнять
  Tests/Core/Integration/       сквозные тесты: генерация + сессия + бот на 20 уровнях
  Data/Levels/Level_XX.json     LevelDefinition с графом блокировок (бейк level-designer)
Tools/CoreTests/                dotnet-проекты для прогона Core-тестов без Unity
Docs/                           Plan.md, AI_LOG.md, Modules/<Module>.md
```

## Правила

- **Core не зависит от Unity.** Никаких `UnityEngine`, `System.Text.Json`, `init`, `record`, default interface methods. C# 9.
- **Модули Core зависят только от `Contracts`**, не друг от друга. Склейка — в Integration и в `Scripts/Game`.
- Данные-классы: публичные camelCase поля, `[Serializable]` (совместимость с JsonUtility).
- Случайность только через `Unscrew.Core.Rng` (детерминизм одинаковый в Unity и .NET).
- Код и комментарии на английском, документация на русском.
- Не трогать файлы вне своей зоны ответственности (см. агента). Нужна правка Contracts — опиши её в отчёте, не правь сам.

## Проверка (definition of done)

- Core-модуль: `bash Tools/CoreTests/test.sh <Module>` — зелёный. Перед мержем — `test.sh All`.
- Unity: скилл `unity-verify` (компиляция без ошибок → EditMode-тесты → при необходимости Play Mode + скриншот).
- Никогда не сообщать «готово» без вывода тестов.

## Параллельная работа

- Unity-редактор один → через UnityMCP работает **только один агент за раз** (level-designer или unity-integrator).
- core-dev агенты работают в **git worktree** на ветках `core/<module>`, Unity их не видит. Проверка — только dotnet.
- core-dev коммитит в свою ветку; в `master` мержит оркестратор (`git merge --no-ff`), затем гоняет `test.sh All` и Unity-тесты.

## Коммиты

Conventional commits: `feat(core/board): ...`, `test(...)`, `fix(...)`, `docs(...)`, `chore(...)`. Скоуп = модуль/папка.
Каждый коммит, сделанный агентом, заканчивается трейлером `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
Подробности журнала и коммитов — скилл `ai-journal`.

## Скиллы и агенты

Скиллы: `.claude/skills/` — `core-module`, `unity-verify`, `level-data`, `android-build`, `ai-journal` (+ установленный `unity-mcp-skill`).
Агенты: `.claude/agents/` — `core-dev`, `level-designer`, `unity-integrator`, `qa-tester`.
