using NUnit.Framework;
using Woodberry.Editor.ArtStudio;

namespace Woodberry.Tests.EditMode
{
    /// <summary>
    /// Очередь генерации. Общий процент и оценка остатка считаются здесь,
    /// а не в окне, потому что окно их только показывает.
    /// </summary>
    public sealed class JobQueueTests
    {
        private static GenerationJob Character(string standId)
        {
            return new GenerationJob
            {
                StandId = standId,
                Pool = standId.Split('_')[2],
                Kind = ArtAssetKind.Portrait,
                Label = standId
            };
        }

        [Test]
        public void Enqueue_AddsJobToQueue()
        {
            var queue = new JobQueue();

            queue.Enqueue("p_Roles_Priest_c", "Priest", ArtAssetKind.Portrait, "Священник — c");

            Assert.That(queue.Total, Is.EqualTo(1));
            Assert.That(queue.Jobs[0].State, Is.EqualTo(JobState.Queued));
            Assert.That(queue.IsIdle, Is.False);
        }

        [Test]
        public void StartNext_TakesFirstQueuedJobAndMarksRunning()
        {
            var queue = new JobQueue();
            queue.Enqueue("p_Roles_Priest_c", "Priest", ArtAssetKind.Portrait, "a");
            queue.Enqueue("p_Roles_Priest_d", "Priest", ArtAssetKind.Portrait, "b");

            GenerationJob running = queue.StartNext();

            Assert.That(running.StandId, Is.EqualTo("p_Roles_Priest_c"));
            Assert.That(running.State, Is.EqualTo(JobState.Running));
            Assert.That(queue.Running, Is.SameAs(running));
        }

        [Test]
        public void StartNext_WhenQueueEmpty_ReturnsNull()
        {
            var queue = new JobQueue();

            Assert.That(queue.StartNext(), Is.Null);
        }

        [Test]
        public void SetProgress_OnRunningJob_UpdatesPercent()
        {
            var queue = new JobQueue();
            GenerationJob job = queue.Enqueue("p_Roles_Priest_c", "Priest", ArtAssetKind.Portrait, "a");
            queue.StartNext();

            Assert.That(queue.SetProgress("p_Roles_Priest_c", 40), Is.True);
            Assert.That(job.Percent, Is.EqualTo(40));
        }

        [Test]
        public void SetProgress_WithValueAboveHundred_ClampsToHundred()
        {
            var queue = new JobQueue();
            GenerationJob job = queue.Enqueue("p_Roles_Priest_c", "Priest", ArtAssetKind.Portrait, "a");
            queue.StartNext();

            queue.SetProgress("p_Roles_Priest_c", 140);

            Assert.That(job.Percent, Is.EqualTo(100));
        }

        [Test]
        public void SetProgress_OnFinishedJob_IsIgnored()
        {
            var queue = new JobQueue();
            GenerationJob job = queue.Enqueue("p_Roles_Priest_c", "Priest", ArtAssetKind.Portrait, "a");
            queue.StartNext();
            queue.Finish("p_Roles_Priest_c", success: true, message: null);

            bool applied = queue.SetProgress("p_Roles_Priest_c", 30);

            Assert.That(applied, Is.False);
            Assert.That(job.Percent, Is.EqualTo(100));
        }

        [Test]
        public void SetProgress_OnUnknownId_ReturnsFalse()
        {
            var queue = new JobQueue();

            Assert.That(queue.SetProgress("p_Roles_Nobody_z", 50), Is.False);
        }

        [Test]
        public void Finish_WhenSuccess_MarksDoneAndFullPercent()
        {
            var queue = new JobQueue();
            GenerationJob job = queue.Enqueue("p_Roles_Priest_c", "Priest", ArtAssetKind.Portrait, "a");
            queue.StartNext();
            queue.SetProgress("p_Roles_Priest_c", 70);

            queue.Finish("p_Roles_Priest_c", success: true, message: null);

            Assert.That(job.State, Is.EqualTo(JobState.Done));
            Assert.That(job.Percent, Is.EqualTo(100));
            Assert.That(queue.Failed, Is.EqualTo(0));
        }

        [Test]
        public void Finish_WhenFailure_KeepsPercentAndStoresReason()
        {
            var queue = new JobQueue();
            GenerationJob job = queue.Enqueue("bg_City_Guild", "Guild", ArtAssetKind.Background, "Гильдия");
            queue.StartNext();
            queue.SetProgress("bg_City_Guild", 30);

            queue.Finish("bg_City_Guild", success: false, message: "кадр шум");

            Assert.That(job.State, Is.EqualTo(JobState.Failed));
            Assert.That(job.Percent, Is.EqualTo(30), "провал не должен выглядеть готовым");
            Assert.That(job.Message, Is.EqualTo("кадр шум"));
            Assert.That(queue.Failed, Is.EqualTo(1));
        }

