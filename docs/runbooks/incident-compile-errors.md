# Runbook — Incident: Ошибки компиляции

## Purpose

Что делать, когда Unity не компилирует код.

## Золотое правило

**При ошибках компиляции работа останавливается.** Не вызывать Unity-инструменты,
не «дописать ещё немного и посмотреть». Сначала — компиляция чистая.

Причина: при ошибках компиляции типы не существуют в сборке, и любой вызов
Unity-инструмента вернёт мусор или ошибку. Отладка вслепую тратит время.

## Шаг 1. Получить список ошибок

```
read_console(action: "get", types: ["error", "warning"], count: 100, include_stacktrace: true)
```

Или в редакторе: **Window → General → Console**, фильтр **Errors**.

## Шаг 2. Классифицировать

| Тип ошибки | Пример | Что делать |
|---|---|---|
| Синтаксис | `; expected`, `} expected` | Проверить файл, номер строки в ошибке |
| Не найден тип | `The type or namespace name 'X' could not be found` | Проверить namespace, using, существование типа |
| Нет доступа | `'X' is inaccessible due to its protection level` | Проверить `internal`/`private`, assembly reference |
| Нет ссылки на сборку | ошибка про assembly reference | Проверить `.asmdef` → `references` |
| Конфликт типов | `The type 'X' exists in both 'A' и 'B'` | Дубликат определения или конфликт пакетов |
| API не существует | `'X' does not contain a definition for 'Y'` | **Проверить через `unity_reflect`** |
| Missing в сцене/префабе | `The referenced script on this Behaviour is missing` | См. ниже |

## Шаг 3. Частые причины в этом проекте

### `The type or namespace name 'Woodberry' could not be found`

**Причина:** скрипт вне namespace или вне asmdef.

**Решение:** проверить, что файл в `Assets/Scripts/` и namespace соответствует папке.

### Ошибка при ссылке на `UnityEditor` из runtime-кода

**Причина:** runtime-скрипт попал в Editor-сборку или наоборот.

**Решение:** если это Editor-код → он в `Assets/Scripts/Editor/`.
Если runtime-код случайно ссылается на `UnityEditor` → убрать ссылку или
обернуть в `#if UNITY_EDITOR`. Ср. `docs/adr/0001-layer-and-assembly-architecture.md`.

### `InputSystem_Actions` не найден

**Причина:** сгенерированный класс не создан.

**Решение:**
1. Открыть `Assets/InputSystem_Actions.inputactions` в инспекторе
2. **Generate C# Class** → проверить `Generated Wrappers Folder` и имя класса
3. Убедиться, что папка с обёртками попала под asmdef с правильными `references`
4. `refresh_unity(compile: "request")` + `read_console(action: "get", types: ["error"])`

### `Netcode` / сетевые типы не найдены

**Причина:** пакет не установлен. **Сейчас это ожидаемо** — сетевой пакет
не установлен, Stage 03 не начат.

**Решение:** не «добавить using и посмотреть». Либо убрать зависимость
(работать через `INetworkService` из `Core`), либо следовать ADR 0003 и установить пакет.

**Это ровно тот случай, который проектировала абстракция:** если сетевой код
просочился в `Gameplay/`, то выход из `Net/` в `Gameplay` — нарушение ADR 0001.

### `Missing (Mono Script)` в сцене или префабе

**Причина:** скрипт удалён/переименован, `.meta` потерян, или тип не компилируется.

**Шаги:**
1. Найти объект со ссылкой (Hierarchy или Prefab Mode)
2. Если скрипт переименован — переназначить компонент
3. Если скрипт удалён — удалить компонент
4. Если это следствие ошибки компиляции — **сначала починить компиляцию**

**Профилактика:** не удалять `.meta` вручную, не переименовывать `.meta` вручную,
не редактировать YAML.

## Шаг 4. Проверка сомнительного API

Если ошибка про отсутствующий метод/свойство — **не гадай, проверь**:

```
```
```
unity_reflect(action: "get_member", class_name: "<Namespace>.<ClassName>", member_name: "<MemberName>")
unity_reflect(action: "get_type", class_name: "<Namespace>.<ClassName>")
unity_reflect(action: "search", query: "TypeName")
```
# Ссылка на документацию под версию проекта
& "F:\Unity\Unity Hub\resources\unity.exe" docs <ClassName> --url
```

Версия Unity `6000.6.3f1` свежая. Многие API из памяти (устаревшей документации
или более старых версий) **не существуют или переименованы**. Это не редкость,
а норма.

## Шаг 5. Проверить результат починки

```
1. read_console(action: "clear")
2. refresh_unity(mode: "if_dirty", compile: "request", wait_for_ready: true)
3. read_console(action: "get", types: ["error", "warning"], count: 50)
4. run_tests(mode: "EditMode", include_failed_tests: true)
5. get_test_job(job_id: ..., include_failed_tests: true, wait_timeout: 60)
6. run_tests(mode: "PlayMode", include_failed_tests: true, init_timeout: 120000)
7. get_test_job(job_id: ..., include_failed_tests: true, wait_timeout: 120)
```

## Чего делать нельзя

- ❌ Продолжать работу при ошибках компиляции
- ❌ Комментировать код, чтобы «закомпилировалось», без понимания причины
- ❌ Удалять `.meta` для починки Missing
- ❌ Править YAML `.unity`/`.prefab` для починки Missing
- ❌ Доверять памяти об Unity API — проверяй через `unity_reflect(action: "get_member", ...)`
- ❌ Заявлять «готово» с ошибками в Console
