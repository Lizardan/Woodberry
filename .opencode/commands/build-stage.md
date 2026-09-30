---
description: Реализовать согласованный план текущего этапа
agent: build
subtask: false
---

Реализуй согласованный план текущего stage.

План: $ARGUMENTS

Прочитай `AGENTS.md` и релевантный файл из `docs/stages/` перед началом.

Правила:
- **Оставайся в scope.** Не расширяй задачу.
- Минимальная жизнеспособная реализация
- Соблюдай границы слоёв из `AGENTS.md → Layer responsibilities`
- Обнови релевантные тесты
- Не заводи абстракции «на будущее»
- Не оставляй мёртвый код и закомментированные куски
- Не утверждай, что проверил то, что не проверил

Обязательный золотой цикл после изменений:
```
1. read_console(action: "clear")
2. refresh_unity(mode: "if_dirty", compile: "request", wait_for_ready: true)
3. read_console(action: "get", types: ["error", "warning"], count: 50)
4. run_tests(mode: "EditMode", include_failed_tests: true)
5. get_test_job(job_id: ..., include_failed_tests: true, wait_timeout: 60)
```
При ошибках компиляции работать нельзя. Не продолжай, пока не чисто.
При ошибках компиляции **остановись** и почини их. Не продолжай работу вслепую.

Затем прогони релевантные тесты.

Обязательный финальный вывод:
1. **Summary of changes** — что сделано, 1–3 предложения
2. **Files/modules affected** — конкретные пути
3. **Verification performed** — фактические команды и их результат
4. **Not verified** — что не удалось проверить и почему
5. **Known risks / tech debt / assumptions** — честно
6. **Manual QA steps** — что проверить руками
7. **Recommended next step** — логичный следующий шаг
