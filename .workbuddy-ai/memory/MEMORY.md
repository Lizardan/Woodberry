# Woodberry — долговременные заметки проекта

Подробности — в репозитории (`AGENTS.md`, `docs/`). Здесь только то, что
нужно помнить между сессиями и чего нет в доках.

## GDD — замысел игры (`docs/gdd/`)

Появился 2026-10-05. По смыслу **выше specs**: GDD — «что и зачем»,
specs — «какой контракт держит код», ADR — «почему технология», stages —
«что делаем сейчас». GDD не дублирует specs, а ссылается на них.

**Игра.** Кооперативный детективный хоррор, вид сверху. Игрок — охотник
на ведьм, член ордена инквизиции. Две петли: город-хаб (гильдия,
торговец, прокачка, алхимия, карета на выезде) и вылазка в деревню
(срок в днях, расследование днём, ночь давит и на игроков, и на
жителей, приговор и казнь). Ошибка приговора → штраф и минус репутации;
череда провалов → казнь охотника → конец игры. Смерть от угрозы — тоже
конец игры, откат к сохранению в городе. 1–4 игрока, соло обязателен.
Угроза шире бестиария: культ, чума; монстр может менять облик.

**Статус файлов.** Написаны: `00-vision`, `01-core-loop`, `02-player`,
`03-threat`, `04-levels`, `07-art-audio-ux`, `99-open-questions`, `README`.
**Не написаны: `05-coop`, `06-world`, `08-scope-roadmap`.**

**Что ждёт решения (журнал `99`):**
- `docs/specs/game-vision.md` описывает ДРУГУЮ петлю — согласовать.
- `core-gameplay.md` и `coop-networking.md` обновить под GDD.
- `docs/stages/` 03–05 детектив, NPC, город и генерацию не описывает —
  роадмап пересобирается в `08-scope-roadmap.md`.
- ADR `0003-networking-stack.md` — статус Proposed, **блокирует кооп**,
  а кооп назван архитектурным ограничением номер один.

## Unity MCP — мост живёт в редакторе

TCP `127.0.0.1:6400`. Приветствие `WELCOME UNITY-MCP 1 FRAMING=1\n`,
далее кадры: 8 байт big-endian uint64 (длина) + UTF-8 JSON. Запрос
`{"type","params"}`, ответ `{"status","result"}`.

