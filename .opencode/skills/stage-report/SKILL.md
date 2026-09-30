---
name: stage-report
description: Пишет stage report по завершении этапа — что сделано, что проверено, что нет. Использовать перед переходом к следующему этапу, чтобы зафиксировать результат и не потерять контекст.
compatibility: opencode
metadata:
  audience: system-analyst, qa-analyst
  domain: delivery
---

# Stage Report

## Триггер

Используй, когда:
- этап реализован
- тесты прогнаны
- review проведён
- переходишь к следующему этапу

## Зачем

Report делает прогресс **видимым** и отдаёт контекст следующему агенту без потери знаний.
Отчёт, написанный по памяти через неделю, врёт.

## Шаблон

```markdown
# Stage Report — Stage NN <Название>

## Stage
NN <Название>, ссылка на `docs/stages/stage-NN-*.md`

## Objective
Что этап должен был дать.

## Implemented
### Core
- изменение
### Gameplay
- изменение
### Net
- изменение / «не затрагивался»
### UI
- изменение / «не затрагивался»
### Art / Scene
- изменение / «не затрагивался»
### Editor / Infra
- изменение

## Contract changes
Что изменилось в контрактах (API, схемы, сетевые правила).
Если ничего — «нет».

## Files / modules affected
- Assets/Scripts/...
- Assets/Woodberry/...
- docs/...

## Verification performed
### Automated checks run
- `read_console(action: "get", types: ["error"])` — 0 ошибок
- `run_tests(mode: "EditMode")` — 12/12 passed
- `<команда>` — `<фактический результат>`

### Manual checks performed
- Пройден уровень в темноте, партнёр виден
- Камера не дёргается при стоянии

### Not verified
- **Что** и **почему**
- Например: « производительность на 4 игроках — Unity Editor не был запущен »

## Acceptance criteria status
- [x] Игрок передвигается по XZ — Done
- [x] `read_console(action: "get", types: ["error"])` чист — Done
- [ ] Проверено на 4 игроках — Not done (см. Not verified)

## Known issues
- Что работает неправильно

## Tech debt
- Что оставлено «на потом» сознательно

## Assumptions
- Что предполагалось, если не уточнено

## Risks
- Что может сломаться позже

## Manual QA checklist
- [ ] шаг для проверки вручную
- [ ] шаг

## Release notes
### Added
### Changed
### Fixed
### Deferred

## Recommended next stage
Какой и почему.
```

## Главное правило

**`Verification performed` и `Not verified` — это разные разделы, и второй
нельзя заполнять словами «всё ок».**

| Формулировка | Допустимо? |
|---|---|
| «Проверено, работает» | ❌ Нет команды и результата |
| «Предположительно работает» | ❌ Это не верификация |
| «Не сломалось при запуске» | ❌ Слабее, чем нужно |
| «Unity Editor не запущен, тесты не выполнялись» | ✅ |
| «EditMode: 12/12 passed, PlayMode: 4/4 passed» | ✅ |
| «`read_console(action: "get", types: ["error"])` — 0 ошибок» | ✅ |

Формулировка «Предположительно всё ок» не заменяет ни тесты, ни сборку.

## Если ничего не проверено

Так и напиши. Это ценная информация: следующий агент будет знать,
где искать проблему.

## Acceptance criteria status

Статусы:

| Статус | Значение |
|---|---|
| `Done` | Выполнено и проверено |
| `Partial` | Частично, есть что не сделано |
| `Not done` | Не выполнено |

**Не ставь `Done`, если не проверял.** Критерий без проверки = `Partial` минимум.

## Ручная проверка в Unity

Обязательные пункты, если этап затрагивает сцены/визуал:

```powershell
# Сцена без Missing-скриптов
# Префабы — источник правды, дубликатов нет
```
# Сцена без Missing-скриптов
read_console(action: "get", types: ["error"], count: 50)
# Префабы и сущности на месте
manage_scene(action: "get_hierarchy", page_size: 100, max_depth: 3)
# .meta на месте
git status --short -- "*.meta"
# Скрины для отчёта
manage_camera(action: "screenshot", capture_source: "game_view")
```
```

⚠️ Скриншот с указанием камеры **не включает** `Screen Space - Overlay` UI.
Для UI-скриншота — без параметра `camera`.

## Именование

```
docs/reports/YYYY-MM-DD-stage-NN-short-name-report.md
```

Пример: `docs/reports/2026-09-30-stage-00-foundation-report.md`

## Recommended next stage

Не «следующий по списку», а **логичный**:

```markdown
## Recommended next stage
Stage 02 — Light and Visibility, потому что видимость — ядро хоррора
и без неё невозможно оценить ни атмосферу, ни кооператив.
```

Обоснование обязательно: агент без контекста не поймёт, почему именно это.

## Критерии успеха

- [ ] Все обязательные разделы заполнены
- [ ] `Not verified` — не пустой, если что-то не проверено
- [ ] Каждый `Acceptance criteria` имеет честный статус
- [ ] Фактические команды и результаты, не описания
- [ ] Изменённые контракты отражены в specs
- [ ] Файл создан в `docs/reports/` с правильным именем
- [ ] `Recommended next stage` с обоснованием

Подробнее: `docs/reports/README.md`
