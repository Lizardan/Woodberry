using UnityEngine;
using Woodberry.Core.Input;

namespace Woodberry.Core
{
    /// <summary>
    /// Composition root: единственное место, где собираются зависимости сцены.
    /// Gameplay получает сервисы отсюда через <c>Initialize</c> и не ищет их
    /// сам — это держит правило «никаких FindObjectOfType в геймплее» проверяемым.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class GameBootstrap : MonoBehaviour
    {
        private InputSystemReader _reader;
        private bool _inputEnabled;

        /// <summary>Сервис ввода, готовый к выдаче геймплею.</summary>
        public IInputReader Input => _reader;

        /// <summary>true, если ввод включён.</summary>
        public bool InputEnabled => _inputEnabled;

        private void Awake()
        {
            // Сгенерированная обёртка самодостаточна: в неё встроен JSON карт,
            // поэтому ассет в сцене назначать не нужно.
            _reader = new InputSystemReader(new InputSystem_Actions());
        }

        private void OnEnable()
        {
            EnableInput();
        }

        private void OnDisable()
        {
            DisableInput();
        }

        private void OnDestroy()
        {
            DisableInput();
        }

        private void EnableInput()
        {
            if (_reader == null || _inputEnabled)
            {
                return;
            }

            _reader.Enable();
            _inputEnabled = true;
        }

        private void DisableInput()
        {
            if (_reader == null || !_inputEnabled)
            {
                return;
            }

            _reader.Disable();
            _inputEnabled = false;
        }
    }
}
