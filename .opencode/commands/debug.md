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
   read_console(action: "get", types: ["error", "warning"], count: 100, include_stacktrace: true)
   ```
2. Классифицировать по таблице в skill `debug-unity-console` §2
3. Если ошибка про отсутствующий Unity API — **не гадать, проверить**:
   ```
   unity_reflect(action: "get_member", class_name: "UnityEngine.AI.NavMeshAgent", member_name: "speed")
   unity_reflect(action: "get_type",   class_name: "UnityEngine.AI.NavMeshAgent")
   unity_reflect(action: "search",     query: "NavMeshAgent")
   unity_docs(action: "get_doc", class_name: "CharacterController")
   ```
   `found: false` — это ответ «члена нет», а не ошибка вызова.
4. Предложить минимальное безопасное исправление
5. Перечислить регрессионные риски
6. Определить шаги верификации
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
