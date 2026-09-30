# Runbook — Unity MCP

## Статус: МОСТ РАБОТАЕТ

Unity MCP установлен и проверен фактическими вызовами.

| Параметр | Значение |
|---|---|
| Пакет | `com.coplaydev.unity-mcp` (`CoplayDev/unity-mcp`, MIT) |
| Резолв в lockfile | `30d22075093d1d35dfb0091c1c7550e9ad948577` |
| Python-сервер | `mcpforunityserver==10.2.0`, транспорт `stdio` |
| Конфиг | `opencode.json` → секция `mcp.unityMCP` (в репозитории) |
| Инстанс | `Woodberry@93447b6f`, порт `6400` |
| Unity | `6000.6.3f1` |
| Активная сцена | `Assets/Scenes/Bootstrap.unity` |

---

## Именование инструментов

MCP отдаёт инструменты без префикса (`read_console`). Клиент OpenCode добавляет
префикс имени сервера, поэтому в вызовах агент использует:

| Каноническое имя | Как вызывает агент |
|---|---|
| `read_console` | `unityMCP_read_console` |
| `run_tests` | `unityMCP_run_tests` |
| `unity_reflect` | `unityMCP_unity_reflect` |

В документации ниже канонические имена — они короче и не зависят от префикса.

**Инструментов 47.** Унифицированные инструменты принимают параметр `action`;
остальные — плоские параметры.

---

## Проверенные инструменты

Протестированы фактическим вызовом. Ответы — реальные.

### `read_console` — ошибки и логи

```json
{ "action": "get", "types": ["error"], "count": 20 }
{ "action": "get", "types": ["all"], "count": 50, "include_stacktrace": true }
{ "action": "clear" }
```

`types`: `error` | `warning` | `log` | `all`. `action`: `get` | `clear`.
Проверено: `types:["error"]` → `Retrieved 0 log entries`.

### `refresh_unity` — компиляция

```json
{ "mode": "if_dirty", "scope": "all", "compile": "request", "wait_for_ready": true }
```

`mode`: `if_dirty` | `force`. `scope`: `assets` | `scripts` | `all`.
`compile`: `none` | `request`. Проверено: `resulting_state: "idle"`.

> **Важно:** инструмента `recompile` **не существует**. Он есть в старой
> документации и в памяти о других сборках сервера. Использовать
> `refresh_unity` с `compile: "request"`.

### `run_tests` и `get_test_job` — тесты

`run_tests` возвращает `job_id`, опрос идёт через `get_test_job`:

```json
{ "mode": "EditMode", "include_failed_tests": true }
{ "job_id": "<id>", "include_failed_tests": true, "wait_timeout": 60 }
```

`mode`: `EditMode` | `PlayMode`. Фильтры: `test_names`, `group_names`,
`category_names`, `assembly_names`. Есть `init_timeout` (PlayMode после
domain reload — ставь 120000) и `clear_stuck`.

Проверено: EditMode job → `status: succeeded`, `resultState: "Passed"`,
`total: 0` (тест в проекте ещё нет — это не ошибка).

**`run_tests` асинхронный.** Вызов без `get_test_job` не является
верификацией. Правило: получил `job_id` → дождался `succeeded`/`failed` →
только потом пишешь результат.

### `unity_reflect` — проверка API

```json
{ "action": "get_member", "class_name": "UnityEngine.AI.NavMeshAgent", "member_name": "speed" }
{ "action": "get_type", "class_name": "CharacterController" }
{ "action": "search", "query": "NavMeshSurface" }
```

`action`: `get_type` | `get_member` | `search`.
`scope`: `unity` | `packages` | `project` | `all`.

Проверено: `NavMeshAgent.speed` → `property_type: "float"`, `can_write: true`,
`is_obsolete: false`.

Это **основной способ не полагаться на память об Unity API**.

### `unity_docs` — документация

```json
{ "action": "get_doc", "class_name": "CharacterController" }
{ "action": "get_doc", "class_name": "NavMeshAgent", "member_name": "SetDestination" }
{ "action": "get_manual", "slug": "execution-order" }
{ "action": "lookup", "queries": "Physics.Raycast,NavMeshAgent" }
{ "action": "get_package_doc", "package": "com.unity.render-pipelines.universal", "page": "index", "pkg_version": "17.0" }
```

Проверено: `get_doc CharacterController` → описание + URL
`https://docs.unity3d.com/ScriptReference/CharacterController.html`.

Версия подставляется автоматически из проекта.

### `manage_build` — сборка

```json
{ "action": "scenes" }
{ "action": "status" }
{ "action": "build", "target": "windows64", "output_path": "Build/Woodberry.exe" }
```

