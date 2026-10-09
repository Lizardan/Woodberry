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

                case ArtEvent.ItemAdded:
                    Jobs.Enqueue(ev.id, ev.pool, KindOf(ev.pool, ev.id), ev.label);
                    break;

                case ArtEvent.ItemProgress:
                    // Драйвер генерирует последовательно. Первое событие
                    // прогресса означает, что задание встало в работу.
                    if (Jobs.Running == null)
                    {
                        Jobs.StartNext();
                    }
                    Jobs.SetProgress(ev.id, ev.percent);
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
        private sealed class CatalogDto
        {
            public PoolDto[] pools;
        }
    }
}
