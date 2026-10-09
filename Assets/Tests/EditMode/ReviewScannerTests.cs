using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using Woodberry.Editor.ArtStudio;

namespace Woodberry.Tests.EditMode
{
    /// <summary>
    /// Кандидаты читаются с диска: папка <c>review/</c> — источник правды.
    /// Проверяется и разбор имени, и то, что служебные папки не попадают в
    /// очередь, и что покрытие считается только там, где оно что-то значит.
    /// </summary>
    public sealed class ReviewScannerTests
    {
        private string _root;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(),
                $"review_{TestContext.CurrentContext.Test.Name}");
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, true);
            }

            Directory.CreateDirectory(_root);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, true);
            }
        }

        private string WritePng(string relativePath, int opaqueOf = -1, int size = 4)
        {
            string full = Path.Combine(_root, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(full) ?? _root);

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            try
            {
                var pixels = new Color32[size * size];
                int opaque = opaqueOf < 0 ? pixels.Length : opaqueOf;
                for (int i = 0; i < pixels.Length; i++)
                {
                    pixels[i] = new Color32(255, 255, 255, i < opaque ? (byte)255 : (byte)0);
                }

                texture.SetPixels32(pixels);
                texture.Apply();
                File.WriteAllBytes(full, texture.EncodeToPNG());
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }

            return full;
        }

        [Test]
        public void TryParseId_Portrait_IgnoresStyleSuffix()
        {
            Assert.That(ReviewScanner.TryParseId("p_Craftsmen_Baker_c_ink", out ArtAssetId id), Is.True);
            Assert.That(id.Kind, Is.EqualTo(ArtAssetKind.Portrait));
            Assert.That(id.Group, Is.EqualTo("Craftsmen"));
            Assert.That(id.Pool, Is.EqualTo("Baker"));
            Assert.That(id.Number, Is.EqualTo(3), "буква c — третий кадр пула");
        }

        [Test]
        public void TryParseId_Portrait_WithoutStyleAlsoWorks()
        {
            Assert.That(ReviewScanner.TryParseId("p_Craftsmen_Baker_c", out ArtAssetId id), Is.True);
            Assert.That(id.Number, Is.EqualTo(3));
        }

        [Test]
        public void TryParseId_Background_HasNoLetter()
        {
            Assert.That(ReviewScanner.TryParseId("bg_City_Bathhouse_ink", out ArtAssetId id), Is.True);
            Assert.That(id.Kind, Is.EqualTo(ArtAssetKind.Background));
            Assert.That(id.StandId, Is.EqualTo("bg_City_Bathhouse"));
        }

        [Test]
        public void TryParseId_WhenNameIsForeign_ReturnsFalse()
        {
            Assert.That(ReviewScanner.TryParseId("Thumbnail", out _), Is.False);
            Assert.That(ReviewScanner.TryParseId("p_Craftsmen", out _), Is.False);
            Assert.That(ReviewScanner.TryParseId("p_Craftsmen_Baker_cc_ink", out _), Is.False,
                "буква варианта ровно одна");
        }

        [Test]
        public void Scan_WhenFolderMissing_ReturnsNothingWithoutProblems()
        {
            ReviewScanner.ScanResult result = ReviewScanner.Scan(Path.Combine(_root, "нет-такой"));

            Assert.That(result.Candidates, Is.Empty);
            Assert.That(result.Problems, Is.Empty,
                "до первой генерации папки просто нет — это не поломка");
        }

        [Test]
        public void Scan_ReadsCandidatesFromDisk()
        {
            WritePng(Path.Combine("Craftsmen_Baker", "p_Craftsmen_Baker_c_ink.png"));
            WritePng(Path.Combine("City_Bathhouse", "bg_City_Bathhouse_ink.png"));

            ReviewScanner.ScanResult result = ReviewScanner.Scan(_root);

            Assert.That(result.Problems, Is.Empty);
            Assert.That(result.Candidates.Count, Is.EqualTo(2));

            ReviewCandidate baker = result.Candidates.Find(candidate =>
                candidate.StandId == "p_Craftsmen_Baker_c");
            Assert.That(baker, Is.Not.Null);
            Assert.That(baker.DisplayName, Is.EqualTo("Baker_03"));
            Assert.That(baker.Path, Does.EndWith("p_Craftsmen_Baker_c_ink.png"));
        }

        [Test]
        public void Scan_SkipsServiceFolders()
        {
            WritePng(Path.Combine("replaced", "Craftsmen_Baker", "p_Craftsmen_Baker_c_ink.png"));
            WritePng(Path.Combine("thumbs", "p_Craftsmen_Baker_c_ink.png"));
            WritePng(Path.Combine("Craftsmen_Baker", "p_Craftsmen_Baker_d_ink.png"));

            ReviewScanner.ScanResult result = ReviewScanner.Scan(_root);

            Assert.That(result.Candidates.Count, Is.EqualTo(1),
                "replaced/ — архив, thumbs/ — превью: это не кандидаты на решение");
            Assert.That(result.Candidates[0].StandId, Is.EqualTo("p_Craftsmen_Baker_d"));
        }

        [Test]
        public void Scan_WhenNameIsUnparsable_ReportsProblem()
        {
            WritePng("portrait.png");

            ReviewScanner.ScanResult result = ReviewScanner.Scan(_root);

            Assert.That(result.Candidates, Is.Empty);
            Assert.That(result.Problems.Count, Is.EqualTo(1));
        }

        [Test]
        public void Scan_MeasuresCoverageForPortraitsOnly()
        {
            WritePng(Path.Combine("Craftsmen_Baker", "p_Craftsmen_Baker_c_ink.png"), opaqueOf: 4);
            WritePng(Path.Combine("City_Bathhouse", "bg_City_Bathhouse_ink.png"), opaqueOf: 4);

            ReviewScanner.ScanResult result = ReviewScanner.Scan(_root);

            ReviewCandidate portrait = result.Candidates.Find(candidate => candidate.Kind == ArtAssetKind.Portrait);
            ReviewCandidate background = result.Candidates.Find(candidate => candidate.Kind == ArtAssetKind.Background);

            Assert.That(portrait.Coverage, Is.EqualTo(25), "4 непрозрачных пикселя из 16");
            Assert.That(portrait.HasCoverage, Is.True);
            Assert.That(background.HasCoverage, Is.False,
                "у фона метрика всегда 100 и ничего не значит — показывать её нельзя");
        }

        [Test]
        public void Scan_RecordsFileIdentity()
        {
            string path = WritePng(Path.Combine("Craftsmen_Baker", "p_Craftsmen_Baker_c_ink.png"));

            ReviewScanner.ScanResult result = ReviewScanner.Scan(_root);
            ReviewCandidate candidate = result.Candidates[0];

            Assert.That(candidate.Size, Is.EqualTo(new FileInfo(path).Length));
            Assert.That(candidate.WrittenUtcTicks, Is.GreaterThan(0));
        }
    }
}
