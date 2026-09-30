# Report — Unity MCP: восстановление автоматизации верификации

## Stage
Инфраструктурный (вне stage-плана). Предпосылка для Stage 00.

## Objective
Снять ручной режим верификации: установить Unity MCP, проверить его фактическим
вызовом, убрать из документации и агентов выдуманные имена инструментов.

## Implemented
1. Установлен `com.coplaydev.unity-mcp` (git, MIT) + optional-зависимости.
2. Проверены 11 инструментов и 2 ресурса фактическим вызовом.
3. `docs/runbooks/unity-automation.md` переписан как источник правды по инструментам.
4. Golden cycle возвращён в 22 файла: agents, commands, skills, runbooks, specs, `AGENTS.md`.
5. Восстановлен русский текст в 7 файлах, где он был повреждён двойным кодированием.

## Contract changes
Контракт верификации изменился: ручной цикл → MCP-цикл.
- `AGENTS.md → Verification commands` — новая норма, обязательна к соблюдению.
- `docs/runbooks/unity-automation.md` — источник правды по именам инструментов.
- `instructions/defaults.md` — `recompile` запрещён, `refresh_unity` обязателен.

## Files affected
- Ядро: `AGENTS.md`, `README.md`, `instructions/defaults.md`
- Runbooks: `unity-automation`, `test-workflow`, `build-and-release`, `incident-compile-errors`, `adding-content`, `local-development`, `git-workflow`
- Specs: `asset-standards`, `performance-budget`; stages: `README`, `stage-00-foundation`
- Agents: `qa-analyst`, `unity-tools-engineer`, `gameplay-engineer`, `ui-engineer`, `scene-level-designer`, `technical-artist`
- Commands: `verify`, `build-stage`, `debug`
- Skills: `debug-unity-console`, `unity-test-strategy`, `urp-visual-tuning`, `unity-prefab-safety`, `horror-level-design`, `stage-report`
- Пакеты: `Packages/manifest.json`, `Packages/packages-lock.json`

## Verification performed
### Automated
| Проверка | Результат |
|---|---|
| `read_console(action: "get", types: ["error"])` | 0 записей |
| `run_tests(mode: "EditMode")` → `get_test_job` | `succeeded`, `resultState: Passed`, `total: 0` |
| `refresh_unity(compile: "request", wait_for_ready: true)` | `resulting_state: idle` |
| `unity_reflect(get_member, NavMeshAgent.speed)` | `found: true`, `float`, `can_write: true` |
| `unity_docs(get_doc, CharacterController)` | описание + URL |
| `manage_build(action: "scenes")` | 1 сцена, `Bootstrap.unity`, enabled |
| `manage_build(action: "status")` | `No build jobs found.` — ожидаемо |
| `manage_editor(action: "telemetry_status")` | `telemetry_enabled: true` |
| `manage_scene(get_hierarchy, page_size: 15)` | 3 корневых объекта, `next_cursor: null` |
| `manage_tools(action: "list_groups")` | 11 групп, `core` включена |
| ресурс `mcpforunity://instances` | 1 инстанс, `Woodberry@93447b6f`, порт 6400 |
| ресурс `mcpforunity://editor/state` | `ready_for_tools: true`, `blocking_reasons: []` |
| `refresh_unity` + `read_console` + `run_tests` после правок | 0 ошибок, `succeeded` |
| `git diff` по 22 изменённым файлам | проверено вручную |

### Manual
- Визуальная проверка диффа 7 файлов с восстановленным кодированием.
- Сверка `manage_tools` group-карты с фактическим набором из 47 инструментов.

### Not verified
- **`manage_build(action: "build")`** — полная сборка player не запускалась.
  Причина: долгая операция, создаёт артефакты, не нужна для задачи.
- **PlayMode-тесты** — запускать нечего, в проекте 0 тестов (нет asmdef, Stage 00).
- **36 инструментов** из 47 не вызваны фактически: `manage_asset`, `manage_gameobject`,
  `manage_components`, `manage_prefabs`, `manage_scriptable_object`, `manage_material`,
  `manage_physics`, `manage_graphics`, `manage_animation`, `manage_probuilder`, `manage_ui`,
  `manage_vfx`, `manage_profiler`, `manage_texture`, `manage_camera`, `manage_packages`,
  `manage_shader`, `create_script`, `delete_script`, `manage_script`, `script_apply_edits`,
  `apply_text_edits`, `validate_script`, `get_sha`, `manage_script_capabilities`,
  `batch_execute`, `execute_code`, `execute_menu_item`, `find_gameobjects`, `find_in_file`,
  `generate_*`, `import_model*`, `set_active_instance`, `debug_request_context`.
  Их имена и параметры взяты из `tools/list`, но не подтверждены вызовом.
