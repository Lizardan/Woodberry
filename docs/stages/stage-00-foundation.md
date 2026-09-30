# Stage 00 — Foundation

Статус: **Не начат**
Коммит: `3c5d2e8` (инфраструктура OpenCode-слоя)
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
- `Woodberry.Core` — composition root, сервис-инициализация
- `Woodberry.Runtime` — весь игровой runtime-код
- `Woodberry.Editor` — editor-only утилиты
- `Woodberry.Tests.EditMode`, `Woodberry.Tests.PlayMode`
- `IInputReader` + реализация поверх `InputSystem_Actions`
- `PlayerController` — движение по XZ
- `TopDownCameraRig` — следование за игроком сверху
- **asmdef-контракт**: отдельная сборка на каждый слой. Межслойные зависимости объявлены явно через `references`
- EditMode-тест на правило движения, PlayMode-тест на жизненный цикл контроллера
- Перенос `SampleScene` → `Assets/Woodberry/Scenes/Woodberry_Prototype.unity`
- Baseline stage report

> **Уже сделано:** служебные ассеты шаблона Unity (`Assets/TutorialInfo/`,
> `Assets/Readme.asset`) удалены при переводе проекта на структуру из `AGENTS.md`.
> В рамках Stage 00 остаётся только перенос сцены.

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
- Composition root: один bootstrap-объект сцены, собирает зависимости
- `IInputReader` — абстракция ввода
- Сборочная панель регистрирует сервисы, геймплей их получает через конструктор/`Initialize`

### Gameplay (`Assets/Scripts/Gameplay/Player/`)
- `PlayerController` — кинематическое движение по XZ
- Никаких `Rigidbody`-физик для движения
- Никаких `FindObjectOfType` — зависимости приходят снаружи

### Camera (`Assets/Scripts/CameraRig/`)
- `TopDownCameraRig` — следует за целью, фиксированный угол сверху
- Цель передаётся снаружи, не ищется в сцене

### Editor (`Assets/Scripts/Editor/`)
- Валидатор: проверяет, что runtime-код не ссылается на `UnityEditor`

### Tests
- EditMode: `PlayerMovementTests` — вектор движения, нормализация, нулевой ввод
- PlayMode: `PlayerControllerTests` — Awake/OnEnable/OnDisable без утечек, движение в Play

### Infra
- asmdef-файлы
- Обновлённый `.gitignore` (служебное в `.opencode/` игнорируется)

## Acceptance criteria

- [ ] Проект компилируется без ошибок (`read_console(action: "get", types: ["error"])` чист)
- [ ] Player assembly **не ссылается** на `UnityEditor`
- [ ] `Woodberry.Core` **не ссылается** на `Woodberry.Gameplay.*` (направление зависимостей вниз)
- [ ] Игрок передвигается по XZ в Play Mode
- [ ] Камера следует за игроком сверху, не дёргается
- [ ] `IInputReader` — единственная точка доступа к вводу в геймплее
- [ ] EditMode-тесты проходят
- [ ] PlayMode-тесты проходят
- [ ] Прототипная сцена лежит в `Assets/Woodberry/Scenes/`
- [x] Служебные ассеты шаблона Unity удалены (сделано до Stage 00)
- [ ] Все `.meta` присутствуют и закоммичены
- [ ] Stage report создан в `docs/reports/`

## Required tests

- **EditMode:** вектор движения, нормализация диагонали, нулевой ввод → ноль движения
- **PlayMode:** контроллер инициализируется, работает, корректно отключается
- **Компиляция:** `read_console(action: "get", types: ["error"])` пуст

## Risks

- `InputSystem_Actions` сгенерирован только после того, как Unity увидит `.inputactions`. Без сгенерированного класса компиляция упадёт.
- Слишком раннее усложнение `Core`. Митигация: минимум абстракций, только то, что нужно этому stage.
- asmdef может «съесть» существующий код из сборки `Assembly-CSharp`. Проверять после каждого добавления.

## Dependencies

- Unity Editor запущен, MCP bridge подключён
- `com.unity.inputsystem` установлен (да)

## Deliverables

- Working prototype: игрок ходит, камера сверху
- 4 asmdef-файла
- EditMode + PlayMode тесты
- Stage report
- Обновлённый `AGENTS.md` (если структура уточнилась)
