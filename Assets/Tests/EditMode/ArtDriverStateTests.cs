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
        public void ItemProgress_WithText_RecordsPhaseOnJob()
        {
            var state = new ArtDriverState();
            state.Apply(new ArtEvent { kind = ArtEvent.ItemAdded, id = "p_X_c", pool = "p_X" });

            state.Apply(new ArtEvent
            {
                kind = ArtEvent.ItemProgress,
                id = "p_X_c",
                percent = 40,
                text = "шаг 4/10"
            });

            Assert.That(state.Jobs.Running.ProgressText, Is.EqualTo("шаг 4/10"),
                "без подписи шага человек видит только процент, а не что происходит");
        }

        [Test]
        public void ItemProgress_WithoutText_KeepsPreviousPhase()
        {
            var state = new ArtDriverState();
            state.Apply(new ArtEvent { kind = ArtEvent.ItemAdded, id = "p_X_c", pool = "p_X" });
            state.Apply(new ArtEvent { kind = ArtEvent.ItemProgress, id = "p_X_c", percent = 30, text = "шаг 3/10" });

            // События границ кадра приходят без text; они не должны стирать фазу.
            state.Apply(new ArtEvent { kind = ArtEvent.ItemProgress, id = "p_X_c", percent = 31 });

            Assert.That(state.Jobs.Running.ProgressText, Is.EqualTo("шаг 3/10"));
        }

        [Test]
        public void ItemDone_ClearsProgressPhase()
        {
            var state = new ArtDriverState();
            state.Apply(new ArtEvent { kind = ArtEvent.ItemAdded, id = "a", pool = "p_X_a" });
            state.Apply(new ArtEvent { kind = ArtEvent.ItemProgress, id = "a", percent = 90, text = "шаг 9/10" });

            state.Apply(new ArtEvent { kind = ArtEvent.ItemDone, id = "a", path = "review/x.png" });

            Assert.That(state.Jobs.Jobs[0].ProgressText, Is.Null,
                "фаза готового кадра — прошлый шаг, её показывать нельзя");
        }

        [Test]
        public void DriverInfo_RecordsStandSummary()
        {
            var state = new ArtDriverState();

            state.Apply(new ArtEvent
            {
                kind = ArtEvent.DriverInfo,
                text = "Модель: REDZ-v1.5-Q8_0.gguf · Сэмплер: euler / simple, 10 шагов"
            });

            Assert.That(state.StandInfo, Does.Contain("REDZ-v1.5-Q8_0"),
                "окно показывает, ЧЕМ рисуется кадр, иначе это приходится искать в логе стенда");
        }

        [Test]
        public void DriverInfo_WhenEmpty_KeepsPreviousSummary()
        {
            var state = new ArtDriverState();
            state.Apply(new ArtEvent { kind = ArtEvent.DriverInfo, text = "Модель: X" });

            state.Apply(new ArtEvent { kind = ArtEvent.DriverInfo, text = "" });

            Assert.That(state.StandInfo, Is.EqualTo("Модель: X"));
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

        /// <summary>
        /// Регрессия на главный баг арт-студии: полоса текущего кадра
        /// заполнялась один раз и дальше висела на 100 %, потому что текущим
        /// считался первый незанятый кадр, а `item_done` приходит только в
        /// конце всего прогона. На четырёх кадрах человек обязан увидеть ЧЕТЫРЕ
        /// прохода полосы от 0 до 100 — по одному на кадр.
        /// </summary>
        [Test]
        public void FourFrames_FrameBarPassesFromZeroToHundredOncePerFrame()
        {
            var state = new ArtDriverState();
            var ids = new[] { "p_Pool_a", "p_Pool_b", "p_Pool_c", "p_Pool_d" };

            foreach (string id in ids)
            {
                state.Apply(new ArtEvent
                {
                    kind = ArtEvent.ItemAdded, id = id, pool = "p_Pool"
                });
            }

            int framesCompleted = 0;

            // Прогон как его ведёт стенд: `item_done` по всем кадрам приходит
            // в самом конце, после вырезки и проверки всего прогона.
            foreach (string id in ids)
            {
                state.Apply(new ArtEvent
                {
                    kind = ArtEvent.ItemProgress, id = id, percent = 1,
                    stage = "prepare"
                });

                Assert.That(state.Jobs.Running.StandId, Is.EqualTo(id),
                    "текущим обязан быть кадр, за которым стенд реально рисует");

                state.Apply(new ArtEvent
                {
                    kind = ArtEvent.ItemProgress, id = id, percent = 99,
                    stage = "sample"
                });

                Assert.That(GenerationProgress.FramePercent(state.Jobs.Running, 0),
                    Is.EqualTo(100), $"кадр {id} обязан доходить до 100 %");

                state.Apply(new ArtEvent { kind = ArtEvent.ItemDrawn, id = id });
                framesCompleted++;
            }

            Assert.That(framesCompleted, Is.EqualTo(4));

            // Приёмка приходит в конце — и только тогда заявка закрывается.
            foreach (string id in ids)
            {
                state.Apply(new ArtEvent { kind = ArtEvent.ItemDone, id = id });
            }

            Assert.That(state.Jobs.Failed, Is.EqualTo(0));
            Assert.That(GenerationProgress.Overall(state.Jobs, null, 0),
                Is.EqualTo(100));
        }

        [Test]
        public void Progress_WhenNextFrameStarts_DoesNotInheritPreviousPercent()
        {
            // Если новый кадр унаследует проценты предыдущего, общая полоса
            // стартует не с нуля, а человек видит «кадр почти готов» до того,
            // как стенд к нему приступил.
            var state = new ArtDriverState();
            state.Apply(new ArtEvent
            {
                kind = ArtEvent.ItemAdded, id = "p_Pool_a", pool = "p_Pool"
            });
            state.Apply(new ArtEvent
            {
                kind = ArtEvent.ItemAdded, id = "p_Pool_b", pool = "p_Pool"
            });
            state.Apply(new ArtEvent
            {
                kind = ArtEvent.ItemProgress, id = "p_Pool_a", percent = 99,
                stage = "sample"
            });
            state.Apply(new ArtEvent
            {
                kind = ArtEvent.ItemProgress, id = "p_Pool_b", percent = 1,
                stage = "prepare"
            });

            GenerationJob current = state.Jobs.Running;
            Assert.That(current.StandId, Is.EqualTo("p_Pool_b"));
            Assert.That(GenerationProgress.FramePercent(current, 0),
                Is.EqualTo(0), "новый кадр начинается с нуля");
            Assert.That(GenerationProgress.Overall(state.Jobs, current, 0),
                Is.LessThan(30), "общая полоса не имеет права быть почти полной");
        }

        [Test]
        public void DriverInfo_WhenDriverSendsList_KeepsOneItemPerLine()
        {
            // Панель «Чем рисуется» показывает сводку по строке на пункт.
            var state = new ArtDriverState();

            state.Apply(new ArtEvent
            {
                kind = ArtEvent.DriverInfo,
                text = "Модель: Z-Image · LoRA: нет",
                detail = "[\"Модель: Z-Image\",\"LoRA: нет\"]",
            });

            Assert.That(state.StandInfoLines,
                Is.EqualTo(new[] { "Модель: Z-Image", "LoRA: нет" }));
        }

        [Test]
        public void DriverInfo_WhenOldDriverSendsOnlyStickyText_SplitsIt()
        {
            // Старый драйвер присылает одну склейку. Резать надо, иначе пять
            // описаний в одной строке не прочитать.
            var state = new ArtDriverState();

            state.Apply(new ArtEvent
            {
                kind = ArtEvent.DriverInfo,
                text = "Модель: Z-Image · LoRA: нет · Энкодер: Qwen",
            });

            Assert.That(state.StandInfoLines.Length, Is.EqualTo(3));
            Assert.That(state.StandInfoLines[2], Is.EqualTo("Энкодер: Qwen"));
        }

        [Test]
        public void DriverInfo_WhenNothingCame_YieldsEmptyList()
        {
            var state = new ArtDriverState();

            state.Apply(new ArtEvent { kind = ArtEvent.DriverInfo });

            Assert.That(state.StandInfoLines, Is.Empty);
        }

        [Test]
        public void ItemStage_KeepsCounterAndSurvivesIntoWindow()
        {
            var state = new ArtDriverState();

            state.Apply(new ArtEvent
            {
                kind = ArtEvent.ItemStage, stage = "cut", done = 37, total = 118,
                text = "p_Pool_c.png"
            });

            Assert.That(state.StandStage, Is.EqualTo("cut"));
            Assert.That(state.StandStageDone, Is.EqualTo(37));
            Assert.That(state.StandStageTotal, Is.EqualTo(118));
            Assert.That(GenerationProgress.StagePercent(
                state.StandStage, state.StandStageDone, state.StandStageTotal, 0),
                Is.EqualTo(31));
        }

        [Test]
        public void ResetStandStage_ClearsLeftoverFromPreviousRun()
        {
            var state = new ArtDriverState();
            state.Apply(new ArtEvent
            {
                kind = ArtEvent.ItemStage, stage = "cut", done = 118, total = 118
            });

            state.ResetStandStage();

            Assert.That(state.StandStage, Is.Null);
            Assert.That(state.StandStageTotal, Is.EqualTo(0));
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
