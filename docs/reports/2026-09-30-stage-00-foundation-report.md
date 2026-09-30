# Stage Report — Stage 00 Foundation

> **Примечание (Stage 01, 2026-09-30).** Этот отчёт описывает состояние на момент
> Stage 00 и не редактировался по существу. С тех пор изменилось:
> - сцена `Woodberry_Prototype.unity` разделена на `Bootstrap.unity`, `Menu.unity`
>   и `Game.unity` — см. `docs/adr/0006-scene-architecture-and-service-registry.md`;
> - `GameBootstrap` переехал в отдельную персистентную сцену `Bootstrap`;
> - поле `PlayerController._bootstrap` удалено, ввод берётся из `ServiceRegistry`.
>
> Ссылки на старые пути ниже оставлены как есть, чтобы отчёт оставался
> достоверной записью о том, что было сделано тогда. Актуальные пути —
> в `docs/specs/asset-standards.md`.

## Stage

Stage 00 — Foundation. Дата: 2026-09-30.
Stage file: `docs/stages/stage-00-foundation.md`.

## Objective

Дать компилируемую вертикаль «ввод → движение → камера», сделать границы слоёв
компилируемым контрактом вместо соглашения, и закрыть это тестами. Без сети,
здоровья, ИИ и UI.

## Implemented

### Core (`Assets/Scripts/Core/`)
- `GameBootstrap` — composition root. Порядок `-1000`, создаёт `InputSystemReader`
  в `Awake`, включает в `OnEnable`, выключает в `OnDisable`/`OnDestroy`.
  Парные вызовы идемпотентны, двойного выключения нет.
- `IInputReader` — единственный seam ввода для геймплея.
- `InputSystemReader` — реализация поверх сгенерированной обёртки Input System.
- `Generated/InputSystem_Actions.cs` — сгенерировано `InputActionCodeGenerator 1.20.0`
  из `Assets/InputSystem_Actions.inputactions`, namespace `Woodberry.Core.Input`.
  Не `ScriptableObject`: самодостаточный класс со встроенным JSON, поэтому в сцену
  ассет не тащится.

### Gameplay (`Assets/Scripts/Gameplay/Player/`)
- `PlayerMovement` — чистое статическое правило: нормализация диагонали, мёртвая
  зона, `Y` всегда 0, безопасные краевые случаи (нулевая/отрицательная скорость,
  нулевой `dt`). Тестируется без сцены и без MonoBehaviour.
- `PlayerController` — `CharacterController.Move` по XZ, спринт-множитель,
  разворот по направлению движения, зависимости снаружи через `Initialize`.
  Без ввода — предупреждение в редакторе, а не молчание.

### CameraRig (`Assets/Scripts/CameraRig/`)
- `TopDownCameraRig` — фиксированный угол 90°, следование за целью через
  `SmoothDamp`, снап на `SetTarget`, мёртвая зона позиции, ограничение
  `maxFollowSpeed` против пролёта карты при телепорте.

### Editor (`Assets/Scripts/Editor/`)
- `RuntimeLayerValidator` — проверяет отсутствие `UnityEditor` в runtime-коде
  (с учётом легальных `#if UNITY_EDITOR` блоков и корня `Assets/Scripts/`),
  наличие asmdef у каждого слоя и `includePlatforms: ["Editor"]` у
  `Woodberry.Editor.asmdef`. Меню: `Woodberry → Validate Runtime Layers`.

### Сборки
- 11 `.asmdef`: по одной на слой (`Core`, `Gameplay`, `AI`, `Net`, `UI`,
  `CameraRig`, `Audio`, `Save`), плюс `Woodberry.Editor` и два тестовых.
- `Woodberry.Core` не ссылается ни на одну `Woodberry.*` сборку — это гарантия
  компилятора, а не соглашение. Проверяется тестом.

### Сцена
- `Assets/Scenes/Bootstrap.unity` → `Assets/Woodberry/Scenes/Woodberry_Prototype.unity`.
  GUID сохранён (`99c9720ab356a0642a771bea13969a05`), `EditorBuildSettings` обновлён.
- В сцене: `GameBootstrap`, `Player` (CharacterController + PlayerController),
  `Main Camera` + `TopDownCameraRig` с целью, назначенной на `Player`.

### Тесты
- EditMode `PlayerMovementTests` — 11 тестов.
- EditMode `RuntimeLayerValidatorTests` — 6 тестов.
- PlayMode `PlayerControllerTests` — 6 тестов.
- PlayMode `TopDownCameraRigTests` — 4 теста.

## Contract changes

- `AGENTS.md` дополнен секцией про сборки: одна сборка на слой, `references`
  объявляются явно, `Woodberry.Core` без ссылок на `Woodberry.*`,
  `Woodberry.Editor.asmdef` ограничен `Editor`. Проверка — меню-валидатор плюс
  EditMode-тесты.
- `docs/specs/asset-standards.md`: сцены игровых уровней лежат в
  `Assets/Woodberry/Scenes/`, отдельной `Assets/Scenes` больше нет. Запрещено
  оставлять скриншоты и артефакты проверки в `Assets/`.
