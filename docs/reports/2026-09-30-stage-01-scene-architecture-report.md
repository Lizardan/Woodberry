# Stage Report — Stage 01 Scene Architecture

> **Примечание.** Отчёт описывает Stage 01 на момент его закрытия: 29/29 EditMode
> и 19/19 PlayMode. Следующий этап (`Stage 02 — 2D Player and Animation`, ADR 0007)
> перевёл игрока и камеру в 2D и добавил тесты анимации; набор вырос до 36/36 и
> 21/21. Числа выше оставлены как есть, чтобы отчёт оставался достоверной
> записью о том, что было сделано тогда.

## Stage

Stage 01 — Scene Architecture. Дата: 2026-09-30.
Stage file: `docs/stages/stage-01-scene-architecture.md`.
Предыдущий: Stage 00 (`3be5cb8`).

## Objective

Три сцены с разным назначением: `Bootstrap` собирает и персистит глобальные
сервисы, `Menu` — вход в игру, `Game` — геймплей. Переходы работают, а не
нарисованы.

## Ключевая находка этапа

Сериализованная ссылка на объект **из другой сцены** Unity отбрасывает молча.
Проверено экспериментом: в YAML попадает `{fileID: 0}` — теряется ссылка уже
при сохранении, не при загрузке. Воспроизводится при живом объекте и
загруженной сцене-доноре.

Следствие: подход Stage 00 (`PlayerController._bootstrap → GameBootstrap`)
работал только пока bootstrap и игрок лежали в одной сцене. При разнесении
игрок тихо остался бы без ввода — компиляция чистая, консоль чистая, в
редакторе всё выглядит исправно.

Схема Stage 00 была в коммите `3be5cb8` и была неверной для трёх сцен.
Исправлено в этом этапе.

## Implemented

### Core
- `Services/ServiceRegistry` — единственный канал доступа к глобальным сервисам.
  Пишет только composition root. Сбрасывается на `SubsystemRegistration`.
  `Unregister` тип-ориентированный: вариант `Unregister(instance)` выводил бы
  `T` из статического типа аргумента и искал бы ключ реализации вместо ключа
  интерфейса — молча ничего не снимая.
- `Scenes/SceneId` + `ScenePaths` — единственное место, где сцены сопоставлены
  с файлами. Есть обратное сопоставление `TryGetId` для реакции на смену сцены.
- `Scenes/ISceneLoader` + `UnitySceneLoader` — уход в сцену как намерение.
  Единственный класс в проекте, знающий про `SceneManager`. Не `MonoBehaviour`:
  экземпляр создаёт composition root, корутину крутит переданный хост.
- `GameBootstrap` — создаёт сервисы, регистрирует, `DontDestroyOnLoad`,
  уводит в меню. Защита от второго экземпляра. Геймплейный ввод включается
  только в игровой сцене.

### UI
- `Menu/MainMenuController` — презентация. Кнопка Play пишет намерение
  `ISceneLoader.Load(SceneId.Game)` и гасит повторные клики. `SceneManager`
  UI не знает.

### Gameplay
- `PlayerController` — поле `_bootstrap` удалено. Если `Initialize` не вызывали,
  ввод берётся из `ServiceRegistry` в `Start`. Приоритет `Initialize` сохранён.

### Сцены
| Сцена | Индекс | Содержимое |
|---|---|---|
| `Bootstrap.unity` | 0 | `GameBootstrap` |
| `Menu.unity` | 1 | Main Camera, EventSystem (`InputSystemUIInputModule`), Canvas с заголовком и кнопкой Play |
| `Game.unity` | 2 | Игрок, Main Camera + `TopDownCameraRig` с целью |

`Woodberry_Prototype.unity` переименована в `Game.unity` без потери GUID.
`GameBootstrap` из неё удалён — он живёт только в `Bootstrap`.

## Contract changes

