using System;

namespace Woodberry.Editor.ArtStudio
{
    /// <summary>
    /// Промпты стенда, разобранные из ответа драйвера: стили, свет, негатив,
    /// персонажи, места и параметры генерации.
    ///
    /// ЗАЧЕМ ЭТО ОТДЕЛЬНЫМ ТИПОМ, А НЕ СРАЗУ В ОКНЕ. Разбор входных данных
    /// драйвера — это логика, которую надо проверить EditMode-тестом без
    /// редактора и без стенда. Окно получает уже готовый список и раскладывает
    /// его по группам; если формат поменяется, падать будет тест, а не пустая
    /// вкладка у человека.
    ///
    /// ПОЧЕМУ НЕ `JsonUtility` НАПРЯМУЮ ПО КОРНЮ. Он не разбирает массив в
    /// корне и не разбирает словари, поэтому драйвер отдаёт всё объектом, а
    /// стили — списком.
    /// </summary>
    public sealed class ArtPrompts
    {
        public PromptStyle[] Styles { get; private set; }

        public PromptLight[] Lights { get; private set; }

        public string NegativeCommon { get; private set; }

        public string NegativeBackground { get; private set; }

        public PromptCharacter[] Characters { get; private set; }

        public PromptBackground[] Backgrounds { get; private set; }

        public PromptGen Gen { get; private set; }

        /// <summary>
        /// Ничего не пришло или пришёл пустой набор. Отличается от «пришёл и
        /// разобрался в пустоту»: во втором случае человек должен видеть пустые
        /// группы, а в первом — что-то должно быть сказано вслух.
        /// </summary>
        public bool IsEmpty =>
            (Styles == null || Styles.Length == 0)
            && (Lights == null || Lights.Length == 0)
            && (Characters == null || Characters.Length == 0)
            && (Backgrounds == null || Backgrounds.Length == 0);

        /// <summary>
        /// Разбирает ответ драйвера. false — строка битая или пустая; в этом
        /// случае промпты остаются пустым набором, а не частично разобранными.
        /// </summary>
        public static bool TryParse(string json, out ArtPrompts prompts)
        {
            prompts = null;

            if (string.IsNullOrWhiteSpace(json))
            {
                return false;
            }

            try
            {
                PromptsDto dto = UnityEngine.JsonUtility.FromJson<PromptsDto>(json);
                if (dto == null)
                {
                    return false;
                }

                prompts = new ArtPrompts
                {
                    Styles = dto.style ?? Array.Empty<PromptStyle>(),
                    Lights = dto.light ?? Array.Empty<PromptLight>(),
                    NegativeCommon = dto.negative?.common,
                    NegativeBackground = dto.negative?.background,
                    Characters = dto.characters ?? Array.Empty<PromptCharacter>(),
                    Backgrounds = dto.backgrounds ?? Array.Empty<PromptBackground>(),
                    Gen = dto.gen,
                };
                return true;
            }
            catch (Exception)
            {
                // Вход приходит из драйвера, а не из сборки: мусорный JSON
                // обязан тихо оставить вкладку пустой с понятной подписью,
                // а не уронить окно редактора.
                return false;
            }
        }

        // ------------------------------------------------------------- DTO

        [Serializable]
        private sealed class PromptsDto
        {
            public PromptStyle[] style;
            public PromptLight[] light;
            public NegativeDto negative;
            public PromptCharacter[] characters;
            public PromptBackground[] backgrounds;

            /// <summary>Ключ в JSON — `gen`: `params` в C# зарезервировано.</summary>
            public PromptGen gen;
        }

        [Serializable]
        private sealed class NegativeDto
        {
            public string common;
            public string background;
        }
    }

    /// <summary>Стиль рисунка стенда: ключ (`ink`) и его словесное описание.</summary>
    [Serializable]
    public sealed class PromptStyle
    {
        public string key;
        public string text;
    }

    /// <summary>Схема освещения: ключ и текст, добавляемый в промпт.</summary>
    [Serializable]
    public sealed class PromptLight
    {
        public string key;
        public string text;
    }

    /// <summary>Персонаж стенда в разрезе промпта.</summary>
    [Serializable]
    public sealed class PromptCharacter
    {
        public string id;
        public string name;

        /// <summary>Ключ света; сам текст лежит один раз в группе «Свет».</summary>
        public string light;

        public string pose;
        public string subject;
    }

    /// <summary>Место (фон) стенда в разрезе промпта.</summary>
    [Serializable]
    public sealed class PromptBackground
    {
        public string id;
        public string name;
        public string scene;
        public string light;
    }

    /// <summary>Числовые параметры генерации стенда.</summary>
    [Serializable]
    public sealed class PromptGen
    {
        public int steps;
        public float cfg;
        public string scheduler;
        public int seed_base;

        public int[] subject_size;
        public int[] background_size;
        public int[] chroma;
    }
}