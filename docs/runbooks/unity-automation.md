# Runbook — Unity Automation через MCP

## Purpose

Работать с Unity из OpenCode/агента, не открывая редактор руками.

## Prerequisites

1. Unity Editor запущен.
2. MCP for Unity установлен в проекте, сервер запущен.
3. В `~/.config/opencode/opencode.json` (или проектном `opencode.json`) есть секция `mcp.unityMCP`.

**Проверка:** `mcp__unityMCP__read_console` отвечает без ошибки
`No Unity Editor instances found`.

## Золотой цикл

**Любая** работа со скриптами в Unity проходит через эти четыре шага:

```
1. Изменить файлы
2. refresh_unity(compile="request", wait_for_ready=true)
3. read_console(types=["error","warning"])
4. Только потом — продолжить работу
```

Шаг 3 — **обязательный**. Ошибка компиляции означает, что следующие вызовы
Unity-инструментов дадут мусор или ошибки. Нельзя продолжать работу при ошибках компиляции.

## Частые команды

### Проверить состояние редактора

```
mcp__unityMCP__read_resource(uri="mcpforunity://editor/state")
```

Ключевое поле: `data.compilation.is_compiling`, `data.advice.ready_for_tools`.

### Компиляция

```
mcp__unityMCP__refresh_unity(compile="request", wait_for_ready=true)
```

### Console

```
mcp__unityMCP__read_console(action="get", types=["error"], count="50")
mcp__unityMCP__read_console(action="get", types=["error","warning"], count="50")
mcp__unityMCP__read_console(action="clear")
```

`clear` — полезно **перед** новой проверкой, чтобы не принять старую ошибку за новую.

### Тесты

```
mcp__unityMCP__run_tests(mode="EditMode", include_failed_tests=true)
mcp__unityMCP__run_tests(mode="PlayMode", include_failed_tests=true)
```

Возвращает `job_id`. Дальше:

```
mcp__unityMCP__get_test_job(job_id="...", include_failed_tests=true, wait_timeout=60)
```

Для PlayMode первый запуск может занять 2 минуты из-за перезагрузки домена.
Указать `init_timeout: 120000`.

### Управление редактором

```
mcp__unityMCP__manage_editor(action="play")
mcp__unityMCP__manage_editor(action="stop")
```

### Сцены

```
mcp__unityMCP__manage_scene(action="get_hierarchy", page_size=50)
mcp__unityMCP__manage_scene(action="save")
```

`get_hierarchy` возвращает много данных. **Всегда** указывать `page_size`,
иначе можно получить нечитаемый ответ.

### Объекты и компоненты

```
mcp__unityMCP__manage_gameobject(action="create", name="Player", ...)
mcp__unityMCP__manage_components(action="add", target="Player", component_type="PlayerController")
mcp__unityMCP__find_gameobjects(search_method="by_component", search_term="PlayerController")
```

### Проверка API перед использованием

```
mcp__unityMCP__unity_reflect(action="get_type", class_name="NavMeshAgent")
mcp__unityMCP__unity_reflect(action="get_member", class_name="NavMeshAgent", member_name="speed")
mcp__unityMCP__unity_docs(action="get_doc", class_name="NavMeshAgent")
```

**Обязательно** перед использованием незнакомого Unity API. Память о Unity API ненадёжна:
версии меняются, API удаляются и переименовываются. Ошибка здесь = ошибка компиляции.

### Скриншоты

```
mcp__unityMCP__manage_camera(action="screenshot", include_image=true, max_resolution=800)
```

## Правила работы

| Правило | Почему |
|---|---|
| Не вызывать Unity-инструменты при ошибках компиляции | Результат будет мусорным или упадёт |
| Всегда `page_size` на больших запросах | Иначе ответ нечитаем |
| Очищать console перед проверкой | Иначе старые ошибки выглядят как новые |
| `read_console` перед заявлением «готово» | Единственный источник правды о компиляции |
| Проверять API через `unity_reflect` | Память о Unity API ненадёжна |
| Сцены/префабы — только через MCP, не YAML | Ручной YAML-редакт ломает ассеты |

## Если MCP недоступен

Симптом: `No Unity Editor instances found`

1. Unity Editor запущен? 
2. MCP server запущен в Unity (**Window → MCP for Unity**)?
3. Порт совпадает с конфигом?

**Если Unity не запущен** — это не блокер для написания кода, но блокер для верификации.
В отчёте честно пиши:

```
Not verified: Unity Editor не запущен, компиляция и тесты не выполнялись.
```

Выдумывать результат проверки нельзя. См. `instructions/safety.md`.
