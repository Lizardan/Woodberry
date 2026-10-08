---
name: vae-replace-and-pipeline-audit
overview: "Замена NC-лицензированного VAE на штатный Z-Image VAE (Apache 2.0), затем полный аудит пайплайна F:\\ai: исправление устаревших размеров в комментариях, битых ссылок, гигиена кода и документации, вердикт по результатам."
todos:
  - id: download-replace-vae
    content: Скачать штатный Z-Image VAE (diffusion_pytorch_model.safetensors, 168 МБ) в F:\ai\models\vae\, переименовать в ae.safetensors, заменив старый FLUX.1-dev VAE
    status: completed
  - id: update-vae-docs
    content: "Обновить VAE-описания в gen.py (комментарий), РЕЦЕПТ.md, README.md и LICENSES.md: FLUX.1-dev NC → Z-Image Apache 2.0, 320→168 МБ"
    status: completed
    dependencies:
      - download-replace-vae
  - id: fix-stale-sizes
    content: "Исправить 9 устаревших размеров в комментариях: seg.py (3), shoot.py (1), scene.py (4), check_frame.py (1) — 512x768→480x720, 896x512→856x480"
    status: completed
  - id: fix-broken-link
    content: Исправить битую ссылку run_comfyui.bat в shoot.py:660 на инструкцию запуска через run.py serve
    status: completed
  - id: audit-verdict
    content: Используйте [subagent:code-explorer] для финальной проверки отсутствия устаревших размеров и битых ссылок, подготовьте итоговый вердикт по аудиту пайплайна
    status: completed
    dependencies:
      - download-replace-vae
      - update-vae-docs
      - fix-stale-sizes
      - fix-broken-link
---

## Обзор продукта

Пайплайн генерации арт-ассетов `F:\ai` для игры Woodberry. Генерирует поясные портреты NPC для диалоговых сцен (стиль визуальных новелл) и фоны локаций через ComfyUI + Z-Image. Две задачи: (1) заменить VAE под некоммерческой лицензией на штатный Z-Image VAE под Apache 2.0, (2) провести аудит пайплайна, навести гигиену в комментариях и документации, дать вердикт.

## Ключевые функции

- Скачать штатный VAE из `Tongyi-MAI/Z-Image-Turbo/vae/` и заменить `ae.safetensors` (FLUX.1-dev, NC) на `diffusion_pytorch_model.safetensors` (Apache 2.0, 168 МБ)
- Обновить все ссылки на VAE в `gen.py`, `РЕЦЕПТ.md`, `README.md`, `LICENSES.md`
- Исправить 12 устаревших упоминаний размеров (512x768 → 480x720, 896x512 → 856x480) в комментариях `seg.py`, `shoot.py`, `scene.py`, `check_frame.py`, `gen.py`
- Исправить битую ссылку на несуществующий `run_comfyui.bat` в `shoot.py:660`
- Подготовить итоговый вердикт по аудиту пайплайна

## Технический стек

- Python 3.12 + PyTorch 2.9.1 + ROCm 7.2.1 (окружение `comfyenv`)
- ComfyUI (движок генерации, API-граф)
- Модели: Z-Image (REDZ-v1.5-Q8_0.gguf, Apache 2.0), Qwen3-4B (Apache 2.0), BiRefNet_lite (MIT), штатный Z-Image VAE (diffusion_pytorch_model.safetensors, Apache 2.0)
- Git для версионирования

## Подход к реализации

