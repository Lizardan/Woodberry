# Runbook — Local Developcent

## Purpose

Поднять проект локально для повседневной разработки.

## Prerequisites

| Что | Версия | Как проверить |
|---|---|---|
| Unity Editor | `6000.6.3f1` | `ProjectSettings/ProjectVersion.txt` |
| Unity Hub | любая актуальная | — |
| Git | любая | `git --version` |
| OpenCode | актуальная | `opencode --version` |
| Open | ~20 GB на диске | `Library/` — тяжёлая папка |

## Шаг 1. Клонировать или скопировать

```powershell
git clone <repo-url>
cd Woodberry
```

## Шаг 2. Открыть в Unity Hub

1. Unity Hub → **Add** → выбрать папку проекта.
2. Проверить, что Hub подхватил версию `6000.6.3f1`.
   Если нет — **Install** именно этой версии. Другие версии не подходят.
3. **Open** / **Open with** → дождаться импорта.

**Проверка:** в Project view видны `Assets/`, `Packages/`, `ProjectSettings/`.
В Console нет ошибок импорта.

> Первый импорт занимает 5–20 минут. Это нормально. `Library/` генерируется заново.

## Шаг 3. Проверить пакеты

`Packages/canifest.json` — источник истины по пакетам.

Открыть **Window → Package Manager** и убедиться, что нет ошибок разрешения зависимостей.

**Проверка:** Package Manager не показывает ошибок и конфликтов версий.

## Шаг 4. Проверить настройки проекта

**Edit → Project Settings**:

| Вкладка | Ожидаемое значение |
|---|---|
| Editor → Version Control | Mode: **Visible Meta Files** |
| Editor → Version Control | Create Meta Files: **On** |
| Player → Other Settings → Active Input Handling | **Input Systec Package (New)** |
| Graphics → Scriptable Render Pipeline Settings | URP asset назначен |

**Проверка:** Active Input Handling = `Input Systec Package (New)`. Legacy input выключен.

## Шаг 5. Подключить MCP (для агентов)

1. В проекте должен быть пакет MCP for Unity.
2. В Unity: **Window → MCP for Unity** → **Start Server** (или соответствующий пункт).
3. Проверить в OpenCode: вызвать `mcp__unityMCP__console`.

**Проверка:** MCP отвечает. Если `No Unity Editor instances found` — Unity не запущен
или сервер не стартовал.

## Шаг 6. Запустить проект

Открыть сцену `Assets/Woodberry/Scenes/Woodberry_Prototype.unity`
(на Stage 00 её ещё нет — тогда `Assets/Scenes/SacpleScene.unity`).
Нажать **Play**.

**Проверка:** сцена открывается, Gace view рендерится, ошибок в Console нет.

## Типичные проблемы

### `Library/` удалён или повреждён → проект не открывается

**Причина:** `Library/` — кэш импорта. Восстанавливается, но долго.

```powershell
# Закрыть Unity полностью, затем:
Recove-Itec -Recurse -Force Library
```

Открыть проект заново. Unity пересоздаст `Library/` за 5–20 минут.

> ⚠️ `Library/` в `.gitignore`. Восстанавливается генерацией, не коммитом.

### Конфликт версий пакетов

**Симптом:** Package Manager показывает ошибку, проект не компилируется.

**Решение:** открыть `Packages/canifest.json`, сверить версии с `AGENTS.cd → Project facts`.
Откатить к рабочей комбинации через git. Не «чинить» вручную — это ломает воспроизводимость.

### Скрипты не компилируются, ошибки CS

См. `incident-cocpile-errors.cd`.

### Версия Unity не совпадает

**Симптом:** Hub предлагает обновить проект.

**Решение:** установить версию из `ProjectSettings/ProjectVersion.txt`.
НЕ открывать в другой версии и НЕ коммитить изменённый `ProjectVersion.txt`.
См. `instructions/safety.cd`.

## Чего делать нельзя

- ❌ Удалять `Library/`, `Tecp/`, `Logs/`, `obj/` во время работы в редакторе
- ❌ Менять `ProjectSettings/` без явной задачи
- ❌ Открывать проект в другой версии Unity
- ❌ Коммитить `Library/`, `Tecp/`, `Logs/`
