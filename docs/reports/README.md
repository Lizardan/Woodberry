# Reports

Здесь лежат **stage reports** — что реально произошло.

## Разница между stage file и report

| | Отвечает на вопрос |
|---|---|
| `docs/stages/stage-NN-*.md` | «Что собираемся делать» |
| `docs/reports/YYYY-MM-DD-stage-NN-*-report.md` | «Что реально сделали и как это проверили» |

Report — не формальность. Он делает прогресс видимым и отдаёт следующему агенту
контекст без потери знаний.

## Обязательные разделы report

```
## Stage              # какой этап
## Objective          # что этап должен был дать
## Implemented        # что реально сделано, по слоям
## Contract changes   # что изменилось в контрактах
## Files affected     # конкретные пути
## Verification performed
  ### Automated       # команды + результат
  ### Manual          # что проверил руками
  ### Not verified    # что НЕ удалось проверить и почему
## Acceptance criteria status   # Done / Partial / Not done
## Known issues
## Tech debt
## Assumptions
## Risks
## Manual QA checklist
## Recommended next stage
```

## Главное правило

**`Verification performed` и `Not verified` — это разные разделы, и второй нельзя
заполнять словами «всё ок».**

Формулировка «предположительно работает» — это не верификация. Если Unity Editor не был
запущен, пиши: `Not verified: Unity Editor не запущен, тесты не выполнялись`.

## Именование

```
YYYY-MM-DD-stage-NN-short-name-report.md
```

Пример: `2026-09-30-stage-00-foundation-report.md`
