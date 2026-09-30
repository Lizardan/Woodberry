---
description: Расследовать баг или ошибку компиляции без широких изменений
agent: build
subtask: false
---

Исследуй проблему, не делая широких несвязанных изменений.

Проблема: $ARGUMENTS

Прочитай skill `debug-unity-console` перед началом.

Задача:
1. Получить ошибки:
   ```
   mcp__unityMCP__read_console(action="get", types=["error","warning"], count="100", include_stacktrace=true)
   ```
2. Классифицировать: синтаксис / не найден тип / нет доступа / assembly reference /
   несуществующий API / Missing Script / runtime / тесты
3. Определить вероятную причину
4. Если ошибка про отсутствующий Unity API — **проверить, не гадать**:
   ```
   mcp__unityMCP__unity_reflect(action="search", query="<Имя>")
   mcp__unityMCP__unity_reflect(action="get_type", class_name="<Класс>")
   mcp__unityMCP__unity_reflect(action="get_member", class_name="<Класс>", member_name="<Член>")
   mcp__unityMCP__unity_docs(action="get_doc", class_name="<Класс>")
   ```
5. Предложить минимальное безопасное исправление
6. Перечислить регрессионные риски
7. Определить шаги верификации

Ограничения:
- Предпочитай минимальное безопасное исправление
- Не переписывай несвязанные модули
- Не комментируй код, не выяснив причину
- Не удаляй `.meta` вручную
- Не правь YAML Unity-ассетов

**Не выдумывай** результат проверки. Если Unity Editor не запущен — так и напиши.