- **ADR 0006** — архитектура сцен и реестр сервисов. Фиксирует, почему
  сериализованные ссылки между сценами не работают, и почему реестр — не
  отмена правила композиции, а его следствие.
- **ADR 0005** дополнен уточнением: статический доступ отвергнут для
  `InputReader`, но `ServiceRegistry` — не то же самое; писатель один.
- `docs/specs/core-gameplay.md` — новая секция «Сцены и переходы».
- `docs/specs/asset-standards.md` — таблица трёх сцен, имена без префикса.
- `AGENTS.md` — межсценовые ссылки запрещены явно, `ServiceRegistry` назван
  единственным разрешённым статическим доступом.

## Files affected

```
Assets/Scripts/Core/GameBootstrap.cs                     (изменён)
Assets/Scripts/Core/Services/ServiceRegistry.cs          (новый)
Assets/Scripts/Core/Scenes/SceneId.cs                    (новый)
Assets/Scripts/Core/Scenes/ISceneLoader.cs               (новый)
Assets/Scripts/Core/Scenes/UnitySceneLoader.cs           (новый)
Assets/Scripts/Gameplay/Player/PlayerController.cs       (изменён)
Assets/Scripts/UI/Menu/MainMenuController.cs             (новый)
Assets/Tests/EditMode/ServiceRegistryTests.cs            (новый)
Assets/Tests/PlayMode/SceneFlowTests.cs                  (новый)
Assets/Tests/PlayMode/PlayerControllerTests.cs           (изменён)
Assets/Tests/PlayMode/Woodberry.Tests.PlayMode.asmdef    (добавлена ссылка Woodberry.UI)
Assets/Woodberry/Scenes/Bootstrap.unity                  (новый)
Assets/Woodberry/Scenes/Menu.unity                       (новый)
Assets/Woodberry/Scenes/Game.unity                       (переименована из Woodberry_Prototype.unity)
ProjectSettings/EditorBuildSettings.asset                (порядок 0/1/2)
docs/adr/0006-scene-architecture-and-service-registry.md (новый)
docs/adr/0005-input-abstraction.md, docs/adr/README.md   (изменены)
docs/specs/core-gameplay.md, docs/specs/asset-standards.md (изменены)
AGENTS.md                                                (изменён)
```

## Инициализация с экраном загрузки (дополнение к Stage 01)

Отдельным запросом: `Bootstrap.unity` получил камеру и экран загрузки, а
composition root перестал уходить в меню сразу.

### Что сделано

**Core** — пайплайн инициализации, UI о нём ничего не знает:
- `Bootstrap/IStartupProgress` — контракт прогресса. Живёт дольше сцены, поэтому
  это сервис, а не поле компонента.
- `Bootstrap/IStartupStep` — шаг. `IEnumerator`, потому что реальные шаги
  (загрузка ассетов, подключение к сессии) асинхронны.
- `Bootstrap/StartupRunner` — выполняет шаги последовательно, делит полосу поровну.
- `Bootstrap/StartupProgress` — считает и уведомляет подписчиков. Событие
  поднимается только на заметное изменение, иначе подписчик пересобирает строку
  впустую — это видно как мусор в GC.
- `Bootstrap/RegisterServicesStep` — единственный реальный шаг сейчас.

**UI** — только презентация:
- `Bootstrap/StartupLoadingView` — статус, полоса, процент. **Не** решает, когда
  переходить: это делает Core. Кэширует последнюю отрисованную строку.

**Сцена `Bootstrap.unity`**: `Loading Camera`, `LoadingUI` с заголовком, строкой
статуса, треком и заливкой полосы (`Image.type = Filled`) и процентом.

**Порядок запуска**: `Awake` регистрирует `IStartupProgress` (экран читает его в
своём `Start`, а порядок `Start` между объектами сцены не гарантирован) →
`Start` запускает пайплайн → переход в `Menu` **только** после `IsComplete`.

### Contract change

