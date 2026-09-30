# Runbook — Adding Content

## Purpose

Добавлять ассеты, скрипты, префабы и сцены по правилам проекта, не ломая структуру.

## Новый скрипт

### 1. Выбрать слой

| Что делает | Слой | Папка |
|---|---|---|
| Композиция, сервисы, абстракции | `Core` | `Assets/Scripts/Core/` |
| Игровые правила | `Gameplay` | `Assets/Scripts/Gameplay/<Domain>/` |
| Поведение, навигация | `AI` | `Assets/Scripts/AI/` |
| Сетевой код | `Net` | `Assets/Scripts/Net/` |
| Презентация | `UI` | `Assets/Scripts/UI/` |
| Камера | `CameraRig` | `Assets/Scripts/CameraRig/` |
| Звук | `Audio` | `Assets/Scripts/Audio/` |
| Сохранение | `Save` | `Assets/Scripts/Save/` |
| Только редактор | `Editor` | `Assets/Scripts/Editor/` |

### 2. Namespace

```csharp
namespace Woodberry.Gameplay.Player
```

Формат: `Woodberry.<Слой>[.<Домен>]`.

### 3. Имя файла = имя типа

Один основной тип на файл. `PlayerController.cs` содержит `PlayerController`.

### 4. Правила полей

```csharp
[SerializeField] private float _moveSpeed = 3f;   // ✅ инспектор
public float MoveSpeed => _moveSpeed;             // ✅ доступ
public float MoveSpeed { get; set; }              // ✅

public float moveSpeed;                           // ❌ public поле в рантайме
public float MoveSpeed;                           // ❌ public поле в рантайме
```

**Все поля, видимые дизайнеру, — `[SerializeField] private` + property.**

### 5. Проверить

```
read_console(action: "get", types: ["error", "warning"], count: 50)
```

## Новый ассет

### Куда

`Assets/Woodberry/<Category>/[<Subcategory>/]`

| Тип | Куда |
|---|---|
| Спрайт, текстура 2D | `Woodberry/Art/` |
| Звук | `Woodberry/Audio/{Music,SFX,Ambience,UI}/` |
| Материал | `Woodberry/Materials/` |
| Модель | `Woodberry/Models/` |
| Префаб | `Woodberry/Prefabs/` |
| Сцена | `Woodberry/Scenes/` |
| SO-конфиг | `Woodberry/Settings/` |
| UI-элемент | `Woodberry/UI/` |
| VFX | `Woodberry/VFX/` |

### Имя

`PascalCase`, без пробелов, без кириллицы.
`PlayerController.prefab`, `Enemy_Zombie.asset`, `ItemDefinition_Flare.asset`.

Переименуй сразу то, что Unity назвал сам: `New GameObject` → `Player`,
`Cube` → `Crate_Wooden`.

### Проверить `.meta`

После создания ассета рядом лежит `.meta`. Он **обязан** попасть в коммит
вместе с ассетом.

## Новый префаб

1. Создать объект(ы) в сцене, настроить.
2. Выделить корневой объект.
3. **Ctrl+D / GameObject → Prefab** → перетащить в `Assets/Woodberry/Prefabs/`.
4. **Применить** в инспекторе префаба (Overrides → Apply All).
5. Удалить экземпляр из сцены, если он был временным.

### Правила

- Префаб — **единственный источник правды** для повторяющихся объектов.
  Настройка дубликата в сцене вместо префаба — антипаттерн.
- Изменения в префабе — через `Apply`. Ручное редактирование инстанса ломает связь.
- Префаб не содержит логики, зависящей от единственного экземпляра.
- **Никогда не редактировать `.prefab` как текст** (YAML). Только редактор или MCP.

## Новый SO-конфиг

```csharp
[CreateAssetMenu(fileName = "Enemy_Definition", menuName = "Woodberry/Enemy Definition")]
public sealed class EnemyDefinition : ScriptableObject
{
    [SerializeField, Min(1f)] private float _maxHealth = 100f;
    [SerializeField] private string _displayName;

    public float MaxHealth => _maxHealth;
    public string DisplayName => _displayName;

    private void OnValidate()
    {
        if (_maxHealth <= 0f)
        {
            Debug.LogError($"[{nameof(EnemyDefinition)}] MaxHealth должен быть > 0", this);
        }
    }
}
```

Правила — см. ADR 0004. Главное:
- SO = **дизайн-данные**, не runtime-состояние
- `OnValidate` ловит невалидные данные в редакторе
- В рантайме SO **неизменяем**

Создать: **Assets → Create → Woodberry → …**

## Новая сцена

1. **File → New Scene** (или **Scene → New Scene**)
2. Настроить: камера, свет, настройки окружения
3. **File → Save As** → `Assets/Woodberry/Scenes/Woodberry_<Name>.unity`

Обязательно в сцене:
- ✅ Камера (или ссылка на `CameraRig`)
- ✅ Свет (хотя бы минимальный, чтобы сцена не была полностью чёрной до настройки)
- ✅ Если сцена — уровень игры: NavMesh (через `NavMeshSurface`)

### Правила

- **Никогда не редактировать `.unity` как текст** (YAML). Только редактор или MCP.
- Добавлять сцену в Build Settings: **File → Build Settings → Add Open Scenes**.
- Имя: `Woodberry_<Name>`, `PascalCase`.

## Порядок изменений (чтобы ничего не сломать)

Если меняешь что-то, что используется в сцене или префабе:

1. Изменить скрипт
2. `refresh_unity(compile: "request")` + `read_console(action: "get", types: ["error"])` — убедиться в компиляции
3. Открыть затронутые сцены и префабы
4. Проверить, что ссылки не стали `Missing`
5. `File → Save`

## Проверка после любых изменений

```
1. read_console(action: "clear")
2. refresh_unity(mode: "if_dirty", compile: "request", wait_for_ready: true)
3. read_console(action: "get", types: ["error", "warning"], count: 50)
4. run_tests(mode: "EditMode", include_failed_tests: true)
5. get_test_job(job_id: ..., include_failed_tests: true, wait_timeout: 60)
6. run_tests(mode: "PlayMode", include_failed_tests: true, init_timeout: 120000)
7. get_test_job(job_id: ..., include_failed_tests: true, wait_timeout: 120)
```

## Чего делать нельзя

- ❌ Ручной YAML-редакт `.unity` / `.prefab` / `.asset`
- ❌ Удаление `.meta` вручную
- ❌ `public` поля в рантайме
- ❌ `Resources.Load` для геймплейных данных
- ❌ Логика в UI-контроллере
- ❌ Сетевые API вне `Assets/Scripts/Net/`
- ❌ `FindObjectOfType` в геймплейном коде
