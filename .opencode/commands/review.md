---
description: Строгий read-only review изменений — correctness, edge cases, architecture drift, contract drift
agent: code-reviewer
subtask: false
---

@code-reviewer сделай code review.

Scope: $ARGUMENTS

Если scope не задан — review текущих незакоммиченных изменений против `HEAD`
(и staged, если есть).

Следуй процессу из `docs/specs/code-review-flow.md`:

1. **Context** — прочитай `AGENTS.md` и релевантные спеки из `docs/specs/`
   **до** чтения диффа
2. **Inspect** — примени скилл `git-diff-analysis`
3. **Read** — прочитай изменённые файлы и их соседей, а также `.asmdef`
   затронутой сборки
4. **Checklists** — примени `code-review-checklist` для каждого затронутого слоя
5. **Cross-cutting** — секреты, выравнивание документации, честность верификации
6. **Report** — recommendation plan по формату из спеки

Не изменяй файлы. Только отчёт.
Формат: Summary → 🔴 Critical → 🟠 Major → 🟡 Minor → 💡 Suggestions →
What looks good → Next steps.