Клиенты: `.workbuddy-ai/tools/unity_mcp.py` (команда) и `unity_code.py`
(C#-сниппет из файла — не бороться с экранированием в shell).

Команды: `manage_scene` (`get_hierarchy`, `screenshot`, `load`, `save`),
`manage_editor` (`play`, `stop`), `manage_asset`, `manage_gameobject`,
`manage_prefabs`, `refresh_unity`, `read_console`, `run_tests`,
`get_test_job`, `execute_code`, `unity_reflect`.

**`execute_code` компилируется Roslyn против всех загруженных сборок**,
минуя asmdef — поэтому код сборки сцены может брать типы URP (`Volume`,
`Vignette`) без ссылок в `Woodberry.Gameplay`. Внутри есть
`UnityEngine`/`UnityEditor`, но НЕТ `UnityEditor.Animations` и
`UnityEditor.SceneManagement` — писать полным именем.

**Заблокированные шаблоны:** `AssetDatabase.DeleteAsset`,
`System.IO.File.Delete`, `Process.Start`, `while(true)`. Удалять ассеты —
`manage_asset(action:"delete")`, перезаписывать — `SaveAsPrefabAsset`.

**Перекомпиляция рвёт соединение** — после `refresh_unity` ждать
домен-релоад (`wait_ready.py`). Скриншоты идут в `Assets/Screenshots/` и
должны быть оттуда убраны, иначе попадут в билд.

**Тесты:** `run_tests` с `{"mode":"EditMode"}` или `{"mode":"PlayMode"}`
(`testMode` игнорируется), результат — `get_test_job` с тем же `job_id`.
Состояние: EditMode 73/73, PlayMode 29/29.

## Слои проекта

| Слой | Индекс | Назначение |
|---|---|---|
| `Occluder` | 8 | режет обзор: стены, деревья, мебель |
| `VisionMask` | 9 | геометрия маски видимости |

Окно намеренно НЕ в `Occluder`: игрока не пропускает, обзор пропускает.

## Сервисы и демо-сцены

Composition root — в `Bootstrap.unity`: `GameBootstrap` регистрирует
`IInputReader`/`ISceneLoader` через `RegisterServicesStep` и уходит в
`_startScene` (`Menu`). `PlayerController` берёт ввод только из
`ServiceRegistry`, поэтому сцены, запущенные мимо `Bootstrap`, стоят —
`ReportMissingInput()` ругается, движения нет.

Обход — `LightingDemoBootstrap.cs` (`[DefaultExecutionOrder(-900)]`):
в `Awake` создаёт `InputSystemReader` + `UnitySceneLoader`, если их нет,
в `Start` зовёт `EnableGameplayInput()`; снимает только своё.

Демо-сцены: `BuildLightingScenes` (меню `Woodberry/Build Lighting Scenes`)
копирует `Game.unity`, гасит `VisionSource`/`VisionMask` и ставит
`LightingDemoBootstrap`. Идемпотентен. Меню выбора — в `Menu.unity` под
`PlayButton` (`BuildMenu`); `LightingMenu` ищет кнопки по имени в `Awake`,
`CloneButton` обнуляет `m_PersistentCalls.m_Calls`.

## Арт

**Портреты и фоны — нейросетевые**, стенд `F:\ai` (Z-Image + ComfyUI,
AMD ROCm), вне репозитория. Справочник — `F:\ai\README.md`, рецепт —
`F:\ai\portraits\deliver\РЕЦЕПТ.md`, рабочий навык —
`portrait-art-generation`. Проектная сторона — `docs/runbooks/
portrait-generation.md`.

Стандарт: фон `856×480`, персонаж `480×720`, посадка = 90 % высоты фона,
низ прижат, центр по X. Проверка — `check_sizes.py`, инвариант снятия
рамки — `check_frame.py`.

**Раскладка есть, массовой генерации ещё не было.** Арт — в
`Assets/Woodberry/Art/Dialogue/`: `Portraits/<группа>/<Пул>/` и
`Backgrounds/<группа>/<Место>/`, имя файла `<Пул>_<NN>_<Стиль>.png`.
88 папок-пулов, заполнено 15 портретов и 7 фонов. Категории — из
`04-levels.md`, не выдуманы. Источник кадров — `portraits/out`:
в `deliver` нет `15_merchant` и `bg_swamp`, а персонажи 11–14 там
старого габарита `512×768`.

**Импорт диалогового арта — Sprite, PPU 128, Bilinear, без mipmap.**
Дефолт Unity для PNG (`Default` + mipmap) для диалогового кадра неверен:
без правки арт уезжает в UI как обычная текстура.

**Массовые вариации: инструмент утрачен, принцип сохранён.** Сетка из
11 осей (age × build × see × pose × gaze × express × light × bg × prop ×
garment × mark) с выбором через `sha256("ось:индекс") % len` — реализация
`grid.py` — удалена при уборке. Восстанавливать по описанию.
**Главное: разнообразие даёт сетка, а не модель.** Линейная арифметика
по индексу НЕ работает — три подхода подряд дали 862 → 738 → 120
уникальных из 1000, потому что линейный сдвиг даёт фазовые корреляции
между осями. Согласованность осей — структурой (словарь с ключом), а не
фильтром.

Процедурный генератор (`.workbuddy-ai/tools/art_*.py`) — **отработанный
путь**, оставлен как источник палитры: значения цвета для GDD 07 берутся
из `art_common.py`.

## Ориентиры визуала

Scorched Sun и Darkwood. Взяты: холодная обесцвеченная палитра, жёсткая
граница видимой области (не «полутемно», а черно), персонаж 3–4 % высоты
кадра, виньетка и зерно. Боевые кадры Scorched Sun от первого лица в
референс не берутся.
