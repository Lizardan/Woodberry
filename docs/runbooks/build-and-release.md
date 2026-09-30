# Runbook — Build and Release

## Purpose

Собрать player-сборку и проверить, что она рабочая.

## Prerequisites

- Unity Editor `6000.6.3f1`
- Все тесты проходят
- `read_console(action: "get", types: ["error"])` чист
- Сцена добавлена в Build Settings

## Шаг 1. Открыть Build Settings

**File → Build Settings**

## Шаг 2. Проверить сцены

В **Scenes In Build** должна быть **только игровая сцена** в правильном порядке.
`SampleScene` из шаблона — убрать.

**Проверка:** список сцен соответствует тому, что должно попасть в сборку.
Первая сцена в списке — та, что запускается при старте.

## Шаг 3. Проверить настройки Player

**Edit → Project Settings → Player**:

| Вкладка | Параметр | Значение |
|---|---|---|
| **Other Settings** | Active Input Handling | Input System Package (New) |
| **Other Settings** | API Compatibility Level | .NET Standard 2.1 |
| **Other Settings** | Scripting Backend | Mono (или IL2CPP для релизов) |
| **Resolution and Presentation** | Fullscreen Mode | Full Screen Window |
| **Configuration** | Scripting Define Symbols | пусто, если не нужно |

## Шаг 4. Проверить Graphics

**Edit → Project Settings → Graphics**:

| Параметр | Значение |
|---|---|
| Scriptable Render Pipeline Settings | URP asset назначен |
| **Never** raytracing | выключен, если не нужен |
| **Instancing** | включён (нужен для GPU instancing) |

## Шаг 5. Собрать

**File → Build Settings → Build** (для платформы, указанной в Build Settings)
РёР»Рё **Build And Run**.

Выбрать выходную папку. Сборка **не** коммитится.

**Проверка:** сборка завершилась без ошибок, папка создана.

## Шаг 6. Проверить через MCP

manage_build(action: "build", target: "windows64", output_path: "Build/Woodberry.exe")

## Шаг 7. Smoke-проверка сборки

Запустить собранный player и проверить минимальный набор:

| Проверка | Ожидаемо |
|---|---|
| Player запускается | Окно открылось, не падает |
| Сцена загрузилась | Видна игровая сцена |
| Ввод работает | WASD двигает, Shift бежит |
| Камера следует | Плавно, без рывков |
| Нет ошибок | Player.log чист |
| Нет `Missing` скриптов | В логе нет `NullReferenceException` на старте |

**Где смотреть лог:**
```
%USERPROFILE%\AppData\LocalLow\<Company>\<Product>\Player.log
```

## Критичная проверка: Editor-слой не попал в сборку

Если в сборке окажется `Woodberry.Editor`, сборка упадёт или будет работать
неправильно. Проверка:

manage_build(action: "build", target: "windows64", output_path: "Build/Woodberry.exe")

И убедиться, что `Woodberry.Editor.asmdef` помечен как **Editor** платформы
(в `.asmdef` поле `includePlatforms: ["Editor"]`).

## Известные проблемы

### Сборка падает с `Scripts have compiler errors`

См. `incident-compile-errors.md`.

### Сборка падает: `Build completed with a result of 'Failed'`

Проверить в Unity Console полный лог. Частые причины:
- Не установлен модуль платформы (Android/iOS/IL2CPP)
- Не хватает Visual Studio / SDK для IL2CPP
- Невалидные ссылки на ассеты

### В сборке `Missing (Mono Script)`

Скрипт есть, но потеряна ссылка.

**Причины:**
- Переименован скрипт, `.meta` не переименован
- Скрипт не в asmdef, а сцена ссылается на старую сборку
- Скрипт удалён, а ссылка осталась

**Решение:** найти объект со ссылкой, переназначить компонент.
Профилактика: не переименовывать `.meta` вручную, не редактировать YAML.

### IL2CPP не собирает

Нужна Visual Studio с компонентом «Desktop development with C++» и Windows SDK.

### Большая сборка

Проверить, что не включены лишние ассеты:
- `Test Framework` не нужен в player-сборке
- Debug-сборка больше релизной в разы
- Текстуры импортированы с неверными настройками сжатия (см. `asset-standards.md`)

## Release checklist

- [ ] Все тесты проходят
- [ ] `read_console(action: "get", types: ["error"])` чист
- [ ] Build Settings: только игровые сцены
- [ ] Player Settings: правильный input backend
- [ ] Сборка проходит smoke-проверку
- [ ] `Player.log` чист
- [ ] Размер сборки в бюджете (`docs/specs/performance-budget.md`)
- [ ] Производительность проверена на 4 игроках
- [ ] Stage report создан
- [ ] Review пройден

## Чего делать нельзя

- ❌ Коммитить папку `Builds/` (в `.gitignore`)
- ❌ Публиковать сборку в репозиторий или в интернет без явного разрешения
- ❌ Менять `ProjectSettings/` ради одной сборки (меняй осознанно, отдельным коммитом)
