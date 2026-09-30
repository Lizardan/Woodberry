---
description: Создать stage report — что сделано, что проверено, что нет
agent: build
subtask: false
---

Создай stage report по завершённой работе.

Stage: $ARGUMENTS

Прочитай `docs/reports/README.md` и skill `stage-report` для формата.

Файл: `docs/reports/YYYY-MM-DD-stage-NN-short-name-report.md`
(используй реальную сегодняшнюю дату)

Структура:
- Stage, Objective
- Implemented (по слоям: Core / Gameplay / Net / UI / Art / Editor / Tests)
- Contract changes
- Files / modules affected
- **Verification performed** (Automated / Manual / **Not verified**)
- Acceptance criteria status (Done / Partial / Not done)
- Known issues, Tech debt, Assumptions, Risks
- Manual QA checklist
- Release notes
- Recommended next stage (с обоснованием)

**Главное правило:** раздел `Not verified` заполняется честно.
«Предположительно работает» — не верификация.
Каждый пункт acceptance criteria получает честный статус.
Не ставь `Done`, если не проверял.
