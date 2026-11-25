using System;
using System.Collections.Generic;
using UnityEngine;

namespace Woodberry.Editor.ArtStudio
{
    /// <summary>
    /// Состояние арт-студии по событиям драйвера: кто подключён, что с
    /// ComfyUI, что в очереди, какие кандидаты ждут решения.
    ///
    /// Почему отдельно от <see cref="ArtDriverClient"/>. Клиент читает файлы
    /// и процессы — это нельзя проверить без живого драйвера. Здесь же только
    /// переходы состояния по событию, и они обязаны проверяться EditMode-тестом:
    /// «сгенерировали кадр → появился кандидат» — это логика, а не I/O.
    /// </summary>
    public sealed class ArtDriverState
    {
        private readonly Dictionary<string, DriverPool> _pools =
            new Dictionary<string, DriverPool>(StringComparer.Ordinal);

        public bool DriverConnected { get; private set; }

        public int DriverProtocol { get; private set; }

        public string ServerState { get; private set; } = "не поднят";

        public string LastError { get; private set; }

        public string LastLog { get; private set; }

        /// <summary>
        /// Сводка стенда из <c>driver_info</c>: модель, энкодер, VAE, сэмплер.
        /// Пусто, пока ни один прогон не отдал свою шапку.
        /// </summary>
        public string StandInfo { get; private set; }

        /// <summary>
        /// Сводка стенда по строкам: «Модель: …», «LoRA: …», «Энкодер: …».
        ///
        /// Склеенная в одну строку через «·» версия нечитаема: пять длинных
        /// описаний в одну строку — это простыня, в которой не найти нужное.
        /// Новый драйвер присылает готовый JSON-массив; старый присылает только
        /// склейку, и тогда она режется по «·» — приёмлемо, потому что сами
        /// значения не содержат этого символа.
        /// </summary>
        public string[] StandInfoLines { get; private set; } = Array.Empty<string>();

        /// <summary>
        /// Промпты стенда последним ответом на команду <c>prompts</c>. Строка
        /// JSON, а не разобранный объект: разбор принадлежит вкладке, и в
        /// состоянии драйвера ему не место. Пусто — стенд ещё не спрашивали
        /// или он не ответил.
        /// </summary>
        public string PromptsJson { get; private set; }

        /// <summary>
        /// Этап работы прогона, который кадру не принадлежит: вырезка фона,
        /// проверка, приёмка. Именно эти этапы раньше были «молчанием» на
        /// десятки секунд, и без них прогресс выглядел зависшим.
        /// </summary>
        public string StandStage { get; private set; }

        /// <summary>Сколько единиц текущего этапа прогона сделано.</summary>
        public int StandStageDone { get; private set; }

        /// <summary>Сколько единиц в текущем этапе прогона всего.</summary>
        public int StandStageTotal { get; private set; }

        /// <summary>Пояснение к этапу: имя файла или кадра.</summary>
        public string StandStageText { get; private set; }

        /// <summary>
        /// Новая заявка — значит этапа прогона пока нет. Без сброса окно
        /// показало бы «Вырезка фона: 118 из 118» секунду после старта новой
        /// генерации: события от прошлого прогона живут в состоянии, а не
        /// приходят заново.
        /// </summary>
        public void ResetStandStage()
        {
            StandStage = null;
            StandStageDone = 0;
            StandStageTotal = 0;
            StandStageText = null;
        }

        /// <summary>
        /// Пути, перенесённые драйвером в проект с прошлого опроса. Окно по ним
        /// обновляет ассеты И отмечает кадры принятыми: раз человек кадр
        /// принял, отдельная кнопка «одобрить» ему уже не нужна.
        /// </summary>
        private readonly List<string> _approvedPaths = new List<string>();

        public bool ApprovedSincePoll => _approvedPaths.Count > 0;

        public bool ProtocolMismatch =>
            DriverConnected && DriverProtocol != ArtProtocol.Version;

