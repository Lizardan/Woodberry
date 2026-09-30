# Stage 00 — Foundation

Статус: **Завершён** (2026-09-30)
Коммит: `3c5d2e8` (инфраструктура OpenCode-слоя) + stage-00 (см. отчёт)
Зависит от: —
Блокирует: все остальные stages

## Goal

Проект компилируется, имеет компилируемые границы слоёв (asmdef), а игрок уже ходит по сцене и камера следует за ним сверху — с самой простой, но рабочей вертикали.

## Business value

Этот этап не даёт фичу, но превращает проект в управляемую инженерную систему.
Без него всё дальнейшее — хаотичная генерация, которую невозможно проверить и откатить.

Наглядно: сейчас в проекте 0 скриптов и 0 asmdef. После Stage 00 — работающая вертикаль
«ввод → движение → камера → тест», и границы слоёв стали проверяемыми компилятором.

## Scope

- **asmdef-файлы** — границы слоёв как компилируемый контракт
- Одна сборка на слой: `Woodberry.Core`, `Woodberry.Gameplay`, `Woodberry.AI`,
  `Woodberry.Net`, `Woodberry.UI`, `Woodberry.CameraRig`, `Woodberry.Audio`,
  `Woodberry.Save`, `Woodberry.Editor`, `Woodberry.Tests.EditMode`, `Woodberry.Tests.PlayMode`
- **asmdef-контракт**: межслойные зависимости объявлены явно через `references`,
  `Woodberry.Core` не ссылается ни на одну сборку `Woodberry.*`
- `IInputReader` + реализация поверх сгенерированной обёртки `InputSystem_Actions`
- `PlayerController` — движение по XZ через `CharacterController`
- Правило движения вынесено в статический `PlayerMovement`, чтобы тестировалось без сцены
- `TopDownCameraRig` — следование за целью сверху
- Валидатор в `Woodberry.Editor`: runtime-код не ссылается на `UnityEditor`, у каждого слоя есть asmdef
- EditMode-тесты на правило движения, PlayMode-тесты на жизненный цикл контроллера
- Перенос сцены `Assets/Scenes/Bootstrap.unity` → `Assets/Woodberry/Scenes/`
  (на момент Stage 00 сцена называлась `Woodberry_Prototype.unity`;
  в Stage 01 переименована в `Game.unity` и разделена на три сцены —
  см. `docs/adr/0006-scene-architecture-and-service-registry.md`)
- Baseline stage report

> **Решение, принятое при старте stage:** вместо «4 asmdef» сделано 11 —
> по одной сборке на слой. Причина: иначе правило «Core не ссылается на Gameplay»
> остаётся соглашением, а не гарантией компилятора.
> Сцена `Bootstrap` переименована и перенесена в `Woodberry/Scenes/`, поэтому
> отдельной папки `Assets/Scenes` больше нет.

## Out of scope

- Сеть. Вообще. Ни одного сетевого API.
- Источники света, видимость, фонарики
- Здоровье, урон, смерть
- Враги, ИИ, навигация
- Инвентарь, предметы
- Сохранение
- UI / HUD
- Настройки уровня, освещение сцены, полировка визуала
- Адресаблы, ассет-пайплайн

## Work

### Core (`Assets/Scripts/Core/`)
- Composition root: один bootstrap-объект сцены, создаёт и включает сервисы
- `IInputReader` — единственный seam ввода; `InputSystemReader` — реализация
- Инициализация сервиса выглядит так: `bootstrap.Input` создаётся в `Awake` и
  включается в `OnEnable`, а геймплей забирает его либо через `Initialize`,
  либо через сериализованную ссылку на bootstrap в `Start`.
  Отдельной «сборочной панели» нет — на этом этапе она была бы лишней
  абстракцией. См. Risks про невозможность вызова из инспектора в редакторе.

### Gameplay (`Assets/Scripts/Gameplay/Player/`)
- `PlayerController` — кинематическое движение по XZ
- `PlayerMovement` — чистое статическое правило движения, тестируется без сцены
- Никаких `Rigidbody`-физик для движения
- Никаких `FindObjectOfType` — зависимости приходят снаружи

### Camera (`Assets/Scripts/CameraRig/`)
- `TopDownCameraRig` — следует за целью, фиксированный угол сверху
- Цель задаётся снаружи: сериализованной ссылкой в сцене либо `SetTarget`
- Мёртвая зона позиции и `maxSpeed` у `SmoothDamp`, чтобы камера не дрейфовала
  на стоящем игроке и не пролетала карту при телепорте

