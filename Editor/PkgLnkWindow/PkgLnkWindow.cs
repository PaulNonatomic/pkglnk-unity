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

		public void CreateGUI()
		{
			minSize = new Vector2(480, 600);

			var root = rootVisualElement;

			var baseSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(
				"Packages/com.nonatomic.pkglnk/Editor/PkgLnkWindow/PkgLnkWindowStyles.uss");
			if (baseSheet != null) root.styleSheets.Add(baseSheet);

			root.AddToClassList("pkglnk-window");

			_browserView = new PackageBrowserView();
			root.Add(_browserView);
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