- `IInputReader` — контракт ввода зафиксирован в коде: `Move`, `SprintHeld`,
  `InteractPressed`.

## Files affected

Новые:

```
Assets/InputSystem_Actions.inputactions (+ .meta)
Assets/Scripts/Core/Woodberry.Core.asmdef
Assets/Scripts/Core/GameBootstrap.cs
Assets/Scripts/Core/Generated/InputSystem_Actions.cs
Assets/Scripts/Core/Input/IInputReader.cs
Assets/Scripts/Core/Input/InputSystemReader.cs
Assets/Scripts/Gameplay/Woodberry.Gameplay.asmdef
Assets/Scripts/Gameplay/Player/PlayerMovement.cs
Assets/Scripts/Gameplay/Player/PlayerController.cs
Assets/Scripts/AI/Woodberry.AI.asmdef
Assets/Scripts/Net/Woodberry.Net.asmdef
Assets/Scripts/UI/Woodberry.UI.asmdef
Assets/Scripts/CameraRig/Woodberry.CameraRig.asmdef
Assets/Scripts/CameraRig/TopDownCameraRig.cs
Assets/Scripts/Audio/Woodberry.Audio.asmdef
Assets/Scripts/Save/Woodberry.Save.asmdef
Assets/Scripts/Editor/Woodberry.Editor.asmdef
Assets/Scripts/Editor/RuntimeLayerValidator.cs
Assets/Tests/EditMode/Woodberry.Tests.EditMode.asmdef
Assets/Tests/EditMode/PlayerMovementTests.cs
Assets/Tests/EditMode/RuntimeLayerValidatorTests.cs
Assets/Tests/PlayMode/Woodberry.Tests.PlayMode.asmdef
Assets/Tests/PlayMode/PlayerControllerTests.cs
Assets/Tests/PlayMode/TopDownCameraRigTests.cs
Assets/Woodberry/Scenes/Woodberry_Prototype.unity
```

Изменённые: `AGENTS.md`, `docs/specs/asset-standards.md`,
`docs/stages/stage-00-foundation.md`, `ProjectSettings/EditorBuildSettings.asset`.

Удалённые: `Assets/Scenes/` (перенесена), временные `Assets/Screenshots/`.

## Verification performed

### Automated

| Проверка | Команда | Результат |
|---|---|---|
| Компиляция | `refresh_unity(compile: "request")` → `read_console(types: ["error","warning"])` | 0 ошибок, 0 предупреждений |
| Инварианты слоёв | `RuntimeLayerValidator.Validate()` | 0 problems |
| EditMode | `run_tests(mode: "EditMode", assembly_names: ["Woodberry.Tests.EditMode"])` → `get_test_job` | `succeeded`, 17/17 passed, `resultState: Passed` |
| PlayMode | `run_tests(mode: "PlayMode", assembly_names: ["Woodberry.Tests.PlayMode"])` → `get_test_job` | `succeeded`, 10/10 passed, `resultState: Passed` |
| Цикл зависимостей | `RuntimeLayerValidatorTests.CoreAssembly_HasNoReferencesToOtherWoodberryAssemblies` | passed (в составе 17) |
| Сцена: цель камеры | сериализованная ссылка в YAML | `_target: {fileID: 1403394980}` → трансформ `Player` |

### Manual

Play Mode, реальная сцена, измерено через `execute_code`:

```
playerPos=(0.000, 1.000, 0.000)
camRigPos=(0.000, 13.000, 2.000)
expectedCamPos=(0.000, 13.000, 2.000)
offset=(0.000, 12.000, 2.000)
camEuler=(90.0, 0.0, 0.0)
mainCamMatches=True
currentSpeed=0
```

Камера стоит ровно над игроком на заданной высоте, угол 90°, является Main Camera.
До исправления `_target` в сцене не было, и камера молча стояла на месте —
это Critical, найденный независимым `code-reviewer`.

Привязки ввода подтверждены по `Assets/InputSystem_Actions.inputactions`:
`Move` — WASD, стрелки, `<Gamepad>/leftStick`; `Sprint` — `leftShift` / `leftStickPress`;
`Interact` — `E` / `buttonNorth`.

Редактор после Play Mode: `read_console(types: ["error","warning"])` — пусто.

### Not verified

- **Реальный ввод с физической клавиатуры или геймпада не проверен.** В этом
  окружении нет доступа к устройству ввода. Подтверждено только наличие
  корректных привязок в asset и то, что `IInputReader` разрешается в
  `InputSystemReader`. Движение в PlayMode проверено через управляемую заглушку
  ввода в тестах, а не через реальные клавиши.
- **Полная player-сборка не выполнялась.** `manage_build(action: "build")` не
  запускался: он создаёт артефакты в проекте, а acceptance criteria этого не
  требовали. Границы Editor-only проверены статически (валидатор + `includePlatforms`),
  а не фактической сборкой player.
- **Производительность не измерялась.** Профилирование, frame time, draw calls,
  GC-аллокации в кадре не снимались. Budget из `performance-budget.md` не проверялся.
- **Сетевой аспект не проверялся** — он вне scope Stage 00 и сетевого стека
  в проекте нет.

