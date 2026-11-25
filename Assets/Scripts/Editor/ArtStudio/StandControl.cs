namespace Woodberry.Editor.ArtStudio
{
    /// <summary>Что сейчас делает кнопка стенда.</summary>
    public enum StandPhase
    {
        /// <summary>Ничего не поднято. Нажатие — поднять.</summary>
        Down,

        /// <summary>Поднимаем драйвер и ComfyUI. Кнопка погашена.</summary>
        Starting,

        /// <summary>Драйвер подключён, ComfyUI готов. Нажатие — погасить всё.</summary>
        Up,

        /// <summary>Гасим. Кнопка погашена.</summary>
        Stopping
    }

    /// <summary>
    /// Автомат одной кнопки «Стенд».
    ///
    /// ПОЧЕМУ ОДНА КНОПКА. Драйвер и ComfyUI — не два независимых решения:
    /// драйвер без сервера не генерирует, сервер без драйвера никто не
    /// спрашивает. Две кнопки заставляли держать в голове порядок нажатий и
    /// допускали половинчатое состояние («драйвер поднят, сервер забыт»).
    /// Одна кнопка знает порядок сама.
    ///
    /// ПОЧЕМУ ОТДЕЛЬНЫМ КЛАССОМ. Это переходы состояния, а не отрисовка: их
    /// обязанность — не пустить в работу половинчатый стенд и не оставить
    /// процессов после остановки. Проверяется EditMode-тестом, потому что
    /// проверить это живьём (а особенно таймаут) дорого и ненадёжно.
    ///
    /// Переход «поднимается» держится не до бесконечности: у него есть срок
    /// (<see cref="StartTimeoutSeconds"/>). Иначе зависший на старте стенд
    /// навсегда оставлял бы кнопку погашенной, и выйти из этого состояния
    /// можно было бы только перезапуском редактора.
    /// </summary>
    public static class StandControl
    {
        /// <summary>
        /// Сколько ждать подъём стенда. ComfyUI поднимает порт за 20–40 с, но
        /// на первом запуске читает 10 ГБ весов; 180 с — с запасом на медленный
        /// диск, и всё ещё заметно меньше, чем «кажется, повисло».
        /// </summary>
        public const double StartTimeoutSeconds = 180.0;

        /// <summary>
        /// Сколько ждать остановку. Драйвер гасит ComfyUI деревом процессов и
        /// отвечает за секунды; запас — на `taskkill` по живому GPU-процессу.
        /// </summary>
        public const double StopTimeoutSeconds = 20.0;

        /// <summary>
        /// Следующее состояние. <paramref name="now"/> и
        /// <paramref name="deadline"/> — время редактора (секунды) и срок
        /// перехода; срок задаётся при нажатии через <see cref="Deadline"/>.
        /// </summary>
        public static StandPhase Resolve(
            StandPhase phase,
            bool driverAlive,
            bool serverReady,
            double now,
            double deadline)
        {
            bool expired = deadline > 0 && now >= deadline;

            switch (phase)
            {
                case StandPhase.Starting:
                    if (driverAlive && serverReady)
                    {
                        return StandPhase.Up;
                    }

                    return expired ? StandPhase.Down : StandPhase.Starting;

                case StandPhase.Stopping:
                    if (!driverAlive && !serverReady)
                    {
                        return StandPhase.Down;
                    }

                    return expired ? StandPhase.Down : StandPhase.Stopping;

                case StandPhase.Up:
                    // Стенд мог умереть сам (крэш ComfyUI, закрытый драйвер):
                    // «работает» на мёртвом стенде — та же ложь, что и
                    // «подключён» при мёртвом драйвере.
                    return driverAlive && serverReady ? StandPhase.Up : StandPhase.Down;

                default:
                    return driverAlive && serverReady ? StandPhase.Up : StandPhase.Down;
            }
        }

        /// <summary>Срок перехода, который ставится при нажатии. 0 — бессрочно.</summary>
        public static double Deadline(StandPhase phase, double now)
        {
            switch (phase)
            {
                case StandPhase.Starting:
                    return now + StartTimeoutSeconds;
                case StandPhase.Stopping:
                    return now + StopTimeoutSeconds;
                default:
                    return 0;
            }
        }

        /// <summary>Можно ли нажимать. Пока идёт переход — нет: кнопка серая.</summary>
        public static bool CanPress(StandPhase phase)
        {
            return phase == StandPhase.Down || phase == StandPhase.Up;
        }

        /// <summary>
        /// Надпись на кнопке. Во время перехода она называет, ЧТО именно
        /// происходит: «поднимается» без подробностей человек читает как
        /// «зависло», а драйвер и ComfyUI поднимаются заметно по-разному —
        /// драйвер за секунды, сервер за десятки.
        /// </summary>
        public static string Label(StandPhase phase, bool driverAlive, string serverState)
        {
            switch (phase)
            {
                case StandPhase.Starting:
                    return driverAlive
                        ? "Поднимается: сервер…"
                        : "Поднимается: драйвер…";
                case StandPhase.Up:
                    return "Остановить стенд";
                case StandPhase.Stopping:
                    return "Останавливается…";
                default:
                    return driverAlive
                        ? "Поднять ComfyUI"
                        : "Поднять стенд";
            }
        }

        /// <summary>
        /// Что сказать, если переход не уложился в срок. Возвращает пустую
        /// строку, пока всё идёт нормально.
        /// </summary>
        public static string TimeoutProblem(StandPhase phase, double now, double deadline)
        {
            if (deadline <= 0 || now < deadline)
            {
                return string.Empty;
            }

            switch (phase)
            {
                case StandPhase.Starting:
                    return "стенд не поднялся за отведённое время — смотри ArtStudio/logs/comfyui.log";
                case StandPhase.Stopping:
                    return "стенд не погас за отведённое время — проверь лишние python.exe";
                default:
                    return string.Empty;
            }
        }
    }
}
