using System.IO;
using NUnit.Framework;
using Woodberry.Editor.ArtStudio;

namespace Woodberry.Tests.EditMode
{
    /// <summary>
    /// Сверка состояния с живым процессом драйвера.
    ///
    /// Почему это тест на файлах, а не на логике. Событие `ready` приходит
    /// один раз, а процесс умирает молча, поэтому «драйвер ответил» и «драйвер
    /// существует» — разные факты, и сходятся они только на чтении
    /// `driver.json` и `events.jsonl`. Здесь проверяется именно это место.
    /// </summary>
    public sealed class ArtDriverClientTests
    {
        private string _root;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(),
                $"artstudio_client_{TestContext.CurrentContext.Test.Name}");
            Directory.CreateDirectory(ArtStudioPaths.Root(_root));
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }

        [Test]
        public void Poll_WhenReadyButNoDriverInfo_MarksDriverDisconnected()
        {
            WriteEvents("{\"seq\":1,\"kind\":\"ready\",\"protocol\":1}");

            var client = new ArtDriverClient(_root, ArtSettings.Default());
            client.Poll();

            Assert.That(client.State.DriverConnected, Is.False,
                "окно не должно показывать «подключён», когда процесса нет: " +
                "иначе кнопка «Сгенерировать» остаётся активной и пишет команду " +
                "в файл, который никто не читает");
        }

        [Test]
        public void Poll_WhenDriverPidPointsToNothing_MarksDriverDisconnected()
        {
            WriteEvents("{\"seq\":1,\"kind\":\"ready\",\"protocol\":1}");
            File.WriteAllText(ArtStudioPaths.DriverInfo(_root),
                "{\"pid\": 2147483647, \"protocol\": 1}");

            var client = new ArtDriverClient(_root, ArtSettings.Default());
            client.Poll();

            Assert.That(client.State.DriverConnected, Is.False,
                "driver.json устаревает: pid из него проверяется на существование");
        }

        [Test]
        public void Poll_AfterDisconnect_KeepsLastKnownServerState()
        {
            WriteEvents(
                "{\"seq\":1,\"kind\":\"ready\",\"protocol\":1}",
                "{\"seq\":2,\"kind\":\"server_state\",\"text\":\"готов\"}");

            var client = new ArtDriverClient(_root, ArtSettings.Default());
            client.Poll();

            Assert.That(client.State.DriverConnected, Is.False);
            Assert.That(client.State.ServerState, Is.EqualTo("готов"),
                "ComfyUI живёт отдельным процессом и переживает смерть драйвера — " +
                "его последнее состояние не стирается");
        }

        [Test]
        public void Poll_WhenEventsFileIsEmpty_KeepsDriverDisconnected()
        {
            File.WriteAllText(ArtStudioPaths.Events(_root), string.Empty);

            var client = new ArtDriverClient(_root, ArtSettings.Default());
            client.Poll();

            Assert.That(client.State.DriverConnected, Is.False);
        }

        private void WriteEvents(params string[] lines)
        {
            File.WriteAllText(ArtStudioPaths.Events(_root), string.Join("\n", lines) + "\n");
        }
    }
}
