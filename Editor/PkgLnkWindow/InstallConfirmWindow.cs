using System;
using Nonatomic.PkgLnk.Editor.Api;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Nonatomic.PkgLnk.Editor.PkgLnkWindow
{
	/// <summary>
	/// Confirmation popup for browser-initiated package installs.
	/// Shows package slug and URL, requires user to click Install.
	/// Then shows animated progress through install phases.
	/// </summary>
	public class InstallConfirmWindow : EditorWindow
	{
		private static readonly char[] SpinnerChars = { '|', '/', '-', '\\' };
		private const float CreepRate = 1.5f;
		private const float MaxDeltaTime = 0.1f;
		private const double SpinnerInterval = 0.15;

		private string _installUrl;
		private string _slug;

		// UI elements
		private VisualElement _confirmSection;
		private VisualElement _progressSection;
		private VisualElement _resultSection;
		private VisualElement _progressTrack;
		private VisualElement _progressFill;
		private Label _spinnerLabel;
		private Label _statusLabel;
		private Label _resultLabel;
		private Button _installButton;
		private Button _cancelButton;
		private Button _closeButton;

		// Animation state
		private bool _isAnimating;
		private float _currentPercent;
		private float _targetPercent;
		private float _ceilPercent;
		private double _lastAnimTime;
		private double _lastSpinnerTime;
		private int _spinnerFrame;

		/// <summary>
		/// Opens the confirmation window for a browser install request.
		/// </summary>
		public static void Show(string url)
		{
			var slug = ParseSlugFromUrl(url);

			var wnd = CreateInstance<InstallConfirmWindow>();
			wnd._installUrl = url;
			wnd._slug = slug;
			wnd.titleContent = new GUIContent("PkgLnk — Install Package");
			wnd.minSize = new Vector2(380, 220);
			wnd.maxSize = new Vector2(380, 220);
			wnd.ShowUtility();
			wnd.CenterOnScreen();
		}

		private void CenterOnScreen()
		{
			var screenWidth = Screen.currentResolution.width;
			var screenHeight = Screen.currentResolution.height;
			var x = (screenWidth - 380) / 2;
			var y = (screenHeight - 220) / 2;
			position = new Rect(x, y, 380, 220);
		}

		public void CreateGUI()
		{
			var root = rootVisualElement;

			var baseSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(
				"Packages/com.nonatomic.pkglnk/Editor/PkgLnkWindow/PkgLnkWindowStyles.uss");
			if (baseSheet != null) root.styleSheets.Add(baseSheet);

			var themeSuffix = EditorGUIUtility.isProSkin ? "Dark" : "Light";
			var themeSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(
				$"Packages/com.nonatomic.pkglnk/Editor/PkgLnkWindow/PkgLnkWindowStyles{themeSuffix}.uss");
			if (themeSheet != null) root.styleSheets.Add(themeSheet);

			root.AddToClassList("pkglnk-window");
			root.AddToClassList("confirm-window-root");

			BuildConfirmSection(root);
			BuildProgressSection(root);
			BuildResultSection(root);

			_progressSection.style.display = DisplayStyle.None;
			_resultSection.style.display = DisplayStyle.None;

			root.RegisterCallback<DetachFromPanelEvent>(_ => StopAnimation());
		}

		private void BuildConfirmSection(VisualElement root)
		{
			_confirmSection = new VisualElement();
			_confirmSection.AddToClassList("confirm-section");
			root.Add(_confirmSection);

			var brandRow = new VisualElement();
			brandRow.AddToClassList("confirm-brand-row");
			_confirmSection.Add(brandRow);

			var logoIcon = new VisualElement();
			logoIcon.AddToClassList("confirm-logo");
			var iconTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(
				"Packages/com.nonatomic.pkglnk/Editor/Icons/pkglnk-box-green.png");
			if (iconTexture != null)
			{
				logoIcon.style.backgroundImage = new StyleBackground(iconTexture);
			}
			brandRow.Add(logoIcon);

			var title = new Label("Install Package");
			title.AddToClassList("login-card-title");
			brandRow.Add(title);

			var message = new Label($"A website is requesting to install a package into your project:");
			message.AddToClassList("login-card-message");
			_confirmSection.Add(message);

			var slugLabel = new Label(_slug);
			slugLabel.AddToClassList("confirm-slug");
			_confirmSection.Add(slugLabel);

			var urlLabel = new Label(_installUrl);
			urlLabel.AddToClassList("confirm-url");
			_confirmSection.Add(urlLabel);

			var buttonRow = new VisualElement();
			buttonRow.AddToClassList("confirm-button-row");
			_confirmSection.Add(buttonRow);

			_cancelButton = new Button(OnCancelClicked);
			_cancelButton.text = "Cancel";
			_cancelButton.AddToClassList("login-card-cancel");
			buttonRow.Add(_cancelButton);

			_installButton = new Button(OnInstallClicked);
			_installButton.text = "Install";
			_installButton.AddToClassList("login-card-button");
			buttonRow.Add(_installButton);
		}

		private void BuildProgressSection(VisualElement root)
		{
			_progressSection = new VisualElement();
			_progressSection.AddToClassList("confirm-section");
			root.Add(_progressSection);

			var title = new Label($"Installing {_slug}");
			title.AddToClassList("login-card-title");
			_progressSection.Add(title);

			var statusRow = new VisualElement();
			statusRow.AddToClassList("confirm-status-row");
			_progressSection.Add(statusRow);

			_spinnerLabel = new Label(SpinnerChars[0].ToString());
			_spinnerLabel.AddToClassList("row-spinner");
			statusRow.Add(_spinnerLabel);

			_statusLabel = new Label("Installing...");
			_statusLabel.AddToClassList("row-status-label");
			statusRow.Add(_statusLabel);

			_progressTrack = new VisualElement();
			_progressTrack.AddToClassList("row-progress-track");
			_progressTrack.AddToClassList("confirm-progress-track");
			_progressSection.Add(_progressTrack);

			_progressFill = new VisualElement();
			_progressFill.AddToClassList("row-progress-fill");
			_progressTrack.Add(_progressFill);
		}

		private void BuildResultSection(VisualElement root)
		{
			_resultSection = new VisualElement();
			_resultSection.AddToClassList("confirm-section");
			root.Add(_resultSection);

			_resultLabel = new Label();
			_resultLabel.AddToClassList("login-card-title");
			_resultSection.Add(_resultLabel);

			_closeButton = new Button(() => Close());
			_closeButton.text = "Close";
			_closeButton.AddToClassList("login-card-cancel");
			_closeButton.AddToClassList("confirm-close-button");
			_resultSection.Add(_closeButton);
		}

		private void OnCancelClicked()
		{
			Close();
		}

		private void OnInstallClicked()
		{
			_confirmSection.style.display = DisplayStyle.None;
			_progressSection.style.display = DisplayStyle.Flex;

			_currentPercent = 0f;
			_targetPercent = 5f;
			_ceilPercent = 28f;
			StartAnimation();

			var pkg = ParsePackageDataFromUrl(_installUrl);
			PackageInstaller.Install(pkg, OnInstallComplete, OnInstallPhase);
		}

		private void OnInstallPhase(InstallPhase phase)
		{
			switch (phase)
			{
				case InstallPhase.Resolving:
					_statusLabel.text = "Resolving...";
					_targetPercent = 33f;
					_ceilPercent = 58f;
					break;

				case InstallPhase.Downloading:
					_statusLabel.text = "Downloading...";
					_targetPercent = 66f;
					_ceilPercent = 85f;
					break;

				case InstallPhase.Importing:
					_statusLabel.text = "Importing...";
					_targetPercent = 90f;
					_ceilPercent = 98f;
					break;

				case InstallPhase.Complete:
					StopAnimation();
					ShowResult(true, null);
					break;
			}
		}

		private void OnInstallComplete(bool success, string error)
		{
			StopAnimation();

			if (success)
			{
				PackageInstaller.InvalidateInstalledCache();
				ShowResult(true, null);
			}
			else
			{
				ShowResult(false, error);
			}
		}

		private void ShowResult(bool success, string error)
		{
			_progressSection.style.display = DisplayStyle.None;
			_resultSection.style.display = DisplayStyle.Flex;

			if (success)
			{
				_resultLabel.text = $"{_slug} installed successfully";
				_resultLabel.RemoveFromClassList("row-status-error");

				// Auto-close after a brief delay
				EditorApplication.delayCall += () =>
				{
					EditorApplication.delayCall += () =>
					{
						if (this != null) Close();
					};
				};
			}
			else
			{
				_resultLabel.text = $"Failed: {error}";
				_resultLabel.AddToClassList("row-status-error");
			}
		}

		private void StartAnimation()
		{
			if (_isAnimating) return;
			_isAnimating = true;
			_lastAnimTime = EditorApplication.timeSinceStartup;
			_lastSpinnerTime = _lastAnimTime;
			_spinnerFrame = 0;
			_spinnerLabel.text = SpinnerChars[0].ToString();
			EditorApplication.update += Animate;
		}

		private void StopAnimation()
		{
			if (!_isAnimating) return;
			_isAnimating = false;
			EditorApplication.update -= Animate;
		}

		private void Animate()
		{
			var now = EditorApplication.timeSinceStartup;
			var dt = Mathf.Min((float)(now - _lastAnimTime), MaxDeltaTime);
			_lastAnimTime = now;

			if (_currentPercent < _ceilPercent)
			{
				_currentPercent += CreepRate * dt;
				if (_currentPercent > _ceilPercent) _currentPercent = _ceilPercent;
				if (_currentPercent < _targetPercent) _currentPercent = _targetPercent;
				_progressFill.style.width = new Length(_currentPercent, LengthUnit.Percent);
			}

			if (now - _lastSpinnerTime >= SpinnerInterval)
			{
				_lastSpinnerTime = now;
				_spinnerFrame = (_spinnerFrame + 1) % SpinnerChars.Length;
				_spinnerLabel.text = SpinnerChars[_spinnerFrame].ToString();
			}
		}

		/// <summary>
		/// Extracts the slug from a pkglnk install URL.
		/// E.g. "https://www.pkglnk.dev/my-package.git#upm" → "my-package"
		/// </summary>
		internal static string ParseSlugFromUrl(string url)
		{
			return ParsePackageDataFromUrl(url).slug;
		}

		/// <summary>
		/// Parses a pkglnk install URL into a PackageData with slug, git_path, and git_ref.
		/// E.g. "https://www.pkglnk.dev/my-pkg.git?path=Assets/Pkg#v1.0" →
		///   { slug="my-pkg", git_path="Assets/Pkg", git_ref="v1.0" }
		/// </summary>
		internal static PackageData ParsePackageDataFromUrl(string url)
		{
			var prefixes = new[]
			{
				"https://pkglnk.dev/track/",
				"https://www.pkglnk.dev/track/",
				"https://pkglnk.dev/",
				"https://www.pkglnk.dev/"
			};

			var remainder = url;
			foreach (var prefix in prefixes)
			{
				if (url.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
				{
					remainder = url.Substring(prefix.Length);
					break;
				}
			}

			// Extract fragment (git_ref) — must be done before query string
			string gitRef = null;
			var hashIdx = remainder.IndexOf('#');
			if (hashIdx >= 0)
			{
				gitRef = remainder.Substring(hashIdx + 1);
				remainder = remainder.Substring(0, hashIdx);
			}

			// Extract query string (git_path)
			string gitPath = null;
			var queryIdx = remainder.IndexOf('?');
			if (queryIdx >= 0)
			{
				var queryString = remainder.Substring(queryIdx + 1);
				remainder = remainder.Substring(0, queryIdx);

				foreach (var param in queryString.Split('&'))
				{
					if (param.StartsWith("path=", StringComparison.OrdinalIgnoreCase))
					{
						gitPath = Uri.UnescapeDataString(param.Substring(5));
						break;
					}
				}
			}

			// Strip .git suffix to get slug
			if (remainder.EndsWith(".git"))
			{
				remainder = remainder.Substring(0, remainder.Length - 4);
			}

			return new PackageData
			{
				slug = remainder,
				git_path = gitPath ?? string.Empty,
				git_ref = gitRef ?? string.Empty
			};
		}
	}
}
