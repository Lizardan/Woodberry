# Runbook — Coplay: мост к Unity

## Purpose

Работать с Unity из OpenCode/агента через MCP-мост Coplay.

## Как устроен мост

Мост — **Coplay**. Две части, обе уже установлены:

```
OpenCode ──stdio MCP──► uvx coplay-mcp-server
                              │
                              └──► TCP ──► Unity Editor + плагин com.coplaydev.coplay
```

| Часть | Что это |
|---|---|
| Плагин в проекте | `com.coplaydev.coplay` (версия 8.20.8), в `Packages/manifest.json` |
| MCP-сервер | `uvx --python ">=3.11" coplay-mcp-server@latest` |

Сервер отдаёт **~96 инструментов** в 14 модулях. Проверено запуском, имена
взяты из установленного пакета, а не из документации.

### Проверка состояния моста

Из OpenCode:

```
mcp__coplay__get_unity_editor_state()
mcp__coplay__check_compile_errors()
```

Если отвечает — мост жив. Coplay должен быть также авторизован в Unity
(окно **Coplay** в редакторе).

Из PowerShell (диагностика, если MCP не отвечает):

```powershell
# Логи сервера
Get-Content "C:\Users\Lizardan\AppData\Local\Coplay\Logs\coplay_mcp_*.log" -Tail 30
```

---

## Золотой цикл

**Любая** работа со скриптами проходит через эти шаги:

```
1. Изменить файлы
2. mcp__coplay__check_compile_errors()
3. mcp__coplay__get_unity_logs(show_errors=true, show_warnings=true, limit=50)
4. Только потом — продолжать работу
```

Шаг 2 **обязательный**. Ошибка компиляции означает, что следующие вызовы
к Editor'у дадут мусор.

---

## Чего Coplay НЕ умеет

Это важно знать до того, как что-то спланировать. Coplay — это ассистент
**внутри** Unity, а не CI-инструмент.

| Задача | Coplay | Как делать |
|---|---|---|
| **Запуск тестов** | ❌ нет `run_tests` | Test Runner вручную, либо `execute_script` (см. ниже) |
| **Сборка player** | ❌ нет `build` | `File → Build Settings` вручную |
| **Очистка консоли** | ❌ нет `clear_console` | `Edit → Clear` вручную; `get_unity_logs(search_term=...)` вместо очистки |
| **API-рефлексия** | ⚠️ только через `execute_script` | См. «Проверка Unity API» |
| **NavMesh / запекание света** | ❌ | Утилиты Unity вручную |
| **Смена интерактивного ввода** | ✅ `simulate_*` нет | `execute_script` или ручной Play Mode |

**Следствие для процесса:** этап нельзя объявить «готово», если тесты не
прогнаны. Прогон тестов — ручная операция, её результат вносится в stage report
как `Manual check` или `Not verified`.

---

## Инструменты по модулям

Полный список — 96 имён. Ниже сгруппировано по назначению.

### Состояние и логи (`agent_tool`)

| Инструмент | Что делает |
|---|---|
| `get_unity_editor_state()` | Состояние Editor'а (без иерархии) |
| `get_unity_logs(limit=, show_errors=, show_warnings=, show_logs=, show_stack_traces=, search_term=, skip_newest_n_logs=)` | Чтение логов Unity |
| `list_game_objects_in_hierarchy()` | Дерево объектов сцены |
| `get_game_object_info()` | Инфо об объекте / компонентах |
| `execute_script(filePath=, methodName=, arguments=)` | **Выполнить C# в Editor'е** |
| `invoke_mcp_tool()` | Вызов другого MCP-инструмента через Coplay |

### Компиляция и Play (`unity_functions`)

| Инструмент | Что делает |
|---|---|
| `check_compile_errors()` | **Есть ли ошибки компиляции** |
| `play_game()` | Войти в Play Mode |
| `stop_game()` | Выйти из Play Mode |
| `save_scene(path=)` | Сохранить сцену |
| `open_scene(path=)` | Открыть сцену |
| `create_scene(path=)` | Создать сцену |

### Объекты и компоненты (`unity_functions`)

`create_game_object(name=, position=, primitive_type=, size=)` ·
`delete_game_object()` · `duplicate_game_object()` ·
`rename_game_object()` · `parent_game_object()` · `set_sibling_index()` ·
`set_transform()` · `set_property()` · `add_component()` ·
`remove_component()` · `set_tag()` · `set_layer()`

`primitive_type` — только `Cube`, `Sphere`, `Capsule`, `Cylinder`, `Plane`.

### Ассеты и префабы (`unity_functions`, `asset_functions`)

`create_prefab()` · `create_prefab_variant()` · `add_nested_object_to_prefab()` ·
`rename_asset()` · `duplicate_asset()` ·
`list_all_prefabs_with_bounding_boxes()` · `place_asset_in_scene()`

