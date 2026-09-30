# Stage 01 — Scene Architecture

Статус: **Завершён** (2026-09-30)
Зависит от: Stage 00

## Goal

Три сцены с разным назначением: `Bootstrap` собирает и персистит глобальные
сервисы, `Menu` — вход в игру, `Game` — сам геймплей. Переход между ними
работает, а не нарисован.

## Business value

До этого игрок запускался сразу в геймплейную сцену, и `GameBootstrap` жил
внутри неё. Это значит, что любой глобальный сервис — сессия, сейв, загрузчик
уровней — пришлось бы создавать заново на каждый вход в игру, а меню не
существовало бы как отдельное состояние приложения.

## Ключевая находка

Сериализованная ссылка на объект **из другой сцены** Unity отбрасывает молча:
при сохранении в YAML пишется `{fileID: 0}`. Проверено экспериментом — объект
живой, сцена-донор загружена, ссылка всё равно теряется.

Следствие: связка `PlayerController._bootstrap → GameBootstrap` работает только
пока они в одной сцене. При разнесении по сценам игрок тихо останется без ввода.
Отсюда — реестр сервисов вместо сериализованных ссылок.

## Scope

- `Bootstrap.unity` — composition root, `DontDestroyOnLoad`, персистит
- `Menu.unity` — камера, EventSystem, заголовок и рабочая кнопка Play
- `Game.unity` — игрок и камера (бывший прототип)
- `ServiceRegistry` в `Core` — единственный канал доступа к глобальным сервисам
- `ISceneLoader` в `Core` — загрузка сцен как намерение, без знания о `SceneManager` в UI
- Build Settings: `Bootstrap` = 0, `Menu` = 1, `Game` = 2
- EditMode-тесты на реестр, PlayMode-тесты на переходы

## Out of scope

- Сеть, здоровье, урон, ИИ, предметы, сохранение
- Настоящий визуал меню — только то, что нужно для проверки перехода
- Локализация
- Несколько игроков, late join

## Work

### Core
- `Services/ServiceRegistry` — регистрирует и отдаёт глобальные сервисы.
  Пишет только composition root. Сбрасывается на `SubsystemRegistration`.
- `Scenes/ISceneLoader` + `SceneId` — единственный способ уйти в другую сцену.
- `GameBootstrap` — создаёт сервисы, регистрирует, делает `DontDestroyOnLoad`,
  уводит в `Menu`.

### UI
- `Menu/MainMenuController` — презентация: кнопка Play пишет намерение в `ISceneLoader`.
  Никакой игровой логики.

### Gameplay
- `PlayerController` — если `Initialize` не вызывали, берёт `IInputReader`
  из реестра в `Start`.

## Acceptance criteria

- [x] Три сцены лежат в `Assets/Woodberry/Scenes/` без префикса `Woodberry_`
- [x] Build Settings: `Bootstrap` = 0, `Menu` = 1, `Game` = 2
- [x] `GameBootstrap` персистит: переживает Menu → Game, экземпляр всегда один
- [x] `Menu → Game` по кнопке Play грузит `Game` и выгружает `Menu`
- [x] Игрок в `Game` получает `IInputReader` без ручной связки в инспекторе
- [x] В UI нет прямых вызовов `SceneManager`
- [x] Геймплейный ввод выключен в меню (ADR 0005, правило 3)
- [x] EditMode-тесты на `ServiceRegistry` проходят (29/29)
- [x] PlayMode-тесты проходят (13/13)
- [x] `docs/specs/core-gameplay.md` и `asset-standards.md` обновлены
- [x] ADR на архитектуру сцен и реестр сервисов (ADR 0006)
- [x] Stage report

## Required tests

- **EditMode:** регистрация, получение, отсутствие сервиса, сброс реестра
- **PlayMode:** переход Menu → Game по кнопке
- **Компиляция:** `read_console(action: "get", types: ["error"])` пуст

## Risks

- Статический реестр — глобальное мутабельное состояние. Митигация: единственный
  писатель (composition root), сброс на `SubsystemRegistration`, чтение только
  один раз в `Start` с внятной диагностикой при промахе.
- Реестр маскирует зависимости: код выглядит автономным, а сервис приходит извне.
  Митигация: `Initialize` остаётся основным путём подстановки, реестр — только
  для кросс-сценовых глобальных сервисов.
- Проброс ссылок в `Menu` из bootstrap: `Menu` тоже должен получить `ISceneLoader`.
  Он берёт его из того же реестра — один путь, а не два.

## Dependencies

- Stage 00 (asmdef, `IInputReader`, `PlayerController`, `TopDownCameraRig`)

## Deliverables

- Три сцены и рабочий цикл переходов
- `ServiceRegistry` и `ISceneLoader` в `Core`
- Тесты
- ADR + stage report
