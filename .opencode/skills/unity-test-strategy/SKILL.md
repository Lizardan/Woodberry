---
name: unity-test-strategy
description: Определяет, что и как тестировать в Unity-проекте, и пишет тесты, которые дают достоверный сигнал. Использовать при добавлении новой логики или при падении/нестабильности тестов.
compatibility: opencode
metadata:
  audience: qa-analyst, gameplay-engineer
  domain: testing
---

# Unity Test Strategy

## Триггер

Используй, когда:
- появилась новая логика, которую нужно покрыть
- выбираешь между EditMode и PlayMode
- тесты падают или нестабильны
- непонятно, что вообще тестировать

## Шаг 1. Выбор уровня теста

```
Код наследует MonoBehaviour?
├─ Нет → EditMode
│        (чистая логика: формулы, состояния, валидаторы, сериализация SO)
└─ Да → Использует Unity-жизненный цикл, физику, навигацию, UI?
         ├─ Да → PlayMode
         └─ Нет → EditMode (всё равно дешевле)
```

| Признак | Уровень |
|---|---|
| `PlayerStamina.Spend()` | EditMode |
| Формула урона | EditMode |
| Сериализация SO | EditMode |
| `OnValidate` инварианты | EditMode |
| `Awake`/`OnEnable`/`OnDisable` | PlayMode |
| `Rigidbody`, `NavMeshAgent` | PlayMode |
| UI-поток, `EventSystem` | PlayMode |
| Взаимодействие через коллайдеры | PlayMode |
| Жизненный цикл сцены | PlayMode |

## Шаг 2. Проверка: тестируется ли поведение

Хороший тест отвечает на вопрос «что наблюдает пользователь игры».
Плохой — «что происходит внутри».

```csharp
// ❌ Тест реализации
Assert.That(_stamina._current, Is.EqualTo(7f));

// ✅ Тест поведения
Assert.That(stamina.Current, Is.EqualTo(7f));
```

## Шаг 3. Написание теста

### Имя

```
Метод_WhenУсловие_ThenОжидание
```

```csharp
[Test]
public void Spend_WhenAmountExceedsCurrent_ThenClampsToZero()
[Test]
public void Restore_WhenCalledWithNegativeDeltaTime_ThenDoesNotIncrease()
[Test]
public void OnDied_WhenCalledTwice_ThenRaisesEventOnce()
```

### Структура

```csharp
[Test]
public void Spend_WhenAmountExceedsCurrent_ThenClampsToZero()
{
    // Arrange
    var stamina = new PlayerStamina(maxValue: 10f);
    stamina.Spend(4f);

    // Act
    stamina.Spend(100f);

    // Assert
    Assert.That(stamina.Current, Is.EqualTo(0f));
}
```

### Правила

| Правило | Причина |
|---|---|
| Один тест — одно поведение | Иначе непонятно, что именно сломалось |
| Arrange–Act–Assert | Читается как сценарий |
| Независимость от порядка | Тесты должны проходить и по отдельности |
| Без `Time.timeScale` без нужды | Ломает поведение |
| Без БД/файлов/сети | Тест должен быть герметичным |
| Ассерты на границах | Не `0.5f`, а `0f` |

## Шаг 4. Обязательные краевые случаи

Для **каждой** функции, меняющей состояние:

```
1. Нормальный случай
2. Нулевое значение
3. Отрицательное значение (если не запрещено входной валидацией)
4. Граничное значение (точно max, ровно 0)
5. Значение за границей (должно клампиться или отклоняться)
6. Повторный вызов (идемпотентность там, где это важно)
```

```csharp
// Обязательный набор для метода, меняющего состояние
[Test] Method_WhenNormalInput_ThenExpectedResult
[Test] Method_WhenZeroInput_ThenExpectedResult
[Test] Method_WhenNegativeInput_ThenClampsOrRejects
[Test] Method_WhenAtMaxValue_ThenDoesNotExceedMax
[Test] Method_WhenValueExceedsMax_ThenClamps
[Test] Method_WhenCalledTwice_ThenIdempotentOrDocumented
```

## Шаг 5. PlayMode-специфика

### Очистка обязательна

```csharp
[TearDown]
public void TearDown()
{
    foreach (var go in _spawned)
    {
        Object.Destroy(go);
    }
    _spawned.Clear();
    LogAssert.ignoreFailingMessages = false;
}
```

### Ожидаемый лог

```csharp
[SetUp]
public void SetUp()
{
    LogAssert.ignoreFailingMessages = true;  // только если ошибки ожидаемые
}
```

Если ошибки **не** ожидаемые — это баг, а не «шум».

### Ожидание состояния

```csharp
yield return new WaitUntil(() => controller.HasArrived);
Assert.That(controller.CurrentSpeed, Is.EqualTo(0f));
```

`WaitUntil`, а не `WaitForSeconds` — второй flaky.

## Шаг 6. Запуск

```
mcp__unityMCP__read_console(action="clear")
mcp__unityMCP__refresh_unity(compile="request", wait_for_ready=true)
mcp__unityMCP__read_console(action="get", types=["error"], count="50")
mcp__unityMCP__run_tests(mode="EditMode", include_failed_tests=true)
mcp__unityMCP__get_test_job(job_id="...", include_failed_tests=true, wait_timeout=60)
mcp__unityMCP__run_tests(mode="PlayMode", include_failed_tests=true, init_timeout=120000)
mcp__unityMCP__get_test_job(job_id="...", include_failed_tests=true, wait_timeout=120)
```

## Разбор проблем

| Симптом | Причина | Решение |
|---|---|---|
| Падает только в «Run All» | Общее состояние: static, синглтон, SO | Изолировать состояние |
| Проходит отдельно, падает вместе | Порядокзависимость | Изолировать состояние |
| Flaky, иногда проходит | `WaitForSeconds`, время, случайность | `WaitUntil`, детерминированные данные |
| Зелёный, но в Console ошибки | Не проверяется лог | `LogAssert` или проверка отсутствия ошибок |
| EditMode зелёный, PlayMode красный | EditMode не воспроизводит окружение | Для жизненного цикла нужен PlayMode |

## Критерии успеха

В ответе должно быть:
- Уровень для каждой новой функции (EditMode/PlayMode) с обоснованием
- Список покрытых краевых случаев
- **Что НЕ покрыто** и почему
- Фактический результат запуска (passed/failed + числа)
- Если не запускал — `Not verified: <причина>`

Подробнее: `docs/runbooks/test-workflow.md`
