using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Woodberry.Tests.EditMode
{
    /// <summary>
    /// Анимация персонажа: связка «движение → состояние аниматора → кадр спрайта».
    ///
    /// Отдельный класс, а не тест в PlayerControllerTests, потому что отказ
    /// здесь невидим глазом и не ломает движение: персонаж едет, а картинка
    /// стоит. Ловится только проверкой самого аниматора и подмены спрайта.
    ///
    /// Живёт в EditMode, а не в PlayMode, по двум причинам. Первая: тесты
    /// инспектируют ассеты и крутят <c>Animator.Update</c> вручную — кадры
    /// игры им не нужны. Вторая: проверка ассетов требует <c>UnityEditor</c>,
    /// а PlayMode-сборка обязана компилироваться и под player, где его нет.
    ///
    /// Раньше здесь проверялся модульный риг (голова/торс/руки/ноги отдельными
    /// спрайтами) и кривые на узлах <c>Visual/Body/LegBackLeft</c>. Фигура
    /// переведена на покадровые спрайты, узлов конечностей больше нет, поэтому
    /// проверки переписаны на подмену спрайта — но проверяют они ровно те же
    /// отказы: пустой клип, отсутствие зацикливания и перезапуск перехода.
    /// </summary>
    public sealed class PlayerAnimationTests
    {
        // StringToHash — вызов времени выполнения, а не константа компиляции.
        private static readonly int IsMovingHash = Animator.StringToHash("IsMoving");
        private static readonly int WalkStateHash = Animator.StringToHash("Walk");
        private static readonly int IdleStateHash = Animator.StringToHash("Idle");

        private const string PrefabPath = "Assets/Woodberry/Prefabs/Player.prefab";
        private const string WalkClipPath =
            "Assets/Woodberry/Art/Characters/Animations/Player_TopDown_Walk.anim";
        private const string IdleClipPath =
            "Assets/Woodberry/Art/Characters/Animations/Player_TopDown_Idle.anim";

        private GameObject _player;

        [TearDown]
        public void TearDown()
        {
            LogAssert.ignoreFailingMessages = false;

            if (_player != null)
            {
                Object.DestroyImmediate(_player);
            }
        }

        [Test]
        public void PlayerPrefab_HasAnimatorWithWalkAndIdleClips()
        {
            Animator animator = CreatePlayer();

            Assert.That(animator.runtimeAnimatorController, Is.Not.Null,
                "префаб игрока без контроллера анимации: персонаж не шагает");

            var names = new List<string>();
            foreach (var clip in animator.runtimeAnimatorController.animationClips)
            {
                names.Add(clip.name);
            }

            Assert.That(names, Has.Some.Contains("Walk"), "нет клипа ходьбы");
            Assert.That(names, Has.Some.Contains("Idle"), "нет клипа покоя");
        }

        [Test]
        public void WalkClip_SwapsSpriteFramesOnVisualNode()
        {
            // Пустой клип ходьбы выглядит как статичная картинка и никак не
            // проявляется в тестах на движение.
            var clip = UnityEditor.AssetDatabase.LoadAssetAtPath<AnimationClip>(WalkClipPath);

            Assert.That(clip, Is.Not.Null, "клип ходьбы не найден");
            Assert.That(clip.legacy, Is.False, "legacy-клип не воспроизводится");
            Assert.That(clip.length, Is.GreaterThan(0.1f),
                "длительность цикла ходьбы подозрительно мала");

            var bindings = UnityEditor.AnimationUtility.GetObjectReferenceCurveBindings(clip);
            Assert.That(bindings.Length, Is.EqualTo(1), "ожидается одна дорожка подмены спрайта");

            Assert.That(bindings[0].path, Is.EqualTo("Visual"),
                "кадры обязаны подменяться на узле Visual: там живёт SpriteRenderer");
            Assert.That(bindings[0].propertyName, Is.EqualTo("m_Sprite"));

            var keys = UnityEditor.AnimationUtility.GetObjectReferenceCurve(clip, bindings[0]);
            Assert.That(keys.Length, Is.GreaterThanOrEqualTo(6),
                "в цикле шага мало кадров: шаг будет читаться как дёрганье картинки");
        }

        [Test]
        public void IdleClip_IsLoopingAndHasFrames()
        {
            var clip = UnityEditor.AssetDatabase.LoadAssetAtPath<AnimationClip>(IdleClipPath);

            Assert.That(clip, Is.Not.Null, "клип покоя не найден");

            var settings = UnityEditor.AnimationUtility.GetAnimationClipSettings(clip);
            Assert.That(settings.loopTime, Is.True, "покой обязан быть зациклен");
            Assert.That(
                UnityEditor.AnimationUtility.GetObjectReferenceCurveBindings(clip).Length,
                Is.EqualTo(1));
        }

        [Test]
        public void Animator_WhenIsMoving_EntersWalkAndReturnsToIdleWhenStopped()
        {
            Animator animator = CreatePlayer();

            animator.SetBool(IsMovingHash, true);
            Advance(animator, 24);

            Assert.That(
                animator.GetCurrentAnimatorStateInfo(0).shortNameHash,
                Is.EqualTo(WalkStateHash),
                "после IsMoving аниматор должен оказаться в Walk");

            animator.SetBool(IsMovingHash, false);
            Advance(animator, 24);

            Assert.That(
                animator.GetCurrentAnimatorStateInfo(0).shortNameHash,
                Is.EqualTo(IdleStateHash),
                "после остановки аниматор должен вернуться в Idle");
        }

        [Test]
        public void Animator_StaysInWalk_WithoutReEnteringTransition()
        {
            // Регрессия: переход из AnyState срабатывает и когда уже находишься
            // в Walk, перезапуская блендинг каждый цикл. Персонаж едет, а
            // картинка дрожит на месте — на скриншоте это не видно.
            Animator animator = CreatePlayer();
            animator.SetBool(IsMovingHash, true);

            Advance(animator, 12);

            Assert.That(
                animator.GetCurrentAnimatorStateInfo(0).shortNameHash,
                Is.EqualTo(WalkStateHash),
                "к этому моменту состояние должно устояться в Walk");

            for (int i = 0; i < 20; i++)
            {
                animator.Update(0.05f);
                Assert.That(
                    animator.IsInTransition(0),
                    Is.False,
                    $"цикл ходьбы не должен перезапускаться: переход начался снова на кадре {i}");
            }
        }

        [Test]
        public void WalkClip_Loops_AndKeepsSwappingSpritesPastOneCycleLength()
        {
            // Регрессия: клип без loopTime проигрывается один раз и замирает на
            // последнем кадре. Персонаж отшагивает цикл и встаёт с поднятой
            // ногой. Состояние при этом остаётся Walk, поэтому проверка
            // «аниматор в Walk» такой отказ пропускает — нужен замер картинки.
            var clip = UnityEditor.AssetDatabase.LoadAssetAtPath<AnimationClip>(WalkClipPath);
            Assert.That(clip, Is.Not.Null, "клип ходьбы не найден");

            var settings = UnityEditor.AnimationUtility.GetAnimationClipSettings(clip);
            Assert.That(settings.loopTime, Is.True, "цикл ходьбы обязан быть зациклен");

            Animator animator = CreatePlayer();
            var renderer = animator.transform.Find("Visual").GetComponent<SpriteRenderer>();
            Assert.That(renderer, Is.Not.Null, "на узле Visual нет SpriteRenderer");

            animator.SetBool(IsMovingHash, true);

            var seenAfterCycle = new HashSet<string>();

            // Заметно больше одного цикла: клип ~0.7 с против 2.0 с проигрывания.
            for (int i = 0; i < 40; i++)
            {
                animator.Update(0.05f);

                if (i * 0.05f > clip.length + 0.3f && renderer.sprite != null)
                {
                    seenAfterCycle.Add(renderer.sprite.name);
                }
            }

            Assert.That(
                seenAfterCycle.Count,
                Is.GreaterThan(1),
                "после первого цикла кадры должны продолжаться, а не замереть на одном");
        }

        private static void Advance(Animator animator, int frames)
        {
            for (int i = 0; i < frames; i++)
            {
                animator.Update(0.05f);
            }
        }

        private Animator CreatePlayer()
        {
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);

            Assert.That(prefab, Is.Not.Null, "Player.prefab не найден");

            _player = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(prefab);
            _player.transform.position = Vector3.zero;

            return _player.GetComponent<Animator>();
        }
    }
}