### Editor (`Assets/Scripts/Editor/`)
- Валидатор: runtime-код не ссылается на `UnityEditor`, у каждого слоя есть
  asmdef, `Woodberry.Editor.asmdef` ограничен платформой `Editor`

### Tests
- EditMode: `PlayerMovementTests` — вектор движения, нормализация, нулевой ввод (11)
- EditMode: `RuntimeLayerValidatorTests` — инварианты слоёв (6)
- PlayMode: `PlayerControllerTests` — Awake/Start, движение, отсутствие ввода (6)
- PlayMode: `TopDownCameraRigTests` — следование за целью, мёртвая зона, потеря цели (4)

### Infra
- asmdef-файлы
- Обновлённый `.gitignore` (служебное в `.opencode/` игнорируется)

## Acceptance criteria

- [x] Проект компилируется без ошибок (`read_console(action: "get", types: ["error"])` чист)
- [x] Player assembly **не ссылается** на `UnityEditor`
- [x] `Woodberry.Core` **не ссылается** на `Woodberry.Gameplay.*` (направление зависимостей вниз)
- [x] Игрок передвигается по XZ в Play Mode
- [x] Камера следует за игроком сверху, не дёргается
- [x] `IInputReader` — единственная точка доступа к вводу в геймплее
- [x] EditMode-тесты проходят (17/17)
- [x] PlayMode-тесты проходят (10/10)
- [x] Прототипная сцена лежит в `Assets/Woodberry/Scenes/`
- [x] Служебные ассеты шаблона Unity удалены (сделано до Stage 00)
- [x] Все `.meta` присутствуют и закоммичены
- [x] Stage report создан в `docs/reports/`

## Required tests

- **EditMode:** вектор движения, нормализация диагонали, нулевой ввод → ноль движения
- **EditMode:** инварианты слоёв — отсутствие `UnityEditor` в runtime, наличие asmdef, `Core` без ссылок на `Woodberry.*`
- **PlayMode:** контроллер инициализируется, работает, корректно отключается
- **PlayMode:** камера после `SetTarget` стоит над целью, сближается при движении, не дрейфует на месте
- **Компиляция:** `read_console(action: "get", types: ["error"])` пуст

## Risks

- Сгенерированная обёртка `InputSystem_Actions` в Unity 1.20 — обычный класс, а не `ScriptableObject`.
  `[SerializeField]` на нём не работает (Unity выдаёт UAC1010). Обёртка встроена в
  `Assets/Scripts/Core/Generated/`, собирается из встроенного JSON, поэтому ассет в сцену не тащится.
- Слишком раннее усложнение `Core`. Митигация: минимум абстракций, только то, что нужно этому stage.
- asmdef может «съесть» существующий код из сборки `Assembly-CSharp`. Проверять после каждого добавления.
- ~~Связка bootstrap → игрок через сериализованную ссылку `_bootstrap`.~~
  **Устарело в Stage 01.** Подход работал, только пока bootstrap и игрок лежали
  в одной сцене. Сериализованные ссылки между сценами Unity не сохраняет, при
  разнесении сцен игрок тихо остался бы без ввода. Сейчас ввод берётся из
  `ServiceRegistry` — см. ADR 0006. Пункт сохранён как напоминание о том,
  что молчаливый отказ возможен и там, где компилятор молчит.
- Связка игрок → камера идёт вниз (`CameraRig` знает только о `Core`, о `Gameplay` не знает),
  поэтому `GameBootstrap` не может вызвать `SetTarget`. Камера получает цель через
  сериализованную ссылку в сцене, а `SetTarget` оставлен как публичный вход для будущего
  сетевого и спавн-кода. Первая версия сцены без этой ссылки выглядит исправной
  в редакторе, но камера молча стоит на месте — на этот случай есть PlayMode-тест.
- Движение считается в `Update` с плавающим `Time.deltaTime`. `coop-networking.md` требует
  симуляцию 20–30 Hz, поэтому перенос на фиксированный тик предстоит на сетевом этапе.
  `PlayerMovement` для этого уже вынесен в чистую функцию без `MonoBehaviour`.

## Dependencies

- Unity Editor запущен, MCP bridge подключён
- `com.unity.inputsystem` установлен (да)

## Deliverables

- Working prototype: игрок ходит, камера сверху
- 11 asmdef-файлов (по одной сборке на слой + Editor + 2 тестовых)
- EditMode 17 тестов + PlayMode 10 тестов
- Stage report: `docs/reports/2026-09-30-stage-00-foundation-report.md`
