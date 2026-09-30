# Woodberry

Кооперативный хоррор с видом сверху (референс камеры и читаемости: GTA 2) в мрачной атмосфере.

Unity `6000.6.3f1` · URP `17.6.0` · Input System · AI Navigation · Test Framework

---

## Статус проекта

**Stage 0 — Foundation** готов к работе. Код ещё не написан, но инфраструктура
проекта и OpenCode-слой развёрнуты.

Активный этап: [`docs/stages/stage-00-foundation.md`](docs/stages/stage-00-foundation.md)

Блокер для Stage 03: [ADR 0003 — выбор сетевого стека](docs/adr/0003-networking-stack.md)
имеет статус `Proposed`. Нужно принять решение.

---

## Быстрый старт

### Для разработчика

```powershell
# Открыть проект
# Unity Hub → Add → выбрать папку → Open
# Требуется версия 6000.6.3f1 ровно

# Проверить, что всё компилируется
# В Unity: Window → General → Console, фильтр Errors
```

Подробная инструкция: [`docs/runbooks/local-development.md`](docs/runbooks/local-development.md)

### Для OpenCode-агента

**1. Прочитай сначала** — без этого работать нельзя:

| Файл | Зачем |
|---|---|
| [`AGENTS.md`](AGENTS.md) | Проектный контракт: слои, правила, запреты, DoD |
| [`opencode.json`](opencode.json) | Control plane: инструкции, permissions, маршрутизация ролей |
| [`instructions/safety.md`](instructions/safety.md) | Что нельзя делать никогда |
| Активный stage file | Рамка текущей работы |

**2. Начни с `plan`**, а не с кода. План — это не формальность, а способ
не закодировать неопределённость.

**3. Цикл работы:**

```
Plan → Spec (если меняется контракт) → Build → Verify → Review → Report
```

**4. Кого звать:**

| Задача | Агент |
|---|---|
| Изменить требования, расплывчатая задача | `system-analyst` |
| Игровая логика, механики, физика | `gameplay-engineer` |
| Сцена, свет, камера, level flow | `scene-level-designer` |
| Интерфейс, HUD, меню | `ui-engineer` |
| Визуал, материалы, шейдеры, VFX | `technical-artist` |
| Сборка, CI, пакеты, Editor-утилиты | `unity-tools-engineer` |
| Тесты, ревью, регрессии | `qa-analyst` |

Маршрутизация уже настроена в `opencode.json → agent.build.permission.task`.

**5. Skills** — подгружай по задаче, не заранее:

| Skill | Когда |
|---|---|
| `stage-planning` | Начало нетривиальной задачи |
| `unity-test-strategy` | Новая логика, падающие тесты |
| `unity-architecture-review` | Перед завершением этапа со слоями |
| `unity-prefab-safety` | Префабы, сцены, `.meta` |
| `coop-networking-model` | Перед Stage 03 или сетевым кодом |
| `horror-level-design` | Сцены, свет, «слишком темно» |
| `urp-visual-tuning` | Визуал, draw calls, frame time |
| `stage-report` | Завершение этапа |
| `debug-unity-console` | Ошибки компиляции, падающие тесты |

**6. Готовые промпты** (вызываются как `/plan-stage`, `/review` и т.д.):

| Команда | Что делает |
|---|---|
| `/onboard` | Сводка по проекту для нового разработчика или агента |
| `/plan-stage <задача>` | Спланировать этап (режим `plan`, файлы не меняются) |
| `/build-stage <план>` | Реализовать согласованный план |
| `/verify` | Компиляция + EditMode/PlayMode тесты, честный отчёт |
| `/review` | Строгий review изменений |
| `/debug <проблема>` | Расследовать баг или ошибку компиляции |
| `/stage-report <stage>` | Создать stage report |
| `/healthcheck` | Проверка целостности репозитория |

---

## Три вещи, которые нужно знать сразу

### 1. Кооператив — это архитектурное ограничение, а не фича

Сетевой seam вводится с первого дня, даже если сетевого кода ещё нет.
Геймплей общается с миром только через `INetworkService` из `Core`.
Сетевые API живут **только** в `Assets/Scripts/Net/`.

Если архитектуру проектировать без учёта сети, потом придётся переписать всё.

### 2. Правила границ — компилируемый контракт, а не соглашение

```
UI  ─────►  Gameplay  ─────►  Core
                ▲
AI  ────────────┘

Net ───────────►  Core   (обратно — только через интерфейсы)

Editor ────────►  всё (только в редакторе)
```

Нарушение ловится компилятором и grep'ом, а не памятью автора.

### 3. Unity-ассеты не редактируются как текст

`.unity` и `.prefab` — только через редактор или Unity MCP.
`.meta` коммитятся всегда, удаляются только через Unity.

Ручной YAML-редактит Unity-ассетов — самая частая причина тихой поломки проекта.

---

## Структура

