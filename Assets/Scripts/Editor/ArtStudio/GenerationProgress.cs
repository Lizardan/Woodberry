using System;
using UnityEngine;

namespace Woodberry.Editor.ArtStudio
{
    /// <summary>
    /// Что показать про прогон: прогресс всей заявки, прогресс текущего
    /// кадра, прогресс текущего этапа, человекочитаемые подписи.
    ///
    /// ПОЧЕМУ ТРИ ШКАЛЫ. Раньше их было две, и обе врали по-своему. Кадровая
    /// брала «первое незакрытое задание», а `item_done` приходит только в
    /// конце всего прогона — значит на кадрах с 2-го и далее текущим оставался
    /// первый кадр, и его полоса заполнялась один раз и висела на 100 %.
    /// Общая полоса при этом ползла вперёд по процентам следующего кадра, и
    /// выглядело это как «верхняя живая, нижняя застыла». Теперь текущим
    /// кадром становится тот, за которым стенд реально рисует, и его полоса
    /// честно сбрасывается в ноль — на четырёх кадрах это четыре прохода
    /// от 0 до 100.
    ///
    /// Третья шкала — этап. Этапы бывают двух родов: те, что считаются сами
    /// (файлы вырезки 37/118, кадры приёмки 2/4), и те, где счётчика нет
    /// (загрузка моделей, энкодер). Первые дают точный процент, вторые —
    /// честный проход по времени С ПОТОЛКОМ: полоса ползёт, но не доходит до
    /// 100 %, пока нет настоящих событий. Без потолка она уезжала в 100 %
    /// через четыре секунды и потом минутами стояла там, а это и есть
    /// «прогресс завис».
    ///
    /// ПОЧЕМУ ОТДЕЛЬНОЙ ФУНКЦИЕЙ, А НЕ В ОКНЕ. Это арифметика и форматирование,
    /// и ошибиться в них легче, чем кажется. Проверяется EditMode-тестом
    /// (<c>GenerationProgressTests</c>); окно только подставляет готовые числа
    /// в полосы и не считает ничего само.
    /// </summary>
    public static class GenerationProgress
    {
        /// <summary>
        /// Сколько секунд молчания прогресса считать остановкой. Число взято
        /// по замерам стенда: шаг сэмплера идёт ~0.75–1.3 с, а загрузка
        /// моделей — до 410 с без единого события. Порог попадает между ними.
        /// </summary>
        public const double StaleSeconds = 4.0;

        /// <summary>До скольких процентов кадра доходит подготовка.</summary>
        public const int PrepCeiling = 15;

        /// <summary>
        /// Сколько «весит» кадр, который отрисован, но ещё не принят.
        ///
        /// Не 100: принятия ещё не было, и общая полоса обязана показывать
        /// разницу между «нарисовано» и «в проекте» — иначе прогон «рисует 5
        /// кадров» выглядел бы полностью завершённым заранее.
        /// </summary>
        public const int DrawnWeight = 90;

        /// <summary>
        /// До какого процента дотягивается этап, у которого нет счётчика.
        /// Дальше — только по настоящим событиям: иначе полоса показывает
        /// «готово» там, где стенд ещё ничего не сделал.
        /// </summary>
        public const int SmoothCeiling = 90;

        /// <summary>За сколько секунд подготовка «доходит» до потолка.</summary>
        public const double PrepSeconds = 20.0;

        /// <summary>
        /// За сколько секунд этап без счётчика доходит до <see cref="SmoothCeiling"/>.
        /// </summary>
        public const double SmoothSeconds = 25.0;

        // ---------------------------------------------------------- этапы

        public const string StageWarmup = "warmup";
        public const string StagePrepare = "prepare";
        public const string StageSample = "sample";
        public const string StageCut = "cut";
        public const string StageCheck = "check";
        public const string StageCopy = "copy";

        /// <summary>
        /// Человеческое название этапа. Пустая строка у старого драйвера — не
        /// поломка, а его отсутствие: тогда полосы считаются по проценту.
        /// </summary>
        public static string StageTitle(string stage)
        {
            switch (stage)
            {
                case StageWarmup:
                    return "Прогрев моделей";
                case StagePrepare:
                    return "Подготовка кадра";
                case StageSample:
                    return "Отрисовка";
                case StageCut:
                    return "Вырезка фона";
                case StageCheck:
                    return "Проверка кадров";
                case StageCopy:
                    return "Приёмка и копирование";
                default:
                    return "Работа стенда";
            }
        }