        /// <summary>
        /// Драйвер перестал существовать: процесс умер между опросами.
        ///
        /// Почему это отдельный переход. <see cref="DriverConnected"/> ставится
        /// событием <c>ready</c> и сам не гаснет, потому что о смерти процесса
        /// события не приходит. Без явного сброса окно рапортует «подключён»,
        /// строка проблем молчит, а кнопка «Сгенерировать» остаётся активной —
        /// команда ложится в файл, который никто не читает. Отказ выглядит как
        /// исправно работающий инструмент, поэтому состояние сбрасывается явно.
        /// </summary>
        public void MarkDriverDisconnected()
        {
            DriverConnected = false;
            DriverProtocol = 0;
        }

        public JobQueue Jobs { get; } = new JobQueue();

        /// <summary>
        /// Драйвер сообщил, что папка ревью изменилась: кадр сгенерирован или
        /// перенесён в проект. Сам список кандидатов окно читает с диска
        /// (<see cref="ReviewScanner"/>), поэтому событие — только повод
        /// пересканировать, а не источник данных.
        /// </summary>
        public bool ReviewChanged { get; private set; }

        public IReadOnlyDictionary<string, DriverPool> Pools => _pools;

        public int FreeFrames
        {
            get
            {
                int total = 0;
                foreach (DriverPool pool in _pools.Values)
                {
                    total += pool.Free.Count;
                }

                return total;
            }
        }

        public void Apply(ArtEvent ev)
        {
            if (ev == null || string.IsNullOrEmpty(ev.kind))
            {
                return;
            }

            switch (ev.kind)
            {
                case ArtEvent.Ready:
                    DriverConnected = true;
                    if (ev.protocol > 0)
                    {
                        DriverProtocol = ev.protocol;
                    }
                    break;

                case ArtEvent.ServerState:
                    if (!string.IsNullOrEmpty(ev.text))
                    {
                        ServerState = ev.text;
                    }
                    break;

                case ArtEvent.CatalogEvent:
                    ApplyCatalog(ev.detail);
                    break;

                case ArtEvent.Prompts:
                    PromptsJson = ev.detail;
                    break;

                case ArtEvent.ItemAdded:
                    Jobs.Enqueue(ev.id, ev.pool, KindOf(ev.pool, ev.id), ev.label);
                    break;

                case ArtEvent.ItemProgress:
                    // Задание встаёт в работу САМО, по своему stand-id. Раньше
                    // здесь стояло `if (Jobs.Running == null) StartNext()`:
                    // текущим объявлялся первый незанятый кадр, а не тот, за
                    // которым стенд реально рисует, — и полоса текущего кадра
                    // прибивалась к первому кадру на весь прогон.
                    Jobs.SetProgress(ev.id, ev.percent, ev.text, ev.stage);
                    break;

                case ArtEvent.ItemDrawn:
                    Jobs.MarkDrawn(ev.id);
                    break;

                case ArtEvent.ItemStage:
                    StandStage = ev.stage;
                    StandStageDone = ev.done;
                    StandStageTotal = ev.total;
                    StandStageText = ev.text;
                    break;

                case ArtEvent.DriverInfo:
                    // Сводка стенда: модель, кодировщик, сэмплер. Показывается
                    // в панели состояния, чтобы человек видел, ЧЕМ рисуется
                    // кадр, не открывая лог стенда.
                    if (!string.IsNullOrEmpty(ev.text))
                    {
                        StandInfo = ev.text;
                    }

                    StandInfoLines = ParseInfoLines(ev.detail, ev.text);
                    break;

                case ArtEvent.ItemDone:
                    Jobs.Finish(ev.id, true, ev.path);
                    break;

                case ArtEvent.ItemFailed:
                    Jobs.Finish(ev.id, false, ev.text);
                    LastError = string.IsNullOrEmpty(ev.id)
                        ? ev.text
                        : $"{ev.id}: {ev.text}";
                    break;

                case ArtEvent.ReviewCandidate:
                    ReviewChanged = true;
                    break;

                case ArtEvent.Approved:
                    if (!string.IsNullOrEmpty(ev.path) && !_approvedPaths.Contains(ev.path))
                    {
                        _approvedPaths.Add(ev.path);
                    }

                    ReviewChanged = true;
                    break;

                case ArtEvent.CommandRejected:
                case ArtEvent.Error:
                    LastError = ev.text;
                    break;

                case ArtEvent.Log:
                    LastLog = ev.text;
                    break;
            }
        }