## Acceptance criteria status

| Критерий | Статус |
|---|---|
| Проект компилируется без ошибок | Done |
| Player assembly не ссылается на `UnityEditor` | Done |
| `Woodberry.Core` не ссылается на `Woodberry.Gameplay.*` | Done |
| Игрок передвигается по XZ в Play Mode | Done |
| Камера следует за игроком сверху, не дёргается | Done |
| `IInputReader` — единственная точка доступа к вводу | Done |
| EditMode-тесты проходят | Done — 17/17 |
| PlayMode-тесты проходят | Done — 10/10 |
| Прототипная сцена в `Assets/Woodberry/Scenes/` | Done |
| Все `.meta` присутствуют | Done |
| Stage report создан | Done — этот файл |

## Known issues

- Игрок — голый `CharacterController` без визуального меша. В сцене почти ничего
  не видно: это ожидаемо для foundation-этапа, визуал вне scope.
- Сцена практически не освещена. Тёмный скриншот — следствие этого, а не баг рендера.
- Локаль `Assets/Scripts/Editor` находится внутри дерева, которое сканирует
  валидатор, но исключён из списка `RuntimeLayers`, поэтому editor-код в проверку
  на `UnityEditor` не попадает.

## Tech debt

- `PlayerController` зависит от конкретного `GameBootstrap` (MonoBehaviour из `Core`),
  а не от абстракции. Направление слоёв не нарушено, но при появлении сетевого
  seam это захочется заменить на минимальный `IInputProvider` в `Core`.
- Скорости и параметры камеры задаются `[SerializeField]` с литералами, а не
  SO-конфигом. `docs/specs/core-gameplay.md` требует SO-конфиг по умолчанию.
  Для foundation-этапа это осознанный компромисс — зафиксировано здесь,
  чтобы следующий этап не унаследовал несуществующий контракт.
- Спринт реализован множителем скорости без выносливости. Спека требует
  расхода ресурса. `IInputReader` уже отдаёт `SprintHeld`, то есть контракт
  «спринт бесплатный» пока зафиксирован в коде.
- Движение считается в `Update` с плавающим `Time.deltaTime`. `coop-networking.md`
  требует симуляцию 20–30 Hz. `PlayerMovement` вынесен в чистую функцию именно
  для того, чтобы перенос на фиксированный тик был дешёвым.
- Список слоёв продублирован в `RuntimeLayerValidator`, `AGENTS.md` и stage file.
  Стоит выделить один источник истины.
- `ProjectSettings/VFXManager.asset` и `Packages/com.unity.probuilder/Settings.json`
  попали в изменения как побочный шум авто-миграции Unity 6 и редактора.
  К Stage 00 отношения не имеют.

## Assumptions

- `InputSystem_Actions.inputactions` из шаблона Unity подходит как стартовый набор
  действий. `Look`, `Attack`, `Crouch`, `Jump` пока не используются геймплеем,
  но остаются в asset — удалять их в foundation-этапе нельзя.
- `CharacterController` — правильный коллайдер для кинематического top-down
  движения, как и требует `core-gameplay.md`.
- Сцена прототипа может остаться почти пустой: цель Stage 00 — компилируемая
  вертикаль, а не уровень.

## Risks

- Связка игрок → камера идёт вниз по зависимостям, поэтому `GameBootstrap` не может
  вызвать `SetTarget`. Связь держится на сериализованной ссылке в сцене. Отсутствие
  этой ссылки не даёт ошибок и не видно в редакторе — камера просто стоит на месте.
  Закрыто PlayMode-тестом `SetTarget_SnapsCameraToTarget_WithoutOvershoot` и
  замером в Play Mode.
- Поведение при спавне/телепорте игрока на реальной сцене не проверялось:
  `maxSpeed` ограничивает пролёт, но порог не подбирался на глаз.

## Manual QA checklist

1. Открыть `Assets/Woodberry/Scenes/Woodberry_Prototype.unity`, нажать Play.
2. WASD — игрок двигается по XZ. Геймпад — левая стик работает.
3. `Shift` — ускорение.
4. Камера: игрок уехал — камера плавно следует с задержкой; стоит на месте —
   не дрейфует.
5. `Woodberry → Validate Runtime Layers` в меню — ни одной ошибки в консоли.
6. Window → General → Test Runner: EditMode 17/17, PlayMode 10/10.
7. Выключить `GameBootstrap` в иерархе — в редакторе появится предупреждение
   `IInputReader не назначен`, игрок стоит на месте. Это ожидаемо.
8. Снять ссылку `Target` у `TopDownCameraRig` в инспекторе, запустить Play —
   камера останется неподвижной, ошибок в консоли нет. Ожидаемо, но полезно
   знать про этот отказ.

## Recommended next stage

Stage 01 — вертикаль взаимодействия: игрок находит интерактивный объект в радиусе,
видит подсветку, отправляет intent, получает результат. Заодно вводит
`INetworkService` в `Core` как настоящий seam, чтобы Stage 03 не начинал с нуля.
Перед реализацией стоит решить две вещи, зафиксированные как tech debt:
SO-конфиг для скоростей и модель выносливости для спринта.
