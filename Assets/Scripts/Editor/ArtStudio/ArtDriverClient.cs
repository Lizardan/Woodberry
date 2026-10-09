using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEngine;

namespace Woodberry.Editor.ArtStudio
{
    /// <summary>
    /// Связь окна с драйвером: команды в <c>commands.jsonl</c>, события из
    /// <c>events.jsonl</c>, запуск и остановка процесса драйвера.
    ///
    /// Здесь только файлы и процессы. Все переходы состояния — в
    /// <see cref="ArtDriverState"/> и проверяются EditMode-тестом.
    ///
    /// Почему драйвер запускает окно, а не человек. Иначе «сгенерировать» —
    /// это последовательность из трёх ручных шагов, и любой забытый шаг
    /// выглядит как сломанный инструмент. Драйвер идемпотентен: повторный
    /// запуск при живом процессе ничего не порождает (проверка по driver.json).
    /// </summary>
    public sealed class ArtDriverClient
    {
        private readonly string _projectRoot;
        private readonly ArtSettings _settings;
        private readonly EventTail _tail = new EventTail();
        private readonly List<string> _problems = new List<string>();

        private ArtDriverState _state = new ArtDriverState();
        private Process _process;

        public ArtDriverClient(string projectRoot, ArtSettings settings)
        {
            _projectRoot = projectRoot;
            _settings = settings;

            CommandsPath = ArtStudioPaths.Commands(projectRoot);
            EventsPath = ArtStudioPaths.Events(projectRoot);
            DriverInfoPath = ArtStudioPaths.DriverInfo(projectRoot);
            LogPath = Path.Combine(ArtStudioPaths.Logs(projectRoot), "driver.log");
            DriverScriptPath = ResolveDriverScript(projectRoot);
        }

        public ArtDriverState State => _state;

        public IReadOnlyList<string> Problems => _problems;

        public string CommandsPath { get; }

        public string EventsPath { get; }

        public string DriverInfoPath { get; }

        public string LogPath { get; }

        public string DriverScriptPath { get; }

        public bool HasDriverScript => File.Exists(DriverScriptPath);

        /// <summary>
        /// Где искать драйвер. Источник истины — версионированный путь рядом
        /// с остальным инструментарием генерации; `ArtStudio/driver/` остаётся
        /// совместимым drop-in из ранней версии плана.
        /// </summary>
        public static string ResolveDriverScript(string projectRoot)
        {
            string canonical = Path.Combine(projectRoot, ".workbuddy-ai", "tools",
                "artstudio", "artgen.py");
            if (File.Exists(canonical))
            {
                return canonical;
            }

            return Path.Combine(ArtStudioPaths.Root(projectRoot), "driver", "artgen.py");
        }

        public bool IsDriverAlive
        {
            get
            {
                int pid = ReadDriverPid();
                if (pid <= 0 || pid == Process.GetCurrentProcess().Id)
                {
                    return false;
                }

                try
                {
                    using (Process process = Process.GetProcessById(pid))
                    {
                        return !process.HasExited;
                    }
                }
                catch (ArgumentException)
                {
                    // Процесса с таким pid нет — driver.json устарел.
                    return false;
                }
            }
        }

        public int DriverPid => ReadDriverPid();

        public void ClearProblems()
        {
            _problems.Clear();
        }

        /// <summary>Запускает драйвер, если он ещё не жив. false — не удалось.</summary>
        public bool StartDriver()
        {
            if (IsDriverAlive)
            {
                return true;
            }

            if (!HasDriverScript)
            {
                _problems.Add($"драйвер не найден: {DriverScriptPath}");
                return false;
            }

            if (string.IsNullOrEmpty(_settings.Python) || !File.Exists(_settings.Python))
            {
                _problems.Add($"python стенда не найден: {_settings.Python}");
                return false;
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath) ?? ".");
                var info = new ProcessStartInfo
                {
                    FileName = _settings.Python,
                    Arguments = $"\"{DriverScriptPath}\"",
                    WorkingDirectory = _projectRoot,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };

                _process = Process.Start(info);
            }
            catch (Exception e)
            {
                _problems.Add($"драйвер не запустился: {e.Message}");
                return false;
            }

