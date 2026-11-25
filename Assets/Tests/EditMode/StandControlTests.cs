using NUnit.Framework;
using Woodberry.Editor.ArtStudio;

namespace Woodberry.Tests.EditMode
{
    /// <summary>
    /// Автомат одной кнопки «Стенд». Проверяется здесь, а не в окне: переходы
    /// решают, можно ли работать и останутся ли после остановки процессы, а
    /// проверить это живьём (особенно таймаут) дорого и ненадёжно.
    /// </summary>
    public sealed class StandControlTests
    {
        private const double Now = 1000.0;

        [Test]
        public void Resolve_DownAndNothingRunning_StaysDown()
        {
            Assert.That(
                StandControl.Resolve(StandPhase.Down, false, false, Now, 0),
                Is.EqualTo(StandPhase.Down));
        }

        [Test]
        public void Resolve_DownButAlreadyUp_BecomesUp()
        {
            // Драйвер и сервер могли пережить перезапуск окна: тогда кнопка
            // обязана сразу предлагать остановку, а не второй подъём.
            Assert.That(
                StandControl.Resolve(StandPhase.Down, true, true, Now, 0),
                Is.EqualTo(StandPhase.Up));
        }

        [Test]
        public void Resolve_StartingWaitsWhileSomethingMissing()
        {
            double deadline = StandControl.Deadline(StandPhase.Starting, Now);

            Assert.That(StandControl.Resolve(StandPhase.Starting, true, false, Now, deadline),
                Is.EqualTo(StandPhase.Starting), "сервер ещё не готов");
            Assert.That(StandControl.Resolve(StandPhase.Starting, false, false, Now, deadline),
                Is.EqualTo(StandPhase.Starting), "драйвер ещё не подключился");
        }

        [Test]
        public void Resolve_StartingBecomesUpWhenBothReady()
        {
            double deadline = StandControl.Deadline(StandPhase.Starting, Now);

            Assert.That(StandControl.Resolve(StandPhase.Starting, true, true, Now, deadline),
                Is.EqualTo(StandPhase.Up));
        }

        [Test]
        public void Resolve_StartingGivesUpAfterDeadline()
        {
            double deadline = StandControl.Deadline(StandPhase.Starting, Now);
            double after = deadline + 1;

            Assert.That(StandControl.Resolve(StandPhase.Starting, true, false, after, deadline),
                Is.EqualTo(StandPhase.Down),
                "иначе зависший старт навсегда оставил бы кнопку погашенной");
        }

        [Test]
        public void Resolve_StoppingWaitsUntilEverythingGone()
        {
            double deadline = StandControl.Deadline(StandPhase.Stopping, Now);

            Assert.That(StandControl.Resolve(StandPhase.Stopping, true, true, Now, deadline),
                Is.EqualTo(StandPhase.Stopping), "драйвер ещё жив");
            Assert.That(StandControl.Resolve(StandPhase.Stopping, false, true, Now, deadline),
                Is.EqualTo(StandPhase.Stopping), "сервер ещё отвечает");
        }

        [Test]
        public void Resolve_StoppingBecomesDownWhenBothGone()
        {
            double deadline = StandControl.Deadline(StandPhase.Stopping, Now);

            Assert.That(StandControl.Resolve(StandPhase.Stopping, false, false, Now, deadline),
                Is.EqualTo(StandPhase.Down));
        }

        [Test]
        public void Resolve_StoppingGivesUpAfterDeadline()
        {
            double deadline = StandControl.Deadline(StandPhase.Stopping, Now);
            double after = deadline + 1;

            Assert.That(StandControl.Resolve(StandPhase.Stopping, true, true, after, deadline),
                Is.EqualTo(StandPhase.Down),
                "кнопка не имеет права остаться серой навсегда");
        }

        [Test]
        public void Resolve_UpFallsBackToDownWhenStandDies()
        {
            Assert.That(StandControl.Resolve(StandPhase.Up, false, false, Now, 0),
                Is.EqualTo(StandPhase.Down), "мёртвый стенд не «работает»");
            Assert.That(StandControl.Resolve(StandPhase.Up, true, false, Now, 0),
                Is.EqualTo(StandPhase.Down), "сервер упал — стенд не готов");
            Assert.That(StandControl.Resolve(StandPhase.Up, true, true, Now, 0),
                Is.EqualTo(StandPhase.Up));
        }

        [Test]
        public void Deadline_IsSetOnlyForTransitions()
        {
            Assert.That(StandControl.Deadline(StandPhase.Down, Now), Is.EqualTo(0));
            Assert.That(StandControl.Deadline(StandPhase.Up, Now), Is.EqualTo(0));
            Assert.That(StandControl.Deadline(StandPhase.Starting, Now),
                Is.EqualTo(Now + StandControl.StartTimeoutSeconds));
            Assert.That(StandControl.Deadline(StandPhase.Stopping, Now),
                Is.EqualTo(Now + StandControl.StopTimeoutSeconds));
        }

        [Test]
        public void CanPress_IsFalseWhileTransitionRuns()
        {
            Assert.That(StandControl.CanPress(StandPhase.Down), Is.True);
            Assert.That(StandControl.CanPress(StandPhase.Up), Is.True);
            Assert.That(StandControl.CanPress(StandPhase.Starting), Is.False,
                "второе нажатие запустило бы второй подъём");
            Assert.That(StandControl.CanPress(StandPhase.Stopping), Is.False);
        }

        [Test]
        public void Label_NamesWhatIsHappeningDuringStart()
        {
            string waitingDriver = StandControl.Label(StandPhase.Starting, false, "не поднят");
            string waitingServer = StandControl.Label(StandPhase.Starting, true, "поднимается");

            Assert.That(waitingDriver, Does.Contain("драйвер"));
            Assert.That(waitingServer, Does.Contain("сервер"));
            Assert.That(waitingDriver, Is.Not.EqualTo(waitingServer),
                "разные шаги подъёма обязаны читаться по-разному");
        }

        [Test]
        public void Label_UpOffersStopAndDownOffersStart()
        {
            Assert.That(StandControl.Label(StandPhase.Up, true, "готов"),
                Does.Contain("Остановить"));
            Assert.That(StandControl.Label(StandPhase.Down, false, "не поднят"),
                Does.Contain("Поднять"));
        }

        [Test]
        public void Label_WhenDriverAliveButServerDown_OffersOnlyServer()
        {
            // Половинчатое состояние: драйвер жив, сервер погашен. Кнопка
            // обязана обещать подъём сервера, а не подъём стенда целиком.
            Assert.That(StandControl.Label(StandPhase.Down, true, "погашен"),
                Is.EqualTo("Поднять ComfyUI"));
        }

        [Test]
        public void TimeoutProblem_IsSilentBeforeDeadlineAndLoudAfter()
        {
            double deadline = StandControl.Deadline(StandPhase.Starting, Now);

            Assert.That(StandControl.TimeoutProblem(StandPhase.Starting, Now, deadline),
                Is.Empty);
            Assert.That(StandControl.TimeoutProblem(StandPhase.Starting, deadline + 0.1, deadline),
                Is.Not.Empty);
            Assert.That(StandControl.TimeoutProblem(StandPhase.Down, Now, 0),
                Is.Empty, "у устойчивого состояния срока нет");
        }
    }
}
