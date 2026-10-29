---
name: ai-journal
description: How to record AI-assisted work for the test-task evaluation - commit conventions, Docs/AI_LOG.md entries (prompts, who did what, how it was verified), screenshots. Use when committing, merging agent branches, finishing a stage, or writing the README section about the AI process.
---

# AI journal

Главный критерий ТЗ — **процесс работы с ИИ**. Всё, что сделано, должно оставлять след: в коммите, в журнале или скриншотом.

## Коммиты
- Conventional commits, скоуп = модуль: `feat(core/collect): cascade buffer-to-box moves`.
- Тело коммита: *что* и *как проверено* (`dotnet: 14/14`, `Unity EditMode 30/30`, `screenshot Docs/Screens/...`).
- Агентские коммиты заканчиваются трейлером `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`. Ручные коммиты человека идут без трейлера, так и видно, что сделано руками.
- Ветки агентов мержатся `--no-ff` с сообщением `merge(core/<m>): <итог>`, чтобы параллельность была видна в графе.
- Коммит только по своим путям, без `git add -A` в параллельной работе.

## Docs/AI_LOG.md (пишет только оркестратор, чтобы не было гонок)
Одна запись на этап:
```
## <YYYY-MM-DD> — <этап>
**Человек:** что сделал руками.
**Промпт:** > цитата ключевого промпта (дословно).
**Агенты:** кто (core-dev/Board, ...), параллельно или нет, сколько заняло.
**Решения:** 2–4 пункта: почему так.
**Проблемы:** что ИИ сделал не так и как это поймали (тест, бот, скриншот, ревью).
**Проверка:** команды и цифры.
```
Саб-агенты журнал не правят: они возвращают отчёт, оркестратор переносит главное.

## Скриншоты
`Docs/Screens/<этап>_<описание>.png` — кладутся в репозиторий и потом используются в README.
