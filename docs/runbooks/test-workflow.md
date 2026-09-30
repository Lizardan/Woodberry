# Runbook — Test Workflow

## Purpose

Писать и запускать тесты так, чтобы они давали достоверный сигнал.

## Prerequisites

`com.unity.test-framework` 1.8.0 установлен.
asmdef-файлы: `Woodberry.Tests.EditMode`, `Woodberry.Tests.PlayMode` (создаются в Stage 00).

## EditMode vs PlayMode

| | EditMode | PlayMode |
|---|---|---|
| Что тестирует | Чистую логику, формулы, валидаторы, сериализацию SO | MonoBehaviour-жизненный цикл, физику, навигацию, UI-поток |
| Сцена | Не нужна | Нужна или создаётся программно |
| Скорость | Миллисекунды | Секунды |
| Ссылки | `Woodberry.Core`, `Woodberry.Runtime` | `Woodberry.Core`, `Woodberry.Runtime` |

**Правило выбора:** если тестируемое не наследует `MonoBehaviour` и не трогает
Unity-жизненный цикл → EditMode. Иначе PlayMode.

## Именование

```
<ТестируемыйТип>Tests.cs
Метод: <Метод>_When<Условие>_Then<Ожидание>
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

1. **Один тест — одно поведение.** Если нужно два `Assert` — вероятно, два теста.
2. **Имя описывает наблюдаемое поведение**, а не внутренний метод.
3. **Не тестируй реализацию** — тестируй наблюдаемое поведение. Не проверяй приватные поля.
4. **Arrange–Act–Assert обязателен.** Читается как сценарий.
5. **Независимость.** Тесты не зависят от порядка выполнения.
6. **Никаких `Time.timeScale`** ради ускорения без явной необходимости — ломает поведение.
7. **EditMode-тесты не зависят от сцены.**
8. **PlayMode-тесты чистят за собой**: destroy спавннутые объекты, снять подписки,
   восстановить изменённые настройки.
9. **Тест без ассертов — мусор.** Удали его или добавь проверку.

## Запуск из редактора

**Test → Test Runner**

| Панель | Что запускает |
|---|---|
| **EditMode** | EditMode-тесты (без перезагрузки сцены) |
| **PlayMode** | PlayMode-тесты (входят в Play Mode) |

**Run All** на каждой вкладке.

## Запуск через MCP

```
mcp__unityMCP__run_tests(mode="EditMode", include_failed_tests=true)
mcp__unityMCP__get_test_job(job_id="...", include_failed_tests=true, wait_timeout=60)

mcp__unityMCP__run_tests(mode="PlayMode", include_failed_tests=true, init_timeout=120000)
mcp__unityMCP__get_test_job(job_id="...", include_failed_tests=true, wait_timeout=120)
```

PlayMode первый запуск долгий из-за перезагрузки домена. Это нормально, не паникуй
и не перезапускай.

Фильтрация по группе:
```
mcp__unityMCP__run_tests(mode="EditMode", assembly_names=["Woodberry.Tests.EditMode"])
mcp__unityMCP__run_tests(mode="EditMode", test_names=["Woodberry.Tests.EditMode.PlayerStaminaTests"])
```

## Приоритет тестов

Если не хватает времени, тестируй в этом порядке:

1. Игровые правила (урон, выносливость, инвентарь, взаимодействие)
2. Сохранение и загрузка
3. Сетевая синхронизация
4. Обработка ошибок
5. Крайние случаи (пустые данные, null, гонки)

## Типичные проблемы

### Тест падает только при запуске всех вместе

**Причина:** тесты не изолированы, есть общее состояние (статическое, синглтон, SO-ассет).

**Решение:** изолировать состояние. Не использовать статические поля в тестируемом коде.

### Тест проходит в EditMode, падает в PlayMode

**Причина:** EditMode-тест не воспроизводит реальное окружение. Или наоборот —
в EditMode объект не инициализирован, а в PlayMode вызывается `Awake`/`Start`.

**Решение:** проверить, что тестируется наблюдаемое поведение, а не побочный эффект
жизненного цикла. Для жизненного цикла — PlayMode-тест.

### PlayMode-тест оставляет объекты в сцене

**Симптом:** следующие тесты падают из-за «лишних» объектов.

**Решение:** `[TearDown]` с уничтожением всех спавннутых объектов.
Использовать `Object.DestroyImmediate` в EditMode, `Object.Destroy` + yield в PlayMode.

### PlayMode-тест падает с ошибкой в логе

**Симптом:** тест зелёный, но Console полон ошибок.

**Решение:** `LogAssert.ignoreFailingMessages = true` в `[SetUp]`,
если ошибки ожидаемые. Если не ожидаемые — это баг, а не «шум».

## Чего делать нельзя

- ❌ Удалять или `[Ignore]` упавший тест без причины в отчёте
- ❌ `Assert.Ignore` для «нестабильных» тестов вместо починки
- ❌ Подавлять ошибки, чтобы тест стал зелёным
- ❌ Ходить в БД, файловую систему или сеть из тестов