ADR 0005 требовал `EnableGameplayInput` / `DisableGameplayInput` в `IInputReader`,
но в Stage 00 они остались только в реализации. Из-за этого `GameBootstrap` держал
поле типа `InputSystemReader`, и поиск сервиса по интерфейсу не компилировался.
Интерфейс приведён к ADR, реализация переименована, `FakeInputReader` дополнен.

### Known issue — честно

**Экран загрузки сейчас не видно.** Единственный реальный шаг занимает ~3 кадра,
поэтому полоса появляется и уходит. Подтверждено скриншотом: к моменту съёмки
приложение уже было в меню.

Решение оставлено пользователем сознательно: искусственные задержки ради
красивой полосы врали бы игроку. Механизм готов и заработает сам, как только
появятся настоящие шаги — они добавляются в `GameBootstrap.BuildSteps()`.
Альтернативы, если понадобится: минимальное время на экране против вспышки,
либо реальный асинхронный шаг (например, прогрев `UnloadUnusedAssets`).

Ещё: шаги делят полосу поровну. С появлением шага, заметно превосходящего
остальные по времени, полоса начнёт врать — потребуется вес у шага.

## Verification performed

### Automated

| Проверка | Команда | Результат |
|---|---|---|
| Компиляция | `refresh_unity(compile: "request")` → `read_console(types: ["error","warning"])` | 0 ошибок, 0 предупреждений |
| Инварианты слоёв | `RuntimeLayerValidator.Validate()` | 0 problems |
| EditMode | `run_tests(EditMode, Woodberry.Tests.EditMode)` → `get_test_job` | `succeeded`, **29/29**, `resultState: Passed` |
| PlayMode | `run_tests(PlayMode, Woodberry.Tests.PlayMode)` → `get_test_job` | `succeeded`, **19/19**, `resultState: Passed` |

PlayMode-тесты покрывают: регистрация сервисов, персистентность bootstrap
(объект в сцене `DontDestroyOnLoad`), ввод выключен в меню, клик Play →
загрузка `Game` и разгрузка `Menu`, движение игрока на вводе из реестра,
единственность экземпляра bootstrap, а также экран загрузки: сервис прогресса
зарегистрирован до смены сцены, статус и полоса назначены, прогресс монотонен
и заканчивается единицей, имя шага видно игроку, переход в меню происходит
только после завершения инициализации.

### Manual

Прогон цикла в Play Mode, замеры через `execute_code`:

```
1) scenes=1: Menu
2) bootstrap in DontDestroyOnLoad | inputEnabled in MENU=False | registryCount=2 | loaderCurrent=Menu
3) playButton=True interactable=True
4) clicked -> interactable=False
```

Диагностика при неверном входе: запуск `Menu.unity` напрямую, без bootstrap,
даёт внятную ошибку вместо тихого отказа:

```
MainMenuController: ISceneLoader не зарегистрирован.
Сцена открыта не через Bootstrap — переход в игру невозможен.
```

### Not verified

- **Полный цикл вручную в Play Mode не доведён до конца.** Редактор в этом
  окружении не тикает, когда окно не в фокусе: `mcpforunity://editor/state`
  показывал `is_focused: false`, `phase: playmode_transition`, а `sequence`
  не рос. Из-за этого корутина перехода не продвинулась. Это ограничение
  окружения, а не код: тот же цикл полностью проходит в PlayMode-тестах, которые
  крутит тест-раннер. Проверка вручную требует открытого окна редактора.
- **Player-сборка не выполнялась.** Границы Editor-only проверены статически
  (валидатор + `includePlatforms`). Из PlayMode-сборки убран `UnityEditor`,
  но player-сборка тестов не запускалась.
- **Реальный ввод с клавиатуры/геймпада не проверен** — нет доступа к устройству.
- **Производительность не измерялась.** Профилирование не выполнялось.
- **Визуал меню не оценивался.** Кнопка и заголовок — минимальная заглушка,
  нужная для проверки перехода.