        [Test]
        public void Enqueue_WhenSameStandIdTwice_ReusesJobInsteadOfDuplicating()
        {
            var queue = new JobQueue();
            GenerationJob first = queue.Enqueue("a", "A", ArtAssetKind.Portrait, "первый");
            queue.StartNext();
            queue.SetProgress("a", 70);

            GenerationJob second = queue.Enqueue("a", "A", ArtAssetKind.Portrait, "повтор");

            Assert.That(second, Is.SameAs(first), "второе задание сломало бы поиск по id");
            Assert.That(queue.Total, Is.EqualTo(1));
            Assert.That(second.State, Is.EqualTo(JobState.Queued));
            Assert.That(second.Percent, Is.EqualTo(0), "кадр делается заново, а не с середины");
            Assert.That(second.Elapsed, Is.EqualTo(0d));
            Assert.That(second.Message, Is.Null, "старый вердикт не должен пережить перезаказ");
        }

        [Test]
        public void Enqueue_AfterSameIdFinished_JobBecomesQueuedAgain()
        {
            var queue = new JobQueue();
            queue.Enqueue("a", "A", ArtAssetKind.Portrait, "a");
            queue.StartNext();
            queue.Finish("a", success: true, message: null);

            GenerationJob again = queue.Enqueue("a", "A", ArtAssetKind.Portrait, "a");

            Assert.That(again.State, Is.EqualTo(JobState.Queued));
            Assert.That(queue.IsIdle, Is.False, "повторный заказ обязан вернуть очередь в работу");
        }

        [Test]
        public void EtaSeconds_WhenNoJobFinished_IsZero()
        {
            var queue = new JobQueue();
            queue.Enqueue("a", "A", ArtAssetKind.Portrait, "a");
            queue.StartNext();
            queue.Tick(5);

            Assert.That(queue.EtaSeconds, Is.EqualTo(0d),
                "оценка без замера была бы выдуманной");
        }

        [Test]
        public void EtaSeconds_AfterOneFinished_ExtrapolatesRemainder()
        {
            var queue = new JobQueue();
            queue.Enqueue("a", "A", ArtAssetKind.Portrait, "a");
            queue.Enqueue("b", "B", ArtAssetKind.Portrait, "b");
            queue.Enqueue("c", "C", ArtAssetKind.Portrait, "c");
            queue.StartNext();
            queue.Tick(20);
            queue.Finish("a", success: true, message: null);

            Assert.That(queue.EtaSeconds, Is.EqualTo(40d).Within(0.001),
                "среднее 20 с на два незавершённых задания");
        }

        [Test]
        public void Tick_OnlyCountsRunningJob()
        {
            var queue = new JobQueue();
            GenerationJob first = queue.Enqueue("a", "A", ArtAssetKind.Portrait, "a");
            GenerationJob second = queue.Enqueue("b", "B", ArtAssetKind.Portrait, "b");

            queue.StartNext();
            queue.Tick(3);

            Assert.That(first.Elapsed, Is.EqualTo(3d).Within(0.001));
            Assert.That(second.Elapsed, Is.EqualTo(0d));
        }

        [Test]
        public void Cancel_OnQueuedJob_MarksCancelled()
        {
            var queue = new JobQueue();
            GenerationJob job = queue.Enqueue("a", "A", ArtAssetKind.Portrait, "a");

            Assert.That(queue.Cancel("a"), Is.True);
            Assert.That(job.State, Is.EqualTo(JobState.Cancelled));
            Assert.That(queue.IsIdle, Is.True);
        }

        [Test]
        public void Cancel_OnFinishedJob_IsIgnored()
        {
            var queue = new JobQueue();
            queue.Enqueue("a", "A", ArtAssetKind.Portrait, "a");
            queue.StartNext();
            queue.Finish("a", success: true, message: null);

            Assert.That(queue.Cancel("a"), Is.False);
        }

        [Test]
        public void ClearFinished_RemovesOnlyFinishedJobs()
        {
            var queue = new JobQueue();
            queue.Enqueue("a", "A", ArtAssetKind.Portrait, "a");
            queue.Enqueue("b", "B", ArtAssetKind.Portrait, "b");
            queue.StartNext();
            queue.Finish("a", success: true, message: null);
            queue.StartNext();

            int removed = queue.ClearFinished();

            Assert.That(removed, Is.EqualTo(1));
            Assert.That(queue.Total, Is.EqualTo(1));
            Assert.That(queue.Running.StandId, Is.EqualTo("b"));
        }
    }
}
