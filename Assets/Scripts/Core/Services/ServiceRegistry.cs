using System;
using System.Collections.Generic;
using UnityEngine;

namespace Woodberry.Core
{
    /// <summary>
    /// Единственный канал доступа к сервисам, которые переживают смену сцены.
    ///
    /// Нужен потому, что Unity молча отбрасывает сериализованные ссылки на
    /// объекты другой сцены (в YAML остаётся <c>{fileID: 0}</c>), а
    /// <c>FindObjectOfType</c> в геймплее запрещён. Оба пути нарушали бы
    /// правило явной композиции, поэтому глобальные сервисы регистрируются
    /// явно.
    ///
    /// Правила:
    /// 1. Регистрирует только composition root (<c>GameBootstrap</c>).
    /// 2. Читают сцены, один раз, обычно в <c>Start</c>.
    /// 3. <c>Initialize</c> остаётся основным путём подстановки — реестр
    ///    нужен только для кросс-сценовых глобальных сервисов.
    /// 4. **Всегда указывай тип явно: <c>TryGet&lt;IInputReader&gt;(out _input)</c>.**
    ///    Без явного аргумента тип выводится из переменной, и
    ///    <c>TryGet(out _input)</c> с полем типа <c>InputSystemReader</c> ищет
    ///    по ключу реализации, тогда как сервис зарегистрирован по ключу
    ///    интерфейса. Ошибка не бросается — она тихо возвращает false.
    /// </summary>
    public static class ServiceRegistry
    {
        private static readonly Dictionary<Type, object> Services = new Dictionary<Type, object>();

        /// <summary>Регистрирует сервис. Повторная регистрация типа перезаписывает.</summary>
        public static void Register<T>(T service) where T : class
        {
            if (service == null)
            {
                throw new ArgumentNullException(nameof(service));
            }

            Services[typeof(T)] = service;
        }

        /// <summary>Возвращает сервис, если он зарегистрирован.</summary>
        public static bool TryGet<T>(out T service) where T : class
        {
            if (Services.TryGetValue(typeof(T), out object found) && found is T typed)
            {
                service = typed;
                return true;
            }

            service = null;
            return false;
        }

        /// <summary>
        /// Убирает сервис по типу. Вызывать должен только тот, кто регистрировал.
        ///
        /// Сделано без проверки экземпляра сознательно: вариант
        /// <c>Unregister(instance)</c> выводил бы <c>T</c> из статического типа
        /// аргумента, и <c>Unregister(reader)</c> искал бы ключ реализации вместо
        /// ключа интерфейса — молча ничего не снимая. Ловушка в выводе типа
        /// дороже защиты, от которой убирает запрет второго composition root.
        /// </summary>
        public static void Unregister<T>() where T : class
        {
            Services.Remove(typeof(T));
        }

        /// <summary>Очищает реестр целиком. Только для тестов и сброса домена.</summary>
        public static void Clear()
        {
            Services.Clear();
        }

        /// <summary>Сколько сервисов зарегистрировано. Для тестов и диагностики.</summary>
        public static int Count => Services.Count;

        // Без этого флага состояние статического словаря протекло бы через
        // перезапуск Play Mode в редакторе и падало бы на «сервис из прошлой сессии».
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnLoad()
        {
            Services.Clear();
        }
    }
}
