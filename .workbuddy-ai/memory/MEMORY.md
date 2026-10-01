# Woodberry — долговременные заметки проекта

## Unity MCP: мост есть, инструментов в сессии может не быть

Мост Unity MCP (`com.coplaydev.unity-mcp`) живёт **внутри редактора** и слушает
TCP `127.0.0.1:6400`. Даже когда в текущей сессии нет готовых инструментов
моста, до него можно дойти напрямую.

**Протокол:**
1. Подключиться к `127.0.0.1:6400`, прочитать строку `WELCOME UNITY-MCP 1 FRAMING=1\n`.
2. Кадры: 8 байт big-endian uint64 (длина) + UTF-8 JSON.
3. Запрос: `{"type": "<команда>", "params": {...}}`.
4. Ответ: `{"status": "success", "result": ...}` либо `{"status": "error", ...}`.

**Готовый клиент:** `.workbuddy-ai/tools/unity_mcp.py` (вызов команды)
и `.workbuddy-ai/tools/unity_code.py` (запуск C#-сниппета из файла — так
не приходится бороться с экранированием кавычек в shell).

**Полезные команды:** `manage_scene` (`get_hierarchy`, `screenshot`, `load`,
`save`), `manage_editor` (`play`, `stop`), `manage_asset`, `manage_gameobject`,
`manage_prefabs`, `refresh_unity`, `read_console`, `run_tests`, `get_test_job`,
`execute_code`, `unity_reflect`.

**Важно:** `execute_code` компилируется Roslyn **против всех загруженных
сборок**, минуя asmdef. Поэтому код сборки сцены может свободно использовать
типы URP (`Volume`, `Vignette`), не добавляя ссылок в `Woodberry.Gameplay`.
Внутри уже есть `using UnityEngine; using UnityEditor;`, но **нет**
`UnityEditor.Animations`, `UnityEditor.SceneManagement` — их надо писать
полным именем.

**Заблокированные шаблоны в `execute_code`:** `AssetDatabase.DeleteAsset`,
`System.IO.File.Delete`, `Process.Start`, `while(true)`. Удалять ассеты —
через `manage_asset(action:"delete")`, перезаписывать — через
`SaveAsPrefabAsset` / `CreateAsset`.

**Перекомпиляция рвёт соединение.** После `refresh_unity` мост отваливается
на время домен-релоада: нужен повтор с паузой (`.workbuddy-ai/tools/wait_ready.py`).

**Скриншоты** идут в `Assets/Screenshots/` и обязаны быть оттуда убраны
после проверки — иначе попадут в импорт и в сборку.

## Слои проекта

| Слой | Индекс | Назначение |
|---|---|---|
| `Occluder` | 8 | режет обзор: стены, деревья, мебель |
| `VisionMask` | 9 | геометрия маски видимости |

Окно намеренно не в `Occluder`: игрока не пропускает, обзор пропускает.

## Арт процедурный и воспроизводимый

Генераторы в `.workbuddy-ai/tools/`: `art_common.py` (примитивы, палитра,
шейдинг), `art_character.py`, `art_environment.py`, `art_props.py`,
`art_preview.py` (сборка превью). Спрайты правятся числом в скрипте.
Запускать через `C:/Users/Lizardan/.workbuddy-ai/binaries/python/envs/default/Scripts/python.exe`.

**Грабли генератора:** PIL `ImageDraw` пишет RGBA напрямую, а не накладывает —
заливка с `alpha < 1` стирает нижележащее. Смешивать только через
`Image.alpha_composite` (уже сделано в `Layer._paint`).

## Ориентиры визуала

Scorched Sun и Darkwood. Взяты: холодная обесцвеченная палитра, жёсткая
граница видимой области (не «полутемно», а черно), персонаж 3–4% высоты
кадра, виньетка и зерно. Боевые кадры Scorched Sun от первого лица
в референс не берутся.
