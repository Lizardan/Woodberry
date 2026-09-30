---
name: debug-unity-console
description: Диагностика ошибок компиляции, runtime-ошибок и Missing-скриптов в Unity через MCP. Использовать, когда Console красный, тесты падают или сборка не собирается.
compatibility: opencode
metadata:
  audience: qa-analyst, unity-tools-engineer, gameplay-engineer
  domain: debugging
---

# Debug Unity Console

## Триггер

Используй, когда:
- Console показывает ошибки
- `refresh_unity` не даёт чистой компиляции
- тесты падают
- `Missing (Mono Script)` в сцене или префабе
- MCP возвращает `No Unity Editor instances found`

## Правило 0: не отлаживай вслепую

**При ошибках компиляции работа останавливается.** Не вызывай Unity-инструменты,
пока компиляция не станет чистой: типы не существуют в сборке, результат будет мусором.

## Шаг 1. Получить ошибки

read_console(action: "get", types: ["error", "warning"], count: 100, include_stacktrace: true)

Или в редакторе: **Window → General → Console**, фильтр Errors.

## Шаг 2. Классифицировать

| Тип | Пример | Раздел |
|---|---|---|
| Синтаксис | `; expected` | §3 |
| Не найден тип | `type or namespace 'X' could not be found` | §4 |
| Нет доступа | `inaccessible due to its protection level` | §4 |
| Нет assembly reference | ошибка про сборку | §5 |
| Не существует API | `'X' does not contain a definition for 'Y'` | §6 |
| Missing Script | `The referenced script is missing` | §7 |
| Runtime NRE | `NullReferenceException` | §8 |
| Тесты падают | assertion | §9 |
| MCP недоступен | `No Unity Editor instances found` | §10 |

## Шаг 3. Синтаксис

Номер строки в ошибке точный. Открыть файл, посмотреть.

Частые причины:
- Пропущенная `;` или `}`
- Незакрытый блок после массового редактирования
- Непарные скобки при добавлении метода

## Шаг 4. Не найден тип / нет доступа

### Проверь namespace

```csharp
// Файл в Assets/Scripts/Gameplay/Player/PlayerController.cs
namespace Woodberry.Gameplay.Player   // ✅ соответствует папке

namespace Woodberry.Gameplay           // ❌ не соответствует
namespace Gameplay.Player             // ❌ нет префикса Woodberry
```

### Проверь assembly reference

Если тип в другой сборке, а ссылка не объявлена в `.asmdef` → `references`.

manage_asset(action: "get_info", path: "Assets/Scripts/.../X.asmdef")

### Проверь, что тип вообще есть

```
unity_reflect(action: "search", query: "TypeName")
unity_reflect(action: "get_type", class_name: "Namespace.TypeName")
```

## Шаг 5. Конфликт сборок

```
The type 'X' exists in both 'Assembly-A' and 'Assembly-B'
```

| Причина | Решение |
|---|---|
| Тип в пакете и в проекте | Переименовать свой или отключить пакет |
| Файл вне asmdef попал в две сборки | Проверить границы asmdef |
| Старая сборка не пересобралась | `refresh_unity(compile: "request", wait_for_ready: true)` |

## Шаг 6. Несуществующий API — **не гадай, проверь**

Это самая частая ошибка при работе с Unity через AI. Версия `6000.6.3f1` свежая,
память о Unity API часто устаревшая.

```
unity_reflect(action: "get_member", class_name: "UnityEngine.AI.NavMeshAgent", member_name: "speed")
unity_reflect(action: "get_type",   class_name: "UnityEngine.AI.NavMeshAgent")
unity_reflect(action: "search",     query: "NavMeshAgent")
unity_docs(action: "get_doc", class_name: "CharacterController")
```
## Шаг 7. Missing (Mono Script)

### Сначала проверить компиляцию

Это **самая частая** причина: тип не существует → компонент показывает Missing.

read_console(action: "get", types: ["error"], count: 50)

### Если компиляция чистая

1. Найти объект (Hierarchy или Prefab Mode)
2. В инспекторе поле `Script` пустое
3. Переназначить перетаскиванием
4. Если скрипт удалён намеренно — удалить компонент
5. `File → Save`

### Чего НЕ делать

- ❌ Удалять `.meta` вручную
- ❌ Править YAML `.unity`/`.prefab`
- ❌ Переименовывать `.meta`

## Шаг 8. NullReferenceException

| Причина | Как найти |
|---|---|
| Не инициализирован в `Awake` | Проверить порядок `Awake` → `Start` |
| `GetComponent` вернул null | Проверить наличие компонента |
| Разыменование уничтоженного объекта | Проверить `!= null` (Unity overload!) |
| Событие без подписки | Проверить порядок `OnEnable` |
| Порядок между объектами | Не полагаться на порядок `Start` |

**Важно:** для Unity-объектов проверяй `if (obj != null)`, а не `if (obj is not null)`.
Unity переопределяет оператор `==` для уничтоженных объектов.

## Шаг 9. Падающие тесты

См. `docs/runbooks/test-workflow.md`. Быстрая диагностика:

| Симптом | Причина |
|---|---|
| Падает в «Run All», проходит отдельно | Общее состояние |
| Flaky | `WaitForSeconds`, время, случайность |
| Зелёный, но в логе ошибки | Не проверяется лог |
| EditMode зелёный, PlayMode красный | Нужен PlayMode для жизненного цикла |

## Шаг 9. Падающие тесты

См. `docs/runbooks/test-workflow.md`.

Запуск через MCP:
```
run_tests(mode: "EditMode", include_failed_tests: true)
-> get_test_job(job_id: ..., include_failed_tests: true, wait_timeout: 60)
```

## Шаг 10. MCP недоступен

Симптом: `No Unity Editor instances found`

1. Unity Editor запущен?
2. Окно Unity MCP открыто?
3. Порт совпадает с конфигом?
4. Проверить ресурс `mcpforunity://instances`; при нескольких инстансах —
   вызвать `set_active_instance` с точным `Name@hash`.

Если Unity не запущен — это **не блокер** для написания кода, но **блокер**
для верификации. Пиши честно:
```
Not verified: Unity Editor не запущен, компиляция и тесты не выполнены.
```

## Золотой цикл проверки

```
1. read_console(action: "clear")
2. refresh_unity(mode: "if_dirty", compile: "request", wait_for_ready: true)
3. read_console(action: "get", types: ["error", "warning"], count: 50)
```

## Критерии успеха

- [ ] Причина найдена, а не обойдена
- [ ] Ошибок в Console: 0
- [ ] Тесты проходят (EditMode + PlayMode)
- [ ] Проверено, что проблема не вернулась (регрессия)
- [ ] Указано, что проверено, а что — нет
- [ ] Ничего не «подавлено» ради зелёного результата

Подробнее: `docs/runbooks/incident-compile-errors.md`