> `create_prefab` / `create_prefab_variant` — преимущества Coplay перед
> ручным YAML-редактом. Пользуйся ими.

### Материалы (`unity_functions`)

`create_material()` · `assign_material()` · `assign_material_to_fbx()` ·
`assign_shader_to_material()`

### Пакеты (`package_tool`)

| Инструмент | Что делает |
|---|---|
| `list_packages()` | Установленные пакеты |
| `search_installed_packages()` | Поиск по установленным |
| `search_all_packages()` | Поиск по реестру Unity |
| `install_unity_package()` | Установка из реестра |
| `install_git_package()` | Установка с git-URL |
| `remove_unity_package()` | Удаление |
| `export_package()` | Экспорт |

### Скриншоты (`screenshot_tool`)

| Инструмент | Что делает |
|---|---|
| `capture_scene_object()` | Снимок объекта в сцене |
| `capture_ui_canvas()` | Снимок UI-канваса |

### Профайлер (`profiler_functions`)

| Инструмент | Что делает |
|---|---|
| `get_worst_cpu_frames()` | Худшие кадры по CPU |
| `get_worst_gc_frames()` | **Худшие кадры по GC-аллокациям** |

`get_worst_gc_frames` — главный инструмент для проверки бюджета
`docs/specs/performance-budget.md` («0 аллокаций в кадре»).

### Ввод (`input_action_tool`) — важно для этого проекта

Проект на Input System, и Coplay умеет генерировать C#-обёртки:

| Инструмент | Что делает |
|---|---|
| `create_input_action_asset()` | Создать `.inputactions` |
| `get_input_action_asset()` | Прочитать существующий |
| **`generate_input_action_wrapper_code()`** | **Сгенерировать C#-класс** |
| `add_action_map()` / `remove_action_map()` | Action maps |
| `add_action()` / `remove_action()` / `rename_action()` | Actions |
| `add_bindings()` / `remove_bindings()` | Bindings |
| `add_composite_binding()` | Composites |
| `add_control_scheme()` / `remove_control_scheme()` | Control schemes |

Это снимает главную боль Stage 00: генерацию обёрток
`InputSystem_Actions.inputactions` вручную.

### UI (`ui_functions`)

`create_ui_element()` · `set_ui_text()` · `set_ui_layout()` ·
`set_rect_transform()` · `add_persistent_listener()` ·
`remove_persistent_listener()` · `create_panel_settings_asset()`

### Анимация (`animation_functions`, 11)

`create_animation_clip()` · `get_animation_clip_data()` ·
`set_animation_curves()` · `set_sprite_animation_curve()` ·
`set_animation_clip_settings()` · `create_animator_controller()` ·
`get_animator_controller_data()` · `modify_animator_controller()` ·
`create_blend_tree_state()` · `get_blend_tree_state_data()` ·
`list_model_animation_clips()`

### Файлы (`general_computer_tool`)

`read_file()` · `search_files()` · `list_files()` ·
`list_code_definition_names()`

### Генерация контента

| Модуль | Инструменты |
|---|---|
| `image_tool` | `generate_or_edit_images()` |
| `generate_3d_model_functions` | `generate_3d_model_from_image()` · `generate_3d_model_from_text()` · `generate_3d_model_texture()` · `auto_rig_3d_model()` · `apply_animation_to_rigged_model()` · `search_animation_library()` |
| `sound_tool` | `generate_sfx()` · `generate_music()` · `generate_tts()` · `search_tts_voice_id()` |

---

## Проверка Unity API через `execute_script`

**Не полагайся на память о Unity API.** Версия проекта `6000.6.3f1` свежая,
устаревшие API — частая причина ошибок компиляции.

`execute_script` принимает **путь к C#-файлу** с публичным статическим методом
(по умолчанию `Execute`) и выполняет его в Editor'е.

### Рецепт

1. Создать временный C#-файл **внутри проекта** (например
   `Assets/Woodberry/Editor/ApiProbe.cs`)
2. Метод возвращает строку с ответом
3. Вызвать `execute_script(filePath="Assets/Woodberry/Editor/ApiProbe.cs")`
4. **Удалить файл** — он не должен попасть в коммит

```csharp
using System;
using System.Linq;
using System.Reflection;

public static class ApiProbe
{
    public static object Execute()
    {
        // Пример 1: существует ли член
        var prop = typeof(UnityEngine.AI.NavMeshAgent)
            .GetProperty("speed");
        if (prop == null) return "NULL: свойства speed не существует";

        // Пример 2: подпись метода
        var m = typeof(UnityEngine.CharacterController)
            .GetMethod("Move", new[] { typeof(Vector3) });
        if (m == null) return "NULL: Move не найден";
        var ps = string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name));

        // Пример 3: поиск типа по имени
        var found = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(SafeTypes)
            .Where(t => t.Name == "NavMeshAgent")
            .Select(t => t.FullName)
            .ToArray();

        return $"Move({ps}) -> {m.ReturnType.Name}; " +
               $"speed canWrite={prop.CanWrite}; типы: {string.Join("; ", found)}";
    }

    private static Type[] SafeTypes(Assembly a)
    {
        try { return a.GetTypes(); } catch { return new Type[0]; }
    }
}
```

