# Templates

Шаблоны артефактов Woodberry. Копируй, заполняй, удаляй подсказки.

## Формат отчёта агента

Используй в конце любой задачи на реализацию. Структура — из `AGENTS.md`.

```
## Summary of changes
Что сделано, 1–3 предложения.

## Files/modules affected
- Assets/Scripts/Gameplay/Player/PlayerController.cs
- docs/specs/core-gameplay.md

## Verification performed
- `read_console(action: "get", types: ["error"])` — 0 ошибок
- `run_tests(mode: "EditMode")` → 12/12 passed

## Not verified
- Производительность на 4 игроках — Unity Editor не был запущен

## Known risks / tech debt / assumptions
- Допущение: подход «Shared Input» выбран без проверки в текущей версии
- Долг: интеграция сетевого слоя отложена на Stage 03

## Manual QA steps
1. Запустить сцену, нажать Play
2. Проверить ...

## Recommended next step
Stage 02 — ...
```

## Формат review

Источник истины — `docs/specs/code-review-flow.md`. Формат зафиксирован, чтобы
вывод одинаково читался человеком и агентом. Пустые секции остаются в выводе:
так читается разница между «проверил и не нашёл» и «не проверял».

```
## Code Review: <scope>

### Summary
<один абзац: что изменилось, какие слои затронуты, сигнал качества>

### Findings
#### 🔴 Critical      — обязательно исправить до merge
#### 🟠 Major         — желательно исправить до merge
#### 🟡 Minor         — исправить или зафиксировать как компромисс
#### 💡 Suggestions   — опционально

Каждая находка: - [ ] **`<file>`:**`<line>`** — <проблема и влияние>

### What looks good
- <что сделано правильно>

### Next steps
1. <первым идёт пункт высшей severity>
```

Severity-уровни и перечень Critical для Woodberry — в
`docs/specs/code-review-flow.md`. Чеклисты по слоям — в
`.opencode/skills/code-review-checklist/SKILL.md`.

## Формат bug-репорта

```
## Summary
Краткое описание.

## Steps to reproduce
1. ...
2. ...
3. ...

## Expected
Что должно быть.

## Actual
Что происходит.

## Environment
- Unity: 6000.6.3f1
- URP: 17.6.0
- Сцена:
- Другие игроки в сессии: да / нет
- Локальный или удалённый игрок:

## Logs
<вывод `read_console(action: "get", types: ["error"], include_stacktrace: true)`>

## Frequency
Всегда / иногда (сколько из N попыток)

## Notes
Что уже проверялось.
```

## Формат ADR

Из `docs/adr/README.md`.

```
# ADR NNNN — Название

## Status
Proposed | Accepted | Superseded by ADR-NNNN | Deprecated

## Date
YYYY-MM-DD

## Context
Какая проблема решаем. Факты, не мнения.

## Decision
Что решили.

## Consequences
### Positive
### Negative

## Alternatives considered
### Вариант A
Плюсы / минусы

## Revisit when
При каких условиях решение стоит пересмотреть.

## Links
Связанные spec'ы и stage'ы.
```

## Формат runbook

Из `docs/runbooks/README.md`.

```
# Runbook — Название

## Purpose
Для чего этот runbook.

## Prerequisites
| Что | Версия | Как проверить |

## Steps
1. Шаг — <что сделать>
   **Проверка:** <как убедиться, что шаг удался>

## Common issues
### Симптом
**Причина:** ...
**Решение:** ...

## What you must not do
- ❌ ...
```

## Формат stage file

Из `docs/stages/README.md`.

```
# Stage NN — Название

## Status
Не начат | В работе | Завершён

## Goal
Одна фраза: какая ВОЗМОЖНОСТЬ появляется.

## Business value
Почему это важно.

## Scope
- ...

## Out of scope
- ...

## Work
### Core
### Gameplay
### Net
### UI
### Art / Scene
### Editor / Infra
### Tests

## Acceptance criteria
- [ ] Проверяемое утверждение
- [ ] Проверяемое утверждение

## Required tests
- EditMode:
- PlayMode:
- Static/grep:

## Risks
| Риск | Митигация |

## Dependencies
- Stage NN

## Deliverables
- ...
```
