using UnityEngine;

namespace Woodberry.Core.Input
{
    /// <summary>
    /// Единственная точка доступа геймплея к вводу.
    /// Реализация скрывает Input System, чтобы геймплей не зависел от пакета
    /// и не знал строковых имён экшенов.
    /// </summary>
    public interface IInputReader
    {
        /// <summary>Вектор движения в плоскости XZ. Уже нормализован по длине.</summary>
        Vector2 Move { get; }

        /// <summary>true пока удерживается спринт.</summary>
        bool SprintHeld { get; }

        /// <summary>true в кадре нажатия интеракта.</summary>
        bool InteractPressed { get; }
    }
}