`action`: `build` | `status` | `platform` | `settings` | `scenes` | `profiles` |
`batch` | `cancel`. `target`: `windows64` | `osx` | `linux64` | `android` |
`ios` | `webgl` | `uwp` | `tvos` | `visionos`. `subtarget`: `player` | `server`.

Проверено: `scenes` → одна сцена `Assets/Scenes/Bootstrap.unity`, `enabled: true`.
`status` без запущенной сборки → `No build jobs found.` (это нормально, не ошибка).

> Полную сборку (`action: "build"`) запускай только когда это явно нужно
> задачей. Это долго и создаёт артефакты.

### `manage_editor` — состояние редактора

```json
{ "action": "telemetry_status" }
{ "action": "play" }
{ "action": "stop" }
{ "action": "add_tag", "tag_name": "Enemy" }
{ "action": "add_layer", "layer_name": "Interactable" }
{ "action": "undo" }
```

Проверено: `telemetry_status` → `telemetry_enabled: true`.

### `manage_scene` — сцены и иерархия

```json
{ "action": "get_hierarchy", "page_size": 50, "max_depth": 2 }
{ "action": "get_active" }
{ "action": "load", "path": "Assets/Scenes/Bootstrap.unity" }
{ "action": "save" }
{ "action": "get_build_settings" }
```

Проверено: `get_hierarchy` → 3 корневых объекта (`Main Camera`,
`Directional Light`, `Global Volume`), `next_cursor: null`.

**Всегда передавай `page_size`.** Без него ответ может быть огромным.

### `manage_tools` — какие группы активны

```json
{ "action": "list_groups" }
{ "action": "activate", "group": "profiling" }
{ "action": "sync" }
```

| Группа | Инструменты | По умолчанию |
|---|---|---|
| `core` | 25 — сцены, скрипты, ассеты, редактор, сборка | **включена** |
| `testing` | `run_tests`, `get_test_job` | выкл |
| `docs` | `unity_reflect`, `unity_docs` | выкл |
| `scripting_ext` | `execute_code`, `manage_scriptable_object` | выкл |
| `profiling` | `manage_profiler` | выкл |
| `animation` | `manage_animation` | выкл |
| `ui` | `manage_ui` | выкл |
| `vfx` | `manage_shader`, `manage_texture`, `manage_vfx` | выкл |
| `probuilder` | `manage_probuilder` | выкл |
| `asset_gen` | `generate_*`, `import_model*` | выкл |

> **Наблюдение:** `list_groups` отдаёт `enabled: false` для не-core групп, но
> инструменты из них при этом вызываются успешно (проверено на `run_tests`,
> `unity_reflect`, `unity_docs`, `manage_editor`, `manage_scene`). Клиент
> получает список инструментов при подключении, и переключатель в Editor'е на
> уже подключённую сессию не влияет. Если после рестарта OpenCode инструменты
> пропадут — включи группу через `manage_tools(action: "activate")`.

---

## Доступные, но не проверенные вызовом инструменты

Присутствуют в `tools/list`, параметры взяты из схемы. **Не проверены фактическим
вызовом** — при первом использовании сверяйся со схемой.

`manage_asset`, `manage_gameobject`, `manage_components`, `manage_prefabs`,
`manage_scriptable_object`, `manage_material`, `manage_physics`,
`manage_graphics`, `manage_animation`, `manage_probuilder`, `manage_ui`,
`manage_vfx`, `manage_profiler`, `manage_texture`, `manage_camera`,
`manage_packages`, `manage_shader`, `create_script`, `delete_script`,
`manage_script`, `script_apply_edits`, `apply_text_edits`, `validate_script`,
`get_sha`, `manage_script_capabilities`, `batch_execute`, `execute_code`,
`execute_menu_item`, `find_gameobjects`, `find_in_file`, `generate_audio`,
`generate_image`, `generate_model`, `import_model`, `import_model_file`,
`set_active_instance`, `debug_request_context`.

`batch_execute` — лимит **25 команд за вызов**
(`editor/state → settings.batch_execute_max_commands`).

---

## Ресурсы MCP

Читать состояние, а не вызывать инструменты:

| URI | Что даёт |
|---|---|
| `mcpforunity://instances` | Список Editor'ов, порт, версия Unity |
| `mcpforunity://editor/state` | Фаза компиляции, play mode, активная сцена, `advice.ready_for_tools` |

Поля обёрнуты в `data.*`, читать именно их.

```json
data.advice.ready_for_tools      // true — можно работать
data.advice.blocking_reasons     // [] — блокировок нет
data.compilation.is_compiling    // ждать false
data.editor.active_scene.path    // текущая сцена
```

