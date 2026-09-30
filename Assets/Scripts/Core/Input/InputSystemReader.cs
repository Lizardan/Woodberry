using UnityEngine;
using UnityEngine.InputSystem;

namespace Woodberry.Core.Input
{
    /// <summary>
    /// Реализация <see cref="IInputReader"/> поверх сгенерированной обёртки
    /// Input System. Экземпляр создаётся composition root'ом и передаётся
    /// геймплею через <c>Initialize</c> — геймплей не ищет его сам.
    /// </summary>
    public sealed class InputSystemReader : IInputReader
    {
        private readonly InputSystem_Actions.PlayerActions _player;
        private bool _enabled;

        public InputSystemReader(InputSystem_Actions actions)
        {
            _player = actions.Player;
        }

        public Vector2 Move => _player.Move.ReadValue<Vector2>();

        public bool SprintHeld => _player.Sprint.IsPressed();

        public bool InteractPressed => _player.Interact.WasPressedThisFrame();

        /// <summary>Включает карту Player. Парные вызовы обязательны.</summary>
        public void Enable()
        {
            if (_enabled)
            {
                return;
            }

            _player.Enable();
            _enabled = true;
        }

        public void Disable()
        {
            if (!_enabled)
            {
                return;
            }

            _player.Disable();
            _enabled = false;
        }
    }
}