Замена VAE — простая подмена файла: штатный VAE Z-Image имеет ту же 16-канальную архитектуру латента, что и FLUX.1-dev VAE (обе модели наследуют архитектуру Lumina2/FLUX). Файл `diffusion_pytorch_model.safetensors` (168 МБ) скачивается из `Tongyi-MAI/Z-Image-Turbo/vae/` и помещается в `F:\ai\models\vae\`. Поскольку ComfyUI загружает VAE по имени через `VAELoader`, имя файла можно переименовать в `ae.safetensors` (минимум правок в коде) либо обновить константу `VAE` в `gen.py`.

**Ключевое решение:** переименовать скачанный файл в `ae.safetensors` и заменить старый. Это требует нулевых правок в `gen.py` (константа остаётся `VAE = "ae.safetensors"`), нужно только обновить комментарий. Альтернатива — обновить константу на новое имя, но тогда нужно править и `РЕЦЕПТ.md`, и `README.md`. Переименование проще.

**Производительность:** штатный VAE на 152 МБ меньше (168 vs 320 МБ), стек VRAM облегчается. Архитектура идентична — качество генерации не меняется.

**Гигиена комментариев:** 12 упоминаний старых размеров в 5 файлах. Разделить на две категории:

- **Актуальные комментарии** (описывают текущий стандарт) — исправить: `seg.py:75,111,133-134`, `shoot.py:528`, `scene.py:582,597,600-601`, `check_frame.py:6`
- **Историческая справка** (описывают период SD 1.5, когда размеры были другими) — оставить как есть, но добавить пометку «(было до перехода на 480×720)»: `gen.py:38,560,682`

## Архитектура

Изменения точечные, архитектура пайплайна не затрагивается:

- `gen.py` — константа `VAE` и комментарий
- `scene.py`, `seg.py`, `shoot.py`, `check_frame.py` — комментарии
- `РЕЦЕПТ.md`, `README.md`, `LICENSES.md` — документация

## Структура директорий

```
F:\ai\
├── .gitignore                          # [существует]
├── README.md                           # [MODIFY] таблица моделей: VAE описание + размер
├── LICENSES.md                         # [MODIFY] запись VAE: NC → Apache 2.0, вердикт
├── models/
│   └── vae/
│       ├── ae.safetensors              # [REPLACE] старый FLUX.1-dev VAE → штатный Z-Image VAE
│       └── diffusion_pytorch_model.safetensors  # [NEW → RENAME] скачанный файл
└── portraits/
    ├── gen.py                          # [MODIFY] комментарий к VAE (строка 127-129)
    ├── scene.py                        # [MODIFY] 6 упоминаний старых размеров в комментариях
    ├── seg.py                           # [MODIFY] 3 упоминания старых размеров в комментариях
    ├── shoot.py                        # [MODIFY] битая ссылка run_comfyui.bat (строка 660) + размер (строка 528)
    ├── check_frame.py                   # [MODIFY] устаревший размер 896x512 (строка 6)
    └── deliver/
        └── РЕЦЕПТ.md                   # [MODIFY] таблица моделей: VAE описание + размер
```

## Замечания по реализации

- **Blast radius:** Замена VAE — единственное изменение, влияющее на рантайм. После замены нужен тестовый прогон (хотя бы один кадр через `run.py quick`) для верификации. Если VAE несовместим, изображение будет мусором — это сразу видно.
- **Кэш ComfyUI:** `--cache-none` уже стоит, перегенерация идёт с нуля.
- **Размер в комментариях:** `scene.py:626` упоминает «12% меньше прежних 512x768» — это сравнение с историческим SD 1.5 размером, корректно, не трогать.
- **gen.py:38,560,682:** упоминания 896x512 в контексте SD 1.5 — историческая справка. Строка 560 — про лимит рамки, расчёт `512*0.08` касается старого размера. Добавить пометку.
- **Битая ссылка:** `shoot.py:660` указывает на `run_comfyui.bat`, которого нет. Заменить на инструкцию `python run.py serve`.

## Agent Extensions

### SubAgent

- **code-explorer**
- Purpose: Быстрый поиск всех оставшихся упоминаний старых размеров и битых ссылок по всему пайплайну `F:\ai\portraits`
- Expected outcome: Полный список строк с устаревшими значениями для верификации полноты правок