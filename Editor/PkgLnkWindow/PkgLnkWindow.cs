using Nonatomic.PkgLnk.Editor.Localization;
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
		private PackageBrowserView _browserView;
		private bool _refreshPending;

		[MenuItem("Tools/PkgLnk/PkgLnk Window")]
		public static void ShowWindow()
		{
			var wnd = GetWindow<PkgLnkWindow>();
			wnd.titleContent = new GUIContent("PkgLnk");
			wnd.minSize = new Vector2(480, 600);
		}

		private void OnEnable()
		{
			L10n.OnLocaleChanged += RebuildForLocaleChange;
		}

		private void OnDisable()
		{
			L10n.OnLocaleChanged -= RebuildForLocaleChange;
		}

		public void CreateGUI()
		{
			minSize = new Vector2(480, 600);

			var root = rootVisualElement;
			root.Clear();

			var baseSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(
				"Packages/com.nonatomic.pkglnk/Editor/PkgLnkWindow/PkgLnkWindowStyles.uss");
			if (baseSheet != null) root.styleSheets.Add(baseSheet);

			root.AddToClassList("pkglnk-window");

			// CJK locales get a system font swapped onto the root so glyphs
			// don't render as tofu on editors whose bundled font lacks CJK
			// coverage. No-op for English/Latin locales.
			var localeFont = L10n.GetLocaleFont();
			if (localeFont != null)
			{
				root.style.unityFont = new StyleFont(localeFont);
			}

			_browserView = new PackageBrowserView();
			root.Add(_browserView);
		}

		private void RebuildForLocaleChange()
		{
			// Tear down and rebuild the entire visual tree so every literal
			// string picked up from L10n.Get at construction time re-reads
			// against the new locale.
			CreateGUI();
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
