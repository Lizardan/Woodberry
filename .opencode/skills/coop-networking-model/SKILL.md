---
name: coop-networking-model
description: Проектирует authority-модель и сетевой seam для кооперативного этапа Woodberry. Использовать перед Stage 03 и при добавлении любой сетевой синхронизации.
compatibility: opencode
metadata:
  audience: gameplay-engineer, system-analyst
  domain: networking
---

# Coop Networking Model

## Триггер

Используй, когда:
- начинаешь Stage 03 или любой сетевой этап
- решаешь, что реплицировать
- пишешь код, который «должен работать в сети»
- чинишь рассинхрон

## Фундаментальное правило

> Клиент **никогда** не является source of truth для игрового состояния.

Клиент отправляет **намерение**. Авторитет решает и **результирует**.

## Обязательный шаг 0: проверить ADR

```
docs/adr/0003-networking-stack.md
```

Статус должен быть `Accepted`. Если `Proposed` — **код не пишется**.
Сначала принять решение о стеке, обновить `AGENTS.md → Project facts`.

## Шаг 1. Определить authority для каждого состояния

Заполни таблицу **до** написания кода:

| Состояние | Кто авторитетен | Реплицируется | Частота | Интерполируется |
|---|---|---|---|---|
| Позиция игрока | | | | |
| Здоровье | | | | |
| Инвентарь | | | | |
| Состояние предмета в мире | | | | |
| Поведение ИИ | | | | |
| Звук, VFX, анимация | Локально | ❌ | — | — |
| UI, HUD | Локально | ❌ | — | — |

Если строка не заполнена — это дыра. Заполни или вынеси в Open questions.

## Шаг 2. Intent → Result, а не «сделай и сообщи»

```csharp
// ✅ Правильно: намерение → валидация → результат
public void RequestPickUp(Guid itemId) { /* отправляем intent */ }

// Авторитет:
public void OnPickUpRequested(Guid itemId)
{
    var item = _items.Get(itemId);
    if (item == null || item.IsClaimed || !IsInRange(item)) 
    {
        return;  // тихий отказ, результат не реплицируется
    }
    item.IsClaimed = true;
    _inventory.Add(item);      // результат
    NotifyInventoryChanged();  // реплицируется всем
}
```

```csharp
// ❌ Неправильно: клиент делает и надеется
public void OnInteractPressed()
{
    _inventory.Add(item);   // у каждого клиента своё, рассинхрон
}
```

## Шаг 3. Что реплицировать, а что нет

### Реплицируй

- Игровое состояние (позиция, здоровье, инвентарь)
- Результаты действий (подобрал, убил, открыл)
- События мира (потушенный огонь, сломанная дверь)

### НЕ реплицируй

- Анимации, VFX, звук
- UI, HUD
- Положение камеры
- Косметика
- Локальные настройки игрока
- Всё, что клиент может восстановить локально

**Вопрос-тест:** «Если это не реплицировать, сломается ли геймплей?»
Нет → не реплицируй.

## Шаг 4. Один класс для local и remote

```csharp
// ✅ Один класс
public sealed class PlayerController : MonoBehaviour
{
    private IInputReader _input;

    // local → InputReader, remote → заглушка/реплицированное состояние
}
```

```csharp
// ❌ Два класса
public sealed class PlayerController : MonoBehaviour { }
public sealed class NetworkPlayerController : PlayerController { }
```

**Причина:** два класса разойдутся в коде через месяц, и поддержка станет невозможной.

## Шаг 5. Seam

```
Gameplay  →  INetworkService (в Core)
               ↑
            реализация в Net/
```

```csharp
// Core/INetworkService.cs
public interface INetworkService
{
    bool IsAuthority { get; }
    bool IsSpawned { get; }
    void RequestPickUp(Guid itemId);
    // ...
}
```

Правила:
- Геймплей знает **только** интерфейс
- Сетевые API — **только** в `Assets/Scripts/Net/`
- Никаких `if (isNetworked)` в геймплее
- Нужно спросить «я authority?» → `INetworkService.IsAuthority`

## Проверки grep'ом

```powershell
# Сетевые API вне Net/
Select-String -Path "Assets/Scripts/**/*.cs" -Pattern "Unity\.Netcode|NetworkBehaviour|NetworkManager" |
  Where-Object { $_.Path -notlike "*\Net\*" }

# if (isNetworked) в геймплее
Select-String -Path "Assets/Scripts/Gameplay/**/*.cs" -Pattern "isNetworked|IsServer|IsClient|IsHost"

# Отдельный сетевой контроллер игрока
Get-ChildItem -Recurse Assets/Scripts -Filter "Network*Player*.cs"
```

Любое срабатывание = **Critical**.

## Тикрейт и интерполяция

Стартовые значения (требуют профилирования, см. `docs/specs/performance-budget.md`):

| Параметр | Старт |
|---|---|
| Симуляция | 20–30 Hz |
| Репликация позиции | 10–15 Hz |
| Интерполяция | Да, буфер ~100 ms |
| Join-снапшот | Полный |

**Без интерполяции управление на 20 Hz будет дёрганым.** Это не «полировка»,
это базовая читаемость.

## Типичные ошибки

| Ошибка | Почему плохо | Правильно |
|---|---|---|
| Клиент меняет состояние напрямую | Рассинхрон, читерство | Intent + серверная валидация |
| Репликация `Transform` каждый кадр | Трафик, шум | Интерполяция на приёме |
| Спавн сущностей на клиенте | Дубликаты, «фантомы» | Спавн только на authority |
| Репликация звука/VFX | Бессмысленный трафик | Локально по реплицированным событиям |
| Нет валидации расстояния | Клиент крадёт издалека | Проверка радиуса на авторитете |
| `NetworkBehaviour` в `Gameplay/` | Утечка транспорта | Компонент в `Net/`, данные в `Gameplay/` |
| Разные классы для local/remote | Расхождение кода | Один класс |
| Не зафиксирован тикрейт | Случайные изменения | Зафиксировать в spec |

## Критерии успеха

- [ ] ADR 0003 = `Accepted`, версия пакета в `AGENTS.md`
- [ ] Таблица authority заполнена для всех состояний
- [ ] Grep-проверки проходят (0 срабатываний)
- [ ] Один класс для local и remote
- [ ] Спавн только на authority
- [ ] Интерполяция работает
- [ ] Join/leave проверены
- [ ] EditMode-тест: геймплей работает с заглушкой `INetworkService` **без** сети
- [ ] Производительность на 4 игроках в бюджете
- [ ] Указано, что проверено, а что — нет

Подробнее: `docs/specs/coop-networking.md`, `docs/stages/stage-03-coop-baseline.md`
