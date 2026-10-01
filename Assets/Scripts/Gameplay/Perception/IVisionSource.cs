using System.Collections.Generic;

namespace Woodberry.Gameplay.Perception
{
    /// <summary>
    /// Всё, что даёт обзор: сейчас это игрок вокруг себя, в будущем — фонарь,
    /// враг, второй игрок в кооперативе.
    ///
    /// Источник не знает, как обзор рисуется. Он только отдаёт конусы;
    /// <see cref="VisibilityOverlay"/> собирает их в один полигон и заливает
    /// маску. Благодаря этому новый источник обзора не требует правок
    /// в отрисовке.
    /// </summary>
    public interface IVisionSource
    {
        /// <summary>
        /// Добавляет свои конусы в общий список. Вызывается каждый кадр,
        /// поэтому реализация не должна аллоцировать.
        /// </summary>
        void CollectCones(List<VisionCone> sink);
    }
}
