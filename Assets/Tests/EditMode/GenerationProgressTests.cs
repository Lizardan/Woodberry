using NUnit.Framework;
using Woodberry.Editor.ArtStudio;

namespace Woodberry.Tests.EditMode
{
    /// <summary>
    /// Подписи и числа прогресса генерации. Проверяются здесь, а не в окне:
    /// окно только подставляет готовую строку, а вся арифметика и
    /// форматирование живут в <see cref="GenerationProgress"/>.
    /// </summary>
    public sealed class GenerationProgressTests
    {
        private static GenerationJob Character(string standId)
        {
            return new GenerationJob
            {
                StandId = standId,
                Pool = "Priest",
                Kind = ArtAssetKind.Portrait,
                Label = standId
            };
        }

        [Test]
        public void Overall_WhenQueueEmpty_IsZero()
        {
            Assert.That(GenerationProgress.Overall(new JobQueue(), null, 0), Is.EqualTo(0));
        }

        [Test]
        public void Overall_AveragesContributionOfEachJob()
        {
            var queue = new JobQueue();
            queue.Enqueue("a", "A", ArtAssetKind.Portrait, "a");
            queue.Enqueue("b", "B", ArtAssetKind.Portrait, "b");
            queue.Enqueue("c", "C", ArtAssetKind.Portrait, "c");

            // Один кадр отрисован и ждёт приёмки, второй рисуется, третий
            // ещё в очереди. Общая полоса — среднее вкладов: (90 + 100 + 0)
            // / 3, а не процент первого кадра. Именно усреднение отвечает на
            // вопрос «сколько осталось», а не «как идёт текущий».
            queue.MarkDrawn("a");
            queue.SetProgress("b", 100, "шаг 10/10", "sample");

            int overall = GenerationProgress.Overall(queue, queue.Running, 0);

            Assert.That(overall, Is.EqualTo(63),
                "(90 отрисованный + 100 готовый + 0 ждущий) / 3");
        }

        [Test]
        public void Overall_WhenWaitingJobsOnly_StartsFromZero()
        {
            // Кадры, к которым стенд ещё не приступил, не имеют права давать
            // вклад: раньше драйвер слал им выдуманный процент, и общая полоса
            // стартовала не с нуля.
            var queue = new JobQueue();
            queue.Enqueue("a", "A", ArtAssetKind.Portrait, "a");
            queue.Enqueue("b", "B", ArtAssetKind.Portrait, "b");

            Assert.That(GenerationProgress.Overall(queue, null, 0), Is.EqualTo(0));
        }

        [Test]
        public void Overall_WhenQueueFinished_IsHundred()
        {
            var queue = new JobQueue();
            queue.Enqueue("a", "A", ArtAssetKind.Portrait, "a");
            queue.StartNext();
            queue.Finish("a", success: true, message: null);

            Assert.That(GenerationProgress.Overall(queue, null, 0), Is.EqualTo(100));
        }

        [Test]
        public void Overall_WhenCurrentJobPassed_UsesItsSmoothValue()
        {
            var queue = new JobQueue();
            queue.Enqueue("a", "A", ArtAssetKind.Portrait, "a");
            queue.Enqueue("b", "B", ArtAssetKind.Portrait, "b");
            GenerationJob running = queue.StartNext();

            // Шагов ещё не было: сырой процент равен 1, а кадровая шкала в
            // подготовке даёт 0 — общая обязана считать по кадровой, иначе
            // две полосы об одном деле показывали бы разное.
            queue.SetProgress("a", 1);

            Assert.That(GenerationProgress.Overall(queue, running, 0),
                Is.EqualTo(GenerationProgress.FramePercent(running, 0) / 2));
        }

        [Test]
        public void FramePercent_WhenNoJob_IsZero()
        {
            Assert.That(GenerationProgress.FramePercent(null, 0), Is.EqualTo(0));
        }

        [Test]
        public void FramePercent_WhenDone_IsHundred()
        {
            var queue = new JobQueue();
            queue.Enqueue("a", "A", ArtAssetKind.Portrait, "a");
            queue.StartNext();
            queue.Finish("a", success: true, message: null);

            Assert.That(GenerationProgress.FramePercent(queue.Jobs[0], 0), Is.EqualTo(100));
        }