- **UI / VFX / ProBuilder / генерация** — требуют реального контента, которого нет.
- **BlenderMCP** — не настроен.

## Acceptance criteria status
**Done:**
- Мост работает, 11 инструментов + 2 ресурса проверены вызовом
- Выдуманные имена устранены, `recompile` запрещён явно
- `unity-automation.md` содержит инвентарь 47 инструментов с разделением проверено/нет
- Golden cycle возвращён во все 22 файла
- 0 ошибок компиляции, EditMode job `succeeded`

**Partial:**
- 36 инструментов документированы, но не проверены вызовом
- `run_tests` на реальных тестах не проверен (0 тестов)

## Known issues
- `manage_tools list_groups` отдаёт `enabled: false` для не-`core` групп, хотя
  инструменты из них вызываются. Клиент получает список при подключении, поэтому
  переключатель в Editor'е на живой сессии не влияет. После рестарта OpenCode
  инструменты могут пропасть — включать через `manage_tools(action: "activate")`.
- Не-`core` группы `enabled: false` — причина в конфигурации сессии, поведение
  документировано как наблюдение, не как гарантия.

## Tech debt
- Повреждённое кодирование (mojibake) осталось в `AGENTS.md` (строки 400+, ~1747 фрагментов),
  `docs/specs/code-review-flow.md`, `docs/specs/game-vision.md`, `docs/runbooks/incident-compile-errors.md`,
  `docs/adr/*`, части skills. Исправлено только 7 файлов, где текст переписывался целиком.
- Причина повреждения: двойное кодирование при записи UTF-8 через PowerShell.

## Assumptions
- Unity Editor останется запущенным; при остановке верификация невозможна.
- `mcpforunityserver==10.2.0` — совместимая версия для `com.coplaydev.unity-mcp` @ `30d2207`.
- Глобальный конфиг MCP — осознанный выбор пользователя; в репозитории его нет.

## Risks
- **`#main` вместо фиксации коммита.** `Packages/manifest.json` тянет ветку.
  Lockfile фиксирует `30d2207`, поэтому обычная проверка воспроизводима, но удаление
  lockfile притянет свежий `main`, и имена инструментов могут измениться без нашего участия.
- **MCP-конфиг вне репозитория.** Новый разработчик не получит автоматизацию, пока
  не настроит сервер у себя. Риск расхождения конфигураций.
- **Смена версии сервера** потребует повторной сверки `unity-automation.md`.

## Manual QA checklist
1. Открыть **Window → MCP for Unity**, убедиться, что инстанс виден и статус `running`.
2. Выполнить golden cycle через агента: `refresh_unity` → `read_console` → `run_tests` → `get_test_job`.
3. Создать минимальный EditMode-тест, прогнать, убедиться, что `total: 1, passed: 1`.
4. Вызвать `manage_editor(action: "play")`, затем `manage_camera(action: "screenshot")` — убедиться, что скриншот приходит.
5. Проверить `manage_scene(action: "get_hierarchy", page_size: 50)` после добавления объекта.
6. Перезапустить OpenCode, вызвать `unityMCP_read_console` — убедиться, что инструменты не пропали.
   Если пропали: `manage_tools(action: "activate", group: "testing")` и т.д.

## Recommended next stage
1. Создать `Woodberry.Tests.EditMode` / `Woodberry.Tests.PlayMode` asmdef в Stage 00 —
   без них тесты запускать нечем, и `run_tests` не даёт сигнала.
2. Написать первый EditMode-тест на чистую логику и прогнать реальный цикл.
3. Принять решение по конфигу MCP: перенос в `opencode.json` + удаление из глобального.
4. Зафиксировать `com.coplaydev.unity-mcp` на коммите вместо `#main`.
5. Отдельной задачей: починить mojibake в `AGENTS.md` и остальных файлах.