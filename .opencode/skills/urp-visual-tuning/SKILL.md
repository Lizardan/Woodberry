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
unity_reflect(action: "get_type",   class_name: "UnityEngine.Rendering.VolumeManager")
unity_reflect(action: "get_type",   class_name: "UnityEngine.Rendering.Volume")
unity_reflect(action: "get_member", class_name: "UnityEngine.Rendering.Volume", member_name: "profile")
unity_docs(action: "get_doc", class_name: "Volume")
manage_graphics(action: "skybox_get")
manage_asset(action: "search", path: "Assets", filter_type: "Shader", page_size: 25)
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

manage_graphics(action: "skybox_set_ambient", ambient_mode: "Flat", color: [0.02, 0.02, 0.03, 1])
manage_graphics(action: "skybox_set_fog", fog_enabled: true, fog_mode: "ExponentialSquared", fog_density: 0.03)

### Запекание

manage_graphics(action: "bake_start", async_bake: true)
manage_graphics(action: "bake_status")

Запечённый свет **бесплатный**. Не запекай то, что двигается.

## Volume: набор для хоррора

manage_graphics(action: "volume_create", name: "Global", profile_path: "Assets/Woodberry/Settings/GlobalVolumeProfile.asset", is_global: true, weight: 1.0)
manage_graphics(action: "volume_add_effect", target: "Global", effect: "Vignette")
manage_graphics(action: "volume_set_effect", target: "Global", effect: "Vignette", parameters: { "intensity": 0.35, "smoothness": 0.4 })

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
manage_material(action: "assign_material_to_renderer", target: "<object>", material_path: "Assets/Woodberry/Materials/X.mat")
```
Материал на объект = сломанный батчинг + утечка памяти.

### Проверка существующего материала

```
manage_material(action: "get_material_info", material_path: "Assets/Woodberry/Materials/X.mat")
```
```
manage_graphics(action: "stats_get")
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
manage_profiler(action: "get_frame_timing")
manage_profiler(action: "get_counters", category: "Render")
```
manage_camera(action: "screenshot", capture_source: "game_view")
manage_editor(action: "stop")
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