        [Test]
        public void FramePercent_WhenFailed_DoesNotLookFinished()
        {
            var queue = new JobQueue();
            queue.Enqueue("a", "A", ArtAssetKind.Portrait, "a");
            queue.StartNext();
            queue.SetProgress("a", 40);
            queue.Finish("a", success: false, message: "шум");

            Assert.That(GenerationProgress.FramePercent(queue.Jobs[0], 0),
                Is.LessThan(100), "провал не должен выглядеть готовым кадром");
        }

        [Test]
        public void FramePercent_DuringPreparation_GrowsWithTimeAndStopsAtCeiling()
        {
            GenerationJob job = Character("a");
            job.State = JobState.Running;
            job.Percent = 1;

            job.Elapsed = 0;
            Assert.That(GenerationProgress.FramePercent(job, 0), Is.EqualTo(0));

            job.Elapsed = GenerationProgress.PrepSeconds / 2;
            int half = GenerationProgress.FramePercent(job, 0);
            Assert.That(half, Is.GreaterThan(0).And.LessThan(GenerationProgress.PrepCeiling));

            // Дальше потолок: подготовка не имеет права «дойти» до сэмплинга,
            // иначе полоса соврёт про готовность.
            job.Elapsed = GenerationProgress.PrepSeconds * 10;
            Assert.That(GenerationProgress.FramePercent(job, 0),
                Is.EqualTo(GenerationProgress.PrepCeiling));
        }

        [Test]
        public void FramePercent_DuringSampling_StaysBetweenCeilings()
        {
            GenerationJob job = Character("a");
            job.State = JobState.Running;

            job.Percent = 2;
            Assert.That(GenerationProgress.FramePercent(job, 0),
                Is.GreaterThanOrEqualTo(GenerationProgress.PrepCeiling));

            job.Percent = 98;
            Assert.That(GenerationProgress.FramePercent(job, 0),
                Is.EqualTo(GenerationProgress.FramePercent(job, 0)));
            Assert.That(GenerationProgress.FramePercent(job, 0),
                Is.GreaterThan(GenerationProgress.PrepCeiling).And.LessThanOrEqualTo(100));
        }

        [Test]
        public void FramePercent_DuringSampling_GrowsWithSteps()
        {
            GenerationJob job = Character("a");
            job.State = JobState.Running;

            job.Percent = 10;
            int early = GenerationProgress.FramePercent(job, 0);
            job.Percent = 80;
            int late = GenerationProgress.FramePercent(job, 0);

            Assert.That(late, Is.GreaterThan(early), "полоса кадра обязана расти вместе с шагами");
        }

        [Test]
        public void FramePercent_WhenFrameIsDrawn_ReadsAsFinished()
        {
            // Кадровая полоса значит «отрисован». Приёмка идёт за пределами
            // этой шкалы и видна в полосе этапов — иначе «100 %» означало бы
            // «уже в проекте», а это неправда.
            GenerationJob job = Character("a");
            job.State = JobState.Running;
            job.Drawn = true;

            Assert.That(GenerationProgress.FramePercent(job, 0), Is.EqualTo(100));
        }

        [Test]
        public void Share_WhenFrameIsDrawn_StaysBelowHundred()
        {
            // Общая полоса обязана отличать «нарисовано» от «принято».
            GenerationJob job = Character("a");
            job.State = JobState.Running;
            job.Drawn = true;

            Assert.That(GenerationProgress.Share(job, null, 0),
                Is.EqualTo(GenerationProgress.DrawnWeight));
            Assert.That(GenerationProgress.DrawnWeight, Is.LessThan(100));
        }

        [Test]
        public void StagePercent_WhenStageHasCounter_UsesItVerbatim()
        {
            Assert.That(GenerationProgress.StagePercent(
                GenerationProgress.StageCut, 37, 118, 0), Is.EqualTo(31));
            Assert.That(GenerationProgress.StagePercent(
                GenerationProgress.StageCopy, 2, 4, 0), Is.EqualTo(50));
        }

        [Test]
        public void StagePercent_WhenStageHasNoCounter_StopsBelowHundred()
        {
            // Этап без счётчика ползёт по времени, но упирается в потолок:
            // «100 %» при незавершённом этапе — то самое враньё, из-за которого
            // прогресс выглядел зависшим на минуты.
            int early = GenerationProgress.StagePercent(
                GenerationProgress.StagePrepare, 0, 0, 0);
            int later = GenerationProgress.StagePercent(
                GenerationProgress.StagePrepare, 0, 0,
                GenerationProgress.SmoothSeconds * 5);

            Assert.That(early, Is.EqualTo(0));
            Assert.That(later, Is.EqualTo(GenerationProgress.SmoothCeiling));
            Assert.That(later, Is.LessThan(100));
        }

