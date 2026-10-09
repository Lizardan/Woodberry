# Отчёт — уборка легаси и рабочая папка арт-студии

Дата: 2026-10-08. База: `main`, HEAD `12cb6b6` + шаг 0 (норматив импорта).
Этап: шаг 1 плана «Арт-студия внутри Unity».

## Objective

Убрать закрытый цикл процедурных портретов из `.workbuddy-ai`, завести
рабочую папку `ArtStudio/` под `.gitignore` и снять ссылки удалённых
скриптов из документации — чтобы следующий агент не искал то, чего нет.

## Implemented

### Удалено (277 файлов, `git rm` — история сохранена)

| Что | Что это было |
|---|---|
| `.workbuddy-ai/portraits_v2/` | выход процедурного генератора портретов |
| `.workbuddy-ai/portraits_v3/` | то же, третий проход |
| `.workbuddy-ai/portraits_stage/` | вывод `art_stage.py` |
| `.workbuddy-ai/portraits_preview/` | превью 81 кадра процедурного пути |
| `.workbuddy-ai/probe/` | слои `art_stage.py` (bg/body/head) |
| `tools/art_portrait.py` | генератор портретов на примитивах |
| `tools/art_stage.py` | сценическая подача поверх портрета |
| `tools/probe_body.py` | диагностика габарита тела |
| `tools/probe_stripe.py` | диагностика полосы |
| `tools/portrait_sheet.py` | контактный лист набора |
| `tools/__pycache__/copy_dialogue_art.cpython-313.pyc` | скомпилированный артефакт в git |

Основание: решение 2026-10-05 из `docs/runbooks/portrait-generation.md` —
процедурный генератор удаляется, как только нейросетевой путь подтвердится.
Подтвердился 06.10 (первый проход признан успешным по стилю).

### Сознательно оставлено

- `tools/art_common.py` — палитра Darkwood/Scorched Sun, из неё берутся
  значения цвета для GDD 07. Файл живёт как источник палитры.
- `tools/art_character.py`, `art_environment.py`, `art_props.py`,
  `art_preview.py` — действующие генераторы спрайтов мира (в `AGENTS.md`),
  их вывод в `artpreview/` и `qa/`.
- `refs/` — референсы Darkwood/Scorched Sun, к портретам не относятся.
- `tools/probe_loop.py` — проверяет, тикает ли игровой цикл редактора
  во время замеров. К портретам не относится, удаление было бы ошибкой.
- Все `probe_*.cs`, `tune_*.cs`, `verify_*.cs`, `unity_mcp*`, `wait_ready.py`
  — инструментарий Unity MCP и диагностики сцены.

### Рабочая папка `ArtStudio/`

```
ArtStudio/
├─ README.md      что где, правила, как чистить, что делать если драйвера нет
├─ config.json    standDir, python, comfyHost, style, light
├─ driver/        сюда кладётся artgen.py (не версионируется)
├─ logs/          лог прогона
└─ review/        thumbs/ и replaced/ — кандидаты и архив
```

Плюс `.gitignore`: `/ArtStudio/`, `__pycache__/`, `*.pyc`.
Проверено `git check-ignore` — все три пути игнорируются.

### Документация

- `docs/runbooks/portrait-generation.md` — секция «Процедурный генератор»
  переписана: было «временный резерв», стало «удалён 2026-10-08» с таблицей
  удалённого, списком оставленного и указанием единственного пути генерации.
- `.workbuddy-ai/memory/MEMORY.md` — актуализирован раздел арта.
- `.workbuddy-ai/memory/2026-10-06.md` — пункт «пока не удалён» закрыт
  со ссылкой на актуальный статус.

## Files affected

Удалено 277 файлов под `.workbuddy-ai/`. Изменено: `.gitignore`,
`docs/runbooks/portrait-generation.md`, `.workbuddy-ai/memory/MEMORY.md`,
`.workbuddy-ai/memory/2026-10-06.md`. Создано: `ArtStudio/` (README, config,
три пустые папки-держателя).

## Verification performed

### Automated

- **Удаление обратимо**: проверено до `git rm` — все 277 файлов были в
  индексе git, untracked-остатков не было. После `git rm` на диске 0
  совпадений по всем 11 целям.
- **Документация**: `git grep` по оставшимся файлам — упоминания удалённых
  скриптов остались только в `.workbuddy-ai/memory/2026-10-05.md` (историю
  сессии не переписываю) и в `docs/runbooks/` уже в новой редакции
  «удалён, что оставлено взамен».
- **`.gitignore`**: `git check-ignore -v` подтвердил игнор `ArtStudio/*`
  и `__pycache__/`.
- **Компиляция**: `refresh_unity(compile: request)` — ошибок 0.
- **EditMode-тесты: 81 из 81, провалов 0.**

### Manual

- `ls` по `.workbuddy-ai/` — остались `artpreview`, `memory`, `qa`,
  `refs`, `tools`; весь мусор procedural-пути убран.
- Консоль: ошибок 0.

### Not verified

- **Ничего не проверено глазами после уборки** — удаление коснулось только
  Python-инструментов и их выводов, в Unity-ассеты оно не задевало.
  Тесты и компиляция это подтверждают, но «открыл и посмотрел» — нет.
- **`git rm` не закоммичен.** Всё лежит staged/working-tree, коммит делаю
  только по твоей просьбе.

## Acceptance criteria status

| Критерий | Статус |
|---|---|
| Легаси удалено | **Done** (277 файлов, обратимо) |
| Действующий инструментарий не тронут | **Done** (сверка состава) |
| Рабочая папка заведена и игнорируется | **Done** (`check-ignore`) |
| Ссылки удалённых скриптов сняты из docs | **Done** (`git grep`) |
| Тесты | **Done** (81/81) |

## Known issues

- `Assets/Woodberry/Scenes/Bootstrap.unity` изменён не мной: Unity
  пересохранила текстовый компонент (`m_MaxSize: 40 → 64`). Вопрос о
  возврате к состоянию репозитория открыт.
- В `.workbuddy-ai/tools/` валяются одноразовые C#-профилировщики прошлых
  сессий (`dump_*.cs`, `probe_rig.cs`, `shots_*.py`). Они не относятся
  к procedural-портретам и не входили в список к удалению, поэтому
  остались. Если захочешь генеральную уборку — отдельная задача.
- `.artgen/extra_pools.json` оверлей ещё не читается стендом: подключение
  в `roster.py` — шаг 5 плана.

## Tech debt

- Драйвер `artgen.py` ещё не написан, поэтому папка `driver/` пуста,
  а `README.md` описывает, что туда положить.

## Assumptions

- «Всё, что связано с portraits_v2/v3/stage» = генератор портретов на
  примитивах и его обвязка. MCP-профилировщики сцены и генераторы
  спрайтов мира не относятся к этой ветке.
- История в `.workbuddy-ai/memory/20XX-XX-XX.md` — дневник сессии, его не
  переписывать; актуальный статус вынесен в `MEMORY.md`.

## Risks

- Удаление обратимо только через git-историю. До коммита «откат» —
  это `git checkout` из индекса, после коммита — из истории. Никакого
  второго места, где лежали бы эти файлы, нет.

## Recommended next stage

Шаг 2 плана: чистая логика на стороне Unity — протокол, `EventTail`,
`JobQueue`, `CatalogModel`, `FeedbackStore`, `ArtSettings` + EditMode-тесты
и пустое окно, показывающее текущий каталог диалогового арта.
