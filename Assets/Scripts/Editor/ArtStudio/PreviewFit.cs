using UnityEngine;

namespace Woodberry.Editor.ArtStudio
{
    /// <summary>
    /// Размер кадра и ширина правого блока — одна арифметика на двоих.
    ///
    /// Правило: <b>блок равен кадру плюс свои отступы и рамку, ровно, без
    /// запаса</b>. Ширину кадра задаёт высота (<c>высота × пропорция</c>):
    /// кадр держит пропорцию, значит его ширина — функция высоты, а не ширины
    /// окна. Ширина и высота кадра — целые пиксели, пропорция держится с
    /// точностью до половины пикселя высоты.
    ///
    /// Почему отдельной чистой функцией и почему входов ровно два. Раньше блок
    /// и кадр считались двумя проходами, причём ширина блока выводилась из
    /// ширины места под кадр — а это место задаёт сам блок. Получалась петля:
    /// блок «залипал» на той ширине, с которой начал, и на высокой панели
    /// оставался узким, а кадр в нём — маленьким. Здесь входы от результата не
    /// зависят: высота места под кадр и ширина, которую окно реально может дать
    /// блоку (окно минус левая колонка и минимум центральной).
    ///
    /// Пол и потолок ширины блока пересчитаны в пол и потолок ширины кадра,
    /// поэтому кадр заполняет содержимое блока при любом размере окна.
    /// Исключение одно: если место ниже пола по высоте (низкое окно), кадр
    /// упирается в высоту, и блок сужается вместе с ним. В окне студии этого
    /// не случается — `.preview-holder` держит `min-height: 220px`, а этого
    /// хватает кадру-портрету при полу ширины 160.
    /// </summary>
    public static class PreviewFit
    {
        /// <summary>
        /// Пол ширины блока: ниже кнопки перестают читаться. Он же в USS
        /// (<c>.panel-right</c>), чтобы первый кадр окна не был уже.
        /// </summary>
        public const float MinPanelWidth = 160f;

        /// <summary>Потолок ширины блока: на очень высоком окне.</summary>
        public const float MaxPanelWidth = 760f;

        /// <summary>
        /// Результат раскладки. <see cref="IsValid"/> = <c>false</c> — считать
        /// было нечего (раскладки ещё нет или неизвестна пропорция):
        /// вызывающий оставляет прежний размер кадра, а не ставит нулевой.
        /// </summary>
        public sealed class Result
        {
            public float PanelWidth { get; set; }

            public float PreviewWidth { get; set; }

            public float PreviewHeight { get; set; }

            public bool IsValid => PreviewWidth > 1f && PreviewHeight > 1f;
        }

        /// <param name="sourceWidth">Ширина кадра-источника (480 у портрета, 856 у фона).</param>
        /// <param name="sourceHeight">Высота кадра-источника.</param>
        /// <param name="boxHeight">Высота места, отведённого кадру сейчас.</param>
        /// <param name="availableWidth">
        /// Сколько ширины окно может отдать блоку. <c>0</c> — неизвестно:
        /// тогда ширину задаёт только высота.
        /// </param>
        /// <param name="chrome">Отступы и рамка блока: блок шире кадра ровно на них.</param>
        /// <param name="minPanelWidth">Пол ширины блока.</param>
        /// <param name="maxPanelWidth">Потолок ширины блока.</param>
        public static Result Compute(
            float sourceWidth,
            float sourceHeight,
            float boxHeight,
            float availableWidth,
            float chrome,
            float minPanelWidth,
            float maxPanelWidth)
        {
            var result = new Result();

            if (sourceWidth <= 0f || sourceHeight <= 0f || boxHeight <= 1f)
            {
                return result;
            }

            float aspect = sourceWidth / sourceHeight;

            float minFrame = Mathf.Max(1f, minPanelWidth - chrome);
            float maxFrame = Mathf.Max(minFrame, maxPanelWidth - chrome);

            float frameWidth = Mathf.Floor(boxHeight * aspect);
            frameWidth = Mathf.Clamp(frameWidth, minFrame, maxFrame);

            // Узкое окно может дать блоку меньше, чем он просит: тогда блок
            // занимает всё, что есть, и кадр всё равно заполняет его целиком.
            if (availableWidth > 1f)
            {
                float roomFrame = Mathf.Max(minFrame, Mathf.Floor(availableWidth) - chrome);
                frameWidth = Mathf.Min(frameWidth, roomFrame);
            }

            float frameHeight = Mathf.Max(1f, Mathf.Round(frameWidth / aspect));

            if (frameHeight > boxHeight)
            {
                // Место ниже пола по высоте: кадр упирается в высоту, и блок
                // сужается вместе с ним — кадр важнее читаемости кнопок.
                frameHeight = Mathf.Max(1f, Mathf.Floor(boxHeight));
                frameWidth = Mathf.Max(1f, Mathf.Floor(frameHeight * aspect));
            }

            result.PreviewWidth = frameWidth;
            result.PreviewHeight = frameHeight;
            result.PanelWidth = frameWidth + chrome;
            return result;
        }
    }
}
