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

        /// <summary>
        /// Ставит кадр в очередь. Повторный заказ ТОГО ЖЕ stand-id не создаёт
        /// второе задание, а переиспользует существующее.
        ///
        /// ПОЧЕМУ ЭТО ВАЖНО. Драйвер шлёт `item_added` и на повторную генерацию
        /// того же кадра — перегенерация брака, повторный запуск пула, отказ от
        /// кандидата. А `Find` ищет по stand-id ПЕРВОЕ совпадение, поэтому
        /// второй экземпляр получал `item_added` и больше ничего: ни прогресса
        /// (`SetProgress` уходил в завершённый первый), ни `item_done`. Снаружи
        /// это «генерация идёт вечно» с застывшей полосой — проверено живьём
        /// 2026-10-10, когда один и тот же кадр заказали дважды.
        /// </summary>
        public GenerationJob Enqueue(
            string standId,
            string pool,
            ArtAssetKind kind,
            string label)
        {
            GenerationJob existing = Find(standId);
            if (existing != null)
            {
                existing.Pool = pool;
                existing.Kind = kind;
                existing.Label = label;
                existing.State = JobState.Queued;
                existing.Percent = 0;
                existing.Elapsed = 0;
                existing.ProgressText = null;
                existing.Message = null;
                existing.Drawn = false;
                existing.Stage = null;
                return existing;
            }

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

        /// <summary>
        /// Ставит ИМЕННО ЭТО задание в работу. Снимает <c>Running</c> с
        /// остальных: стенд рисует кадры строго по очереди, значит и текущим
        /// является тот, за которым ПРИШЁЛ прогресс, а не тот, что стоит
        /// первым в списке.
        ///
        /// ПОЧЕМУ НЕ ЧЕРЕЗ <see cref="StartNext"/>. Старая схема брала первый
        /// незанятый кадр и надеялась, что порядок событий совпадёт с
        /// порядком заявки. Он не совпадает: `item_done` приходит в конце
        /// всего прогона (проверка кадров идёт после отрисовки всех), поэтому
        /// первый кадр навсегда оставался <c>Running</c>, а полоса текущего
        /// кадра намертво прибивалась к нему — на четырёх кадрах она
        /// заполнялась один раз и дальше стояла на 100 %, тогда как общая
        /// полоса честно ползла вперёд по процентам следующего кадра.
        /// </summary>
        public GenerationJob Activate(GenerationJob job)
        {
            if (job == null || job.IsFinished)
            {
                return job;
            }

            for (int i = 0; i < _jobs.Count; i++)
            {
                if (!ReferenceEquals(_jobs[i], job) && _jobs[i].State == JobState.Running)
                {
                    _jobs[i].State = JobState.Queued;
                }
            }

            if (job.State == JobState.Running)
            {
                return job;
            }

            job.State = JobState.Running;
            // Честный старт с нуля. Прежний остаток процентов и «сбитое»
            // состояние отрисовки нового кадра читались бы как «кадр почти
            // готов», хотя стенд к нему только приступил.
            job.Percent = 0;
            job.Elapsed = 0;
            job.Drawn = false;
            job.Stage = null;
            job.ProgressText = null;
            return job;
        }

        /// <summary>
        /// Кадр отрисован моделью и ждёт проверки. Это не конец работы над
        /// кадром (следом идут вырезка и приёмка), но рисовать его уже не
        /// будут, и общей полосе пора это показать.
        /// </summary>
        public bool MarkDrawn(string standId)
        {
            GenerationJob job = Find(standId);
            if (job == null || job.IsFinished)
            {
                return false;
            }

            job.Drawn = true;
            job.Percent = 100;
            return true;
        }

        /// <summary>
        /// Процент кадра и подпись его фазы. Текст необязателен: подпись
        /// приходит только от websocket-прогресса («шаг 4/10»), а границы
        /// кадра драйвер шлёт без неё.
        ///
        /// Задание, получившее прогресс, становится текущим само — по своему
        /// stand-id, а не по порядку в очереди. См. <see cref="Activate"/>.
        /// </summary>
        public bool SetProgress(string standId, int percent,
            string progressText = null, string stage = null)
        {
            GenerationJob job = Find(standId);
            if (job == null || job.IsFinished)
            {
                return false;
            }

            Activate(job);
            job.Percent = Clamp(percent);
            if (!string.IsNullOrEmpty(progressText))
            {
                job.ProgressText = progressText;
            }

            if (!string.IsNullOrEmpty(stage))
            {
                job.Stage = stage;
            }

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
            // У завершённого кадра фазы больше нет: иначе «шаг 9/10» висел бы
            // под галочкой готового кадра.
            job.ProgressText = null;
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
        /// Оценка остатка в секундах: среднее время завершённого задания,
        /// умноженное на число невыполненных. До первого завершённого
        /// задания оценка невозможна и равна 0 — это честнее, чем показывать
        /// выдуманное время.
        ///
        /// ПОЧЕМУ ЗДЕСЬ НЕТ «ОБЩЕГО ПРОЦЕНТА». Он был, но считал сырые
        /// проценты заданий, а те не отражают подготовку кадра: полоса дёргалась
        /// рывками. Теперь его считает <see cref="GenerationProgress.Overall"/>
        /// по кадровой шкале, и держать вторую, «почти такую же» формулу значит
        /// однажды получить две разные цифры об одном прогоне.
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
