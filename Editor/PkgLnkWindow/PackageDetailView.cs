using System;
using Nonatomic.PkgLnk.Editor.Api;
using Nonatomic.PkgLnk.Editor.Utils;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Nonatomic.PkgLnk.Editor.PkgLnkWindow
{
	/// <summary>
	/// Detail view shown when a package card is clicked.
	/// Displays full package information and install controls.
	/// </summary>
	public class PackageDetailView : VisualElement
	{
		private readonly Action _onBack;
		private readonly Action<string> _onTopicClicked;

		private readonly Label _titleLabel;
		private readonly Label _platformLabel;
		private readonly Label _ownerLabel;
		private readonly Label _descriptionLabel;
		private readonly Label _packageNameLabel;
		private readonly Label _starsLabel;
		private readonly Label _installCountLabel;
		private readonly Label _updatedLabel;
		private readonly Label _installUrlLabel;
		private readonly VisualElement _topicsRow;
		private readonly Button _installButton;
		private readonly Label _errorLabel;

		private PackageData _currentPackage;
		private bool _isInstalled;

		public PackageDetailView(Action onBack, Action<string> onTopicClicked)
		{
			_onBack = onBack;
			_onTopicClicked = onTopicClicked;

			AddToClassList("package-detail");

			// Back button
			var backButton = new Button(() => _onBack?.Invoke());
			backButton.text = "\u2190 Back";
			backButton.AddToClassList("detail-back-button");
			Add(backButton);

			// Title row
			var titleRow = new VisualElement();
			titleRow.AddToClassList("detail-title-row");
			Add(titleRow);

			_titleLabel = new Label();
			_titleLabel.AddToClassList("detail-title");
			titleRow.Add(_titleLabel);

			_platformLabel = new Label();
			_platformLabel.AddToClassList("detail-platform-badge");
			titleRow.Add(_platformLabel);

			// Owner
			_ownerLabel = new Label();
			_ownerLabel.AddToClassList("detail-owner");
			Add(_ownerLabel);

			// Stats row
			var statsRow = new VisualElement();
			statsRow.AddToClassList("detail-stats-row");
			Add(statsRow);

			_installCountLabel = new Label();
			_installCountLabel.AddToClassList("detail-stat");
			statsRow.Add(_installCountLabel);

			_starsLabel = new Label();
			_starsLabel.AddToClassList("detail-stat");
			statsRow.Add(_starsLabel);

			_updatedLabel = new Label();
			_updatedLabel.AddToClassList("detail-stat");
			statsRow.Add(_updatedLabel);

			// Description
			_descriptionLabel = new Label();
			_descriptionLabel.AddToClassList("detail-description");
			Add(_descriptionLabel);

			// Package name
			_packageNameLabel = new Label();
			_packageNameLabel.AddToClassList("detail-package-name");
			Add(_packageNameLabel);

			// Topics
			_topicsRow = new VisualElement();
			_topicsRow.AddToClassList("topic-tags-row");
			_topicsRow.AddToClassList("detail-topics-row");
			Add(_topicsRow);

			// Install URL
			var urlRow = new VisualElement();
			urlRow.AddToClassList("detail-url-row");
			Add(urlRow);

			_installUrlLabel = new Label();
			_installUrlLabel.AddToClassList("detail-install-url");
			urlRow.Add(_installUrlLabel);

			var copyButton = new Button(CopyInstallUrl);
			copyButton.text = "Copy";
			copyButton.AddToClassList("detail-copy-button");
			urlRow.Add(copyButton);

			// Install button
			_installButton = new Button(OnInstallClicked);
			_installButton.AddToClassList("detail-install-button");
			Add(_installButton);

			// Error label
			_errorLabel = new Label();
			_errorLabel.AddToClassList("error-label");
			_errorLabel.style.display = DisplayStyle.None;
			Add(_errorLabel);
		}

		/// <summary>
		/// Populates the detail view with package data and shows it.
		/// </summary>
		public void Show(PackageData pkg, int installCount)
		{
			_currentPackage = pkg;
			_errorLabel.style.display = DisplayStyle.None;

			_titleLabel.text = pkg.display_name;
			_platformLabel.text = pkg.git_platform;
			_ownerLabel.text = $"{pkg.git_owner}/{pkg.git_repo}";

			_installCountLabel.text = $"{FormatUtils.FormatCount(installCount)} installs";
			_starsLabel.text = pkg.github_stars > 0 ? $"{FormatUtils.FormatCount(pkg.github_stars)} stars" : string.Empty;
			_starsLabel.style.display = pkg.github_stars > 0 ? DisplayStyle.Flex : DisplayStyle.None;
			_updatedLabel.text = $"Updated {DateUtils.FormatRelative(pkg.updated_at)}";

			_descriptionLabel.text = !string.IsNullOrEmpty(pkg.description)
				? pkg.description
				: "No description available.";

			if (!string.IsNullOrEmpty(pkg.package_json_name))
			{
				_packageNameLabel.text = pkg.package_json_name;
				_packageNameLabel.style.display = DisplayStyle.Flex;
			}
			else
			{
				_packageNameLabel.style.display = DisplayStyle.None;
			}

			// Topics
			_topicsRow.Clear();
			if (pkg.topics != null)
			{
				foreach (var topic in pkg.topics)
				{
					var topicLabel = new Label(topic);
					topicLabel.AddToClassList("topic-tag");
					var capturedTopic = topic;
					topicLabel.RegisterCallback<ClickEvent>(evt =>
					{
						evt.StopPropagation();
						_onTopicClicked?.Invoke(capturedTopic);
					});
					_topicsRow.Add(topicLabel);
				}
			}

			// Install URL
			var installUrl = PackageInstaller.BuildInstallUrl(pkg);
			_installUrlLabel.text = installUrl;

			// Install state
			_isInstalled = PackageInstaller.IsInstalled(pkg);
			UpdateInstallButton();
		}

		/// <summary>
		/// Re-checks installed state for the currently displayed package.
		/// </summary>
		public void RefreshInstalledState()
		{
			if (_currentPackage == null) return;
			_isInstalled = PackageInstaller.IsInstalled(_currentPackage);
			UpdateInstallButton();
		}

		private void CopyInstallUrl()
		{
			if (_currentPackage == null) return;
			var url = PackageInstaller.BuildInstallUrl(_currentPackage);
			EditorGUIUtility.systemCopyBuffer = url;
		}

		private void OnInstallClicked()
		{
			if (_currentPackage == null || _isInstalled || PackageInstaller.IsInstalling) return;

			_installButton.text = "Installing...";
			_installButton.SetEnabled(false);
			_errorLabel.style.display = DisplayStyle.None;

			PackageInstaller.Install(_currentPackage, (success, error) =>
			{
				if (success)
				{
					_isInstalled = true;
					UpdateInstallButton();
				}
				else
				{
					_installButton.text = "Install";
					_installButton.SetEnabled(true);
					_errorLabel.text = error;
					_errorLabel.style.display = DisplayStyle.Flex;
				}
			});
		}

		private void UpdateInstallButton()
		{
			if (_isInstalled)
			{
				_installButton.text = "Installed";
				_installButton.SetEnabled(false);
				_installButton.AddToClassList("installed-button");
			}
			else
			{
				_installButton.text = "Install";
				_installButton.SetEnabled(true);
				_installButton.RemoveFromClassList("installed-button");
			}
		}
	}
}
