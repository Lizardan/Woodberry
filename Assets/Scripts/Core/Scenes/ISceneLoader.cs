namespace Woodberry.Core.Scenes
{
    /// <summary>
    /// Уход в другую сцену — намерение, а не прямой вызов <c>SceneManager</c>.
    /// UI и геймплей знают только об этом интерфейсе.
    /// </summary>
    public interface ISceneLoader
    {
        /// <summary>
        /// Загружает сцену, выгружая текущую. <c>Single</c> —
        /// сознательный выбор: игрок не должен видеть меню и ходить по нему.
        /// </summary>
        void Load(SceneId scene);

        /// <summary>Сцена, которая сейчас активна. Не меняется, если загрузка не удалась.</summary>
        SceneId Current { get; }
    }
}
