using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Woodberry.Tests.EditMode
{
    /// <summary>
    /// Анимация персонажа: связка «движение → состояние аниматора».
    ///
    /// Отдельный класс, а не тест в PlayerControllerTests, потому что отказ
    /// здесь невидим глазом и не ломает движение: персонаж едет, а ноги
    /// подрагивают на месте. Ловится только проверкой самого аниматора.
    ///
    /// Живёт в EditMode, а не в PlayMode, по двум причинам. Первая: тесты
    /// инспектируют ассеты и крутят <c>Animator.Update</c> вручную — кадры
    /// игры им не нужны. Вторая: проверка ассетов требует <c>UnityEditor</c>,
    /// а PlayMode-сборка обязана компилироваться и под player, где его нет.
    ///
    /// Тесты помечены <c>[Test]</c>, а не <c>[UnityTest]</c>: ни один из них
    /// не ждёт кадров. Корутина потребовала бы пустого <c>yield</c> ради
    /// соответствия атрибуту.
    /// </summary>
    public sealed class PlayerAnimationTests
    {
        // StringToHash — вызов времени выполнения, а не константа компиляции.
        private static readonly int IsMovingHash = Animator.StringToHash("IsMoving");
        private static readonly int WalkStateHash = Animator.StringToHash("Walk");
        private static readonly int IdleStateHash = Animator.StringToHash("Idle");

        private const string PrefabPath = "Assets/Woodberry/Prefabs/Player.prefab";
        private const string WalkClipPath = "Assets/Woodberry/Art/Characters/Animations/Walk.anim";

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

            var names = new System.Collections.Generic.List<string>();
            foreach (var clip in animator.runtimeAnimatorController.animationClips)
            {
                names.Add(clip.name);
            }

            Assert.That(names, Does.Contain("Walk"), "нет клипа ходьбы");
            Assert.That(names, Does.Contain("Idle"), "нет клипа покоя");
        }

        [Test]
        public void WalkClip_HasCurvesOnLegsArmsAndBody()
        {
            // Пустой клип ходьбы выглядит как статичная картинка и никак не
            // проявляется в тестах на движение.
            var clip = UnityEditor.AssetDatabase.LoadAssetAtPath<AnimationClip>(WalkClipPath);

            Assert.That(clip, Is.Not.Null, "Walk.anim не найден");
            Assert.That(clip.legacy, Is.False, "legacy-клит не воспроизводится");
            Assert.That(clip.length, Is.GreaterThan(0.1f), "длительность цикла ходьбы подозрительно мала");

            var paths = new System.Collections.Generic.List<string>();
            foreach (var binding in UnityEditor.AnimationUtility.GetCurveBindings(clip))
            {
                paths.Add(binding.path);
            }

            Assert.That(paths, Does.Contain("Visual/Body/LegBackLeft"), "нет кривой для левой ноги");
            Assert.That(paths, Does.Contain("Visual/Body/LegBackRight"), "нет кривой для правой ноги");
            Assert.That(paths, Does.Contain("Visual/Body/ArmLeft"), "нет кривой для левой руки");
            Assert.That(paths, Does.Contain("Visual/Body"), "нет покачивания корпуса");
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
            // в Walk, перезапуская блендинг каждый цикл. Персонаж едет, а ноги
            // дрожат на месте — на скриншоте это не видно.
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
        public void WalkClip_Loops_AndKeepsMovingPastOneCycleLength()
        {
            // Регрессия: клип без loopTime проигрывается один раз и замирает на
            // последнем кадре. Персонаж отшагивает 0.7 с и встаёт с поднятой
            // ногой. Состояние при этом остаётся Walk, поэтому проверка
            // «аниматор в Walk» такой отказ пропускает — нужен замер позы.
            var clip = UnityEditor.AssetDatabase.LoadAssetAtPath<AnimationClip>(WalkClipPath);
            Assert.That(clip, Is.Not.Null, "Walk.anim не найден");

            var settings = UnityEditor.AnimationUtility.GetAnimationClipSettings(clip);
            Assert.That(settings.loopTime, Is.True, "цикл ходьбы обязан быть зациклен");

            Animator animator = CreatePlayer();
            var leg = animator.transform.Find("Visual/Body/LegBackLeft");
            Assert.That(leg, Is.Not.Null, "левая нога не найдена в риге");

            animator.SetBool(IsMovingHash, true);

            float baseline = leg.localPosition.y;
            bool movedAfterCycle = false;

            // Well past one clip length: 0.7s clip vs 2.0s of playback.
            for (int i = 0; i < 40; i++)
            {
                animator.Update(0.05f);

                if (i * 0.05f > clip.length + 0.3f &&
                    Mathf.Abs(leg.localPosition.y - baseline) > 0.02f)
                {
                    movedAfterCycle = true;
                }
            }

            Assert.That(
                movedAfterCycle,
                Is.True,
                "после первого цикла нога должна продолжать качаться, а не замереть");
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
