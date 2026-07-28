using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Nonatomic.PkgLnk.Editor.Localization
{
	/// <summary>
	/// Small string-catalogue system for editor UI text.
	///
	/// - Locale JSON files live in Editor/Localization/{locale}.json and are
	///   flat { "key": "value" } dictionaries. English (en) is the source of
	///   truth; missing keys in other locales fall back to English, then to
	///   the raw key so nothing renders blank.
	/// - CurrentLocale is persisted in EditorPrefs and auto-detected from
	///   Application.systemLanguage on first launch.
	/// - Subscribe to OnLocaleChanged to rebuild UI text after a switch.
	///
	/// Not tied to Unity's com.unity.localization package (which is built
	/// for runtime content pipelines and would be massive overkill for
	/// editor chrome).
	/// </summary>
	public static class L10n
	{
		public const string EnglishLocale = "en";
		public const string SimplifiedChineseLocale = "zh-Hans";

		private const string EditorPrefsKey = "PkgLnk.Locale";
		private const string LocalizationFolder = "Packages/com.nonatomic.pkglnk/Editor/Localization";

		/// <summary>Locales the package currently ships. Keep in sync with the JSON files on disk.</summary>
		public static readonly string[] AvailableLocales =
		{
			EnglishLocale,
			SimplifiedChineseLocale,
		};

		/// <summary>Human-readable label for each locale, shown in the locale picker.</summary>
		public static readonly Dictionary<string, string> LocaleDisplayNames = new Dictionary<string, string>
		{
			{ EnglishLocale, "English" },
			{ SimplifiedChineseLocale, "简体中文" },
		};

		private static readonly Dictionary<string, Dictionary<string, string>> Tables =
			new Dictionary<string, Dictionary<string, string>>();

		private static string _currentLocale;
		private static bool _initialised;

		public static event Action OnLocaleChanged;

		public static string CurrentLocale
		{
			get
			{
				EnsureInitialised();
				return _currentLocale;
			}
		}

		[InitializeOnLoadMethod]
		private static void EagerInitialise()
		{
			EnsureInitialised();
		}

		private static void EnsureInitialised()
		{
			if (_initialised) return;
			_initialised = true;

			LoadAllTables();
			_currentLocale = LoadPersistedLocale();
		}

		private static string LoadPersistedLocale()
		{
			var saved = EditorPrefs.GetString(EditorPrefsKey, string.Empty);
			if (!string.IsNullOrEmpty(saved) && Tables.ContainsKey(saved)) return saved;

			// Auto-detect from system language on first run.
			var systemLang = Application.systemLanguage;
			if (systemLang == SystemLanguage.ChineseSimplified) return SimplifiedChineseLocale;
			if (systemLang == SystemLanguage.Chinese) return SimplifiedChineseLocale;

			return EnglishLocale;
		}

		public static void SetLocale(string locale)
		{
			EnsureInitialised();
			if (string.IsNullOrEmpty(locale)) return;
			if (!Tables.ContainsKey(locale)) return;
			if (_currentLocale == locale) return;

			_currentLocale = locale;
			EditorPrefs.SetString(EditorPrefsKey, locale);
			OnLocaleChanged?.Invoke();
		}

		/// <summary>
		/// Resolve a key against the current locale, falling back to English then
		/// to the raw key so a missing translation is visible but not blank.
		/// </summary>
		public static string Get(string key)
		{
			EnsureInitialised();

			if (Tables.TryGetValue(_currentLocale, out var current) && current.TryGetValue(key, out var val))
			{
				return val;
			}

			if (_currentLocale != EnglishLocale &&
				Tables.TryGetValue(EnglishLocale, out var en) && en.TryGetValue(key, out var enVal))
			{
				return enVal;
			}

			return key;
		}

		/// <summary>Get(key) + string.Format for simple parameterised strings.</summary>
		public static string Format(string key, params object[] args)
		{
			var template = Get(key);
			if (args == null || args.Length == 0) return template;
			try { return string.Format(template, args); }
			catch (FormatException) { return template; }
		}

		private static void LoadAllTables()
		{
			Tables.Clear();
			foreach (var locale in AvailableLocales)
			{
				var path = $"{LocalizationFolder}/{locale}.json";
				var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
				if (asset == null)
				{
					// Fall back to reading the file directly in case AssetDatabase
					// hasn't picked it up yet (fresh install / import).
					var absolute = Path.Combine(Application.dataPath, "..", path);
					if (File.Exists(absolute))
					{
						Tables[locale] = ParseFlatJson(File.ReadAllText(absolute));
					}
					else
					{
						Tables[locale] = new Dictionary<string, string>();
					}
					continue;
				}
				Tables[locale] = ParseFlatJson(asset.text);
			}
		}

		/// <summary>
		/// Flat { "key": "value" } JSON parser — avoids adding a dependency
		/// for a shape we fully control. Ignores nested objects and arrays.
		/// </summary>
		private static Dictionary<string, string> ParseFlatJson(string json)
		{
			var result = new Dictionary<string, string>();
			if (string.IsNullOrEmpty(json)) return result;

			// Matches "key" : "value" pairs, tolerating whitespace and escaped
			// quotes inside the value. Non-greedy value capture stops at the
			// first unescaped closing quote.
			var pattern = @"""((?:[^""\\]|\\.)+)""\s*:\s*""((?:[^""\\]|\\.)*)""";
			foreach (Match match in Regex.Matches(json, pattern))
			{
				var key = Unescape(match.Groups[1].Value);
				var value = Unescape(match.Groups[2].Value);
				result[key] = value;
			}
			return result;
		}

		private static string Unescape(string s)
		{
			return s.Replace("\\\"", "\"").Replace("\\n", "\n").Replace("\\t", "\t").Replace("\\\\", "\\");
		}

		/// <summary>
		/// A CJK-capable OS font to swap onto the root element when a CJK
		/// locale is active. Returns null on non-CJK locales or when no
		/// suitable OS font can be found.
		/// </summary>
		public static Font GetLocaleFont()
		{
			if (_currentLocale != SimplifiedChineseLocale) return null;

			string[] candidates;
			switch (Application.platform)
			{
				case RuntimePlatform.WindowsEditor:
					candidates = new[] { "Microsoft YaHei UI", "Microsoft YaHei", "SimHei", "SimSun" };
					break;
				case RuntimePlatform.OSXEditor:
					candidates = new[] { "PingFang SC", "Hiragino Sans GB", "STHeiti" };
					break;
				default:
					candidates = new[] { "Noto Sans CJK SC", "WenQuanYi Micro Hei", "Droid Sans Fallback" };
					break;
			}

			foreach (var name in candidates)
			{
				try
				{
					var font = Font.CreateDynamicFontFromOSFont(name, 12);
					if (font != null) return font;
				}
				catch { /* try next */ }
			}
			return null;
		}
	}
}
