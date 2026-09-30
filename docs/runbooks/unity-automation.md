# Runbook — Unity Automation через MCP

## Purpose

Работать с Unity из OpenCode/агента, не открывая редактор руками.

## Как устроен мост

Мост — **нативный MCP-сервер Unity CLI**, а не сторонний пакет.

```
OpenCode ──stdio MCP──► unity.exe mcp --project-path <project>
                              │
                              └──► TCP 127.0.0.1:7801 ──► Unity Editor
                                                        (Unity.Pipeline.Editor)
```

Три части, все из коробки:

| Часть | Что это | Где |
|---|---|---|
| Пакет в проекте | `com.unity.pipeline` — регистрирует команды Editor'а | `Packages/manifest.json` |
| Клиентский MCP-сервер | `unity.exe mcp` — stdio-сервер (`unity-mcp 1.0.0-beta.11`) | `F:\Unity\Unity Hub\resources\unity.exe` |
| Транспорт | Editor слушает `127.0.0.1:7801` | внутри Editor'а |

**Мост уже запущен**, если Editor открыт. Ничего включать в Unity не нужно.

## Проверка состояния

```powershell
$cli = "F:\Unity\Unity Hub\resources\unity.exe"

& $cli status --json
```

Ожидаемо:
```json
{ "count": 1,
  "instances": [{ "port": 7801,
                  "project": "F:\\Unity Projects\\Woodberry",
                  "version": "6000.6.3f1",
                  "state": "ready" }] }
```

Если `count: 0` или `state` не `ready` — Editor не открыт или не успел поднять сервер.

## Диагностика CLI

```powershell
& $cli doctor --json      # состояние окружения CLI
& $cli command --json     # все команды, которые Editor сейчас отдаёт
```

`unity command --json` — источник истины по доступным инструментам.
Если сомневаешься, какие параметры у команды:

```powershell
(& $cli command --json | ConvertFrom-Json).data.commands |
  Where-Object { $_.name -eq 'run_tests' } | ConvertTo-Json -Depth 5
```

В проекте доступно **160 команд** (143 из `Unity.Pipeline.Editor`, 17 из `Unity.Pipeline`).

---

## Золотой цикл

**Любая** работа со скриптами проходит через эти шаги:

```
1. Изменить файлы
2. mcp__unityMCP__recompile(focus=false)
3. mcp__unityMCP__console(level="error", tail=50)
4. Только потом — продолжать работу
```

Шаг 3 **обязательный**. Ошибка компиляции означает, что следующие обращения
к Editor'у дадут мусор. Нельзя продолжать при ошибках.

`recompile` асинхронный. Дождаться завершения:

```
mcp__unityMCP__recompile_status()
```

Если статус не финализировался — `wait_for`:

```
mcp__unityMCP__wait_for(condition=<NotCompiling>, timeout_s=120)
```

---

## Основные команды

### Компиляция и состояние

```
mcp__unityMCP__recompile(focus=false)      # запустить компиляцию
mcp__unityMCP__recompile_status()           # статус компиляции
mcp__unityMCP__editor_status()              # состояние Editor'а
mcp__unityMCP__runtime_status()             # состояние рантайма (Play Mode)
mcp__unityMCP__cleanup_codereload()         # очистить состояние после reload
```

### Console

```
mcp__unityMCP__clear_console()                                   # очистить
mcp__unityMCP__console(level="error", tail=50)                   # ошибки
mcp__unityMCP__console(level="warning", tail=50)                 # предупреждения
mcp__unityMCP__console_status()                                  # счётчики
```

`clear` — **перед** проверкой. Иначе старые ошибки выглядят как новые.

### Тесты

```
mcp__unityMCP__run_tests(mode="EditMode", timeout=120)
mcp__unityMCP__test_status()
mcp__unityMCP__list_tests(mode="EditMode")
mcp__unityMCP__cancel_tests()
```

Фильтрация:

```
mcp__unityMCP__run_tests(mode="PlayMode", filter_type="assembly", filter="Woodberry.Tests.PlayMode")
```

> PlayMode гоняет реальный Play Mode. Первый запуск долгий из-за перезагрузки домена —
> это нормально. Укажи `timeout=180`.

### Сцены

```
mcp__unityMCP__list_open_scenes()
mcp__unityMCP__open_scene(path="Assets/Woodberry/Scenes/Woodberry_Prototype.unity")
mcp__unityMCP__save_scene()
mcp__unityMCP__get_scene_hierarchy(path=<optional>)
```

`get_scene_hierarchy` возвращает много данных. **Всегда** передавай `path`,
чтобы не получать нечитаемый ответ по всей сцене.

### Объекты и компоненты

```
mcp__unityMCP__find_gameobjects(type="PlayerController", include_inactive=false)
mcp__unityMCP__create_gameobject(name="Player", primitive="Capsule")
mcp__unityMCP__add_component(target=<ObjectRef>, type="PlayerController")
mcp__unityMCP__get_component_properties(target=<ObjectRef>, type="PlayerController")
mcp__unityMCP__set_component_properties(target=<ObjectRef>, type="PlayerController", properties={...})
mcp__unityMCP__set_transform(target=<ObjectRef>, position=[0,0,0])
mcp__unityMCP__delete_gameobject(target=<ObjectRef>)
```

### Скрипты

```
mcp__unityMCP__create_script(name="PlayerController", path="Assets/Scripts/Gameplay/Player", namespace="Woodberry.Gameplay.Player", base_class="MonoBehaviour")
```

### Ассеты

