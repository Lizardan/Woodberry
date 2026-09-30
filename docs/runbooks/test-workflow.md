# Runbook — Test Workflow

## Purpose

Писать и запускать тесты так, чтобы они давали достоверный сигнал.

## Prerequisites

`com.unity.test-framework` 1.8.0 установлен.
asmdef-файлы: `Woodberry.Tests.EditMode`, `Woodberry.Tests.PlayMode` (создаются в Stage 00).

## EditMode vs PlayMode

| | EditMode | PlayMode |
|---|---|---|
| Что тестирует | Чистую логику, формулы, валидаторы, сериализацию SO | MonoBehaviour-жизненный цикл, физику, навигацию, UI-потоки |
| Сцена | Не нужна | Нужна или создаётся программно |
| Скорость | Миллисекунды | Секунды |
| Ссылки | `Woodberry.Core`, `Woodberry.Runtime` | `Woodberry.Core`, `Woodberry.Runtime` |

**Правило выбора:** если тестируемое не наследует `MonoBehaviour` и не трогает
Unity-жизненный цикл → EditMode. Иначе PlayMode.

## Именование

```
<TestTarget>Tests.cs
Методы: <Метод>_When<Условие>_Then<Ожидание>
```

Примеры:
```
PlayerStaminaTests.cs
Spend_WhenStaminaIsZero_ThenDoesNotGoNegative
Restore_WhenIdle_ThenRecoversUpToMax
PlayerController_WhenMovementInputIsZero_ThenStaysInPlace
```

## Структура теста

```csharp
using NUnit.Framework;

namespace Woodberry.Tests.EditMode
{
    public sealed class PlayerStaminaTests
    {
        [Test]
        public void Spend_WhenStaminaIsZero_ThenDoesNotGoNegative()
        {
            // Arrange
            var stamina = new PlayerStamina(maxValue: 10f);

            // Act
            stamina.Spend(5f);
            stamina.Spend(50f);

            // Assert
            Assert.That(stamina.Current, Is.EqualTo(0f));
        }
    }
}
```

## Правила

1. **Один тест — одно поведение.** Если нужны два `Assert` — вероятно, это два теста.
2. **Имя описывает поведение, а не внутренний метод.**
3. **Не тестируй реализацию** — тестируй наблюдаемое поведение. Проверка приватных
   полей ради тестов — признак, что не хватает публичного наблюдаемого интерфейса.
4. **Arrange–Act–Assert обязателен.**
5. **Независимость.** Тесты не зависят от порядка выполнения.
6. **Никаких `Time.timeScale` ради ускорения без явной необходимости.**
7. **EditMode-тесты не зависят от сцены.**
8. **PlayMode-тесты чистят за собой**: destroy спавнутых объектов, снимают подписки,
   восстанавливают изменённые настройки.
9. **Тест без ассертов — мусор.** Удали его или добавь проверку.

---

## Запуск из редактора

**Test → Test Runner**, вкладки EditMode и PlayMode, **Run All**.

---

## Запуск через MCP

Unity MCP запускает тесты автоматически. Инструменты: `run_tests`, `get_test_job`.

### EditMode

```json
{ "mode": "EditMode", "include_failed_tests": true }
```

### PlayMode

```json
{ "mode": "PlayMode", "include_failed_tests": true, "init_timeout": 120000 }
```

`init_timeout` для PlayMode обязательно увеличь: первый запуск после domain reload
долгий. Значение по умолчанию (15 с) не хватает.

### Дождаться результата

`run_tests` **асинхронный** — возвращает `job_id`, а не результат:

```json
{ "job_id": "<id>", "include_failed_tests": true, "wait_timeout": 60 }
```

Статусы: `running` → `succeeded` | `failed`.

Ответ содержит:

```json
{
  "status": "succeeded",
  "result": {
    "summary": {
      "total": 12, "passed": 12, "failed": 0,
      "skipped": 0, "durationSeconds": 0.31,
      "resultState": "Passed"
    },
    "results": []
  }
}
```

### Фильтрация

```json
{ "mode": "EditMode", "assembly_names": ["Woodberry.Tests.EditMode"] }
{ "mode": "EditMode", "group_names": ["Woodberry.Tests.EditMode.PlayerStaminaTests"] }
{ "mode": "EditMode", "test_names": ["Spend_WhenStaminaIsZero_ThenDoesNotGoNegative"] }
```

Также доступны `category_names` и `include_details`.

### Если job завис

Если `get_test_job` долго возвращает `running`, а Editor явно готов —
перезапусти прогон с `run_tests(clear_stuck: true)`.

---

## Честность отчёта

| Как прогнано | Как записать в отчёте |
|---|---|
| Через `run_tests` + `get_test_job` | `EditMode: 12/12 passed (run_tests)` |
| Вручную через Test Runner | `EditMode: 12/12 passed (ручной прогон)` |
| Не прогнано | `Not verified: тесты не запускались` |

Формулировка «тесты, вероятно, проходят» — не верификация.

**`total: 0` — не ошибка.** Это означает, что тестов в проекте нет. Так и пиши:
`total: 0 — тестов в проекте ещё нет`, а не «все тесты прошли».

---

## Приоритет тестов

1. Игровые правила (урон, стоимость, инвентарь, взаимодействие)
2. Сохранение и загрузка
3. Сетевая синхронизация
4. Крайние случаи (пустые данные, null, границы)
5. Обратная совместимость (старые данные, миграции)

## Типичные проблемы

### Тест падает только при запуске всех вместе

**Причина:** тесты не изолированы, есть общее состояние.
**Решение:** изолировать состояние. Общее статическое поле ради стабильности теста —
крайняя мера: сначала попробуй убрать само общее состояние.

### Тест проходит в EditMode, падает в PlayMode

**Причина:** EditMode-тест не воспроизводит реальное окружение.
**Решение:** проверяй, что тестируется на самом деле, а не покрытие поведения.

### PlayMode-тест оставляет объекты в сцене

**Симптом:** следующие тесты падают, но Console полон ошибок.
**Решение:** `[TearDown]` с уничтожением всех спавнутых объектов.
В EditMode используй `Object.DestroyImmediate`, в PlayMode — `Object.Destroy` + yield.

### PlayMode-тест падает с «килбекой в логе»

**Симптом:** тест зелёный, но Console полон ошибок.
**Решение:** `LogAssert.ignoreFailingMessages = true` в `[SetUp]`,
**если** ошибки ожидаемые. Если не ожидаемые — это стоп, а не «сумм».

## Чего делать нельзя

- ❌ Удалять или `[Ignore]` упавший тест без причины в отчёте
- ❌ `Assert.Ignore` для «нестабильных» тестов вместо починки
- ❌ Подавлять ошибки, чтобы тест стал зелёным
- ❌ Ходить в БД, датчики системы или сеть из тестов