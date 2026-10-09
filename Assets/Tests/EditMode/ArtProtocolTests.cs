using NUnit.Framework;
using Woodberry.Editor.ArtStudio;

namespace Woodberry.Tests.EditMode
{
    /// <summary>
    /// Команда и событие — контракт на другой стороне которого Python.
    /// Проверяется не «красота», а то, что разбор обязан быть устойчив:
    /// строка приходит из файла, который пишет другой процесс.
    /// </summary>
    public sealed class ArtProtocolTests
    {
        [Test]
        public void ProtocolVersion_IsOne()
        {
            Assert.That(ArtProtocol.Version, Is.EqualTo(1));
        }

        [Test]
        public void Command_RoundTrip_KeepsAllFields()
        {
            ArtCommand command = ArtCommand.Create(ArtCommand.Generate)
                .WithNonce("n-7");
            command.pool = "Priest";
            command.count = 3;
            command.style = "ink";
            command.target = "ArtStudio/review/Priest/x.png";
            command.comment = "плоское";

            bool parsed = ArtCommand.TryParse(command.ToJson(), out ArtCommand restored);

            Assert.That(parsed, Is.True);
            Assert.That(restored.cmd, Is.EqualTo(ArtCommand.Generate));
            Assert.That(restored.protocol, Is.EqualTo(ArtProtocol.Version));
            Assert.That(restored.nonce, Is.EqualTo("n-7"));
            Assert.That(restored.pool, Is.EqualTo("Priest"));
            Assert.That(restored.count, Is.EqualTo(3));
            Assert.That(restored.style, Is.EqualTo("ink"));
            Assert.That(restored.target, Is.EqualTo("ArtStudio/review/Priest/x.png"));
            Assert.That(restored.comment, Is.EqualTo("плоское"));
        }

        [Test]
        public void Command_WhenLineIsBroken_ReturnsFalse()
        {
            Assert.That(ArtCommand.TryParse("{не json", out _), Is.False);
        }

        [Test]
        public void Command_WhenLineHasNoCmd_ReturnsFalse()
        {
            Assert.That(ArtCommand.TryParse("{\"pool\":\"Priest\"}", out _), Is.False);
        }

        [Test]
        public void Event_WhenLineIsBroken_ReturnsFalse()
        {
            Assert.That(ArtEvent.TryParse("[[[", out _), Is.False);
        }

        [Test]
        public void Event_WhenLineHasNoKind_ReturnsFalse()
        {
            Assert.That(ArtEvent.TryParse("{\"seq\":4}", out _), Is.False);
        }

        [Test]
        public void Event_WhenEmptyLine_ReturnsFalse()
        {
            Assert.That(ArtEvent.TryParse("   ", out _), Is.False);
        }

        [Test]
        public void Event_WhenKindPresent_ParsesFields()
        {
            bool parsed = ArtEvent.TryParse(
                "{\"seq\":12,\"kind\":\"item_progress\",\"id\":\"p_Roles_Priest_b\",\"percent\":42}",
                out ArtEvent parsedEvent);

            Assert.That(parsed, Is.True);
            Assert.That(parsedEvent.seq, Is.EqualTo(12));
            Assert.That(parsedEvent.kind, Is.EqualTo(ArtEvent.ItemProgress));
            Assert.That(parsedEvent.id, Is.EqualTo("p_Roles_Priest_b"));
            Assert.That(parsedEvent.percent, Is.EqualTo(42));
        }
    }
}