## Acceptance criteria status

Все критерии закрыты. Подробности — в
`docs/stages/stage-01-scene-architecture.md`.

## Known issues

- В `Menu.unity` нет Directional Light. Для Screen Space Overlay Canvas это
  не влияет на отображение, и добавление света — визуальная задача, а не
  foundation. Намеренно не добавлялось.
- Кнопка Play гасится после нажатия, но экран загрузки не показывается:
  переход занимает один кадр. Полноценный экран загрузки — задача UI-этапа.

## Tech debt

- Реестр — глобальное мутабельное состояние. ADR 0006 ставит порог
  пересмотра: больше трёх глобальных сервисов — пора заводить агрегат.
- Создание `GameBootstrap` в произвольной сцене уводит приложение в меню.
  Защита от дубля есть, но в тестах это ловушка: тест, создавший bootstrap
  ради ввода, выгружает сцену из-под себя. Такой тест в Stage 00 был и упал.
- `ISceneLoader.Current` меняется только после успешной загрузки. Если
  понадобится «сцена, которую мы просим грузить», нужен отдельный признак.
- Скорости и параметры камеры по-прежнему `[SerializeField]` с литералами,
  а не SO-конфиг. Унаследовано из Stage 00, `core-gameplay.md` этого требует.
- Спринт без выносливости, движение в `Update` — унаследовано из Stage 00.

## Assumptions

- Три сцены достаточно для всего приложения. Late join и несколько
  одновременных игровых сцен не проектировались.
- `LoadSceneMode.Single` верен: постоянно живой паузы-оверлея не планируется.
  Если он понадобится, модель перейдёт на аддитивную.
- Ввод — сервис сессии, а не свойство объекта сцены, поэтому им владеет
  bootstrap, а не сцена `Game`.

## Risks

- **Реестр маскирует зависимости.** Код выглядит автономным, а сервис
  приходит откуда-то. Митигация: `Initialize` остаётся основным путём,
  реестр — только для кросс-сценовых глобальных сервисов.
- **Тесты зависели от порядка выполнения.** `SceneFlowTests` оставлял bootstrap
  в `DontDestroyOnLoad` и сервисы в реестре; тест «игрок без ввода» рассчитан
  на пустой реестр. Прогон проходил только потому, что NUnit случайно ставил
  класс первым. Исправлено очисткой в `SetUp`/`TearDown` обоих классов.
- **Ошибка в `Unregister` прошла бы незамеченной**, если бы не тест: снятие
  по экземпляру выводило `T` из статического типа аргумента и искало ключ
  реализации вместо ключа интерфейса. Реестр не опустошался бы вовсе.

## Manual QA checklist

1. Открыть `Assets/Woodberry/Scenes/Bootstrap.unity`, нажать Play.
   Через кадр должна открыться `Menu` с заголовком WOODBERRY и кнопкой PLAY.
2. В иерархии виден `GameBootstrap` со сценой `DontDestroyOnLoad`.
3. Нажать PLAY. Сцена `Menu` выгружается, загружается `Game` с игроком.
4. WASD двигает игрока, камера следует сверху. В меню WASD не делает ничего.
5. `Window → General → Test Runner`: EditMode 29/29, PlayMode 13/13.
6. `Woodberry → Validate Runtime Layers` — ни одной ошибки.
7. Запустить `Menu.unity` напрямую без bootstrap: ожидается ошибка про
   незарегистрированный `ISceneLoader` и неактивная кнопка.

## Recommended next stage

Stage 02 — выносливость и камера по плану `docs/stages/stage-02-*`, либо
(предпочтительно) вертикаль взаимодействия: игрок находит интерактивный
объект, отправляет intent, получает результат. Взаимодействие — первое
место, где понадобится `INetworkService` как настоящий seam, и момент,
когда сетевой стек (ADR 0003) перестанет быть отложенным.
