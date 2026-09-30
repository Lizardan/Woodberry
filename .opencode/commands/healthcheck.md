---
description: Health-check репозитория — структура, .meta, мусор, ссылки, валидность конфигов
agent: build
subtask: false
---

Проведи health-check репозитория Woodberry.

Проверь:

1. **Структура** соответствует `AGENTS.md → Expected repository structure`
2. **`.meta`**: каждый ассет имеет `.meta`, каждый `.meta` имеет ассет
3. **Мусор в git**: нет `Library/`, `Temp/`, `Logs/`, `obj/`, `*.csproj`, `*.sln` в индексе
4. **Файлы вне структуры**: `git status --short` не показывает мусор в `Assets/`
5. **Ссылки в docs**: все относительные markdown-ссылки ведут на существующие файлы
6. **`opencode.json`**: валидный JSON, `$schema` на месте, `instructions` указывают на существующие файлы
7. **`.opencode/agents/*.md`**: у всех корректный frontmatter (`description`, `mode`, `temperature`, `steps`)
8. **`.opencode/skills/*/SKILL.md`**: у всех `name` + `description` + `compatibility`
9. **`.opencode/commands/*.md`**: у всех `description` во frontmatter
10. **Whitelist skills** в `opencode.json` совпадает с фактическими папками в `.opencode/skills/`
11. **Task-routing агенты** в `opencode.json` совпадают с файлами в `.opencode/agents/`
12. **Нет `TODO`** без ссылки на issue / stage / ADR
13. **Нет закомментированного кода** в `Assets/Scripts/`

Выведи:

| Проблема | Файл | Что сделать |
|----------|------|-------------|
| ... | ... | ... |

Если всё чисто — так и напиши, и перечисли что именно проверено.
Не выдумывай результаты: запускай проверки реально.
