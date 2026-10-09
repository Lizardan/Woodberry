using System;
using UnityEngine;

namespace Woodberry.Editor.ArtStudio
{
    /// <summary>
    /// Команда Unity → драйверу. Одна строка JSON в <c>commands.jsonl</c>.
    ///
    /// Все поля необязательные кроме <see cref="Cmd"/>: JsonUtility не умеет
    /// сериализовать null и не даёт словарей, поэтому вместо «одного класса на
    /// команду» — один плоский набор полей. Драйвер игнорирует то, что не
    /// относится к его команде.
    ///
    /// <see cref="Nonce"/> уникален для каждой команды: если окно пережило
    /// перекомпиляцию и не знает, дошла ли команда, драйвер по nonce
    /// отвечает — и команду нельзя применить дважды.
    /// </summary>
    [Serializable]
    public sealed class ArtCommand
    {
        public const string Ping = "ping";
        public const string Stop = "stop";
        public const string ServerStart = "server_start";
        public const string ServerStop = "server_stop";
        public const string Catalog = "catalog";
        public const string Generate = "generate";
        public const string Approve = "approve";
        public const string Reject = "reject";
        public const string RegenerateMarked = "regen_marked";

        /// <summary>Имя команды из констант выше.</summary>
        public string cmd;

        /// <summary>Версия протокола пишущей стороны.</summary>
        public int protocol;

        public string pool;

        public int count;

        public string style;

        public string light;

        /// <summary>Путь кандидата в ревью или ассета в проекте.</summary>
        public string target;

        public string comment;

        public string nonce;

        public static ArtCommand Create(string cmd)
        {
            return new ArtCommand { cmd = cmd, protocol = ArtProtocol.Version };
        }

        public ArtCommand WithNonce(string value)
        {
            nonce = value;
            return this;
        }

        public string ToJson()
        {
            return JsonUtility.ToJson(this);
        }

        /// <summary>Разбирает строку команды. false — строка битая.</summary>
        public static bool TryParse(string line, out ArtCommand command)
        {
            command = null;

            if (string.IsNullOrWhiteSpace(line))
            {
                return false;
            }

            try
            {
                ArtCommand parsed = JsonUtility.FromJson<ArtCommand>(line);
                if (parsed == null || string.IsNullOrEmpty(parsed.cmd))
                {
                    return false;
                }

                command = parsed;
                return true;
            }
            catch (Exception)
            {
                // JsonUtility на битом JSON бросает ArgumentException, а не
                // возвращает null. Команда из файла — это входные данные,
                // поэтому поведение парсера не должно зависеть от версии
                // движка: любая ошибка разбора означает «строка битая».
                return false;
            }
        }
    }
}
