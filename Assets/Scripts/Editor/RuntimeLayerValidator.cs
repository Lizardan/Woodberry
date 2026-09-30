using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Woodberry.Editor
{
    /// <summary>
    /// Проверяет инварианты, которые компилятор не ловит:
    /// runtime-код не должен ссылаться на <c>UnityEditor</c>, а каждый
    /// runtime-слой обязан иметь собственный asmdef с корректными платформами.
    /// </summary>
    public static class RuntimeLayerValidator
    {
        public const string EditorAssemblyName = "Woodberry.Editor";

        private static readonly string[] RuntimeLayers =
        {
            "AI", "Audio", "CameraRig", "Core", "Gameplay", "Net", "Save", "UI"
        };

        private static string ScriptsRoot =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "Scripts"));

        /// <summary>Собирает нарушения инвариантов. Пустой список — всё в порядке.</summary>
        public static IReadOnlyList<string> Validate()
        {
            var problems = new List<string>();
            problems.AddRange(FindRuntimeReferencesToUnityEditor());
            problems.AddRange(FindMissingAssemblyDefinitions());
            problems.AddRange(FindInvalidEditorPlatforms());
            return problems;
        }

        /// <summary>
        /// Ищет обращения к <c>UnityEditor</c> в runtime-слоях, включая корень
        /// <c>Assets/Scripts</c>. Директивы <c>#if UNITY_EDITOR</c> — легальная
        /// форма editor-кода в runtime-файле (её допускает AGENTS.md), поэтому
        /// такие блоки пропускаются.
        /// </summary>
        public static IReadOnlyList<string> FindRuntimeReferencesToUnityEditor()
        {
            var problems = new List<string>();

            foreach (string file in EnumerateRuntimeScripts())
            {
                string relative = ToProjectRelative(file);
                bool insideEditorGuard = false;

                foreach (string raw in ReadLinesSafe(file, problems, relative))
                {
                    string line = StripComment(raw).Trim();

                    if (line.Length == 0)
                    {
                        continue;
                    }

                    if (line.StartsWith("#if", StringComparison.Ordinal))
                    {
                        insideEditorGuard = line.Contains("UNITY_EDITOR");
                        continue;
                    }

                    if (line.StartsWith("#endif", StringComparison.Ordinal))
                    {
                        insideEditorGuard = false;
                        continue;
                    }

                    if (insideEditorGuard)
                    {
                        continue;
                    }

                    if (line.StartsWith("using UnityEditor", StringComparison.Ordinal) ||
                        line.Contains("UnityEditor."))
                    {
                        problems.Add(
                            $"{relative}: обращение к UnityEditor в runtime-слое. " +
                            "Перенесите код в Assets/Scripts/Editor или закройте " +
                            "его блоком #if UNITY_EDITOR.");
                    }
                }
            }

            return problems;
        }

        /// <summary>Проверяет, что у каждого runtime-слоя есть свой asmdef.</summary>
        public static IReadOnlyList<string> FindMissingAssemblyDefinitions()
        {
            var problems = new List<string>();

            foreach (string layer in RuntimeLayers)
            {
                string folder = Path.Combine(ScriptsRoot, layer);

                if (!Directory.Exists(folder))
                {
                    problems.Add($"Assets/Scripts/{layer}: папка слоя отсутствует.");
                    continue;
                }

                string expected = Path.Combine(folder, $"Woodberry.{layer}.asmdef");

                if (!File.Exists(expected))
                {
                    problems.Add(
                        $"{ToProjectRelative(expected)}: отсутствует. Слой без asmdef " +
                        "не имеет компилируемой границы.");
                }
            }

            return problems;
        }

        /// <summary>
        /// Проверяет, что Editor-сборка помечена как Editor-only. Без этого
        /// runtime-код утечёт в player-сборку — это Critical по code-review-flow.
        /// </summary>
        public static IReadOnlyList<string> FindInvalidEditorPlatforms()
        {
            var problems = new List<string>();
            string editorAsmdef = Path.Combine(ScriptsRoot, "Editor", $"{EditorAssemblyName}.asmdef");

            if (!File.Exists(editorAsmdef))
            {
                problems.Add(
                    $"{ToProjectRelative(editorAsmdef)}: отсутствует. Editor-слой " +
                    "обязан иметь собственную сборку.");
                return problems;
            }

            try
            {
                string json = File.ReadAllText(editorAsmdef);
                bool hasEditorPlatform =
                    json.Contains("\"Editor\"") && json.Contains("includePlatforms");

                if (!hasEditorPlatform)
                {
                    problems.Add(
                        $"{ToProjectRelative(editorAsmdef)}: includePlatforms должен " +
                        "содержать \"Editor\", иначе editor-код попадёт в player-сборку.");
                }
            }
            catch (IOException e)
            {
                problems.Add($"{ToProjectRelative(editorAsmdef)}: не прочитан — {e.Message}");
            }

            return problems;
        }

        private static IEnumerable<string> EnumerateRuntimeScripts()
        {
            if (!Directory.Exists(ScriptsRoot))
            {
                yield break;
            }

            // Корень Scripts тоже сканируется: скрипт в Assets/Scripts/Utils/
            // с using UnityEditor иначе прошёл бы молча.
            foreach (string file in Directory.GetFiles(ScriptsRoot, "*.cs", SearchOption.TopDirectoryOnly))
            {
                if (!IsGenerated(file))
                {
                    yield return file;
                }
            }

            foreach (string layer in RuntimeLayers)
            {
                string folder = Path.Combine(ScriptsRoot, layer);

                if (!Directory.Exists(folder))
                {
                    continue;
                }

                foreach (string file in Directory.GetFiles(folder, "*.cs", SearchOption.AllDirectories))
                {
                    if (!IsGenerated(file))
                    {
                        yield return file;
                    }
                }
            }
        }

        private static IEnumerable<string> ReadLinesSafe(
            string file,
            ICollection<string> problems,
            string relative)
        {
            try
            {
                return File.ReadAllLines(file);
            }
            catch (IOException e)
            {
                problems.Add($"{relative}: не прочитан — {e.Message}");
                return Array.Empty<string>();
            }
        }

        private static string StripComment(string line)
        {
            int index = line.IndexOf("//", StringComparison.Ordinal);
            return index < 0 ? line : line.Substring(0, index);
        }

        private static bool IsGenerated(string path)
        {
            string normalized = path.Replace('\\', '/');
            return normalized.Contains("/Generated/") ||
                   Path.GetFileName(path).StartsWith("InputSystem_Actions", StringComparison.Ordinal);
        }

        private static string ToProjectRelative(string path)
        {
            string project = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            return path.StartsWith(project, StringComparison.Ordinal)
                ? path.Substring(project.Length).TrimStart('/', '\\').Replace('\\', '/')
                : path;
        }

        [MenuItem("Woodberry/Validate Runtime Layers")]
        private static void ValidateFromMenu()
        {
            IReadOnlyList<string> problems = Validate();

            if (problems.Count == 0)
            {
                Debug.Log("Woodberry: инварианты runtime-слоёв соблюдены.");
                return;
            }

            foreach (string problem in problems)
            {
                Debug.LogError($"Woodberry: {problem}");
            }
        }
    }
}
