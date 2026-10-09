using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Woodberry.Editor.ArtStudio
{
    /// <summary>
    /// Настройки арт-студии: где стенд, каким python его звать, куда стучаться
    /// в ComfyUI. Хранятся в <c>ArtStudio/config.json</c>.
    ///
    /// Значения по умолчанию — с этой машины, но они не зашиты в код:
    /// файл читается при каждом открытии окна и правится без правки скриптов.
    /// Битый или отсутствующий файл не должен ронять окно — он означает
    /// «настроек нет», и тогда показываются defaults с предупреждением.
    /// </summary>
    public sealed class ArtSettings
    {
        public const string DefaultStandDir = @"F:\ai\portraits";
        public const string DefaultPython = @"F:\ai\comfyenv\Scripts\python.exe";
        public const string DefaultComfyHost = "127.0.0.1:8188";
        public const string DefaultStyle = "ink";

        public string StandDir { get; set; }

        public string Python { get; set; }

        public string ComfyHost { get; set; }

        public string Style { get; set; }

        /// <summary>null — свет берётся из записи кадра, не переопределяется.</summary>
        public string Light { get; set; }

        public static ArtSettings Default()
        {
            return new ArtSettings
            {
                StandDir = DefaultStandDir,
                Python = DefaultPython,
                ComfyHost = DefaultComfyHost,
                Style = DefaultStyle,
                Light = null
            };
        }

        public sealed class LoadResult
        {
            public ArtSettings Settings;
            public readonly List<string> Problems = new List<string>();

            public static LoadResult Ok(ArtSettings settings)
            {
                var result = new LoadResult { Settings = settings };
                return result;
            }

            public static LoadResult WithProblem(ArtSettings settings, string problem)
            {
                var result = new LoadResult { Settings = settings };
                result.Problems.Add(problem);
                return result;
            }
        }

        public static LoadResult Load(string path)
        {
            if (!File.Exists(path))
            {
                return LoadResult.Ok(Default());
            }

            string text;
            try
            {
                text = File.ReadAllText(path);
            }
            catch (Exception e)
            {
                return LoadResult.WithProblem(Default(),
                    $"config.json не прочитан: {e.Message}");
            }

            ArtSettings settings;
            try
            {
                FileDto dto = JsonUtility.FromJson<FileDto>(text);
                if (dto == null)
                {
                    return LoadResult.WithProblem(Default(),
                        "config.json пуст, взяты значения по умолчанию");
                }

                settings = dto.ToSettings();
            }
            catch (Exception e)
            {
                return LoadResult.WithProblem(Default(),
                    $"config.json повреждён, взяты значения по умолчанию: {e.Message}");
            }

            var loaded = new LoadResult { Settings = settings };

            // Стенд обязателен: без каталога генерации не будет вовсе, и это
            // предупреждение. А python и хост ComfyUI в частичном файле просто
            // подставляются — заполнять все три поля руками, чтобы не получить
            // три баннера, незачем.
            if (string.IsNullOrEmpty(settings.StandDir))
            {
                settings.StandDir = DefaultStandDir;
                loaded.Problems.Add(
                    $"standDir не указан в config.json, взят по умолчанию: {DefaultStandDir}");
            }

            return loaded;
        }

        public void Save(string path)
        {
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(path,
                JsonUtility.ToJson(new FileDto(this), prettyPrint: true));
        }

        /// <summary>Готов ли инструмент к работе: стенд и python на месте.</summary>
        public IReadOnlyList<string> FindSetupProblems()
        {
            var problems = new List<string>();

            if (string.IsNullOrEmpty(StandDir) || !Directory.Exists(StandDir))
            {
                problems.Add($"каталог стенда не найден: {StandDir}");
            }

            if (string.IsNullOrEmpty(Python) || !File.Exists(Python))
            {
                problems.Add($"python стенда не найден: {Python}");
            }

            return problems;
        }

        [Serializable]
        private sealed class FileDto
        {
            public string standDir;
            public string python;
            public string comfyHost;
            public string style;
            public string light;

            public FileDto(ArtSettings settings)
            {
                standDir = settings.StandDir;
                python = settings.Python;
                comfyHost = settings.ComfyHost;
                style = settings.Style;
                light = settings.Light;
            }

            public ArtSettings ToSettings()
            {
                return new ArtSettings
                {
                    StandDir = standDir,
                    Python = python,
                    ComfyHost = comfyHost,
                    Style = string.IsNullOrEmpty(style) ? DefaultStyle : style,
                    Light = light
                };
            }
        }
    }
}
