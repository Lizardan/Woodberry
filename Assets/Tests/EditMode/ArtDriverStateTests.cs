using NUnit.Framework;
using Woodberry.Editor.ArtStudio;

namespace Woodberry.Tests.EditMode
{
    /// <summary>
    /// Переходы состояния по событиям драйвера. Проверяется без файлов и
    /// процессов: это правило «что значит событие», а не чтение jsonl.
    /// </summary>
    public sealed class ArtDriverStateTests
    {
        [Test]
        public void Ready_MarksDriverConnectedWithProtocol()
        {
            var state = new ArtDriverState();

            state.Apply(new ArtEvent { kind = ArtEvent.Ready, protocol = ArtProtocol.Version });

            Assert.That(state.DriverConnected, Is.True);
            Assert.That(state.DriverProtocol, Is.EqualTo(ArtProtocol.Version));
            Assert.That(state.ProtocolMismatch, Is.False);
        }

        [Test]
        public void Ready_WhenProtocolDiffers_ReportsMismatch()
        {
            var state = new ArtDriverState();

            state.Apply(new ArtEvent { kind = ArtEvent.Ready, protocol = ArtProtocol.Version + 1 });

            Assert.That(state.ProtocolMismatch, Is.True,
                "расхождение версий — ошибка конфигурации, а не молчаливая поломка");
        }

        [Test]
        public void ServerState_UpdatesText()
        {
            var state = new ArtDriverState();

            state.Apply(new ArtEvent { kind = ArtEvent.ServerState, text = "готов" });

            Assert.That(state.ServerState, Is.EqualTo("готов"));
        }

        [Test]
        public void Catalog_ParsesPoolsAndFreeFrames()
        {
            var state = new ArtDriverState();
            const string detail =
                "[{\"pool\":\"p_Roles_Priest\",\"kind\":\"character\"," +
                "\"all\":[\"p_Roles_Priest_a\"],\"free\":[\"p_Roles_Priest_a\"]}," +
                "{\"pool\":\"bg_City_Guild\",\"kind\":\"background\"," +
                "\"all\":[\"bg_City_Guild\"],\"free\":[]}]";

            state.Apply(new ArtEvent { kind = ArtEvent.CatalogEvent, detail = detail });

            Assert.That(state.Pools.Count, Is.EqualTo(2));
            Assert.That(state.FreeFrames, Is.EqualTo(1));

            DriverPool priest = state.FindPool("p_Roles_Priest");
            Assert.That(priest, Is.Not.Null);
            Assert.That(priest.Kind, Is.EqualTo(ArtAssetKind.Portrait));
            Assert.That(priest.Free.Count, Is.EqualTo(1),
                "Has.Count не работает на IReadOnlyList — считаем явно");
        }