        /// <summary>
        /// Этапы, у которых есть точный счётчик «сделано из всего». У них
        /// процент берётся из события, а не из времени.
        /// </summary>
        public static bool StageHasCounter(string stage)
        {
            return stage == StageCut || stage == StageCheck || stage == StageCopy;
        }

        // ---------------------------------------------------------- заявка

        /// <summary>
        /// Прогресс всей заявки 0..100, целым числом.
        ///
        /// Вклад каждого задания: принятый — 100, отрисованный — <see cref="DrawnWeight"/>,
        /// рисуемый — по кадровой шкале, ждущий — ноль. Считается от факта,
        /// а не от порядка событий.
        /// </summary>
        public static int Overall(JobQueue jobs, GenerationJob current,
            double secondsSinceChange)
        {
            if (jobs == null || jobs.Total == 0)
            {
                return 0;
            }

            double sum = 0;
            for (int i = 0; i < jobs.Jobs.Count; i++)
            {
                sum += Share(jobs.Jobs[i], current, secondsSinceChange);
            }

            return Mathf.Clamp((int)Math.Round(sum / jobs.Total), 0, 100);
        }

        /// <summary>Вклад одного задания в общую полосу заявки.</summary>
        public static int Share(GenerationJob job, GenerationJob current,
            double secondsSinceChange)
        {
            if (job == null)
            {
                return 0;
            }

            if (job.State == JobState.Done)
            {
                return 100;
            }

            if (job.IsFinished)
            {
                // Провал: показываем, где остановился. Считать провал за
                // выполненную работу нельзя, а за ноль — общая полоса никогда
                // не дойдёт до 100 и человек не поймёт, что пора смотреть.
                return Mathf.Clamp(job.Percent, 0, 100);
            }

            if (ReferenceEquals(job, current))
            {
                return FramePercent(job, secondsSinceChange);
            }

            return job.Drawn ? DrawnWeight : 0;

            // Задание в работе, но окно передало не его (рассинхрон между
            // опросом и событием). Показывать ноль значило бы «прогресс
            // пропал», поэтому кадр всё равно меряем по его же шкале.
            return job.State == JobState.Running
                ? FramePercent(job, secondsSinceChange)
                : 0;
        }

        // ---------------------------------------------------------- кадр

        /// <summary>
        /// Прогресс одного кадра 0..100. Внутри монотонен по времени и не
        /// убывает на переходах, поэтому полоса не дёргается назад.
        ///
        /// Считается по этапу, если драйвер его назвал, и по проценту шагов,
        /// если не назвал (старый драйвер, протокол 1 без этапов).
        /// </summary>
        public static int FramePercent(GenerationJob job, double secondsSinceChange)
        {
            if (job == null)
            {
                return 0;
            }

            if (job.State == JobState.Done)
            {
                return 100;
            }

            if (job.Drawn)
            {
                // Кадр отрисован. Проверка и приёмка идут за пределами этой
                // шкалы — их показывает полоса этапов.
                return 100;
            }

            if (job.IsFinished)
            {
                return Mathf.Clamp(job.Percent, 0, 100);
            }

            if (job.Stage == StageSample)
            {
                return Map(job.Percent, 1, 99, PrepCeiling, 100);
            }

            if (job.Stage == StagePrepare || job.Percent <= 1)
            {
                return ByTime(job.Elapsed, PrepSeconds, 0, PrepCeiling);
            }

            // Сэмплинг — и новый драйвер, и старый (у которого нет этапов):
            // шкала одна, различается только тем, кто её двигает.
            return Map(job.Percent, 1, 99, PrepCeiling, 100);
        }

        // ---------------------------------------------------------- этап

