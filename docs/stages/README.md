# Stages

Здесь лежат **stage definitions** — рамки этапов.

## Что такое stage

Stage — это единица поставки, а не единица кода. Stage:
- даёт **демонстрируемую ценность** (пользовательская или системная возможность)
- тестируем
- ревьюим
- имеет чёткие acceptance criteria
- имеет явные out-of-scope границы

**Плохой stage goal:** «Реализовать систему инвентаря и ScriptableObject-ы» — это слой кода.
**Хороший stage goal:** «Игрок может подобрать фонарь, и его свет влияет на видимость у всех игроков в сессии».

## Формат файла stage

```
# Stage NN — Название

## Goal              # одна фраза: какая возможность появляется
## Business value    # почему это важно
## Scope             # что входит
## Out of scope      # что НЕ входит (явно!)
## Work              # по слоям: Core / Gameplay / Net / UI / Art / Infra
## Acceptance criteria  # проверяемый список
## Required tests    # unit / integration / playmode
## Risks
## Dependencies
## Deliverables      # code, tests, report, QA checklist
```

## Workflow

1. Создать или обновить stage file.
2. Отдать stage агенту в режиме `plan` → получить implementation plan.
3. Согласовать plan и уменьшить scope.
4. Отдать в `build` → реализовать строго по file.
5. `verify` → прогнать тесты и `console(level="error")`.
6. `review` → отдельный проход, найти drift.
7. Создать report в `docs/reports/`.
8. Перейти к следующему stage.

Цепочка: **Stage Definition → Plan → Build → Verify → Review → Report → Next Stage.**

## Список stages

| Файл | Stage | Статус |
|---|---|---|
| `stage-00-foundation.md` | Foundation: asmdef, папки, слои, сборка | Готов к работе |
| `stage-01-movement-and-camera.md` | Игрок ходит, камера следует сверху | Не начат |
| `stage-02-light-and-visibility.md` | Источник света и ограниченная видимость | Не начат |
| `stage-03-coop-baseline.md` | Два игрока видят друг друга, сетевой seam введён | Не начат |

## Правило одного stage

**Не работать над несколькими stages одновременно.** Это прямой запрет из `AGENTS.md`.
Причина: частичная реализация нескольких slices даёт систему, которую невозможно ни
проверить, ни показать, ни откатить.

## Дисциплина вертикальных срезов

В пределах одного stage держи вертикальный срез: код + ассеты + сцена + тесты.
Не откладывай «потом подключу визуал» — срез без визуала невозможно оценить.
