using System;
using UnityEngine;

namespace Woodberry.Editor.ArtStudio
{
    /// <summary>
    /// Событие драйвера → Unity. Одна строка JSON в <c>events.jsonl</c>.
    ///
    /// Файл — источник правды для интерфейса. Читается по смещению, поэтому
    /// перекомпиляция, закрытие окна и падение редактора не рвут прогон:
    /// события не теряются, а дочитываются с того места, где читатель встал.
    ///
    /// Имена полей — ASCII и не меняются: их пишет Python. Локализация живёт
    /// в значениях (label, text), а не в ключах.
    /// </summary>
    [Serializable]
    public sealed class ArtEvent
    {
        public const string Ready = "ready";
        public const string ServerState = "server_state";
        public const string Log = "log";
        public const string CatalogEvent = "catalog";

        /// <summary>
        /// Промпты стенда целиком, JSON в <c>detail</c>. Приходит по команде
        /// <c>prompts</c>, а не сам: промпты нужны, когда человек смотрит на
        /// вкладку, а не постоянно.
        /// </summary>
        public const string Prompts = "prompts";
        public const string ItemAdded = "item_added";
        public const string ItemProgress = "item_progress";
        public const string ItemDone = "item_done";
        public const string ItemFailed = "item_failed";

        /// <summary>
        /// Кадр отрисован и лежит в raw. Приходит, пока стенд ещё не закончил
        /// прогон: без него окно считало текущим первый незакрытый кадр и
        /// держало полосу на 100 % весь прогон.
        /// </summary>
        public const string ItemDrawn = "item_drawn";

        /// <summary>
        /// Прогресс этапа работы. Этап не принадлежит кадру (вырезка бьёт по
        /// всей папке raw), поэтому <c>id</c> пуст, а номер этапа приходит в
        /// <see cref="stage"/> вместе с <see cref="done"/> из <see cref="total"/>.
        /// </summary>
        public const string ItemStage = "item_stage";

        public const string ReviewCandidate = "review_candidate";
        public const string Approved = "approved";

        /// <summary>
        /// Сводка стенда: модель, энкодер, VAE, сэмплер. Необязательное
        /// событие (старые окна его игнорируют), поэтому версию протокола не
        /// поднимает; приходит после шапки прогона `shoot.py`.
        /// </summary>
        public const string DriverInfo = "driver_info";
        public const string Error = "error";
        public const string CommandRejected = "command_rejected";

        /// <summary>Монотонный номер события. Пропуск нумерации = прогон сброшен.</summary>
        public int seq;

        public string kind;

        /// <summary>
        /// Версия протокола драйвера. Заполняется только в <c>ready</c>:
        /// окно сравнивает её со своей и показывает баннер при расхождении.
        /// </summary>
        public int protocol;

        /// <summary>Идентификатор кадра стенда: p_Roles_Priest_b, bg_City_Guild.</summary>
        public string id;

        public string pool;

        public string label;

        /// <summary>Прогресс 0..100: кадр или очередь в целом.</summary>
        public int percent;

        /// <summary>
        /// Этап работы: prepare, sample, cut, check, copy, warmup. Приходит
        /// в <c>item_progress</c> (этап кадра) и в <c>item_stage</c> (этап
        /// прогона). Пустая строка у старого драйвера — это не поломка, а
        /// старый протокол, и окно обязано рисовать кадр и без неё.
        /// </summary>
        public string stage;

        /// <summary>Сколько единиц этапа сделано. Только для <c>item_stage</c>.</summary>
        public int done;

        /// <summary>Сколько единиц в этапе всего. Только для <c>item_stage</c>.</summary>
        public int total;

        /// <summary>
        /// Человекочитаемый текст: строка лога, причина провала, подпись фазы
        /// прогресса («шаг 4/10»), сводка стенда в <c>driver_info</c>.
        /// </summary>
        public string text;

        /// <summary>Путь: ассет в проекте или кандидат в ревью.</summary>
        public string path;

        /// <summary>Уровень лога: info, warn, error. Только для kind = log.</summary>
        public string level;

        /// <summary>
        /// Полезная нагрузка события <c>catalog</c>: JSON-массив пулов
        /// (строка, а не вложенный объект — так решил протокол). Разбирается
        /// в <see cref="ArtDriverState"/>.
        /// </summary>
        public string detail;

        public static ArtEvent Of(string kind)
        {
            return new ArtEvent { kind = kind };
        }

        /// <summary>Разбирает строку события. false — строка битая или пустая.</summary>
        public static bool TryParse(string line, out ArtEvent parsed)
        {
            parsed = null;

            if (string.IsNullOrWhiteSpace(line))
            {
                return false;
            }

            try
            {
                ArtEvent candidate = JsonUtility.FromJson<ArtEvent>(line);
                if (candidate == null || string.IsNullOrEmpty(candidate.kind))
                {
                    return false;
                }

                parsed = candidate;
                return true;
            }
            catch (Exception)
            {
                // JsonUtility на битом JSON бросает исключение. Строка
                // события — входные данные, поэтому обработчик не должен
                // зависеть от поведения движка на мусоре.
                return false;
            }
        }
    }
}
