---
name: unity-architecture-review
description: Проверяет, что изменения не нарушили границы слоёв Unity-проекта. Использовать перед завершением любого этапа, затрагивающего геймплей, сеть или UI.
compatibility: opencode
metadata:
  audience: qa-analyst, gameplay-engineer
  domain: architecture
---

# Unity Architecture Review

## Триггер

Используй, когда:
- закончил этап со слоями `Gameplay`, `Net`, `UI`
- добавлял сетевой код
- сомневаешься, не протёк ли сетевой слой в геймплей
- делаешь ревью перед коммитом

## Проверки

### 1. Границы слоёв

```powershell
# Gameplay не должен знать про UI
Select-String -Path "Assets/Scripts/Gameplay/**/*.cs" -Pattern "Woodberry\.UI|UnityEngine\.UI"

# Gameplay не должен знать про Net напрямую
Select-String -Path "Assets/Scripts/Gameplay/**/*.cs" -Pattern "Woodberry\.Net|Unity\.Netcode|NetworkBehaviour|NetworkVariable"

# Core не должен знать про Gameplay
Select-String -Path "Assets/Scripts/Core/**/*.cs" -Pattern "Woodberry\.Gameplay"

# UI не должен решать геймплей
Select-String -Path "Assets/Scripts/UI/**/*.cs" -Pattern "Woodberry\.Gameplay"
```

Последняя проверка даёт ложные срабатывания: UI **должен** ссылаться на
абстракции геймплея. Ищи не namespace, а конкретные нарушения:
`FindObjectOfType`, прямое изменение полей игровых сущностей.

### 2. Сетевой seam

```powershell
# Сетевые API только в Net/
Select-String -Path "Assets/Scripts/**/*.cs" -Pattern "Unity\.Netcode|NetworkBehaviour|NetworkManager|NetworkVariable" |
  Where-Object { $_.Path -notlike "*\Net\*" }

# if (isNetworked) в геймплее — нарушение
Select-String -Path "Assets/Scripts/Gameplay/**/*.cs" -Pattern "isNetworked|IsServer|IsClient|IsHost"
```

### 3. Input seam

```powershell
# InputSystem только в Core/
Select-String -Path "Assets/Scripts/**/*.cs" -Pattern "UnityEngine\.InputSystem" |
  Where-Object { $_.Path -notlike "*\Core\*" }

# Строковые имена экшенов — хрупко
Select-String -Path "Assets/Scripts/**/*.cs" -Pattern '"(Move|Jump|Interact|Sprint)"'
```

### 4. Запрещённые практики

```powershell
# FindObjectOfType в геймплее
Select-String -Path "Assets/Scripts/Gameplay/**/*.cs" -Pattern "FindObjectOfType|FindFirstObjectByType|GameObject\.Find"

# Resources.Load для геймплейных данных
Select-String -Path "Assets/Scripts/**/*.cs" -Pattern "Resources\.Load"

# public-поля в рантайме
Select-String -Path "Assets/Scripts/Gameplay/**/*.cs" -Pattern "public\s+(?!void|static|override|class|readonly)[A-Z]\w*\s+\w+\s*[;=]"
```

Последний regex приблизительный. Проверяй глазами по `AGENTS.md → Naming conventions`.

### 5. Runtime → Editor

```powershell
# Runtime-код не должен ссылаться на UnityEditor
Select-String -Path "Assets/Scripts/**/*.cs" -Pattern "using UnityEditor|UnityEditor\." |
  Where-Object { $_.Path -notlike "*\Editor\*" -and $_.Path -notlike "*\Tests\*" }
```

### 6. Производительность в горячих путях

```powershell
# GetComponent в Update (нужен контекст — смотри вручную)
Select-String -Path "Assets/Scripts/**/*.cs" -Pattern "void (Update|FixedUpdate|LateUpdate)"

# Ручная проверка: внутри этих методов ищи
# GetComponent, Find, new, LINQ (.Where/.Select/.ToList), строковая конкатенация
```

### 7. asmdef-контракт

Проверь в `Woodberry.Editor.asmdef`: `includePlatforms` содержит только `Editor`.
Проверь, что `Woodberry.Runtime.asmdef` **не** ссылается на `Woodberry.Editor`.

## Интерпретация

| Результат | Вывод |
|---|---|
| Любое срабатывание в п. 1–2 | **Critical** — нарушение обязательной границы |
| Срабатывание в п. 4 | **Critical**, если в геймплее; `Important` в Editor/тестах |
| Срабатывание в п. 6 | **Important** — проверить вручную, действительно ли в горячем пути |
| Нет срабатываний | Пройдено |

## Критерии успеха

В ответе должно быть:
- Список выполненных проверок с результатом (пусто / N срабатываний)
- Каждое срабатывание: файл, строка, почему это нарушение
- Вердикт: `[Ready / Ready with follow-ups / Not ready]`
- Ссылка на `AGENTS.md` раздел, который нарушен

Не выдумывай результаты. Если grep не запускался — скажи `Not verified`.