        /// <summary>
        /// Прогресс ТЕКУЩЕГО этапа 0..100 — третья полоса.
        ///
        /// У этапов со счётчиком процент точный: <paramref name="done"/> из
        /// <paramref name="total"/>. У остальных полоса ползёт по времени, но
        /// упирается в <see cref="SmoothCeiling"/>: пока стенд не прислал
        /// настоящих событий, «100 %» показывать рано, и уж тем более не
        /// держать её там минутами.
        ///
        /// <paramref name="secondsSinceStageChange"/> — секунд с последнего
        /// события ЭТОГО этапа (не всего прогресса): смена этапа обнуляет
        /// счётчик молчания, иначе новый этап стартовал бы с «подвисла».
        /// </summary>
        public static int StagePercent(string stage, int done, int total,
            double secondsSinceStageChange)
        {
            if (string.IsNullOrEmpty(stage))
            {
                return 0;
            }

            if (total > 0)
            {
                return Mathf.Clamp((int)Math.Round(done * 100.0 / total), 0, 100);
            }

            return ByTime(secondsSinceStageChange, SmoothSeconds, 0, SmoothCeiling);
        }

        // ---------------------------------------------------------- тексты

        /// <summary>
        /// Подпись задания. Пустая строка перед процентом читалась бы как
        /// сломанный интерфейс, поэтому пустой label заменяется stand-id.
        /// </summary>
        public static string JobLabel(GenerationJob job)
        {
            if (job == null)
            {
                return "—";
            }

            if (!string.IsNullOrEmpty(job.Label))
            {
                return job.Label;
            }

            return string.IsNullOrEmpty(job.StandId) ? "—" : job.StandId;
        }

        /// <summary>
        /// Что происходит с кадром — человеческим языком, без «событий нет».
        /// Техническая подпись нужна разработчику в логе, а не человеку,
        /// который ждёт картинку.
        /// </summary>
        public static string PhaseText(GenerationJob job, double secondsSinceChange)
        {
            if (job == null)
            {
                return "Ждём следующий кадр";
            }

            if (job.State == JobState.Done)
            {
                return "Кадр принят";
            }

            if (job.IsFinished)
            {
                return job.State == JobState.Failed
                    ? "Кадр не прошёл проверку"
                    : "Кадр отменён";
            }

            if (job.Drawn)
            {
                return "Кадр нарисован, ждёт проверки";
            }

            if (!string.IsNullOrEmpty(job.ProgressText))
            {
                return job.ProgressText;
            }

            if (job.Stage == StageSample || job.Percent >= 99)
            {
                return job.Drawn ? "Кадр нарисован, идёт проверка" : "Стена рисует кадр";
            }

            if (IsStalled(true, secondsSinceChange))
            {
                return $"Стенд готовит кадр — идёт {Elapsed(job.Elapsed)}, дольше обычного";
            }

            return $"Стенд готовит кадр · {Elapsed(job.Elapsed)}";
        }

        /// <summary>
        /// Подпись этапа прогона. Показывается у третьей полосы и отвечает на
        /// вопрос «почему полоса идёт медленно» — например, вырезка бьёт по
        /// всей папке raw, а не по заказанным кадрам.
        /// </summary>
        public static string StandStageText(string stage, int done, int total,
            string note)
        {
            if (string.IsNullOrEmpty(stage))
            {
                return string.Empty;
            }

            string title = StageTitle(stage);
            if (total > 0)
            {
                return $"{title}: {done} из {total}"
                    + (string.IsNullOrEmpty(note) ? string.Empty : $" · {note}");
            }

            return string.IsNullOrEmpty(note) ? title : $"{title} · {note}";
        }

        /// <summary>
        /// Оценка остатка. До первого завершённого кадра средней нет, и
        /// выдумывать её нельзя — тогда показываем только прошедшее время.
        /// </summary>
        public static string EtaText(JobQueue jobs, GenerationJob current)
        {
            if (jobs == null || current == null)
            {
                return string.Empty;
            }

            string elapsed = $"идёт {Elapsed(current.Elapsed)}";
            double eta = jobs.EtaSeconds;

            return eta <= 0
                ? elapsed + " · остаток оценится после первого кадра"
                : elapsed + $" · остаток ≈ {Elapsed(eta)}";
        }

