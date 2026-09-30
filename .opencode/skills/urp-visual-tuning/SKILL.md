---
name: urp-visual-tuning
description: Настройка визуала в URP-проекте Woodberry — свет, Volume, материалы, шейдеры, draw calls. Использовать при визуальных проблемах и оптимизации рендера.
compatibility: opencode
metadata:
  audience: technical-artist, scene-level-designer
  domain: rendering
---

# URP Visual Tuning

## Триггер

Используй, когда:
- сцена выглядит «плоско» или «неправильно»
- нужно настроить пост-обработку
- draw calls или frame time выше бюджета
- подозреваешь, что шейдер/настройка неверны

## Правило 0: проверь API

Unity `6000.6.3f1`, URP `17.6.0`. API меняется между версиями.
**Не полагайся на память.**

```
mcp__unityMCP__unity_reflect(action="search", query="VolumeManager", scope="packages")
mcp__unityMCP__unity_reflect(action="get_type", class_name="Volume")
mcp__unityMCP__unity_docs(action="get_doc", class_name="Volume")

# Поиск шейдеров в проекте — до создания нового
mcp__unityMCP__manage_asset(action="search", filter_type="Shader", page_size=50)
mcp__unityMCP__manage_graphics(action="ping")
```

## Правило 1: URP, не Standard

| Вместо | Используй |
|---|---|
| `Standard` | `Universal Render Pipeline/Lit` |
| `Unlit/Texture` | `Universal Render Pipeline/Unlit` |
| Built-in Post-processing | URP **Volume** |

Шейдер из устаревшего туториала в URP может **отображаться фиолетовым** (not supported)
или работать неправильно.

## Свет: порядок настройки

1. **Render Settings** — ambient mode, ambient intensity, рендереры
2. **Освещение** — Directional / Spot / Point
3. **Тени** — только там, где критично для читаемости
4. **Reflection** — Reflection Probe, skybox
5. **Post** — Volume

### Проверить текущие настройки

```
mcp__unityMCP__manage_graphics(action="skybox_get")
mcp__unityMCP__manage_graphics(action="pipeline_get_info")
mcp__unityMCP__manage_graphics(action="volume_get_info", target="Global Volume")
```

### Запекание

```
mcp__unityMCP__manage_graphics(action="bake_get_settings")
mcp__unityMCP__manage_graphics(action="bake_start")
mcp__unityMCP__manage_graphics(action="bake_status")
```

Запечённый свет **бесплатный**. Не запекай то, что двигается.

## Volume: набор для хоррора

```
mcp__unityMCP__manage_graphics(action="volume_create", name="Woodberry_Horror", is_global=true)
mcp__unityMCP__manage_graphics(action="volume_add_effect", target="Woodberry_Horror", effect="Vignette")
mcp__unityMCP__manage_graphics(action="volume_set_effect", target="Woodberry_Horror", effect="Vignette",
  parameters={...})
```

| Эффект | Параметры | Типичная ошибка |
|---|---|---|
| `Vignette` | Intensity, Smoothness, Color | Слишком сильная → «слепое» поле зрения |
| `Bloom` | Intensity, Threshold, Scatter | Слишком высокий threshold → только самые яркие засвечены |
| `ColorAdjustments` | Post Exposure, Contrast, Saturation, Filter | Saturation вниз = «грязь», а не хоррор |
| `FilmGrain` | Intensity | Слишком сильный → мешает читаемости |
| `Tonemapping` | Mode | Neutral/ACES — пробовать оба |

## Материалы

### Правило: один материал — много объектов

| Ситуация | Решение |
|---|---|
| Разный цвет | `MaterialPropertyBlock` |
| Разные текстуры | Атлас |
| Одинаковый материал | Переиспользовать |

```
mcp__unityMCP__manage_material(action="set_renderer_color", target="Obj", color=[1,0,0], mode="property_block")
```

Материал на объект = сломанный батчинг + утечка памяти.

### Проверка существующего материала

```
mcp__unityMCP__manage_material(action="get_material_info", material_path="Assets/Woodberry/Materials/X.mat")
```

## Draw calls: бюджет

Цель: **< 800**, жёсткий предел 1500.

```
mcp__unityMCP__manage_graphics(action="stats_get")
mcp__unityMCP__manage_profiler(action="get_frame_timing")
```

### Как снижать

| Приём | Когда |
|---|---|
| GPU Instancing | Повторяющиеся объекты с одним материалом |
| Static Batching | Статичная геометрия |
| Объединение мешей | Много мелких объектов в одном |
| LOD | Крупная геометрия вдали |
| Occlusion culling | Закрытые уровни |
| Убрать прозрачность | Blend-сортировка дорогая |
| Меньше теневых каскадов | Каждый каскад = проход |
| Один материал вместо N | Батчинг |

## Frame time: бюджет

Цель: **16.6 ms** (60 FPS), жёсткий предел 22 ms.

```
mcp__unityMCP__manage_profiler(action="profiler_start")
mcp__unityMCP__manage_profiler(action="get_counters", category="Render")
mcp__unityMCP__manage_profiler(action="profiler_stop")
```

Что смотреть в порядке убывания влияния:
1. **GPU** — шейдеры, overdraw, разрешение
2. **Rendering** — draw calls, culling
3. **Scripts** — аллокации, `Update` стоимость
4. **Physics** — количество коллайдеров, broadphase
5. **GC** — аллокации в рантайме

## Типичные проблемы

| Симптом | Причина | Решение |
|---|---|---|
| Фиолетовый объект | Шейдер не поддерживается URP | Заменить на URP-шейдер |
| Объекты чёрные в билде | Не запечён свет, стриппинг шейдера | Запечь, проверить шейдер на стриппинг |
| Сцена «плоская» | Нет запечённого света, слабый ambient | Ambient ↑, directional свет настроить |
| Всё засвечено | Bloom intensity высокий, threshold низкий | Threshold ↑, intensity ↓ |
| Мерцание теней | Shadow acne / низкое разрешение теней | Bias ↑, resolution ↓ |
| FPS ниже бюджета | См. порядок проверки выше | Профилируй, не гадай |
| Много draw calls | Материал на объект | `MaterialPropertyBlock`, инстансинг |

## Критерии успеха

- [ ] Шейдеры проверены на существование и URP-совместимость
- [ ] Свет запечён где возможно
- [ ] Volume настроен осмысленно, эффекты не перебарщены
- [ ] Один материал на много объектов
- [ ] Draw calls в бюджете
- [ ] Frame time в бюджете
- [ ] Проверено на реальной сцене в Play Mode
- [ ] Фактические цифры указаны в отчёте
- [ ] Указано, что проверено, а что — нет

Подробнее: `docs/specs/performance-budget.md`, `docs/specs/asset-standards.md`
