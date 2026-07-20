using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;

namespace VanishFF
{
    // settings.json рядом с exe (ТЗ 1.4): плоский словарь ключ-значение.
    // Запись — при выходе и перед стартом каждой операции.
    static class Settings
    {
        static Dictionary<string, object> data = new Dictionary<string, object>();

        static string FilePath
        {
            get { return Path.Combine(Program.AppDir, "settings.json"); }
        }

        public static void Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var ser = new JavaScriptSerializer();
                    var d = ser.Deserialize<Dictionary<string, object>>(
                        File.ReadAllText(FilePath));
                    if (d != null) data = d;
                }
            }
            catch
            {
                // битый файл настроек — не повод не запуститься
                data = new Dictionary<string, object>();
            }
        }

        public static void Save()
        {
            try
            {
                var ser = new JavaScriptSerializer();
                File.WriteAllText(FilePath, ser.Serialize(data));
            }
            catch { }
        }

        public static string GetS(string key, string def)
        {
            object v;
            if (data.TryGetValue(key, out v) && v != null) return v.ToString();
            return def;
        }

        public static int GetI(string key, int def)
        {
            object v;
            if (data.TryGetValue(key, out v) && v != null)
            {
                try { return Convert.ToInt32(v); }
                catch { }
            }
            return def;
        }

        public static bool GetB(string key, bool def)
        {
            object v;
            if (data.TryGetValue(key, out v) && v != null)
            {
                try { return Convert.ToBoolean(v); }
                catch { }
            }
            return def;
        }

        public static void Set(string key, object val)
        {
            data[key] = val;
        }
    }
}
