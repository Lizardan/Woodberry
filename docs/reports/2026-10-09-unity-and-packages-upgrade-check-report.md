# Отчёт — проверка после апгрейда Unity и пакетов

Дата: 2026-10-09 (вечер). Повод: пользователь обновил редактор Unity, пакет
MCP и ещё несколько пакетов. Задача — проверить работоспособность и оценить,
что из нового применимо к проекту.

## Что изменилось

| Что | Было | Стало |
|---|---|---|
| Unity | `6000.6.3f1` | **`6000.6.5f1`** |
| `com.unity.inputsystem` | `1.20.0` | **`1.20.1`** |
| `com.unity.ai.navigation` | `2.0.15` | **`2.0.16`** |
| `com.unity.probuilder` | `6.1.2` | **`6.1.3`** |
| `com.coplaydev.unity-mcp` | закреплённый коммит `30d2207` | ветка **`#main`** → разрешилась в **10.3.0** |

Остальные пакеты не менялись: URP `17.6.0`, Cinemachine `6.6.0`, test-framework
`1.8.0`, timeline `6.6.0`, visualscripting `1.9.12`, ugui `2.6.0`.

## Проверка работоспособности

| Проверка | Результат |
|---|---|
| Компиляция (`refresh_unity` → `read_console`) | **0 `error CS`, 0 `warning CS`** |
| EditMode-тесты | **216/216**, провалов 0 |
| PlayMode-тесты | **29/29**, провалов 0 |
| Консоль редактора | пусто |
| Рост `Editor.log` (5 замеров по 2 с) | **0 байт** — цикла перерисовки нет |
| Исключения в логе | нет; `NotImplementedException` — 0 |
| Удалённые API миграции на EntityId | в коде проекта **не встречаются** |
| Мост MCP | работает, порт найден автоматически |
| Odin (Sirenix) | в проекте **отсутствует** — обходы из навыка не нужны |

**Остаточные предупреждения.** В логе 4 варнинга Unity «Assembly for Assembly
Definition File … will not be compiled, because it has no scripts associated
with it» — это пустые сборки-заготовки `Woodberry.AI`, `Woodberry.Audio`,
`Woodberry.Save` из структуры слоёв. Не следствие апгрейда, лечится добавлением
первого скрипта в слой.

**Что дал патч 6000.6.5 нам.** Новых функций нет — 39 фиксов. Из релевантного:
починен сортировочный слой камеры 2D Renderer под настройкой Render Scale (у нас
URP 2D) и пакет фиксов UI Toolkit, включая утечку памяти на элементах со стилем
`-unity-material` и потерю текста при пересборке панели — это ровно тот стек, на
котором написан Art Studio.

## Новое в MCP 10.3.0

Поверхность выросла с ~11 инструментов, описанных в runbook, до **36**. Все
новые группы **доступны в проекте прямо сейчас** (проверено вызовом):

| Инструмент | Что даёт | Чем полезен проекту |
|---|---|---|
| `manage_profiler` | frame timing, счётчики, memory snapshot и сравнение, frame debugger | `docs/specs/performance-budget.md` — теперь бюджет меряется, а не оценивается |
| `manage_probuilder` | ProBuilder-меши | грейбокс сцен (пакет уже стоял) |
| `manage_graphics` | URP-пайплайн, Volume и эффекты, запечка света, skybox | ADR `0010-2d-lighting.md` |
| `manage_camera` | Cinemachine: brain, тела, цели, приоритеты | камера-риг проекта |
| `manage_physics` | настройки, коллизионная матрица, raycast/shape cast | слои и запросы |
| `manage_vfx`, `manage_ui`, `manage_material`, `manage_texture` | эффекты, UI, материалы, спрайты | UI Toolkit / uGUI, импорт кадров |
| `manage_build`, `manage_packages` | сборка и управление пакетами | `build-and-release.md` |
| `generate_image` / `generate_audio` / `generate_model` | генерация ассетов через fal.ai / OpenRouter | доступно, но **провайдеры без ключа** (`configured: false`) |
| `execute_menu_item`, `batch_execute`, `find_gameobjects` | пункт меню, пачка команд, поиск объектов | мелкая автоматизация |

Проверено фактическими вызовами: `manage_profiler` (в т. ч. `get_frame_timing`
отдал реальные миллисекунды), `manage_probuilder`, `manage_graphics` (URP
определён), `manage_physics`, `manage_vfx`, `manage_camera` (Cinemachine
найден), `manage_ui`, `manage_material`, `manage_packages`.

## Найденное и исправленное

**Пин Python-сервера в `opencode.json` отставал от плагина.** Плагин выводит пин
из своей версии (`AssetPathUtility.GetMcpServerPackageSource` → `10.3.0`), а в
конфиге стояло `mcpforunityserver==10.2.0`: сервер и плагин разговаривали
разными версиями. Поднял до `10.3.0` (на PyPI опубликована, `latest`).

**Устаревшие версии в доках.** Обновлены `AGENTS.md` (таблица фактов: Unity,
inputsystem, navigation; добавлены строки ProBuilder и AI-мост) и runbook'и
`unity-automation`, `build-and-release`, `local-development`,
`incident-compile-errors`. ADR и отчёты не трогал — это исторические записи.

## Осталось на решение

**Зависимость MCP в `manifest.json` теперь `#main`.** Версия плавает: каждый
перерезолв может принести другой коммит, а пакет — единственный мост к
редактору. Раньше он был закреплён коммитом осознанно (коммит
`fix(packages): restore Unity MCP package and pin it to a commit`). Фиксирует
сейчас только lockfile (`aa5fc638d623f56178d50329d9ad8c541a57fe66`). Если
плавающая версия не нужна — вернуть пин на этот коммит.

**Гейтинг read-only агентов не покрывает два новых инструмента.**
`opencode.json` закрывает `unityMCP_manage_*`, `execute_*`, `batch_execute`,
`generate_*`, `import_*` и точечные имена. Маски в конфиге поддерживаются
(документация OpenCode: `"mymcp_*": "ask"`), поэтому новые `manage_*` под
запрет попадают. А вот `unityMCP_execute_menu_item` (запускает ЛЮБОЙ пункт меню
Unity) и `unityMCP_blender_bridge` в списке отсутствуют — их стоит добавить в
`agent.plan.tools` и в denylist ревьюера.

## Verification commands

```
python .workbuddy-ai/tools/unity_mcp.py refresh_unity '{"compile":"request","wait_for_ready":true}'
python .workbuddy-ai/tools/unity_mcp.py read_console '{"types":["error","warning"],"count":30}'
python .workbuddy-ai/tools/unity_mcp.py run_tests '{"mode":"EditMode"}'   # затем get_test_job
python .workbuddy-ai/tools/unity_mcp.py run_tests '{"mode":"PlayMode"}'
```