            // Новый прогон начинается с seq = 1, значит читать надо с нуля.
            _state = new ArtDriverState();
            _tail.Reset();
            return _process != null;
        }

        /// <summary>Вежливо гасит драйвер; если не ответил — снимает процесс.</summary>
        public void StopDriver()
        {
            if (!IsDriverAlive)
            {
                return;
            }

            Send(ArtCommand.Create(ArtCommand.Stop));

            for (int i = 0; i < 20 && IsDriverAlive; i++)
            {
                System.Threading.Thread.Sleep(100);
            }

            if (!IsDriverAlive)
            {
                return;
            }

            try
            {
                using (Process process = Process.GetProcessById(ReadDriverPid()))
                {
                    process.Kill();
                }
            }
            catch (Exception)
            {
                // Процесс уже умер между проверкой и Kill — это норма.
            }
        }

        public void Send(ArtCommand command)
        {
            if (command == null)
            {
                return;
            }

            if (string.IsNullOrEmpty(command.nonce))
            {
                command.nonce = Guid.NewGuid().ToString("N");
            }

            ArtCommandStore.Append(CommandsPath, command);
        }

        public void Ping()
        {
            Send(ArtCommand.Create(ArtCommand.Ping));
        }

        public void RequestCatalog()
        {
            Send(ArtCommand.Create(ArtCommand.Catalog));
        }

        public void Generate(string poolPrefix, int count)
        {
            ArtCommand command = ArtCommand.Create(ArtCommand.Generate);
            command.pool = poolPrefix;
            command.count = count < 1 ? 1 : count;
            Send(command);
        }

        /// <summary>
        /// Перегенерация помеченного брака. Список кадров окно не присылает:
        /// марки лежат в журнале ревью, а какие id за ними стоят — знает
        /// драйвер, у него каталог стенда.
        /// </summary>
        public void RegenerateMarked()
        {
            Send(ArtCommand.Create(ArtCommand.RegenerateMarked));
        }

        public void ServerStart()
        {
            Send(ArtCommand.Create(ArtCommand.ServerStart));
        }

        public void ServerStop()
        {
            Send(ArtCommand.Create(ArtCommand.ServerStop));
        }

        public void Approve(string candidatePath)
        {
            ArtCommand command = ArtCommand.Create(ArtCommand.Approve);
            command.target = candidatePath;
            Send(command);
        }

        /// <summary>Дочитывает события и применяет их к состоянию.</summary>
        public void Poll()
        {
            if (File.Exists(EventsPath) && new FileInfo(EventsPath).Length < _tail.Offset)
            {
                // Файл короче прочитанного — драйвер перезапустился.
                _tail.Reset();
                _state = new ArtDriverState();
            }

            foreach (ArtEvent ev in _tail.ReadNew(EventsPath, _problems))
            {
                _state.Apply(ev);
            }

            // Сверка с процессом: событие `ready` флаг ставит, но о смерти
            // драйвера никто не сообщает. Без этой сверки окно показывает
            // «подключён» при мёртвом драйвере, а «Сгенерировать» остаётся
            // активной и пишет команду в файл, который никто не читает.
            if (_state.DriverConnected && !IsDriverAlive)
            {
                _state.MarkDriverDisconnected();
            }

            Trim(_problems, 5);
        }

        public void Dispose()
        {
            _process?.Dispose();
            _process = null;
        }

        private int ReadDriverPid()
        {
            if (!File.Exists(DriverInfoPath))
            {
                return -1;
            }

            try
            {
                string text = File.ReadAllText(DriverInfoPath);
                DriverInfoDto dto = JsonUtility.FromJson<DriverInfoDto>(text);
                return dto?.pid ?? -1;
            }
            catch (Exception)
            {
                return -1;
            }
        }

        private static void Trim(List<string> list, int max)
        {
            if (list.Count > max)
            {
                list.RemoveRange(0, list.Count - max);
            }
        }

        [Serializable]
        private sealed class DriverInfoDto
        {
            public int pid;
            public int protocol;
        }
    }
}
