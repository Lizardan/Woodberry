using System.IO;
using NUnit.Framework;
using Woodberry.Editor.ArtStudio;

namespace Woodberry.Tests.EditMode
{
    /// <summary>
    /// Запись команды. Правило протокола — одна команда = одна строка с одним
    /// переводом строки: драйвер читает файл по смещению и берёт только целые
    /// строки, поэтому склеенная запись означает потерянную команду.
    /// </summary>
    public sealed class ArtCommandStoreTests
    {
        private string _path;

        [SetUp]
        public void SetUp()
        {
            _path = Path.Combine(Path.GetTempPath(),
                $"artstudio_cmd_{TestContext.CurrentContext.Test.Name}.jsonl");
            if (File.Exists(_path))
            {
                File.Delete(_path);
            }
        }

        [TearDown]
        public void TearDown()
        {
            if (File.Exists(_path))
            {
                File.Delete(_path);
            }
        }

        [Test]
        public void Append_WritesOneCompleteLinePerCommand()
        {
            ArtCommandStore.Append(_path, ArtCommand.Create(ArtCommand.Ping));
            ArtCommandStore.Append(_path, ArtCommand.Create(ArtCommand.Catalog));

            string[] lines = File.ReadAllLines(_path);

            Assert.That(lines, Has.Length.EqualTo(2));
            Assert.That(ArtCommand.TryParse(lines[0], out ArtCommand first), Is.True);
            Assert.That(first.cmd, Is.EqualTo(ArtCommand.Ping));
            Assert.That(ArtCommand.TryParse(lines[1], out ArtCommand second), Is.True);
            Assert.That(second.cmd, Is.EqualTo(ArtCommand.Catalog));
        }

        [Test]
        public void Append_KeepsFieldsAndNonce()
        {
            ArtCommand command = ArtCommand.Create(ArtCommand.Generate).WithNonce("abc");
            command.pool = "p_Roles_Priest";
            command.count = 2;

            ArtCommandStore.Append(_path, command);

            ArtCommand restored = ArtCommand.TryParse(File.ReadAllText(_path).Trim(), out ArtCommand parsed)
                ? parsed
                : null;

            Assert.That(restored, Is.Not.Null);
            Assert.That(restored.pool, Is.EqualTo("p_Roles_Priest"));
            Assert.That(restored.count, Is.EqualTo(2));
            Assert.That(restored.nonce, Is.EqualTo("abc"));
            Assert.That(restored.protocol, Is.EqualTo(ArtProtocol.Version));
        }

        [Test]
        public void Append_ToMissingDirectory_CreatesIt()
        {
            string nested = Path.Combine(Path.GetTempPath(),
                $"artstudio_dir_{TestContext.CurrentContext.Test.Name}", "commands.jsonl");

            ArtCommandStore.Append(nested, ArtCommand.Create(ArtCommand.Ping));

            Assert.That(File.Exists(nested), Is.True);

            Directory.Delete(Path.GetDirectoryName(nested), recursive: true);
        }
    }
}
