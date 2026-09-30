---
description: Верифицировать проект — компиляция, ошибки, EditMode/PlayMode тесты
agent: build
subtask: false
---

Проверь текущее состояние проекта и честно сообщи результат.

Scope: $ARGUMENTS

Выполни строго по порядку:

```
1. mcp__unityMCP__read_console(action="clear")
2. mcp__unityMCP__refresh_unity(compile="request", wait_for_ready=true)
3. mcp__unityMCP__read_console(action="get", types=["error","warning"], count="50")
4. mcp__unityMCP__run_tests(mode="EditMode", include_failed_tests=true)
5. mcp__unityMCP__get_test_job(job_id="<из шага 4>", include_failed_tests=true, wait_timeout=60)
6. mcp__unityMCP__run_tests(mode="PlayMode", include_failed_tests=true, init_timeout=120000)
7. mcp__unityMCP__get_test_job(job_id="<из шага 6>", include_failed_tests=true, wait_timeout=120)
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
