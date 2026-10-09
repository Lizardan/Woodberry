using NUnit.Framework;
using UnityEngine;
using Woodberry.Editor.ArtStudio;

namespace Woodberry.Tests.EditMode
{
    /// <summary>
    /// Арифметика раскладки: ширина правого блока и размер кадра обязаны
    /// сходиться при любом размере окна.
    ///
    /// Тест без окна и без UI Toolkit: проверяется правило «блок равен кадру
    /// плюс отступы», а не то, как это отрисовалось. Отступы передаются числом
    /// (13 = 6+6 падинга плюс рамка блока в USS), потому что это вход функции,
    /// а не её решение.
    /// </summary>
    public sealed class PreviewFitTests
    {
        private const float Chrome = 13f;
        private const float Room = 900f;

        private const float PortraitWidth = 480f;
        private const float PortraitHeight = 720f;
        private const float BackgroundWidth = 856f;
        private const float BackgroundHeight = 480f;

        private static readonly float[] BoxHeights = { 220f, 260f, 420f, 608f, 925f, 1200f, 1700f };

        private static PreviewFit.Result Fit(float sourceWidth, float sourceHeight, float boxHeight)
        {
            return Fit(sourceWidth, sourceHeight, boxHeight, Room);
        }

        private static PreviewFit.Result Fit(float sourceWidth, float sourceHeight, float boxHeight, float room)
        {
            return PreviewFit.Compute(
                sourceWidth,
                sourceHeight,
                boxHeight,
                room,
                Chrome,
                PreviewFit.MinPanelWidth,
                PreviewFit.MaxPanelWidth);
        }

        [Test]
        public void PanelWidth_WhenPortrait_ThenEqualsPreviewWidthPlusChrome()
        {
            foreach (float boxHeight in BoxHeights)
            {
                PreviewFit.Result fit = Fit(PortraitWidth, PortraitHeight, boxHeight);

                Assert.That(fit.PreviewWidth + Chrome, Is.EqualTo(fit.PanelWidth).Within(0.001f),
                    $"высота {boxHeight}: блок обязан быть ровно кадром плюс отступы");
            }
        }

        [Test]
        public void PanelWidth_WhenBackground_ThenEqualsPreviewWidthPlusChrome()
        {
            foreach (float boxHeight in BoxHeights)
            {
                PreviewFit.Result fit = Fit(BackgroundWidth, BackgroundHeight, boxHeight);

                Assert.That(fit.PreviewWidth + Chrome, Is.EqualTo(fit.PanelWidth).Within(0.001f),
                    $"высота {boxHeight}: у фона пропорция другая, правило то же");
            }
        }

        [Test]
        public void Preview_WhenPortrait_ThenKeepsAspect()
        {
            float aspect = PortraitWidth / PortraitHeight;

            foreach (float boxHeight in BoxHeights)
            {
                PreviewFit.Result fit = Fit(PortraitWidth, PortraitHeight, boxHeight);

                Assert.That(fit.PreviewWidth / fit.PreviewHeight, Is.EqualTo(aspect).Within(0.01f),
                    $"высота {boxHeight}: пропорция держится с точностью до половины пикселя");
            }
        }

        [Test]
        public void PreviewWidth_WhenBoxGrowsTaller_ThenNeverShrinks()
        {
            float previous = 0f;

            foreach (float boxHeight in BoxHeights)
            {
                PreviewFit.Result fit = Fit(PortraitWidth, PortraitHeight, boxHeight);

                Assert.That(fit.PreviewWidth, Is.GreaterThanOrEqualTo(previous),
                    $"высота {boxHeight}: выше окно — не уже кадр");
                previous = fit.PreviewWidth;
            }
        }

