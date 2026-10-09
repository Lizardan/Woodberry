using System.Collections.Generic;
using System.Linq;

namespace Woodberry.Editor.ArtStudio
{
    /// <summary>
    /// Очередь генерации: строго одна задача за раз.
    ///
    /// Почему одна. ComfyUI выполняет задачи последовательно, и второй
    /// процесс генерации не ускоряет работу, а складывается с первым —
    /// проверено замерами стенда (кадр 54.3 с вместо 28.1 при параллельном
    /// прогоне). Значит очередь линейная, а не параллельная, и «общий
    /// процент» считается по задачам, а не по ядрам.
    ///
    /// Почему проценты живут здесь, а не в окне. Интерфейс обязан показать
    /// две величины: прогресс текущего кадра и прогресс всей очереди. Обе —
    /// функция состояния очереди, и обе должны считаться одинаково в окне
    /// и в тесте.
    /// </summary>
    public sealed class JobQueue
    {
        private readonly List<GenerationJob> _jobs = new List<GenerationJob>();

        public IReadOnlyList<GenerationJob> Jobs => _jobs;

        public int Total => _jobs.Count;

        public int Finished => _jobs.Count(IsFinished);

        public int Failed => _jobs.Count(j => j.State == JobState.Failed);

        public GenerationJob Running => _jobs.FirstOrDefault(j => j.State == JobState.Running);

        public bool IsIdle => _jobs.All(IsFinished);

        private static bool IsFinished(GenerationJob job)
        {
            return job.IsFinished;
        }

        public GenerationJob Enqueue(
            string standId,
            string pool,
            ArtAssetKind kind,
            string label)
        {
            var job = new GenerationJob
            {
                StandId = standId,
                Pool = pool,
                Kind = kind,
                Label = label
            };

            _jobs.Add(job);
            return job;
        }

        /// <summary>Берёт следующее задание из очереди в работу. null — очередь пуста.</summary>
        public GenerationJob StartNext()
        {
            GenerationJob job = _jobs.FirstOrDefault(j => j.State == JobState.Queued);
            if (job != null)
            {
                job.State = JobState.Running;
            }

            return job;
        }

        public bool SetProgress(string standId, int percent)
        {
            GenerationJob job = Find(standId);
            if (job == null || job.IsFinished)
            {
                return false;
            }

            job.Percent = Clamp(percent);
            return true;
        }

        public GenerationJob Finish(string standId, bool success, string message)
        {
            GenerationJob job = Find(standId);
            if (job == null)
            {
                return null;
            }

            job.State = success ? JobState.Done : JobState.Failed;
            job.Percent = success ? 100 : job.Percent;
            job.Message = message;
            return job;
        }

        public bool Cancel(string standId)
        {
            GenerationJob job = Find(standId);
            if (job == null || job.IsFinished)
            {
                return false;
            }

            job.State = JobState.Cancelled;
            job.Message = "отменено";
            return true;
        }

        /// <summary>Убирает завершённые задания, оставив в работе как есть.</summary>
        public int ClearFinished()
        {
            int removed = _jobs.RemoveAll(IsFinished);
            return removed;
        }

        /// <summary>
        /// Прогресс всей очереди 0..100. Завершённое считается за 100,
        /// невыполненное за 0 — так общий процент не врёт, пока задания
        /// ещё не начались.
        /// </summary>
        public double OverallPercent
        {
            get
            {
                if (_jobs.Count == 0)
                {
                    return 0;
                }

                double sum = _jobs.Sum(j =>
                    j.State == JobState.Done ? 100d
                    : j.IsFinished ? (double)j.Percent
                    : (double)j.Percent);

                return sum / _jobs.Count;
            }
        }

        /// <summary>
        /// Оценка остатка в секундах: среднее время завершённого задания,
        /// умноженное на число невыполненных. До первого завершённого
        /// задания оценка невозможна и равна 0 — это честнее, чем показывать
        /// выдуманное время.
        /// </summary>
        public double EtaSeconds
        {
            get
            {
                double average = _jobs
                    .Where(j => j.State == JobState.Done)
                    .Select(j => j.Elapsed)
                    .DefaultIfEmpty()
                    .Average();

                if (average <= 0)
                {
                    return 0;
                }

                int remaining = _jobs.Count(j => !j.IsFinished);
                return average * remaining;
            }
        }

        /// <summary>Тикает таймеру задания в работе. Секунды, не кадры.</summary>
        public void Tick(double deltaSeconds)
        {
            GenerationJob job = Running;
            if (job != null && deltaSeconds > 0)
            {
                job.Elapsed += deltaSeconds;
            }
        }

        private GenerationJob Find(string standId)
        {
            if (string.IsNullOrEmpty(standId))
            {
                return null;
            }

            return _jobs.FirstOrDefault(j => j.StandId == standId);
        }

        private static int Clamp(int percent)
        {
            if (percent < 0)
            {
                return 0;
            }

            return percent > 100 ? 100 : percent;
        }
    }
}
