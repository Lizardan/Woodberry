# ADRs

Здесь хранятся **Architecture Decision Records** — почему выбрано именно это решение.

## Когда нужен ADR

ADR пишется, когда решение:
- влияет на несколько модулей или stages
- имеет альтернативы, которые кто-то захочет пересмотреть
- несёт компромисс
- будет жить дольше одного stage
- важно для AI-агента: без контекста агент предложит «очевидное» неправильное решение

## Когда ADR НЕ нужен

- Локальный рефакторинг без влияния на границы
- Очевидное техническое решение без альтернатив
- Решение, которое легко откатить и которое не влияет на другие модули

## Формат

```
# ADR NNNN — Название

## Status           # Proposed / Accepted / Superseded by ADR-NNNN / Deprecated
## Date
## Context          # какая проблема решаем
## Decision         # что решили
## Consequences
  ### Positive
  ### Negative
## Alternatives considered
## Revisit when     # при каких условиях решение стоит пересмотреть
```

## Список ADR

| Файл | Решение | Статус |
|---|---|---|
| `0001-layer-and-assembly-architecture.md` | Слои и asmdef как границы | Accepted |
| `0002-save-data-format.md` | Формат сейвов | Proposed |
| `0003-networking-stack.md` | Сетевой стек для кооператива | **Proposed — блокирует Stage 03** |
| `0004-scriptable-objects-for-design-data.md` | SO для дизайн-данных | Accepted |
| `0005-input-abstraction.md` | `IInputReader` как единственная точка ввода | Accepted |
| `0006-scene-architecture-and-service-registry.md` | Три сцены + `ServiceRegistry` вместо межсценовых ссылок | Accepted |
| `0007-2d-sprite-game.md` | Переход на 2D-спрайты: движение, камера, риг персонажа | Accepted |

## Правило

**Меняешь контракт → обновляй ADR.** Не заводи новый, если решение ещё не принято:
меняй `Status` на `Superseded` и ссылайся на новый.