        [Test]
        public void PreviewWidth_WhenRoomIsNarrow_ThenFitsRoom()
        {
            // Узкое окно: блоку некуда расти, кадр занимает всё, что дали.
            PreviewFit.Result fit = Fit(BackgroundWidth, BackgroundHeight, 900f, 300f);

            Assert.That(fit.PanelWidth, Is.LessThanOrEqualTo(300f));
            Assert.That(fit.PreviewWidth, Is.LessThanOrEqualTo(300f - Chrome + 0.001f),
                "кадр не должен вылезать из блока");
            Assert.That(fit.PanelWidth, Is.EqualTo(fit.PreviewWidth + Chrome).Within(0.001f));
        }

        [Test]
        public void PanelWidth_WhenVeryTallBox_ThenCappedAtMax()
        {
            PreviewFit.Result fit = Fit(PortraitWidth, PortraitHeight, 2000f);

            Assert.That(fit.PanelWidth, Is.EqualTo(PreviewFit.MaxPanelWidth).Within(0.001f),
                "потолок держит блок в разумных берегах");
            Assert.That(fit.PreviewWidth, Is.EqualTo(PreviewFit.MaxPanelWidth - Chrome).Within(0.001f),
                "и кадр занимает блок целиком, а не половину");
        }

        [Test]
        public void PanelWidth_WhenVeryShortBox_ThenFlooredAtMin()
        {
            PreviewFit.Result fit = Fit(PortraitWidth, PortraitHeight, 220f);

            Assert.That(fit.PanelWidth, Is.EqualTo(PreviewFit.MinPanelWidth).Within(0.001f),
                "пол держит кнопки читаемыми");
            Assert.That(fit.PreviewWidth, Is.EqualTo(PreviewFit.MinPanelWidth - Chrome).Within(0.001f));
        }

        [Test]
        public void PreviewHeight_WhenBoxIsBelowFloorHeight_ThenFitsBoxHeight()
        {
            // Низкое окно: пол ширины уже не помещается по высоте, и кадр
            // упирается в высоту — иначе он залез бы на кнопки.
            PreviewFit.Result fit = Fit(PortraitWidth, PortraitHeight, 100f);

            Assert.That(fit.PreviewHeight, Is.LessThanOrEqualTo(100f));
            Assert.That(fit.PanelWidth, Is.EqualTo(fit.PreviewWidth + Chrome).Within(0.001f),
                "и здесь блок остаётся ровно кадром плюс отступы");
            Assert.That(fit.IsValid, Is.True);
        }

        [Test]
        public void PanelWidth_WhenBoxIs608_ThenMatchesKnownSize()
        {
            // Размер, снятый с живого окна студии: блок 418, кадр 405x608.
            PreviewFit.Result fit = Fit(PortraitWidth, PortraitHeight, 608f);

            Assert.That(fit.PanelWidth, Is.EqualTo(418f).Within(0.001f));
            Assert.That(fit.PreviewWidth, Is.EqualTo(405f).Within(0.001f));
            Assert.That(fit.PreviewHeight, Is.EqualTo(608f).Within(0.001f));
        }

        [Test]
        public void Compute_WhenLayoutMissing_ThenInvalid()
        {
            PreviewFit.Result fit = Fit(PortraitWidth, PortraitHeight, 0f);

            Assert.That(fit.IsValid, Is.False, "без раскладки размер не считается");
        }

        [Test]
        public void Compute_WhenSourceUnknown_ThenInvalid()
        {
            PreviewFit.Result fit = Fit(0f, 0f, 600f);

            Assert.That(fit.IsValid, Is.False, "пропорция неизвестна — рисовать нечего");
        }

        [Test]
        public void Compute_WhenRoomUnknown_ThenUsesHeightOnly()
        {
            PreviewFit.Result withRoom = Fit(PortraitWidth, PortraitHeight, 608f, Room);
            PreviewFit.Result withoutRoom = Fit(PortraitWidth, PortraitHeight, 608f, 0f);

            Assert.That(withoutRoom.PanelWidth, Is.EqualTo(withRoom.PanelWidth).Within(0.001f),
                "неизвестное место не должно сужать блок");
        }
    }
}