        [Test]
        public void FramePercent_IsMonotonicAcrossPhases()
        {
            GenerationJob job = Character("a");
            job.State = JobState.Running;
            job.Elapsed = GenerationProgress.PrepSeconds;

            int previous = -1;
            // Подготовка -> сэмплинг: полоса не имеет права дёрнуться назад
            // ни на одном переходе.
            for (int percent = 1; percent <= 99; percent++)
            {
                job.Percent = percent;
                int value = GenerationProgress.FramePercent(job, 0);
                Assert.That(value, Is.GreaterThanOrEqualTo(previous),
                    $"процент кадра упал на шаге {percent}");
                previous = value;
            }
        }

        [Test]
        public void JobLabel_WhenLabelMissing_FallsBackToStandId()
        {
            var job = new GenerationJob { StandId = "p_Roles_Priest_c", Label = null };

            Assert.That(GenerationProgress.JobLabel(job), Is.EqualTo("p_Roles_Priest_c"));
        }

        [Test]
        public void JobLabel_WhenJobMissing_IsDash()
        {
            Assert.That(GenerationProgress.JobLabel(null), Is.EqualTo("—"));
        }

        [Test]
        public void PhaseText_WhenSamplerStepsArrived_ShowsThem()
        {
            GenerationJob job = Character("a");
            job.ProgressText = "шаг 4/10";

            Assert.That(GenerationProgress.PhaseText(job, 0.2), Is.EqualTo("шаг 4/10"));
        }

        [Test]
        public void PhaseText_WhenNoStepsAndFresh_TalksAboutPreparation()
        {
            GenerationJob job = Character("a");

            Assert.That(GenerationProgress.PhaseText(job, 1.0),
                Is.EqualTo("Стенд готовит кадр · 0:00"),
                "человеку нужна фраза о работе, а не техническое «подготовка кадра»");
        }

        [Test]
        public void PhaseText_AfterLastStep_TalksAboutFinishing()
        {
            GenerationJob job = Character("a");
            job.State = JobState.Running;
            job.Percent = 99;

            Assert.That(GenerationProgress.PhaseText(job, 0.5),
                Does.Contain("рисует"));
        }

        [Test]
        public void PhaseText_WhenFrameDrawn_SaysItWaitsForCheck()
        {
            GenerationJob job = Character("a");
            job.State = JobState.Running;
            job.Drawn = true;

            Assert.That(GenerationProgress.PhaseText(job, 0),
                Does.Contain("проверк"));
        }

        [Test]
        public void PhaseText_OnFinishedJob_SaysItWasAccepted()
        {
            var queue = new JobQueue();
            queue.Enqueue("a", "A", ArtAssetKind.Portrait, "a");
            queue.StartNext();
            queue.Finish("a", success: true, message: null);

            Assert.That(GenerationProgress.PhaseText(queue.Jobs[0], 0),
                Is.EqualTo("Кадр принят"));
        }

        [Test]
        public void PhaseText_WhenNoStepsAndStalled_ShowsElapsedWait()
        {
            GenerationJob job = Character("a");
            job.Elapsed = 372.0;

            string text = GenerationProgress.PhaseText(job, 372.0);

            Assert.That(text, Does.Contain("6:12"), "человеку нужен таймер ожидания");
        }

        [Test]
        public void IsStalled_WhenNotRunning_IsFalse()
        {
            Assert.That(GenerationProgress.IsStalled(false, 600.0), Is.False);
        }

        [Test]
        public void IsStalled_WhenWithinThreshold_IsFalse()
        {
            Assert.That(
                GenerationProgress.IsStalled(true, GenerationProgress.StaleSeconds - 0.1),
                Is.False);
        }

        [Test]
        public void IsStalled_WhenSilentLongerThanThreshold_IsTrue()
        {
            Assert.That(
                GenerationProgress.IsStalled(true, GenerationProgress.StaleSeconds + 0.1),
                Is.True);
        }

        [Test]
        public void Elapsed_FormatsMinutesAndSeconds()
        {
            Assert.That(GenerationProgress.Elapsed(42), Is.EqualTo("0:42"));
            Assert.That(GenerationProgress.Elapsed(125.9), Is.EqualTo("2:05"));
        }

