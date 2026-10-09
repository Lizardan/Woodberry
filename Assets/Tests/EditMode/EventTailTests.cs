using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using Woodberry.Editor.ArtStudio;

namespace Woodberry.Tests.EditMode
{
    /// <summary>
    /// Чтение <c>events.jsonl</c> по смещению — фундамент интерфейса: файл
    /// пишет другой процесс, и именно здесь теряется прогресс, если читатель
    /// берёт недописанную строку.
    /// </summary>
    public sealed class EventTailTests
    {
        private string _path;
        private readonly List<string> _problems = new List<string>();

        [SetUp]
        public void SetUp()
        {
            _problems.Clear();
            _path = Path.Combine(Path.GetTempPath(), $"artstudio_events_{TestContext.CurrentContext.Test.Name}.jsonl");
            if (File.Exists(_path))
            {
                File.Delete(_path);
            }
        }

        [TearDown]
        public void TearDown()
        {
            if (File.Exists(_path))
            {
                File.Delete(_path);
            }
        }

        [Test]
        public void ReadNew_WhenFileMissing_ReturnsEmptyAndNoProblems()
        {
            var tail = new EventTail();

            IReadOnlyList<ArtEvent> events = tail.ReadNew(_path, _problems);

            Assert.That(events, Is.Empty);
            Assert.That(_problems, Is.Empty);
        }

        [Test]
        public void ReadNew_WhenOneWholeLineWritten_ReturnsOneEvent()
        {
            File.WriteAllText(_path,
                "{\"seq\":1,\"kind\":\"ready\",\"percent\":0}\n");
            var tail = new EventTail();

            IReadOnlyList<ArtEvent> events = tail.ReadNew(_path, _problems);

            Assert.That(events.Count, Is.EqualTo(1));
            Assert.That(events[0].kind, Is.EqualTo(ArtEvent.Ready));
            Assert.That(events[0].seq, Is.EqualTo(1));
            Assert.That(_problems, Is.Empty);
        }

        [Test]
        public void ReadNew_WhenLastLineIsTruncated_ParsesOnlyWholeLines()
        {
            // Кадр генерируется, а драйвер пишет строку события прямо сейчас.
            File.WriteAllText(_path, "{\"seq\":1,\"kind\":\"log\"}\n{\"seq\":2,\"kin");
            var tail = new EventTail();

            IReadOnlyList<ArtEvent> first = tail.ReadNew(_path, _problems);

            Assert.That(first.Count, Is.EqualTo(1));
            Assert.That(first[0].seq, Is.EqualTo(1));
            Assert.That(_problems, Is.Empty,
                "недописанная строка — не ошибка, это состояние гонки");

            // Драйвер дописал остаток строки.
            File.AppendAllText(_path, "d\":\"item_progress\"}\n");

            IReadOnlyList<ArtEvent> second = tail.ReadNew(_path, _problems);

            Assert.That(second.Count, Is.EqualTo(1));
            Assert.That(second[0].seq, Is.EqualTo(2));
            Assert.That(second[0].kind, Is.EqualTo(ArtEvent.ItemProgress));
            Assert.That(_problems, Is.Empty);
        }

        [Test]
        public void ReadNew_WhenLineIsBrokenJson_ReportsProblemAndKeepsGoing()
        {
            File.WriteAllText(_path,
                "{\"seq\":1,\"kind\":\"log\"}\nне json вовсе\n{\"seq\":3,\"kind\":\"error\"}\n");
            var tail = new EventTail();

            IReadOnlyList<ArtEvent> events = tail.ReadNew(_path, _problems);

            Assert.That(events.Count, Is.EqualTo(2));
            Assert.That(events[1].seq, Is.EqualTo(3));
            Assert.That(_problems.Count, Is.EqualTo(1),
                "битая строка обязана быть названа, а не проглочена");
        }

        [Test]
        public void ReadNew_WhenLineHasNoKind_ReportsProblem()
        {
            File.WriteAllText(_path, "{\"seq\":7}\n");
            var tail = new EventTail();

            IReadOnlyList<ArtEvent> events = tail.ReadNew(_path, _problems);

            Assert.That(events, Is.Empty);
            Assert.That(_problems.Count, Is.EqualTo(1));
        }

        [Test]
        public void ReadNew_WhenNothingAppended_ReturnsNothing()
        {
            File.WriteAllText(_path, "{\"seq\":1,\"kind\":\"log\"}\n");
            var tail = new EventTail();
            tail.ReadNew(_path, _problems);

            IReadOnlyList<ArtEvent> second = tail.ReadNew(_path, _problems);

            Assert.That(second, Is.Empty);
            Assert.That(_problems, Is.Empty);
        }

        [Test]
        public void ReadNew_WhenCrLfEndings_ParsesLinesWithoutCarriageReturn()
        {
            File.WriteAllText(_path, "{\"seq\":1,\"kind\":\"ready\"}\r\n");
            var tail = new EventTail();

            IReadOnlyList<ArtEvent> events = tail.ReadNew(_path, _problems);

            Assert.That(events.Count, Is.EqualTo(1));
            Assert.That(events[0].kind, Is.EqualTo(ArtEvent.Ready));
        }

        [Test]
        public void Reset_AfterReading_ReadsFileFromStart()
        {
            File.WriteAllText(_path, "{\"seq\":1,\"kind\":\"log\"}\n");
            var tail = new EventTail();
            tail.ReadNew(_path, _problems);
            tail.Reset();

            IReadOnlyList<ArtEvent> events = tail.ReadNew(_path, _problems);

            Assert.That(events.Count, Is.EqualTo(1));
        }
    }
}
