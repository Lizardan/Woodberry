# Woodberry — грабли проекта между сессиями

Детали — в репозитории (`AGENTS.md`, `docs/`); дневные логи — `memory/YYYY-MM-DD.md`.
Кооп-детективный хоррор, вид сверху; охотник на ведьм: город-хаб → деревня
(день — расследование, ночь давит, приговор/казнь). GDD **выше** specs. Открыто:
`specs/game-vision.md` описывает ДРУГУЮ петлю; ADR 0003 (Proposed) блокирует кооп.

## Unity MCP
Мост живёт в редакторе, TCP `127.0.0.1:<порт>`. **Порт не хардкодить:** плагин
10.3.0 → 6401, откат на 6400; поиск — `UNITY_MCP_PORT` → `~/.unity-mcp/unity-mcp-port-<sha1(Assets)[:8]>.json`
→ общий → перебор 6400–6410. Домен-релоад пересоздаёт мост: ~10 с никто не
отвечает, «мост мёртв» — ложный вывод. Пин Python-сервера в `opencode.json` обязан
совпадать с версией плагина. Клиенты: `.workbuddy-ai/tools/unity_mcp.py`,
`unity_code.py` (C#-сниппет из файла). Команды — `docs/runbooks/unity-automation.md`.

- `execute_code` компилируется Roslyn против всех загруженных сборок, минуя asmdef;
  внутри НЕТ `UnityEditor.Animations`/`.SceneManagement` — полным именем.
- Заблокировано: `AssetDatabase.DeleteAsset`, `System.IO.File.Delete`,
  `Process.Start`, `while(true)`. Удалять — `manage_asset(action:"delete")`,
  перезаписывать — `SaveAsPrefabAsset`.
- Тесты: `run_tests {"mode":"EditMode"|"PlayMode"}` (`testMode` игнорируется).
- Скриншоты идут в `Assets/Screenshots/` — убирать, попадут в билд.

**Play Mode:** domain И scene reload ОТКЛЮЧЕНЫ (`m_EnterPlayModeOptionsEnabled: 1`,
`m_EnterPlayModeOptions: 3`). Статика живёт между запусками — сбрасывать через
`[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]`,
иначе второй запуск падает, а первый выглядит исправным.

**Слои/сервисы.** `Occluder` = 8, `VisionMask` = 9; окно намеренно НЕ в `Occluder`.
Composition root — `Bootstrap.unity`; `PlayerController` берёт ввод только из
`ServiceRegistry` — сцены мимо `Bootstrap` стоят.

## Арт: стандарт
Стенд `F:\ai` (Z-Image + ComfyUI 0.38.0, RX 9070 XT 16 ГБ, ROCm 7.2.1), вне
репозитория; `F:\ai\README.md`, рецепт `F:\ai\portraits\deliver\РЕЦЕПТ.md`, лицензии
`F:\ai\LICENSES.md` (Apache 2.0 / MIT — требование). Навык `portrait-art-generation`.
Фон `856×480`, персонаж `480×720`, посадка 90 % высоты, низ прижат, центр по X.
Арт — `Art/Dialogue/Portraits|Backgrounds/<группа>/<Пул>/`, имя `<Пул>_<NN>_<Стиль>.png`;
88 пулов, заполнено 15 портретов и 7 фонов. **Импорт — Sprite, PPU 128, Bilinear,
без mipmap** (дефолт Unity неверен).

**Скорость.** `--cache-none` стоил 2.5x (29.8 → 12.0 с) и ничего не защищал; флаги
в `ArtStudio/config.json` → `comfyFlags`. Шаги — не узкое место (1 шаг 26.2 с против
10 шагов 28.5 с): время кадра — фиксированные накладные. 195–312 с/кадр = чужой
процесс на GPU. **Qwen-Image-2.1-Turbo не берём:** не ускорит + Research-only
лицензия (у 1.0 был Apache 2.0); по той же причине отказались от FLUX VAE.

## 3D: стоит нативно, весов нет (2026-10-11)
ComfyUI 0.38.0 уже содержит: Hunyuan3D v2/v2.1, TRELLIS.2, Stable3D, `Save3D`→GLB,
postprocess (`DecimateMesh`, `RemeshMesh`, `UnwrapMesh`, `RenderMesh` — меш в
картинку) и **SAM 3D Body**: картинка → риг `MHR_POSE_DATA` (MHR70, LBS, кисти,
мимика) → `BuildPoseFile` (анимированный GLB или BVH). **CUDA-ядер нет** (ни spconv,
ни nvdiffrast, ни flash_attn) — всё на torch; `torch 2.9.1+rocm7.2.1`,
`cuda.is_available()` = True, ROCm-риск снят. **Нет только весов.** Kimodo — внешний
пакет `ComfyUI-Kimodo` (`KimodoSample` → `KIMODO_POSE_DATA`). Blender — **5.2**
(папка 4.5 без exe). Woodberry — 2D-спрайты (ADR 0007/0009), 3D только как пре-рендер.

## Арт-студия
**Два объекта, два набора действий.** Кандидат (`ArtStudio/review/`): принять =
ПЕРЕНЕСТИ файл, отклонить = перегенерировать тот же id. Кадр проекта: принять =
ЗАПИСАТЬ отметку в журнал, брак = перегенерировать следующим номером. Кандидаты
читаются **с диска** (`ReviewScanner`), не из событий — те не переживают перезапуск
и оставляют призраков. Действия — только в правой панели, её заголовок называет объект.

**Раскладка.** Правая панель = ТРИ взаимоисключающие страницы под `rightTabs`
(`tabObject`/`tabPrompts`/`tabGenerator`) → `detailPanel`/`promptsPanel`/
`generatorPanel`, переключает `SetRightPage(RightPage)`. Тулбара в окне нет. ОДНА
кнопка `standButton` на драйвер и ComfyUI, фазы `StandControl`. Слева «На ревью (N)»
верхом + дерево. Нижняя панель — ВНУТРИ `.panel-center`. Миниатюры 2:3 (96×144).
«Свободно» = свободные id МИНУС кандидаты. У `.body` обязателен `flex-basis: 0`,
нижние панели `flex-shrink: 0`. Кэш `Texture2D` проверяет живость объекта:
домен-релоад убивает текстуры, а словарь переживает.

**Смысл и шум разделены.** `infoActivity` — ТОЛЬКО ошибка; последняя строка stdout
`shoot.py` уходит в приглушённый `generationLog`.

**Уборка и ротация — контракт.** `accept_candidate` удаляет исходный PNG в `out`
(точный файл текущего id, не каталог; ошибка → `warn`, приёмку не роняет).
`rotate_if_large()` → `<имя>.prev` (ОДИН предшественник): `driver.log` 4 МБ,
`commands.jsonl` 2 МБ. `events.jsonl` обнуляется при старте.

**`EventWriter` держит дескриптор открытым**, flush после каждой записи: событий
тысячи за прогон, открывать файл на каждое нельзя, а Unity читает по смещению.

**Приёмка не берёт устаревшее:** `_is_fresh_output(path, run_started)` — mtime позже
старта прогона. `regen_targets` строит каталог ОДИН раз (`_build_path_to_id`), номер
берёт через `free.pop(0)` — иначе две метки одного пула получали один id.

## Стенд `F:\ai\portraits`
- **`seg.py --only`: `default=None`.** Без флага — вся папка, `--only` без значений —
  НИЧЕГО. При `default=()` первые два неотличимы, и пустой список молча вырезал всю
  папку (баг «заказ 1 кадра → прошёл по 103»). Вырезка идёт по `--only` из
  `shoot.py cut_characters`. **Фоны не вырезаются.**
- **`scene.GEN` — источник истины для steps/cfg.** Генерацию ведёт `gen.GEN`, а
  `shoot.py` печатает `scene.GEN` — два разных словаря, `shoot.py main()` их
  синхронизирует; без этого лог соврал бы.
- **Модель** Z-Image (Lumina2), Q8_0 GGUF, дистиллирована: `cfg 1.0`, 10 шагов,
  негатив не действует. Цена шага ≈0.26 с.

**Стоп не оставляет процессов.** `ArtDriverClient.StopEverything`: `stop` → ждать
выхода → снять процесс → добить ComfyUI по маркеру `.comfy_pid`/`comfyPid`, но только
если это реально `python` (маркер переживает перезапуск Windows). Осиротевший ComfyUI
держит 10 ГБ VRAM и замедляет прогон в разы.

**Команда сразу после старта драйвера ТЕРЯЕТСЯ** — он читает только написанное после
запуска. Окно повторяет их по приходу `ready` (`PollDriver`); забыть = «сервер не
поднимается», кнопка навсегда серая.

**Один кадр — одно задание.** Драйвер шлёт `item_added` и на повторную генерацию того
же stand-id, а `Find` ищет ПЕРВОЕ совпадение: дубль получал `item_added` и больше
ничего — «генерация идёт вечно». `JobQueue.Enqueue` переиспользует задание того же id.

**Прогресс.** Полос ДВЕ («Вся заявка» и «Текущий кадр»). Процент кадра — НЕ процент
шагов: подготовка 0→15 по времени, сэмплинг 15→92 по шагам, постобработка 92→100 по
времени. Общий считает `GenerationProgress.Overall` (`JobQueue.OverallPercent` удалён).
Молчание > 4 с — подсветка и «событий прогресса нет М:СС». Логика — `GenerationProgress.cs`.

**`artgen.py`** — `.workbuddy-ai/tools/artstudio/` (не в `ArtStudio/` — та под
.gitignore). Генерация в ОТДЕЛЬНОМ ПОТОКЕ, `stop` рвёт `shoot.py` деревом процессов.
`-u` + `PYTHONUNBUFFERED=1` обязательны. `--no-warmup`, если генерация была < 15 мин
назад. `os.kill(pid, 0)` на Windows — НЕ проверка живости; `_alive` — через
`OpenProcess` + `GetExitCodeProcess`.

**`ArtStudio/extra_pools.json`** (не в git, правится из окна). Группа фона обязана
быть из четырёх известных переносу (`copy_dialogue_art.BG_GROUP_PATH`):
`destination()` падает на незнакомой и роняет весь `catalog`.
