using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MusicBot
{
    public static class EnvironmentVariablesHandler
    {
        public static Dictionary<string, string> Variables { get; private set; } = new Dictionary<string, string>();

        public static void Load()
        {
            Variables = ReadJSON(".env") ?? new Dictionary<string, string>();
        }

        public static void Save()
        {
            WriteJSON(Variables, ".env");
        }

        /// <summary>
        /// Сохраняет словарь значений в json-файл.
        /// </summary>
        /// <param name="dict">Словарь</param>
        /// <param name="path">Путь к файлу.</param>
        internal static void WriteJSON(Dictionary<string, string> dict, string path)
        {
            StreamWriter stream = new StreamWriter(path);
            try
            {
                string text = JsonConvert.SerializeObject(dict);
                stream.Write(text);
            }
            finally
            {
                stream.Close();
            }
        }

        /// <summary>
        /// Метод для считывания JSON-файла и получения значения. Используется для чтения токена.
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        internal static Dictionary<string, string>? ReadJSON(string path)
        {
            Dictionary<string, string>? ret = null;
            StreamReader? stream = null;
            try
            {
                stream = new StreamReader(path);
                string text = stream.ReadToEnd();
                object t = JsonConvert.DeserializeObject(text, typeof(Dictionary<string, string>))!;
                ret = t as Dictionary<string, string>;
            }
            finally
            {
                stream?.Close();
            }
            return ret;
        }
    }
}
