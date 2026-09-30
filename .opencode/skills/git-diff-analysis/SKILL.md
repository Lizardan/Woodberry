---
name: git-diff-analysis
description: Inspect git history and diffs to identify what changed, which Woodberry layers are affected, and what risk signals to prioritise before applying review checklists. Use at the start of every code review.
license: MIT
compatibility: opencode
metadata:
  audience: code-reviewer
  domain: git
---

# Git Diff Analysis

## What I do

Guide the initial inspection of git changes so the reviewer understands **scope** and
**risk** before applying layer-specific checklists.

Применяется на **шаге 2** review flow из `docs/specs/code-review-flow.md`.

---

## Рекомендуемые команды

```bash
# Состояние рабочего дерева
git status

# Файлы, изменённые vs последний коммит
git diff --name-only HEAD

# Полный unified diff vs последний коммит
git diff HEAD

# Файлы и diff vs main (для PR-style review)
git diff main..HEAD --name-only
git diff main..HEAD

# Diff одного файла
git diff HEAD -- path/to/file.cs

# Diff с учётом staged
git diff --cached --name-only
git diff --cached

# Недавняя история для контекста
git log --oneline -10

# Blame конкретной области — авторство
git blame -L 10,30 -- path/to/file.cs

# Все отслеживаемые файлы (когда diff недоступен)
git ls-files

# Какие файлы менялись в конкретном коммите
git show --name-only --format="%H %s" HEAD
```

**Не используй** `git add`, `git commit`, `git checkout`, `git stash`, `git reset`.
У агента `bash: "*": deny` с белым списком только этих read-only команд.

---

## Группировка файлов по слоям

После `git diff --name-only` сопоставь каждый файл со слоем и его уровнем риска.

| Слой | Префикс пути | Риск | Почему |
|---|---|---|---|
| **Net** | `Assets/Scripts/Net/` | 🔴 High | Единственное место с сетевым кодом. Ошибка = рассинхрон у всех |
| **Core** | `Assets/Scripts/Core/` | 🔴 High | Абстракции, которые видит весь геймплей. Ошибка ломает всё |
| **Gameplay** | `Assets/Scripts/Gameplay/` | 🟠 Medium | Игровые правила. Нарушение = неверная механика |
| **AI** | `Assets/Scripts/AI/` | 🟠 Medium | Поведение и навигация. Застревание врагов |
| **asmdef** | `Assets/Scripts/**/*.asmdef` | 🔴 High | Границы сборок. Неверная ссылка = Editor-код в player-сборке |
| **Editor** | `Assets/Scripts/Editor/` | 🟡 Low-Medium | Только редактор. Ошибка ломает tooling, не игру |
| **CameraRig** | `Assets/Scripts/CameraRig/` | 🟠 Medium | Камера. Дёргается или не видит |
| **Audio** | `Assets/Scripts/Audio/` | 🟡 Low-Medium | Звук. Атмосфера страдает |
| **Save** | `Assets/Scripts/Save/` | 🔴 High | Данные игрока. Потеря прогресса |
| **UI** | `Assets/Scripts/UI/` | 🟠 Medium | Презентация. Нарушение = геймплей в UI |
| **Tests** | `Assets/Tests/` | 🟡 Low | Пробелы = необнаруженный риск |
| **Unity-ассеты** | `Assets/Woodberry/**/*.prefab`, `*.unity`, `*.mat` | 🔴 High | Ручной YAML-редакт ломает всё. Проверять всегда |
| **Инфраструктура** | `Packages/manifest.json`, `ProjectSettings/**` | 🔴 High | Триггерит массовую перекомпиляцию, меняет поведение всего |
| **Документация** | `docs/`, `AGENTS.md`, `README.md`, `.opencode/` | 🟡 Low | Отсутствие обновлений = сигнал contract drift |

Обходить слои **в порядке убывания риска**: Net → Core → asmdef → Unity-ассеты →
Infrastructure → Gameplay → AI → CameraRig → UI → Save → Editor → Tests → Docs.

---

## Risk signals

Признаки, что изменение опаснее, чем кажется по списку файлов.

### Сеть

| Сигнал | Почему опасно |
|---|---|
| Изменён `*.cs` в `Net/` | Прямое влияние на рассинхрон |
| Добавлен `if (IsServer)` где-либо вне `Net/` | Утечка транспорта в геймплей |
| Появился новый класс рядом с `PlayerController` | Риск дубля для local/remote |
| Изменена частота репликации | Влияет на трафик и отзывчивость |

### Unity-ассеты

| Сигнал | Почему опасно |
|---|---|
| Изменены `.unity` или `.prefab` в diff | Почти всегда — ручной YAML-редакт. Требует объяснения |
| Добавлен/удалён ассет | Проверь `.meta` в том же коммите |
| Переименован ассет | Проверь, что `.meta` переехал вместе с ним |
| Изменён `.mat` или шейдер | Проверь URP-совместимость |

### Ассембли

| Сигнал | Почему опасно |
|---|---|
| Изменён `.asmdef` | Меняет видимость типов по всему проекту |
| В `references` добавлена сборка | Может создать циклическую зависимость |
| `Woodberry.Runtime` ссылается на `Woodberry.Editor` | Editor-код попадёт в player-сборку |
| В `includePlatforms` снят `Editor` | То же |

### Инфраструктура

| Сигнал | Почему опасно |
|---|---|
| Изменён `Packages/manifest.json` | Триггерит массовую перекомпиляцию и меняет поведение |
| Изменён `ProjectSettings/ProjectVersion.txt` | Меняет версию движка. Почти всегда ошибка |
| Изменён `ProjectSettings/TagManager.asset` | Может сломать ссылки на теги в коде |

---

## Порядок работы

1. `git status` — есть ли вообще изменения, нет ли мусора
2. `git diff --name-only HEAD` — список файлов
3. Сгруппировать по слоям, отметить затронутые уровни риска
4. **Проверить, обновлены ли `docs/` и `AGENTS.md` вместе с кодом**
5. Прочитать полный diff каждого файла, начиная с наивысшего риска
6. Отметить intent каждого изменения и разрывы между intent и реализацией
7. Передать структурированную проверку скиллу `code-review-checklist`
8. Сверить с `docs/stages/stage-NN-*.md`: соответствует ли изменение объявленному scope

---

## Красные флаги в самом диффе

| Паттерн | Проверь |
|---|---|
| Большой diff, затрагивающий 5+ слоёв | Не один ли это stage? Нарушение «один stage за раз» |
| Изменены только `.meta` без самих ассетов | Случайное действие? |
| Тесты изменены, но код, который они покрывают, — нет | Тесты подстроены под неверную реализацию |
| Код изменён, тесты — нет | Test gap |
| `docs/specs/` не тронут, хотя контракт изменился | Contract drift |
| Stage Report заявляет `Done`, а проверить нечем | Нечестная верификация |
