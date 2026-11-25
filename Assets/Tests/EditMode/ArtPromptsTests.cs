using NUnit.Framework;
using Woodberry.Editor.ArtStudio;

namespace Woodberry.Tests.EditMode
{
    /// <summary>
    /// Разбор ответа драйвера с промптами. Ответ приходит извне, поэтому
    /// проверяется и хороший JSON, и мусор: вкладка обязана остаться живой и
    /// сказать, что не разобралось, а не уронить окно редактора.
    /// </summary>
    public sealed class ArtPromptsTests
    {
        private const string Sample = "{\"style\":[{\"key\":\"ink\",\"text\":\"dark fantasy pen and ink\"}],"
            + "\"light\":[{\"key\":\"candle_low\",\"text\":\"lit by a single candle\"}],"
            + "\"negative\":{\"common\":\"blurry\",\"background\":\"people\"},"
            + "\"characters\":[{\"id\":\"01_priest\",\"name\":\"Священник\","
            + "\"light\":\"candle_low\",\"pose\":\"hands folded\",\"subject\":\"a stern priest\"}],"
            + "\"backgrounds\":[{\"id\":\"bg_house\",\"name\":\"Дом\",\"scene\":\"interior\","
            + "\"light\":\"hearth_warm\"}],"
            + "\"gen\":{\"steps\":10,\"cfg\":1.0,\"scheduler\":\"simple\",\"seed_base\":880000,"
            + "\"subject_size\":[480,720],\"background_size\":[856,480],\"chroma\":[128,128,128]}}";

        [Test]
        public void TryParse_WhenDriverAnswer_ReadsEveryGroup()
        {
            Assert.That(ArtPrompts.TryParse(Sample, out ArtPrompts prompts), Is.True);

            Assert.That(prompts.Styles.Length, Is.EqualTo(1));
            Assert.That(prompts.Styles[0].key, Is.EqualTo("ink"));
            Assert.That(prompts.Lights.Length, Is.EqualTo(1));
            Assert.That(prompts.NegativeCommon, Is.EqualTo("blurry"));
            Assert.That(prompts.NegativeBackground, Is.EqualTo("people"));
            Assert.That(prompts.Characters.Length, Is.EqualTo(1));
            Assert.That(prompts.Characters[0].id, Is.EqualTo("01_priest"));
            Assert.That(prompts.Backgrounds[0].scene, Is.EqualTo("interior"));
            Assert.That(prompts.Gen.steps, Is.EqualTo(10));
            Assert.That(prompts.Gen.subject_size, Is.EqualTo(new[] { 480, 720 }));
        }

        [Test]
        public void TryParse_WhenAnswerEmpty_FailsInsteadOfHalfParsing()
        {
            Assert.That(ArtPrompts.TryParse(string.Empty, out ArtPrompts empty), Is.False);
            Assert.That(ArtPrompts.TryParse(null, out ArtPrompts missing), Is.False);
            Assert.That(ArtPrompts.TryParse("не json вовсе", out ArtPrompts junk), Is.False);
        }

        [Test]
        public void TryParse_WhenGroupsAbsent_FillsWithEmptyArrays()
        {
            // Старый драйвер без промптов ответит пустым объектом. Рендер обязан
            // показать пустые разделы, а не упасть на null.
            Assert.That(ArtPrompts.TryParse("{}", out ArtPrompts prompts), Is.True);

            Assert.That(prompts.Styles, Is.Empty);
            Assert.That(prompts.Characters, Is.Empty);
            Assert.That(prompts.IsEmpty, Is.True);
        }

        [Test]
        public void IsEmpty_WhenGroupsPresent_IsFalse()
        {
            ArtPrompts.TryParse(Sample, out ArtPrompts prompts);

            Assert.That(prompts.IsEmpty, Is.False);
        }
    }
}