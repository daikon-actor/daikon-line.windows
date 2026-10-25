using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace DaikonLine
{
    /// <summary>表示するモニター</summary>
    internal enum MonitorTarget
    {
        All,
        Primary,
        Active
    }

    /// <summary>設定（data\settings.json）。範囲外の値は Normalize で丸める。</summary>
    internal sealed class LineSettings
    {
        public const int MinThickness = 1;
        public const int MaxThickness = 16;
        public const int MinTransparency = 0;
        public const int MaxTransparency = 90; // これ以上だとラインが見えなくなる
        public const int MinInterval = 50;
        public const int MaxInterval = 1000;

        public static readonly Color DefaultJapanese = ColorTranslator.FromHtml("#18B957");
        public static readonly Color DefaultAlphanumeric = ColorTranslator.FromHtml("#E54848");
        public static readonly Color DefaultUnknown = ColorTranslator.FromHtml("#808080");

        public bool Visible = true;
        public int Thickness = 4;
        public int Transparency = 0; // 透過率（%）。0 = 不透明
        public MonitorTarget Monitors = MonitorTarget.All;
        public int PollIntervalMs = 100;
        public bool HideInFullscreen = true;
        public Color Japanese = DefaultJapanese;
        public Color Alphanumeric = DefaultAlphanumeric;
        public Color Unknown = DefaultUnknown;

        public LineSettings Clone()
        {
            return (LineSettings)MemberwiseClone();
        }

        public void Normalize()
        {
            Thickness = Clamp(Thickness, MinThickness, MaxThickness);
            Transparency = Clamp(Transparency, MinTransparency, MaxTransparency);
            PollIntervalMs = Clamp(PollIntervalMs, MinInterval, MaxInterval);
        }

        /// <summary>レイヤードウィンドウの不透明度（0〜255）</summary>
        public byte Alpha
        {
            get { return (byte)Math.Round(255.0 * (100 - Transparency) / 100.0); }
        }

        public Color ColorOf(InputMode mode)
        {
            switch (mode)
            {
                case InputMode.Japanese: return Japanese;
                case InputMode.Alphanumeric: return Alphanumeric;
                default: return Unknown;
            }
        }

        private static int Clamp(int v, int min, int max)
        {
            return v < min ? min : (v > max ? max : v);
        }
    }

    /// <summary>settings.json の読み書き。</summary>
    internal sealed class ConfigStore
    {
        private readonly string _path;

        public ConfigStore(string appDir)
        {
            _path = Path.Combine(Path.Combine(appDir, "data"), "settings.json");
        }

        public string FilePath
        {
            get { return _path; }
        }

        /// <summary>読み込む。ファイルが無ければ既定値。壊れていれば既定値と、エラーを error に返す。</summary>
        public LineSettings Load(out string error)
        {
            error = null;
            LineSettings s = new LineSettings();
            if (!File.Exists(_path)) return s;

            try
            {
                string json = File.ReadAllText(_path, Encoding.UTF8);
                IDictionary<string, object> root = new JavaScriptSerializer().DeserializeObject(json) as IDictionary<string, object>;
                if (root == null) throw new FormatException("JSON の最上位が { } ではありません。");
                root = IgnoreCase(root);

                s.Visible = GetBool(root, "visible", s.Visible);
                s.Thickness = GetInt(root, "thickness", s.Thickness);
                s.Transparency = GetInt(root, "transparency", s.Transparency);
                s.PollIntervalMs = GetInt(root, "pollIntervalMs", s.PollIntervalMs);
                s.HideInFullscreen = GetBool(root, "hideInFullscreen", s.HideInFullscreen);
                s.Monitors = ParseMonitors(GetString(root, "monitors"), s.Monitors);

                object colorsObj;
                IDictionary<string, object> colors = null;
                if (root.TryGetValue("colors", out colorsObj)) colors = colorsObj as IDictionary<string, object>;
                if (colors != null)
                {
                    colors = IgnoreCase(colors);
                    s.Japanese = ParseColor(GetString(colors, "japanese"), s.Japanese);
                    s.Alphanumeric = ParseColor(GetString(colors, "alphanumeric"), s.Alphanumeric);
                    s.Unknown = ParseColor(GetString(colors, "unknown"), s.Unknown);
                }
            }
            catch (Exception ex)
            {
                error = _path + "\n  " + ex.Message;
                s = new LineSettings();
            }
            s.Normalize();
            return s;
        }

        /// <summary>保存する。一時ファイルに書いてから置き換える。</summary>
        public void Save(LineSettings s)
        {
            string dir = Path.GetDirectoryName(_path);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            string tmp = _path + ".tmp";
            File.WriteAllText(tmp, ToJson(s), new UTF8Encoding(false));
            if (File.Exists(_path)) File.Replace(tmp, _path, null);
            else File.Move(tmp, _path);
        }

        private static string ToJson(LineSettings s)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("{\r\n");
            sb.Append("  \"visible\": ").Append(s.Visible ? "true" : "false").Append(",\r\n");
            sb.Append("  \"thickness\": ").Append(s.Thickness.ToString(CultureInfo.InvariantCulture)).Append(",\r\n");
            sb.Append("  \"transparency\": ").Append(s.Transparency.ToString(CultureInfo.InvariantCulture)).Append(",\r\n");
            sb.Append("  \"monitors\": \"").Append(MonitorsToString(s.Monitors)).Append("\",\r\n");
            sb.Append("  \"pollIntervalMs\": ").Append(s.PollIntervalMs.ToString(CultureInfo.InvariantCulture)).Append(",\r\n");
            sb.Append("  \"hideInFullscreen\": ").Append(s.HideInFullscreen ? "true" : "false").Append(",\r\n");
            sb.Append("  \"colors\": {\r\n");
            sb.Append("    \"japanese\": \"").Append(ToHex(s.Japanese)).Append("\",\r\n");
            sb.Append("    \"alphanumeric\": \"").Append(ToHex(s.Alphanumeric)).Append("\",\r\n");
            sb.Append("    \"unknown\": \"").Append(ToHex(s.Unknown)).Append("\"\r\n");
            sb.Append("  }\r\n");
            sb.Append("}\r\n");
            return sb.ToString();
        }

        public static string ToHex(Color c)
        {
            return "#" + c.R.ToString("X2", CultureInfo.InvariantCulture) +
                   c.G.ToString("X2", CultureInfo.InvariantCulture) +
                   c.B.ToString("X2", CultureInfo.InvariantCulture);
        }

        public static string MonitorsToString(MonitorTarget m)
        {
            switch (m)
            {
                case MonitorTarget.Primary: return "primary";
                case MonitorTarget.Active: return "active";
                default: return "all";
            }
        }

        private static MonitorTarget ParseMonitors(string v, MonitorTarget fallback)
        {
            if (v == null) return fallback;
            switch (v.Trim().ToLowerInvariant())
            {
                case "all": return MonitorTarget.All;
                case "primary": return MonitorTarget.Primary;
                case "active": return MonitorTarget.Active;
                default: return fallback;
            }
        }

        // "#RRGGBB" だけを受け付ける（不正なら既定値）
        private static Color ParseColor(string v, Color fallback)
        {
            if (v == null) return fallback;
            v = v.Trim();
            if (v.Length != 7 || v[0] != '#') return fallback;
            int rgb;
            if (!int.TryParse(v.Substring(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out rgb)) return fallback;
            return Color.FromArgb((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
        }

        private static IDictionary<string, object> IgnoreCase(IDictionary<string, object> src)
        {
            Dictionary<string, object> d = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, object> kv in src) d[kv.Key] = kv.Value;
            return d;
        }

        private static string GetString(IDictionary<string, object> d, string key)
        {
            object v;
            return d.TryGetValue(key, out v) ? v as string : null;
        }

        private static bool GetBool(IDictionary<string, object> d, string key, bool fallback)
        {
            object v;
            if (d.TryGetValue(key, out v) && v is bool) return (bool)v;
            return fallback;
        }

        private static int GetInt(IDictionary<string, object> d, string key, int fallback)
        {
            object v;
            if (!d.TryGetValue(key, out v) || v == null) return fallback;
            if (v is int) return (int)v;
            if (v is long) return (int)Math.Max(int.MinValue, Math.Min(int.MaxValue, (long)v));
            if (v is decimal) return (int)Math.Max(int.MinValue, Math.Min(int.MaxValue, Math.Round((decimal)v)));
            if (v is double) return (int)Math.Max(int.MinValue, Math.Min(int.MaxValue, Math.Round((double)v)));
            return fallback;
        }
    }
}
