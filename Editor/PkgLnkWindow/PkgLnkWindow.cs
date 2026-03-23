using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Nonatomic.PkgLnk.Editor.PkgLnkWindow
{
	/// <summary>
	/// Main EditorWindow for browsing and installing pkglnk.dev packages.
	/// </summary>
	public class PkgLnkWindow : EditorWindow
	{
		private const string ThemePrefKey = "PkgLnk.Theme";
		private const string ThemeDark = "dark";
		private const string ThemeLight = "light";
		private const string ThemeGrey = "grey";

		private static readonly string[] ThemeCycle = { ThemeDark, ThemeLight, ThemeGrey };

		private PackageBrowserView _browserView;
		private bool _refreshPending;

		private StyleSheet _darkSheet;
		private StyleSheet _lightSheet;
		private StyleSheet _greySheet;
		private StyleSheet _activeThemeSheet;
		private string _currentTheme;
		private VisualElement _toggleIcon;
		private Texture2D _darkIcon;
		private Texture2D _lightIcon;
		private Texture2D _greyIcon;

		[MenuItem("Tools/PkgLnk/PkgLnk Window")]
		public static void ShowWindow()
		{
			var wnd = GetWindow<PkgLnkWindow>();
			wnd.titleContent = new GUIContent("PkgLnk");
			wnd.minSize = new Vector2(480, 600);
		}

		public void CreateGUI()
		{
			var root = rootVisualElement;

			var baseSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(
				"Packages/com.nonatomic.pkglnk/Editor/PkgLnkWindow/PkgLnkWindowStyles.uss");
			if (baseSheet != null) root.styleSheets.Add(baseSheet);

			_darkSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(
				"Packages/com.nonatomic.pkglnk/Editor/PkgLnkWindow/PkgLnkWindowStylesDark.uss");
			_lightSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(
				"Packages/com.nonatomic.pkglnk/Editor/PkgLnkWindow/PkgLnkWindowStylesLight.uss");
			_greySheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(
				"Packages/com.nonatomic.pkglnk/Editor/PkgLnkWindow/PkgLnkWindowStylesGrey.uss");

			_currentTheme = LoadThemePreference();
			_activeThemeSheet = GetSheetForTheme(_currentTheme);
			if (_activeThemeSheet != null) root.styleSheets.Add(_activeThemeSheet);

			root.AddToClassList("pkglnk-window");

			_browserView = new PackageBrowserView();
			root.Add(_browserView);

			CreateThemeToggle();
		}

		private void CreateThemeToggle()
		{
			_darkIcon = AssetDatabase.LoadAssetAtPath<Texture2D>(
				"Packages/com.nonatomic.pkglnk/Editor/Icons/toggle-icon-green.png");
			_lightIcon = AssetDatabase.LoadAssetAtPath<Texture2D>(
				"Packages/com.nonatomic.pkglnk/Editor/Icons/toggle-icon-white.png");
			_greyIcon = AssetDatabase.LoadAssetAtPath<Texture2D>(
				"Packages/com.nonatomic.pkglnk/Editor/Icons/toggle-icon-grey.png");

			var toggleButton = new Button(OnThemeToggleClicked);
			toggleButton.AddToClassList("theme-toggle");

			_toggleIcon = new VisualElement();
			_toggleIcon.AddToClassList("theme-toggle-icon");
			_toggleIcon.pickingMode = PickingMode.Ignore;
			UpdateToggleIcon();
			toggleButton.Add(_toggleIcon);

			_browserView.AddToHeader(toggleButton);
		}

		private void OnThemeToggleClicked()
		{
			var root = rootVisualElement;

			if (_activeThemeSheet != null)
			{
				root.styleSheets.Remove(_activeThemeSheet);
			}

			_currentTheme = GetNextTheme(_currentTheme);
			_activeThemeSheet = GetSheetForTheme(_currentTheme);

			if (_activeThemeSheet != null)
			{
				root.styleSheets.Add(_activeThemeSheet);
			}

			EditorPrefs.SetString(ThemePrefKey, _currentTheme);
			UpdateToggleIcon();
		}

		private void UpdateToggleIcon()
		{
			if (_toggleIcon == null) return;
			var icon = GetIconForTheme(_currentTheme);
			_toggleIcon.style.backgroundImage = icon != null
				? new StyleBackground(icon)
				: StyleKeyword.None;
		}

		private StyleSheet GetSheetForTheme(string theme)
		{
			return theme switch
			{
				ThemeLight => _lightSheet,
				ThemeGrey => _greySheet,
				_ => _darkSheet
			};
		}

		private Texture2D GetIconForTheme(string theme)
		{
			return theme switch
			{
				ThemeLight => _lightIcon,
				ThemeGrey => _greyIcon,
				_ => _darkIcon
			};
		}

		private static string GetNextTheme(string current)
		{
			for (var i = 0; i < ThemeCycle.Length; i++)
			{
				if (ThemeCycle[i] != current) continue;
				return ThemeCycle[(i + 1) % ThemeCycle.Length];
			}

			return ThemeDark;
		}

		private static string LoadThemePreference()
		{
			if (!EditorPrefs.HasKey(ThemePrefKey))
			{
				return EditorGUIUtility.isProSkin ? ThemeDark : ThemeLight;
			}

			return EditorPrefs.GetString(ThemePrefKey);
		}

		private void OnFocus()
		{
			ScheduleRefresh();
		}

		private void ScheduleRefresh()
		{
			if (_refreshPending) return;
			_refreshPending = true;
			EditorApplication.delayCall += () =>
			{
				if (this == null) return;
				_browserView?.RefreshInstalledState();
				_refreshPending = false;
			};
		}
	}
}