```
Woodberry/
├─ AGENTS.md                     ← проектный контракт
├─ opencode.json                 ← control plane
├─ instructions/                 ← долгоживущие правила
├─ docs/
│  ├─ specs/                     ← что делаем (durable контракты)
│  ├─ stages/                    ← рамка этапа
│  ├─ reports/                   ← что реально сделали
│  ├─ adr/                       ← почему так
│  └─ runbooks/                  ← как запускать/чинить
├─ .opencode/
│  ├─ agents/                    ← 7 ролей
│  ├─ skills/                    ← 9 playbooks
│  ├─ patterns/                  ← whitelist / blacklist
│  ├─ templates/                 ← шаблоны отчётов
│  └─ commands/                  ← готовые промпты
└─ Assets/
   ├─ Scenes/                    ← служебные сцены Unity
   ├─ Settings/                  ← URP assets
   └─ Woodberry/                 ← ВЕСЬ игровой контент
      └─ Art/ Audio/ Materials/ Models/ Prefabs/ Scenes/ Settings/ UI/ VFX/
   ├─ Scripts/                   ← ВЕСЬ C# код
   │  └─ Core/ Gameplay/ AI/ Net/ UI/ CameraRig/ Audio/ Save/ Editor/
   └─ Tests/
      └─ EditMode/ PlayMode/
```

---

## Документация

### Что где искать

| Вопрос | Файл |
|---|---|
| Что это за проект? | [`docs/specs/game-vision.md`](docs/specs/game-vision.md) |
| Какие правила архитектуры? | [`AGENTS.md`](AGENTS.md) |
| Что строим сейчас? | [`docs/stages/`](docs/stages/) |
| Почему такой стек? | [`docs/adr/`](docs/adr/) |
| Как запустить? | [`docs/runbooks/local-development.md`](docs/runbooks/local-development.md) |
| Как запустить тесты? | [`docs/runbooks/test-workflow.md`](docs/runbooks/test-workflow.md) |
| Как работать через MCP? | [`docs/runbooks/unity-automation.md`](docs/runbooks/unity-automation.md) |
| Как добавить контент? | [`docs/runbooks/adding-content.md`](docs/runbooks/adding-content.md) |
| Как коммитить? | [`docs/runbooks/git-workflow.md`](docs/runbooks/git-workflow.md) |
| Как собрать релиз? | [`docs/runbooks/build-and-release.md`](docs/runbooks/build-and-release.md) |
| Ошибки компиляции? | [`docs/runbooks/incident-compile-errors.md`](docs/runbooks/incident-compile-errors.md) |

### Спецификации

| Файл | О чём |
|---|---|
| [`game-vision.md`](docs/specs/game-vision.md) | Концепция, атмосфера, петля геймплея |
| [`core-gameplay.md`](docs/specs/core-gameplay.md) | Движение, ресурсы, взаимодействие |
| [`coop-networking.md`](docs/specs/coop-networking.md) | Authority-модель, что реплицируется |
| [`save-system.md`](docs/specs/save-system.md) | Формат и версионирование сейвов |
| [`performance-budget.md`](docs/specs/performance-budget.md) | FPS, draw calls, память |
| [`asset-standards.md`](docs/specs/asset-standards.md) | Имена, импорт, масштабы |

### Архитектурные решения

| Файл | Решение | Статус |
|---|---|---|
| [ADR 0001](docs/adr/0001-layer-and-assembly-architecture.md) | Слои и asmdef | Accepted |
| [ADR 0002](docs/adr/0002-save-data-format.md) | Формат сейвов | Proposed |
| [ADR 0003](docs/adr/0003-networking-stack.md) | Сетевой стек | **Proposed — блокирует Stage 03** |
| [ADR 0004](docs/adr/0004-scriptable-objects-for-design-data.md) | SO для дизайн-данных | Accepted |
| [ADR 0005](docs/adr/0005-input-abstraction.md) | `IInputReader` | Accepted |

---

## Верификация

```powershell
# Компиляция
mcp__unityMCP__refresh_unity(compile="request", wait_for_ready=true)
mcp__unityMCP__read_console(action="get", types=["error","warning"], count="50")

# Тесты
mcp__unityMCP__run_tests(mode="EditMode", include_failed_tests=true)
mcp__unityMCP__run_tests(mode="PlayMode", include_failed_tests=true, init_timeout=120000)
```

**Правило:** «Предположительно работает» — не верификация.
Если Unity Editor не запущен, пиши `Not verified: Unity Editor не запущен`.

---

## Definition of Done

Stage завершён, только если:

- [ ] scope реализован полностью
- [ ] acceptance criteria из stage file выполнены
- [ ] тесты добавлены или обновлены
- [ ] проверки запущены (компиляция, тесты)
- [ ] `read_console` чист от ошибок
- [ ] границы слоёв не нарушены
- [ ] specs обновлены, если менялись контракты
- [ ] known issues перечислены явно
- [ ] допущения перечислены явно
- [ ] manual QA steps предоставлены
- [ ] stage report создан в `docs/reports/`
- [ ] отдельный review-проход проведён

---

## Git

Репозиторий под git, `.gitignore` из Unity-шаблона.

| Действие | Статус |
|---|---|
| `git status` / `diff` / `log` | ✅ |
| `git add` / `commit` | ⚠️ только по явной просьбе |
| `git push` | ❌ |
| `git reset --hard` / `clean` | ❌ |

Подробнее: [`docs/runbooks/git-workflow.md`](docs/runbooks/git-workflow.md)