        [Test]
        public void Elapsed_ForLongRun_IncludesHours()
        {
            Assert.That(GenerationProgress.Elapsed(3725), Is.EqualTo("1:02:05"));
        }

        [Test]
        public void Elapsed_WhenNegative_IsZero()
        {
            Assert.That(GenerationProgress.Elapsed(-5), Is.EqualTo("0:00"));
        }

        [Test]
        public void EtaText_WhenNothingFinishedYet_PromisesEstimateAfterFirstFrame()
        {
            var queue = new JobQueue();
            GenerationJob job = queue.Enqueue("a", "A", ArtAssetKind.Portrait, "a");
            queue.StartNext();
            queue.Tick(5);

            Assert.That(GenerationProgress.EtaText(queue, job),
                Does.Contain("оценится после первого кадра"),
                "оценка без замера была бы выдуманной");
        }

        [Test]
        public void EtaText_AfterOneFinishedFrame_ShowsExtrapolatedRemainder()
        {
            var queue = new JobQueue();
            queue.Enqueue("a", "A", ArtAssetKind.Portrait, "a");
            queue.Enqueue("b", "B", ArtAssetKind.Portrait, "b");
            queue.Enqueue("c", "C", ArtAssetKind.Portrait, "c");
            queue.StartNext();
            queue.Tick(20);
            queue.Finish("a", success: true, message: null);
            GenerationJob next = queue.StartNext();

            string text = GenerationProgress.EtaText(queue, next);

            // Среднее 20 с на кадр, незавершённых два — остаток 40 с.
            Assert.That(text, Does.Contain("0:40"));
        }

        [Test]
        public void Eased_WhenBehind_GrowsTowardTargetAndNeverOvershoots()
        {
            float value = 0f;
            float target = 60f;

            for (int i = 0; i < 200; i++)
            {
                float before = value;
                value = GenerationProgress.Eased(value, target, 0.08);
                Assert.That(value, Is.GreaterThanOrEqualTo(before),
                    "полоса обязана расти монотонно");
                Assert.That(value, Is.LessThanOrEqualTo(target),
                    "полоса не имеет права пролететь мимо цели");
                if (value == target)
                {
                    break;
                }
            }

            Assert.That(value, Is.EqualTo(target), "полоса обязана догнать настоящий процент");
        }

        [Test]
        public void Eased_WhenGapIsBigger_MovesFaster()
        {
            // Требование человека о полосе: чем больше отставание, тем быстрее
            // догоняние. Иначе полоса надолго отстаёт и «залипает» позади.
            float smallGap = GenerationProgress.Eased(0f, 5f, 0.08) - 0f;
            float bigGap = GenerationProgress.Eased(0f, 80f, 0.08) - 0f;

            Assert.That(bigGap, Is.GreaterThan(smallGap),
                "большее отставание должно ехаться быстрее меньшего");
        }

        [Test]
        public void Eased_FallsBackFasterThanItRises()
        {
            // Откат намеренно быстрее: сброс кадра в ноль — это новый отсчёт,
            // и задержка показывала бы проценты ПРОШЛОГО кадра в новом.
            float rise = GenerationProgress.Eased(0f, 50f, 0.08);
            float fall = 50f - GenerationProgress.Eased(50f, 0f, 0.08);

            Assert.That(fall, Is.GreaterThan(rise));
        }

        [Test]
        public void Eased_WhenAtTarget_StaysExactly()
        {
            Assert.That(GenerationProgress.Eased(42f, 42f, 0.5), Is.EqualTo(42f));
        }

        [Test]
        public void Eased_WhenNoTimePassed_KeepsValue()
        {
            Assert.That(GenerationProgress.Eased(10f, 90f, 0), Is.EqualTo(10f));
            Assert.That(GenerationProgress.Eased(10f, 90f, double.NaN), Is.EqualTo(10f));
        }

        [Test]
        public void Eased_AfterLongPause_JumpsToTargetRatherThanCrawling()
        {
            // Без потолка скорости шаг мог превысить разрыв и полоса пролетала
            // бы мимо цели; с потолком — просто приходит в неё за один шаг.
            float value = GenerationProgress.Eased(0f, 100f, 30.0);

            Assert.That(value, Is.EqualTo(100f));
        }
    }
}