### Альтернатива: поиск по документации

```powershell
& "F:\Unity\Unity Hub\resources\unity.exe" docs NavMeshAgent --url
```

Даёт ссылку на документацию под версию проекта. Быстро, но без проверки
существования в загруженных сборках — нужен `execute_script`.

### Ограничение

Coplay **не умеет рефлексию «из коробки»**. Это единственный способ. Файл
создаётся, запускается, удаляется — три действия на каждый запрос.

---

## Запуск тестов: обходной путь

Coplay не имеет `run_tests`. Варианты:

### Вариант A — Test Runner вручную (проще)

1. `Test → Test Runner → EditMode → Run All`
2. `mcp__coplay__get_unity_logs(show_errors=true, search_term="test")`
3. Результат внести в stage report как `Manual check`

### Вариант B — через `execute_script`

Test Framework умеет из кода. Файл `Assets/Woodberry/Editor/RunTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using UnityEditor.TestTools.TestRunner.Api;

public static class RunTests
{
    private static ICallbacks _cb;

    public static object Execute(string arguments)
    {
        var mode = "EditMode";
        if (!string.IsNullOrEmpty(arguments)) mode = arguments;

        _cb = new Callbacks();
        var api = ScriptableObject.CreateInstance<TestRunnerApi>();
        var filter = new Filter
        {
            testMode = mode == "PlayMode"
                ? TestMode.PlayMode
                : TestMode.EditMode
        };
        api.RegisterCallbacks(_cb);
        api.Execute(new ExecutionSettings(filter));
        return "started: " + mode;
    }

    private class Callbacks : ICallbacks
    {
        public void RunStarted(ITestAdaptor tests) { }
        public void RunFinished(ITestResultAdaptor result)
        {
            Debug.Log($"TESTS_DONE passed={result.PassCount} " +
                      $"failed={result.FailCount} skipped={result.SkipCount}");
        }
        public void TestStarted(ITestAdaptor test) { }
        public void TestFinished(ITestResultAdaptor result)
        {
            if (result.TestStatus == TestStatus.Failed)
            {
                Debug.LogError($"TEST_FAILED {result.FullName}: {result.Message}");
            }
        }
    }
}
```

Запуск: `execute_script(filePath="Assets/Woodberry/Editor/RunTests.cs", arguments="EditMode")`
Результат придёт в лог — читать через `get_unity_logs(search_term="TESTS_DONE")`.

⚠️ PlayMode-тесты через скрипт **перезапускают домен** — вызов оборвётся.
Используй `EditMode` программно, `PlayMode` — вручную (вариант A).

---

## Сборка

Coplay не умеет собирать. Процедура — в
[`build-and-release.md`](build-and-release.md): File → Build Settings вручную.

---

## Правила работы

| Правило | Почему |
|---|---|
| Не обращаться к Editor'у при ошибках компиляции | Результат будет мусорным |
| `check_compile_errors()` после каждого изменения скриптов | Единственная проверка компиляции |
| `get_unity_logs` перед заявлением «готово» | Единственный источник правды об ошибках |
| Не заявлять «тесты прошли» без прогона | Coplay не запускает тесты — только ты, вручную |
| Проверять API через `execute_script` | Память о Unity API ненадёжна |
| Удалять временные файлы пробников | Не должны попасть в коммит |
| Сцены/префабы — через Coplay, не YAML | Ручной YAML-редакт ломает ассеты |

---

## Если мост не отвечает

| Симптом | Проверка |
|---|---|
| MCP-сервер не стартует | `Get-Content "C:\Users\Lizardan\AppData\Local\Coplay\Logs\coplay_mcp_*.log" -Tail 40` |
| Сервер отвечает, Editor — нет | Coplay не авторизован. Открой окно **Coplay** в Unity |
| Плагин не установлен | `Select-String -Path Packages\manifest.json -Pattern coplay` |
| `uvx` не найден | `Test-Path "C:\Users\Lizardan\.local\bin\uvx.exe"` |
| Таймаут на долгих задачах | `MCP_TOOL_TIMEOUT` уже = 720000 (12 мин) в `opencode.json` |

После правки `opencode.json` нужен **перезапуск OpenCode** — сервер
подхватывается при старте сессии.

---

## Чего делать нельзя

- ❌ Править YAML Unity-ассетов
- ❌ Удалять `.meta` вручную
- ❌ Выдумывать имена инструментов Coplay — сверяй с этим файлом
- ❌ Оставлять временные файлы пробников в репозитории
- ❌ Заявлять «проверено», не вызвав `check_compile_errors()` и `get_unity_logs()`
- ❌ Заявлять «тесты прошли», не прогнав их
