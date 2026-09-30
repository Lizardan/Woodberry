# Spec — Code Review Flow

Статус: Accepted
Дата: 2026-09-30
Агент: `.opencode/agents/code-reviewer.md`

## Purpose

Repeatable, layer-aware review process that checks correctness, architecture compliance
and quality for all Woodberry layers before changes are considered done.

Спецификация существует, потому что review — **роль чтения и анализа**, а не расширение
builder-агента. Она делает review предсказуемым и позволяет человеку повторить тот же
процесс вручную.

## Фундаментальное правило

> Агент, который только что написал код, плохо его проверяет. Разделяй роли во времени:
> сначала build-агент завершает задачу и выдаёт Stage Report, потом `code-reviewer`
> делает независимый review.

Поэтому у `code-reviewer` в frontmatter `edit: deny` и `bash: "*": deny` с белым
списком только read-only git-команд. Найти проблему — его работа. Исправить —
работа builder-агента, отдельным вызовом, с отдельной ответственностью.

## Рекомендуемая последовательность

### Step 1 — Понять контекст

- Прочитать `AGENTS.md` — правила архитектуры, границы слоёв, naming, Unity-правила
- Найти релевантные спеки в `docs/specs/`
- Прочитать stage file из `docs/stages/` — понимать, **зачем** изменение, а не только **что**
- Определить, было ли изменение контракта. Если да — specs обязаны быть обновлены

**Не читай diff до этого шага.** Без контекста review превращается в поиск опечаток.

### Step 2 — Проинспектировать изменения

```bash
git status                            # состояние рабочего дерева
git diff --name-only HEAD             # файлы, изменённые vs последний коммит
git diff HEAD                         # полный unified diff
git log --oneline -10                 # недавняя история для контекста
git diff main..HEAD --name-only       # vs main, для PR-style review
git blame -L <start>,<end> <file>     # авторство конкретных строк
```

Сгруппировать файлы по слоям. Полная методика — в скилле `git-diff-analysis`.

### Step 3 — Review по слоям

Обходить затронутые слои **в порядке убывания риска**. Для каждого применить скилл
`code-review-checklist`.

### Step 4 — Сквозные проверки

Всегда, независимо от слоёв:

- **Секреты** — нет захардкоженных ключей, токенов, паролей
- **Идемпотентность** — скрипты и пайплайны безопасны при повторном запуске
- **Выравнивание документации** — если контракт изменился, `docs/specs/` обновлён
- **Честность верификации** — Stage Report не заявляет `Done` для непроверенного
- **Наблюдаемость** — логирование и обработка ошибок на длинных и внешних путях

### Step 5 — Сформировать recommendation plan

Классифицировать каждую находку по severity и вывести по формату ниже.

## Severity Levels

| Level | Значение | Требуемое действие |
|---|---|---|
| **Critical** 🔴 | Потеря данных, сломанная корректность, раскрытый секрет, нарушение обязательной архитектурной границы, поломка ассетов у всей команды | **Обязательно исправить до merge** |
| **Major** 🟠 | Нарушение правил проекта, отсутствие идемпотентности, contract drift, пробел в тестах, риск в hot path | **Желательно исправить до merge** |
| **Minor** 🟡 | Naming, отсутствие комментариев, небольшие пробелы в качестве | Исправить или зафиксировать как осознанный компромисс |
| **Suggestion** 💡 | Рефакторинг, улучшение, идея на будущее | Опционально |

### Что считается Critical в Woodberry

Это не «субъективно плохо», а нарушение обязательных границ из `AGENTS.md`:

- Сетевой API вне `Assets/Scripts/Net/`
- `if (isNetworked)` / `IsServer` / `IsClient` / `IsHost` в геймплее
- Клиент как source of truth для игрового состояния
- `Gameplay` → `UI` или `Core` → `Gameplay`
- Runtime-код ссылается на `UnityEditor`
- Ручной YAML-редакт `.unity` / `.prefab` / `.asset`
- Ассет добавлен или удалён без `.meta`
- Два параллельных контроллера игрока (local и remote)
- Секрет в коде или в закоммиченном конфиге
- `Woodberry.Editor.asmdef` без `includePlatforms: ["Editor"]`

## Формат Recommendation Plan

Использовать **ровно** эту структуру:

```markdown
## Code Review: <scope, branch, или набор файлов>

### Summary
<один абзац: что изменилось, какие слои затронуты, общий сигнал качества>

### Findings

#### 🔴 Critical
- [ ] **`<file>`:**`<line>`** — <проблема и её влияние>

#### 🟠 Major
- [ ] **`<file>`:**`<line>`** — <описание>

#### 🟡 Minor
- [ ] **`<file>`:**`<line>`** — <описание>

#### 💡 Suggestions
- [ ] **`<file>`** — <описание>

### What looks good
- <краткий список того, что сделано правильно>

### Next steps
<упорядоченный список. Первый пункт — находка высшей severity>
1. …
2. …
```

**Пустые секции остаются в выводе** — так читается разница между «проверил и не нашёл»
и «не проверял». Если находок нет вообще — напиши это явно в Summary.

## Notes

- Default diff target is `HEAD` unless the user specifies a branch or commit.
- For PR-style review, compare against `main`.
- If no git context is available, ask the user to paste the diff or specify file paths.
- Формат фиксирован, чтобы вывод одинаково читался человеком и агентом.

## Связанные документы

- `.opencode/agents/code-reviewer.md` — агент
- `.opencode/skills/git-diff-analysis/SKILL.md` — анализ диффа и группировка по слоям
- `.opencode/skills/code-review-checklist/SKILL.md` — чеклисты по слоям
- `.opencode/skills/unity-architecture-review/SKILL.md` — grep-проверки архитектуры
- `AGENTS.md → Review expectations` — что искать в целом
- `docs/reports/README.md` — формат Stage Report
