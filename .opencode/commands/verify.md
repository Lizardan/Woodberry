---
description: Верифицировать проект — компиляция, ошибки, EditMode/PlayMode тесты
agent: build
subtask: false
---

Проверь текущее состояние проекта и честно сообщи результат.

Scope: $ARGUMENTS

Выполни строго по порядку:

```
1. read_console(action: "clear")

2. refresh_unity(mode: "if_dirty", compile: "request", wait_for_ready: true)

3. read_console(action: "get", types: ["error", "warning"], count: 50)

4. ТОЛЬКО если ошибок нет:
   run_tests(mode: "EditMode", include_failed_tests: true)
   -> get_test_job(job_id: ..., include_failed_tests: true, wait_timeout: 60)
   -> дождись статуса succeeded/failed

5. ТОЛЬКО если ошибок нет:
   run_tests(mode: "PlayMode", include_failed_tests: true, init_timeout: 120000)
   -> get_test_job(job_id: ..., include_failed_tests: true, wait_timeout: 120)
```

`clear` идёт **до** проверки — иначе старые ошибки выглядят как новые.

Выведи таблицу:

| Шаг | Команда | Результат | Детали |
|-----|---------|-----------|--------|
| 1 | ... | passed / failed / not-run | ... |

**Критично:** если Unity Editor не запущен или MCP недоступен — НЕ выдумывай результат.
Напиши ровно:

```
Not verified: Unity Editor не запущен, компиляция и тесты не выполнялись.
```

и заверши. Отметка `not-run` в таблице честнее, чем выдуманный `passed`.
