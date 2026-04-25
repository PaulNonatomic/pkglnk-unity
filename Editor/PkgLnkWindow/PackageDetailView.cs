using System;
using System.Collections.Generic;
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
		private readonly Action<PackageData> _onAddToCollection;

		private readonly Label _titleLabel;
		private readonly VisualElement _platformIcon;
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
		private readonly Button _addToCollectionButton;
		private readonly VisualElement _imageArea;
		private readonly VisualElement _imageElement;
		private readonly VisualElement _placeholderIcon;
		private readonly ScrollView _readmeScroll;
		private readonly Label _readmeTitle;
		private readonly VisualElement _readmeContainer;
		private readonly Label _readmeLoading;

		private static readonly Dictionary<string, string> ReadmeCache = new Dictionary<string, string>();

		private PackageData _currentPackage;
		private bool _isInstalled;
		private string _boundImageUrl = string.Empty;

		public PackageDetailView(Action onBack, Action<string> onTopicClicked, Action<PackageData> onAddToCollection = null)
		{
			_onBack = onBack;
			_onTopicClicked = onTopicClicked;
			_onAddToCollection = onAddToCollection;

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

			_platformIcon = new VisualElement();
			_platformIcon.AddToClassList("detail-platform-icon");
			titleRow.Add(_platformIcon);

			_platformLabel = new Label();
			_platformLabel.AddToClassList("detail-platform-badge");
			titleRow.Add(_platformLabel);

			// Owner
			_ownerLabel = new Label();
			_ownerLabel.AddToClassList("detail-owner");
			Add(_ownerLabel);

			// Image
			_imageArea = new VisualElement();
			_imageArea.AddToClassList("detail-image-area");
			Add(_imageArea);

			_imageElement = new VisualElement();
			_imageElement.AddToClassList("card-image");
			_imageElement.style.display = DisplayStyle.None;
			_imageArea.Add(_imageElement);

			_placeholderIcon = new VisualElement();
			_placeholderIcon.AddToClassList("card-image-placeholder");
			var placeholderTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(
				"Packages/com.nonatomic.pkglnk/Editor/Icons/pkglnk-box-white.png");
			if (placeholderTexture != null)
			{
				_placeholderIcon.style.backgroundImage = new StyleBackground(placeholderTexture);
			}
			_imageArea.Add(_placeholderIcon);

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

			// Add to Collection button
			_addToCollectionButton = new Button(() => _onAddToCollection?.Invoke(_currentPackage));
			_addToCollectionButton.text = "Add to Collection";
			_addToCollectionButton.AddToClassList("add-to-collection-button");
			_addToCollectionButton.style.display = DisplayStyle.None;
			Add(_addToCollectionButton);

			// Error label
			_errorLabel = new Label();
			_errorLabel.AddToClassList("error-label");
			_errorLabel.style.display = DisplayStyle.None;
			Add(_errorLabel);

			// README section
			_readmeScroll = new ScrollView(ScrollViewMode.Vertical);
			_readmeScroll.AddToClassList("detail-readme-scroll");
			Add(_readmeScroll);

			_readmeTitle = new Label("Readme");
			_readmeTitle.AddToClassList("detail-readme-title");
			_readmeTitle.style.display = DisplayStyle.None;
			_readmeScroll.Add(_readmeTitle);

			_readmeLoading = new Label("Loading README...");
			_readmeLoading.AddToClassList("detail-readme-loading");
			_readmeLoading.style.display = DisplayStyle.None;
			_readmeScroll.Add(_readmeLoading);

			_readmeContainer = new VisualElement();
			_readmeContainer.AddToClassList("detail-readme");
			_readmeScroll.Add(_readmeContainer);
		}

		/// <summary>
		/// Populates the detail view with package data and shows it.
		/// </summary>
		public void Show(PackageData pkg, int installCount)
		{
			_currentPackage = pkg;
			_errorLabel.style.display = DisplayStyle.None;

			// Image — prefer server-optimised PNG for Unity compatibility
			var imageUrl = ResolveImageUrl(pkg);
			if (imageUrl != _boundImageUrl)
			{
				_boundImageUrl = imageUrl;
				_imageElement.style.backgroundImage = StyleKeyword.None;
				_imageElement.style.display = DisplayStyle.None;
				_placeholderIcon.style.display = DisplayStyle.Flex;

				if (!string.IsNullOrEmpty(imageUrl))
				{
					ImageLoader.Load(imageUrl, texture =>
					{
						if (texture == null || panel == null) return;
						if (_currentPackage != pkg) return;

						_imageElement.style.backgroundImage = new StyleBackground(texture);
						_imageElement.style.display = DisplayStyle.Flex;
						_placeholderIcon.style.display = DisplayStyle.None;
					});
				}
			}

			_titleLabel.text = pkg.display_name;
			_platformIcon.style.backgroundImage = new StyleBackground(TabIcons.GetPlatformIcon(pkg.git_platform));
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

			// Add to Collection — visible only when logged in
			_addToCollectionButton.style.display =
				PkgLnkAuth.IsLoggedIn && _onAddToCollection != null
					? DisplayStyle.Flex
					: DisplayStyle.None;

			// README
			FetchAndRenderReadme(pkg);
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

		private void FetchAndRenderReadme(PackageData pkg)
		{
			_readmeContainer.Clear();
			_readmeLoading.style.display = DisplayStyle.None;
			_readmeTitle.style.display = DisplayStyle.None;

			if (pkg.git_platform != "github" ||
				string.IsNullOrEmpty(pkg.git_owner) ||
				string.IsNullOrEmpty(pkg.git_repo))
			{
				return;
			}

			var cacheKey = $"{pkg.git_owner}/{pkg.git_repo}";

			if (ReadmeCache.TryGetValue(cacheKey, out var cached))
			{
				RenderReadme(cached, pkg);
				return;
			}

			_readmeLoading.style.display = DisplayStyle.Flex;

			PkgLnkApiClient.FetchReadme(pkg.git_owner, pkg.git_repo, (markdown, error) =>
			{
				if (_currentPackage != pkg) return;

				_readmeLoading.style.display = DisplayStyle.None;

				if (!string.IsNullOrEmpty(error) || string.IsNullOrEmpty(markdown))
				{
					return;
				}

				ReadmeCache[cacheKey] = markdown;
				RenderReadme(markdown, pkg);
			});
		}

		private void RenderReadme(string markdown, PackageData pkg)
		{
			_readmeContainer.Clear();
			_readmeTitle.style.display = DisplayStyle.Flex;
			var rendered = MarkdownRenderer.Render(markdown, pkg.git_owner, pkg.git_repo, pkg.git_ref);
			_readmeContainer.Add(rendered);
		}

		/// <summary>
		/// Returns the best image URL for the detail view, preferring the
		/// server-optimised PNG when available for Unity compatibility.
		/// </summary>
		private static string ResolveImageUrl(PackageData pkg)
		{
			var pngUrl = pkg.card_image_png_url ?? string.Empty;
			if (!string.IsNullOrEmpty(pngUrl)) return pngUrl;

			return pkg.card_image_url ?? string.Empty;
		}
	}
}
