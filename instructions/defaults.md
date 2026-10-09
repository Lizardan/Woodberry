# Instructions — Defaults

Долгоживущие правила, подключаемые через `opencode.json → instructions`.  
Один файл — одна тема. Не смешивай с `AGENTS.md`: там проект, здесь — как работать.  
Всё, что уже описано в `AGENTS.md` (язык общения, порядок работы, Unity-верификация,  
git-политика, запрет YAML-правки ассетов), здесь намеренно **не повторяется** —  
источник истины один.

## Спецификации

- В контекст автоматически подключены ключевые спеки: `game-vision`, `core-gameplay`,
  `coop-networking`, `save-system`, `performance-budget`, `code-review-flow`
  (список — `opencode.json → instructions`). Остальные (`art-generation`,
  `asset-standards`) читай по необходимости — они источник истины по своим темам.
- Расхождение spec и кода — это `contract drift`: его надо либо закрыть, либо  
  явно зафиксировать. Правила, когда обновлять spec, — в `AGENTS.md`.

## Плагин `plan`

В `opencode.json` для `plan` выставлен `edit: deny`. Это намеренно: plan не должен менять файлы.

Если плану нужно зафиксировать спецификацию:

- вызови субагента `system-analyst` (у него `edit: allow`), либо
- переключись в `build` и обнови spec отдельным инкрементом.

## Git

- Коммит создаётся только по явной просьбе пользователя. Политика по  
  `push`/`reset --hard`/`clean` и правила `.meta` — в `AGENTS.md` и  
  `instructions/safety.md`.