### Маршрутизация к инстансу

Инстанс в проекте один. Если появится второй — сервер вернёт ошибку, и тогда
нужно вызвать `set_active_instance` с точным именем `Name@hash` из
`mcpforunity://instances`.

---

## Золотой цикл проверки

Порядок обязателен. Нарушение даёт ложный «зелёный» результат.

```
1. refresh_unity(compile: "request", wait_for_ready: true)
2. read_console(action: "get", types: ["error"])
3. только если ошибок нет → run_tests → get_test_job
4. записать в отчёт: что проверено, что нет
```

**Правило:** не переходить к тестам, пока в консоли есть ошибки компиляции.
Unity может запустить тесты на старой сборке и вернуть ложный `Passed`.

---

## Проверка Unity API: не полагаться на память

`unity_reflect` и `unity_docs` доступны и проверены. **Используй их вместо памяти.**

```
unity_reflect(action: "search", query: "X")      // существует ли тип
unity_reflect(action: "get_type", class_name: "X")   // какие члены
unity_reflect(action: "get_member", ...)          // сигнатура, устарело ли
unity_docs(action: "get_doc", class_name: "X")    // как использовать
```

Unity `6000.6.3f1` — свежая версия; память о Unity API ненадёжна, особенно для
пакетных API (Input System, Cinemachine, ProBuilder, NavMesh, URP).

---

## Установленные пакеты

`Packages/manifest.json` помимо MCP содержит optional-зависимости Unity MCP:

| Пакет | Версия | Зачем |
|---|---|---|
| `com.coplaydev.unity-mcp` | git `@main` | сам мост |
| `com.unity.probuilder` | `6.1.2` | группа `probuilder` |
| `com.unity.cinemachine` | `6.6.0` | работа камер |
| `com.unity.cloud.gltfast` | `6.20.0` | импорт glTF/GLB |
| `com.unity.visualeffectgraph` | `17.6.0` | группа `vfx` |

Roslyn лежит в `Assets/Plugins/Roslyn/` (5 DLL, у всех есть `.meta`) — это
зависимость `execute_code` с Roslyn-компилятором. Удалять нельзя: без неё
`execute_code` падает на `codedom` (C# 6).

---

## Версия пакета зафиксирована

`Packages/manifest.json` пиннит **коммит**, а не ветку:

```json
"com.coplaydev.unity-mcp": "https://github.com/CoplayDev/unity-mcp.git?path=/MCPForUnity#30d22075093d1d35dfb0091c1c7550e9ad948577"
```

Это сделано намеренно. При ссылке на `#main` принудительный resolve или удаление
`packages-lock.json` притягивал бы свежий код сервера, и имена инструментов могли
бы поменяться без нашего участия — а документация основана на фактической проверке.

При намеренном обновлении: сменить хеш в `manifest.json`, дать Unity перерезолвить
пакеты, затем **перепроверить** инвентарь в этом файле фактическим вызовом.


---

## Известные шумные сообщения

| Сообщение | Значение |
|---|---|
| `HTTP 429 (Too Many Requests)` в `unity_mcp_server.log` | Telemetry. На работу не влияет |
| `No build jobs found.` | Сборок не запускалось. Не ошибка |
| `total: 0` в результате тестов | Тестов в проекте нет. Не ошибка |
| `No Unity Editor instances found` | Editor не запущен или мост не подключён |

Лог: `%LOCALAPPDATA%\UnityMCP\Logs\unity_mcp_server.log`.

---

## Правила

| Правило | Почему |
|---|---|
| Не выдумывать имена инструментов и параметры | Документация по памяти уже дважды вводила в заблуждение: описан `recompile`, которого нет |
| Проверять Unity API через `unity_reflect` / `unity_docs` | Unity `6000.6.3f1` свежая, память устаревает |
| Ждать `get_test_job` до конца | `run_tests` асинхронный, ответ ≠ результат |
| Не запускать тесты при ошибках компиляции | Ложный `Passed` на старой сборке |
| Указывать в отчёте, что проверено, а что — нет | Иначе верификация фиктивна |

---

## Чего делать нельзя

- ❌ Править YAML Unity-ассетов (`.unity`, `.prefab`, `.mat`) — в том числе через
  `apply_text_edits`; для этого есть `manage_asset`, `manage_prefabs`
- ❌ Удалять `.meta` вручную
- ❌ Приписывать агенту инструменты, которых в его наборе нет
- ❌ Заявлять «тесты прошли», не дождавшись `get_test_job`
- ❌ Заявлять «сборка собралась», не собрав её
- ❌ Удалять `Assets/Plugins/Roslyn/` без проверки `execute_code`