        /// <summary>Прогресс стоит: работа идёт, а события не приходят.</summary>
        public static bool IsStalled(bool running, double secondsSinceProgress)
        {
            return running && secondsSinceProgress > StaleSeconds;
        }

        /// <summary>
        /// Секунды в «м:сс» (и «ч:мм:сс» для долгих прогонов). Отрицательное
        /// время — это отсутствие замера, а не «минус секунды».
        /// </summary>
        public static string Elapsed(double seconds)
        {
            if (seconds < 0 || double.IsNaN(seconds))
            {
                seconds = 0;
            }

            int total = (int)Math.Floor(seconds);
            int hours = total / 3600;
            int minutes = total % 3600 / 60;
            int secs = total % 60;

            return hours > 0
                ? $"{hours}:{minutes:00}:{secs:00}"
                : $"{minutes}:{secs:00}";
        }

        /// <summary>
        /// Скорость догоняния на каждое процентное различие. Растёт линейно:
        /// отстал на 2 % — едет медленно, отстал на 40 % — едет быстро.
        ///
        /// Линейно, а не «всегда одинаково», потому что обе крайности плохи:
        /// постоянная скорость оставляет полосу позади на каждом шаге
        /// сэмплера, а очень быстрая превращает прогресс в щёлчок. Чем дальше
        /// отставание, тем заметнее человек ждёт — тем быстрее и догоняем.
        /// </summary>
        public const float CatchUpGain = 2.2f;

        /// <summary>
        /// Потолок скорости, в процентах за секунду. Без него скачок 0 → 100
        /// (старт заказа, переход к новому кадру) отрабатывался бы за один
        /// кадр опроса, и плавности не было бы вовсе.
        /// </summary>
        public const float CatchUpMaxPerSecond = 240f;

        /// <summary>
        /// Скорость отката, в процентах за секунду. Падение быстрее роста
        /// намеренно: сброс кадра в ноль — это новый отсчёт, и задержать его
        /// на полсекунды значит показать прошлый кадр после начала нового.
        /// </summary>
        public const float FallPerSecond = 420f;

        /// <summary>
        /// Шаг плавного догоняния: где окажется видимая полоса через
        /// <paramref name="deltaSeconds"/>, если её настоящее значение равно
        /// <paramref name="target"/>.
        ///
        /// Смысл разделения «настоящее» и «видимое». Настоящий процент
        /// меняется рывками: шаг сэмплера, приход кадра, конец вырезки.
        /// Прыгающая по ним полоса читается как сбой, особенно на общей
        /// шкале, где один кадр из четырёх сразу добавляет 25 %. Видимое
        /// значение едет к настоящему плавно, замедляясь к цели, — и чем
        /// больше разрыв, тем быстрее движение.
        ///
        /// Никогда не проходит мимо цели и не раскручивается назад.
        /// </summary>
        public static float Eased(float shown, float target, double deltaSeconds)
        {
            if (double.IsNaN(deltaSeconds) || deltaSeconds <= 0)
            {
                return shown;
            }

            float diff = target - shown;
            if (Mathf.Abs(diff) < 0.05f)
            {
                return target;
            }

            float speed = diff < 0
                ? FallPerSecond
                : Mathf.Min(Mathf.Abs(diff) * CatchUpGain, CatchUpMaxPerSecond);

            float step = speed * (float)deltaSeconds;
            if (Mathf.Abs(step) >= Mathf.Abs(diff))
            {
                return target;
            }

            return shown + (diff < 0 ? -step : step);
        }

        private static int ByTime(double seconds, double spanSeconds,
            int from, int to)
        {
            if (spanSeconds <= 0)
            {
                return to;
            }

            double share = seconds / spanSeconds;
            return Mathf.Clamp((int)Math.Round(from + (to - from)
                * Math.Min(1, Math.Max(0, share))), from, to);
        }

        private static int Map(int value, int fromIn, int toIn, int fromOut, int toOut)
        {
            double span = toIn - fromIn;
            double mapped = span == 0
                ? fromOut
                : fromOut + (value - fromIn) * (double)(toOut - fromOut) / span;
            return Mathf.Clamp((int)Math.Round(mapped), Math.Min(fromOut, toOut),
                Math.Max(fromOut, toOut));
        }
    }
}