        /// <summary>
        /// Пути, перенесённые в проект. Читается один раз: отметка «принят»
        /// ставится ровно на переходе «кандидат → кадр проекта», а не на
        /// каждом опросе.
        /// </summary>
        public List<string> TakeApprovedPaths()
        {
            if (_approvedPaths.Count == 0)
            {
                return new List<string>();
            }

            var copy = new List<string>(_approvedPaths);
            _approvedPaths.Clear();
            return copy;
        }

        /// <summary>
        /// Флаг «папка ревью изменилась» читается один раз, как и
        /// <see cref="TakeApprovedPaths"/>.
        /// </summary>
        public bool AcknowledgeReviewChanged()
        {
            bool was = ReviewChanged;
            ReviewChanged = false;
            return was;
        }

        public DriverPool FindPool(string prefix)
        {
            return !string.IsNullOrEmpty(prefix) && _pools.TryGetValue(prefix, out DriverPool pool)
                ? pool
                : null;
        }

        private void ApplyCatalog(string detail)
        {
            if (string.IsNullOrWhiteSpace(detail))
            {
                return;
            }

            try
            {
                // JsonUtility не умеет разбирать массив в корне, поэтому
                // оборачиваем его в объект — иначе каталог теряется молча.
                CatalogDto parsed = JsonUtility.FromJson<CatalogDto>(
                    "{\"pools\":" + detail + "}");
                if (parsed?.pools == null)
                {
                    return;
                }

                _pools.Clear();
                foreach (PoolDto dto in parsed.pools)
                {
                    if (dto == null || string.IsNullOrEmpty(dto.pool))
                    {
                        continue;
                    }

                    _pools[dto.pool] = new DriverPool
                    {
                        Prefix = dto.pool,
                        Kind = KindOf(dto.pool, null),
                        All = dto.all ?? Array.Empty<string>(),
                        Free = dto.free ?? Array.Empty<string>(),
                    };
                }
            }
            catch (Exception e)
            {
                LastError = $"каталог драйвера не разобран: {e.Message}";
            }
        }

        /// <summary>
        /// Строки сводки стенда. Новый драйвер присылает JSON-массив, старый —
        /// одну склейку через «·». Оба приводятся к списку строк, потому что
        /// окно показывает их по одной.
        /// </summary>
        private static string[] ParseInfoLines(string detail, string text)
        {
            if (!string.IsNullOrWhiteSpace(detail))
            {
                try
                {
                    var wrapper = JsonUtility.FromJson<DriverInfoDto>(
                        "{\"lines\":" + detail + "}");
                    if (wrapper?.lines != null)
                    {
                        return wrapper.lines;
                    }
                }
                catch (Exception)
                {
                    // Ниже есть запасной путь — резать склейку.
                }
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                return Array.Empty<string>();
            }

            // Обрезка обязательна: стенд склеивает через «·» с пробелами, и без
            // неё каждая строка панели начиналась бы с отступа, а сравнение
            // «Энкодер: …» с эталоном не проходило.
            string[] parts = text.Split(new[] { "·" },
                StringSplitOptions.RemoveEmptyEntries);
            var lines = new List<string>();
            foreach (string part in parts)
            {
                string trimmed = part.Trim();
                if (trimmed.Length > 0)
                {
                    lines.Add(trimmed);
                }
            }

            return lines.ToArray();
        }

        private static ArtAssetKind KindOf(string pool, string id)
        {
            string probe = !string.IsNullOrEmpty(pool) ? pool : id;
            return probe != null && probe.StartsWith("p_", StringComparison.Ordinal)
                ? ArtAssetKind.Portrait
                : ArtAssetKind.Background;
        }

        [Serializable]
        private sealed class PoolDto
        {
            public string pool;
            public string kind;
            public string[] all;
            public string[] free;
        }

        [Serializable]
        private sealed class DriverInfoDto
        {
            public string[] lines;
        }

        [Serializable]
        private sealed class CatalogDto
        {
            public PoolDto[] pools;
        }
    }
}