```
mcp__unityMCP__find_assets(type="Shader", name="Universal", limit=50)
mcp__unityMCP__find_assets(type="Material", search_in="Assets/Woodberry/Materials")
mcp__unityMCP__create_asset(path="Assets/Woodberry/Materials/M_Wood.mat", type="Material", shader="Universal Render Pipeline/Lit")
mcp__unityMCP__get_import_settings(asset=<ObjectRef>, platform="Standalone")
mcp__unityMCP__set_import_settings(asset=<ObjectRef>, settings={...})
mcp__unityMCP__import_asset(source="...", path="Assets/Woodberry/...")
mcp__unityMCP__rename_asset(source="...", path="...")
mcp__unityMCP__move_asset(source="...", path="...")
mcp__unityMCP__delete_asset(path="...")
```

### Префабы

```
mcp__unityMCP__create_prefab(source=<ObjectRef>, path="Assets/Woodberry/Prefabs/X.prefab")
mcp__unityMCP__apply_prefab_overrides(instance=<ObjectRef>)
mcp__unityMCP__revert_prefab_overrides(instance=<ObjectRef>)
mcp__unityMCP__unpack_prefab(instance=<ObjectRef>)
mcp__unityMCP__instantiate_prefab(prefab=<ObjectRef>)
```

### Сборка

```
mcp__unityMCP__list_build_targets()
mcp__unityMCP__set_build_settings(settings={...}, confirm=true)
mcp__unityMCP__build(target="StandaloneWindows64", outputPath="Builds/Windows/Woodberry.exe", confirm=true)
mcp__unityMCP__build_status()
mcp__unityMCP__switch_build_target(target="StandaloneWindows64")
```

### Пакеты

```
mcp__unityMCP__package_list()
mcp__unityMCP__package_add(identifier="com.unity.render-pipelines.universal@17.6.0", confirm=true)
mcp__unityMCP__package_status()
```

Установка пакета триггерит массовую перекомпиляцию. Только по явной задаче.

### Рендер и производительность

```
mcp__unityMCP__get_graphics_settings()
mcp__unityMCP__get_lighting_settings()
mcp__unityMCP__set_lighting_settings(settings={...}, dry_run=false)
mcp__unityMCP__bake_lighting(confirm=true)
mcp__unityMCP__lighting_bake_status()
mcp__unityMCP__bake_navmesh(confirm=true)
mcp__unityMCP__navmesh_bake_status()
mcp__unityMCP__get_performance_stats()
mcp__unityMCP__list_shaders(filter="Universal", limit=50)
mcp__unityMCP__get_material_properties(material=<ObjectRef>)
mcp__unityMCP__set_material_properties(material=<ObjectRef>, properties={...})
```

### Скриншоты

```
mcp__unityMCP__capture_game_view(width=1280, height=720, include_inline_image=true, max_resolution=800)
mcp__unityMCP__capture_scene_view(view=<target>)
```

⚠️ `capture_game_view` **не включает** `Screen Space - Overlay` UI.
Для UI-скриншотов нужен `ScreenCapture API` без указания камеры —
то есть `capture_game_view` **без** параметра `camera`.

### Ввод (для тестов)

```
mcp__unityMCP__simulate_key(key="W")
mcp__unityMCP__simulate_pointer(x=0.5, y=0.5, click="left")
```

Полезно для проверки ввода в PlayMode-тестах.

### Прочее

```
mcp__unityMCP__menu(menuPath="Assets/Reimport All")
mcp__unityMCP__audit(categories="assets,scripts")
mcp__unityMCP__audit_status()
mcp__unityMCP__eval(code="Debug.Log(1);")
mcp__unityMCP__save_all()
mcp__unityMCP__list_open_scenes()
mcp__unityMCP__set_tags_layers()
mcp__unityMCP__quit()
```

---

## Правила работы

| Правило | Почему |
|---|---|
| Не обращаться к Editor'у при ошибках компиляции | Результат будет мусорным |
| Всегда `path`/`target` в больших запросах | Иначе ответ нечитаем |
| `clear_console` перед проверкой | Иначе старые ошибки выглядят как новые |
| `console()` перед заявлением «готово» | Единственный источник правды о компиляции |
| Проверять параметры через `unity command --json` | Не выдумывать API |
| Сцены/префабы — только через MCP | Ручной YAML-редакт ломает ассеты |
| `dry_run=true` для разрушительных операций | Посмотреть последствия до применения |

---

## Если MCP недоступен

### `No Unity Editor instances found`

1. Editor открыт? `& $cli status --json`
2. Пакет `com.unity.pipeline` на месте?
   ```powershell
   Select-String -Path "Packages\manifest.json" -Pattern "pipeline"
   ```
3. Мост поднялся? `state` должен быть `ready`, порт `7801`.

### Порт занят / Editor не отвечает

```powershell
& $cli status --json          # порт и состояние
& $cli doctor --json          # диагностика CLI
```

Перезапуск Editor'а пересоздаёт сервер.

### `opencode.json` указывает на не тот сервер

Проектный `opencode.json` должен содержать **нативный** CLI:

```json
"mcp": {
  "unityMCP": {
    "type": "local",
    "command": [
      "F:\\Unity\\Unity Hub\\resources\\unity.exe",
      "mcp",
      "--project-path",
      "F:\\Unity Projects\\Woodberry"
    ],
    "enabled": true
  }
}
```

Сторонний вариант (`uvx mcpforunityserver`) требует пакет `mcpforunity`
в проекте, которого здесь нет.

**После правки `opencode.json` нужен перезапуск OpenCode** — сервер подхватывается
при старте сессии.

---

## Чего делать нельзя

- ❌ Обращаться к Editor'у при ошибках компиляции
- ❌ Править YAML Unity-ассетов
- ❌ Удалять `.meta` вручную
- ❌ Выдумывать команды или параметры — проверяй `unity command --json`
- ❌ Заявлять «проверено», не вызвав `console()` и `run_tests()`
