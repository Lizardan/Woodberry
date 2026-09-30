using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Woodberry.Editor;

namespace Woodberry.Tests.EditMode
{
    /// <summary>
    /// Валидатор объявлен в AGENTS.md как механизм контроля границ слоёв,
    /// поэтому его поведение проверяется тестами, а не только глазами.
    /// </summary>
    public sealed class RuntimeLayerValidatorTests
    {
        private static string ScriptsRoot =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "Scripts"));

        [Test]
        public void FindRuntimeReferencesToUnityEditor_OnCurrentProject_ReturnsNoProblems()
        {
            IReadOnlyList<string> problems =
                RuntimeLayerValidator.FindRuntimeReferencesToUnityEditor();

            Assert.That(
                problems,
                Is.Empty,
                "runtime-код не должен ссылаться на UnityEditor:\n" +
                string.Join("\n", problems));
        }

        [Test]
        public void FindMissingAssemblyDefinitions_OnCurrentProject_ReturnsNoProblems()
        {
            IReadOnlyList<string> problems =
                RuntimeLayerValidator.FindMissingAssemblyDefinitions();

            Assert.That(
                problems,
                Is.Empty,
                "у каждого runtime-слоя обязан быть asmdef:\n" +
                string.Join("\n", problems));
        }

        [Test]
        public void FindInvalidEditorPlatforms_OnCurrentProject_ReturnsNoProblems()
        {
            IReadOnlyList<string> problems =
                RuntimeLayerValidator.FindInvalidEditorPlatforms();

            Assert.That(
                problems,
                Is.Empty,
                "Editor-сборка обязана быть Editor-only:\n" +
                string.Join("\n", problems));
        }

        [Test]
        public void Validate_OnCurrentProject_ReturnsNoProblems()
        {
            IReadOnlyList<string> problems = RuntimeLayerValidator.Validate();

            Assert.That(
                problems,
                Is.Empty,
                "инварианты слоёв нарушены:\n" + string.Join("\n", problems));
        }

        [Test]
        public void ScriptsRoot_ResolvesToExistingDirectory_NotRelativeToProcessCwd()
        {
            Assert.That(Directory.Exists(ScriptsRoot), Is.True, ScriptsRoot);
        }

        [Test]
        public void CoreAssembly_HasNoReferencesToOtherWoodberryAssemblies()
        {
            var references = UnityEditor.Compilation
                .CompilationPipeline.GetAssemblies(UnityEditor.Compilation.AssembliesType.Player)
                .First(a => a.name == "Woodberry.Core")
                .assemblyReferences
                .Select(r => r.name)
                .Where(n => n.StartsWith("Woodberry."))
                .ToList();

            Assert.That(
                references,
                Is.Empty,
                "Core не должен ссылаться ни на одну Woodberry.* сборку, " +
                "иначе направление зависимостей сломано: " + string.Join(", ", references));
        }
    }
}