        [Test]
        public void Catalog_WhenBroken_KeepsStateAndRecordsError()
        {
            var state = new ArtDriverState();

            state.Apply(new ArtEvent { kind = ArtEvent.CatalogEvent, detail = "{не массив}" });

            Assert.That(state.Pools.Count, Is.EqualTo(0));
            Assert.That(state.LastError, Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void ItemAdded_EnqueuesJob()
        {
            var state = new ArtDriverState();

            state.Apply(new ArtEvent
            {
                kind = ArtEvent.ItemAdded,
                id = "p_Roles_Priest_c",
                pool = "p_Roles_Priest",
                label = "Священник — c"
            });

            Assert.That(state.Jobs.Total, Is.EqualTo(1));
            Assert.That(state.Jobs.Jobs[0].Kind, Is.EqualTo(ArtAssetKind.Portrait));
        }

        [Test]
        public void ItemProgress_StartsJobAndSetsPercent()
        {
            var state = new ArtDriverState();
            state.Apply(new ArtEvent { kind = ArtEvent.ItemAdded, id = "p_Roles_Priest_c", pool = "p_Roles_Priest" });

            state.Apply(new ArtEvent { kind = ArtEvent.ItemProgress, id = "p_Roles_Priest_c", percent = 55 });

            Assert.That(state.Jobs.Running, Is.Not.Null);
            Assert.That(state.Jobs.Running.Percent, Is.EqualTo(55));
        }

        [Test]
        public void ItemDone_FinishesJobWithPath()
        {
            var state = new ArtDriverState();
            state.Apply(new ArtEvent { kind = ArtEvent.ItemAdded, id = "a", pool = "p_X_a" });
            state.Apply(new ArtEvent { kind = ArtEvent.ItemProgress, id = "a", percent = 40 });

            state.Apply(new ArtEvent { kind = ArtEvent.ItemDone, id = "a", path = "review/x.png" });

            Assert.That(state.Jobs.Jobs[0].State, Is.EqualTo(JobState.Done));
        }

        [Test]
        public void ItemFailed_FinishesJobAndRecordsReason()
        {
            var state = new ArtDriverState();
            state.Apply(new ArtEvent { kind = ArtEvent.ItemAdded, id = "a", pool = "p_X_a" });

            state.Apply(new ArtEvent { kind = ArtEvent.ItemFailed, id = "a", text = "углы не прозрачны" });

            Assert.That(state.Jobs.Failed, Is.EqualTo(1));
            Assert.That(state.LastError, Does.Contain("углы не прозрачны"));
        }

        [Test]
        public void ReviewCandidate_FlagsReviewForRescan()
        {
            var state = new ArtDriverState();

            state.Apply(new ArtEvent
            {
                kind = ArtEvent.ReviewCandidate,
                id = "p_Roles_Priest_c",
                path = "ArtStudio/review/Roles_Priest/p_Roles_Priest_c_ink.png",
                percent = 52
            });

            Assert.That(state.AcknowledgeReviewChanged(), Is.True,
                "список кандидатов окно читает с диска — событие только повод пересканировать");
            Assert.That(state.AcknowledgeReviewChanged(), Is.False,
                "флаг читается один раз, иначе окно сканировало бы папку каждый опрос");
        }

        [Test]
        public void Approved_ReportsPathAndFlagsReviewRescan()
        {
            var state = new ArtDriverState();

            state.Apply(new ArtEvent
            {
                kind = ArtEvent.Approved,
                path = "Assets/Woodberry/Art/Dialogue/Portraits/Roles/Priest/Priest_03_Ink.png"
            });

            Assert.That(state.TakeApprovedPaths(),
                Is.EquivalentTo(new[]
                {
                    "Assets/Woodberry/Art/Dialogue/Portraits/Roles/Priest/Priest_03_Ink.png"
                }),
                "путь нужен окну, чтобы отметить перенесённый кадр принятым");
            Assert.That(state.TakeApprovedPaths(), Is.Empty, "читается один раз");
            Assert.That(state.AcknowledgeReviewChanged(), Is.True,
                "перенос в проект меняет и папку ревью: кандидат из неё ушёл");
        }

        [Test]
        public void MarkDriverDisconnected_AfterReady_ClearsConnectionAndMismatch()
        {
            var state = new ArtDriverState();
            state.Apply(new ArtEvent { kind = ArtEvent.Ready, protocol = ArtProtocol.Version + 1 });
            Assert.That(state.ProtocolMismatch, Is.True, "предусловие: расхождение версий видно");

            state.MarkDriverDisconnected();

            Assert.That(state.DriverConnected, Is.False,
                "мёртвый драйвер не может считаться подключённым");
            Assert.That(state.ProtocolMismatch, Is.False,
                "расхождение версий у мёртвого драйвера — вечный баннер в строке проблем");
        }

        [Test]
        public void CommandRejected_RecordsErrorForTheUser()
        {
            var state = new ArtDriverState();

            state.Apply(new ArtEvent
            {
                kind = ArtEvent.CommandRejected,
                text = "в пуле p_Roles_Priest нет свободных кадров"
            });

            Assert.That(state.LastError, Does.Contain("нет свободных кадров"));
        }
    }
}
