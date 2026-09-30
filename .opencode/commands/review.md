---
description: Строгий review изменений — correctness, edge cases, drift, test gaps
agent: build
subtask: true
---

Сделай строгий review текущих изменений как senior engineer.

Задача: $ARGUMENTS

Сначала посмотри фактический diff:
```
git status --short
git diff --stat
```

Фокус:
- **Correctness** — делает ли логика то, что заявлено
- **Edge cases** — пустые данные, `null`, ноль, отрицательные значения, гонки, отсутствие ресурсов
- **Architecture drift** — смешение слоёв
- **Test gaps** — логика без тестов, happy path без негативных
- **Contract drift** — spec разошёлся с кодом
- **Performance** — аллокации и `GetComponent` в `Update`/`FixedUpdate`/`LateUpdate`
- **Сетевые проблемы** — доверие клиенту, рассинхрон
- **Утечки** — неотписанные подписки, `static event`
- **Избыточная сложность** и мёртвый код

Проверь grep'ами (см. skill `unity-architecture-review`):
- Сетевые API вне `Assets/Scripts/Net/`
- `if (isNetworked)` / `IsServer` в геймплее
- `UnityEngine.InputSystem` вне `Core`
- `FindObjectOfType` в геймплее
- `Resources.Load` для геймплейных данных
- Runtime-код ссылается на `UnityEditor`
- `public`-поля в рантайме

Формат вывода:

```
## Critical issues
- <файл:строка> — <что не так> — <почему критично>

## Important follow-ups
- ...

## Nice-to-have improvements
- ...

## Testing gaps
- <что не покрыто тестами>

## Architecture concerns
- <нарушение границ>

## Overall assessment
[Ready / Ready with follow-ups / Not ready]
```

Не правь код. Только отчёт.
