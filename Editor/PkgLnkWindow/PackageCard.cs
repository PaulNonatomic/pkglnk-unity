using System;
using Nonatomic.PkgLnk.Editor.Api;
using Nonatomic.PkgLnk.Editor.Utils;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Nonatomic.PkgLnk.Editor.PkgLnkWindow
{
	/// <summary>
	/// Poolable card element with zero-allocation rebinding.
	/// Pre-allocates all child elements; Bind() and ShowGhost() only
	/// update text/styles — no DOM mutations during scroll.
	/// </summary>
	public class PackageCard : VisualElement
	{
		private const int MaxTopics = 8;

		public PackageData Package { get; private set; }

		private const long ImageRecheckIntervalMs = 60_000;

		private readonly Action<PackageCard> _onClicked;
		private readonly Action<string> _onTopicClicked;
		private readonly Action<PackageCard> _onInstallClicked;
		private readonly Action<PackageCard> _onBookmarkClicked;
		private readonly Action<PackageCard> _onImageRecheck;

		// Ghost skeleton
		private readonly VisualElement _ghostBody;

		// Real content
		private readonly VisualElement _cardBody;
		private readonly VisualElement _ownerAvatar;
		private readonly Label _ownerLabel;
		private readonly VisualElement _imageArea;
		private readonly VisualElement _imageElement;
		private readonly VisualElement _placeholderIcon;
		private readonly Label _nameLabel;
		private readonly Label _descLabel;
		private readonly VisualElement _topicsRow;
		private readonly VisualElement _topicsInner;
		private readonly Label[] _topicLabels = new Label[MaxTopics];
		private readonly VisualElement _platformIcon;
		private readonly Label _repoLabel;
		private readonly Label _updatedLabel;
		private readonly Button _installButton;
		private readonly VisualElement _installButtonIcon;
		private readonly Button _bookmarkButton;
		private readonly VisualElement _bookmarkIcon;

		private static Texture2D _bookmarkOutlineTex;
		private static Texture2D _bookmarkFilledTex;

		private string _boundImageUrl;
		private string _boundAvatarOwner;
		private string _boundAvatarPlatform;
		private string _boundOwner;
		private string _boundRepo;
		private int _boundInstallCount = -1;
		private bool _isInstalled;
		private bool _isBookmarked;
		private bool _isGhost = true;
		private IVisualElementScheduledItem _imageRecheckTask;

		public PackageCard(
			Action<PackageCard> onClicked,
			Action<string> onTopicClicked,
			Action<PackageCard> onInstallClicked,
			Action<PackageCard> onBookmarkClicked,
			Action<PackageCard> onImageRecheck = null)
		{
			_onClicked = onClicked;
			_onTopicClicked = onTopicClicked;
			_onInstallClicked = onInstallClicked;
			_onBookmarkClicked = onBookmarkClicked;
			_onImageRecheck = onImageRecheck;

			AddToClassList("package-card");
			AddToClassList("package-card-ghost");

			// ─── Ghost skeleton ─────────────────────────────────────
			_ghostBody = new VisualElement();
			_ghostBody.AddToClassList("ghost-body");
			Add(_ghostBody);

			var ghostHeader = new VisualElement();
			ghostHeader.AddToClassList("ghost-line");
			ghostHeader.AddToClassList("ghost-line-short");
			_ghostBody.Add(ghostHeader);

			var ghostImage = new VisualElement();
			ghostImage.AddToClassList("ghost-image");
			_ghostBody.Add(ghostImage);

			var ghostTitle = new VisualElement();
			ghostTitle.AddToClassList("ghost-line");
			ghostTitle.AddToClassList("ghost-line-medium");
			_ghostBody.Add(ghostTitle);

			var ghostDesc = new VisualElement();
			ghostDesc.AddToClassList("ghost-line");
			ghostDesc.AddToClassList("ghost-line-long");
			_ghostBody.Add(ghostDesc);

			var ghostFooter = new VisualElement();
			ghostFooter.AddToClassList("ghost-line");
			ghostFooter.AddToClassList("ghost-line-medium");
			_ghostBody.Add(ghostFooter);

			// ─── Real card body ─────────────────────────────────────
			_cardBody = new VisualElement();
			_cardBody.AddToClassList("card-body");
			_cardBody.style.display = DisplayStyle.None;
			_cardBody.RegisterCallback<ClickEvent>(_ => _onClicked?.Invoke(this));
			Add(_cardBody);

			// Header
			var header = new VisualElement();
			header.AddToClassList("card-header");
			_cardBody.Add(header);

			var ownerRow = new VisualElement();
			ownerRow.AddToClassList("owner-row");
			header.Add(ownerRow);

			_ownerAvatar = new VisualElement();
			_ownerAvatar.AddToClassList("owner-avatar");
			ownerRow.Add(_ownerAvatar);

			_ownerLabel = new Label();
			_ownerLabel.AddToClassList("owner-name");
			ownerRow.Add(_ownerLabel);

			if (_bookmarkOutlineTex == null)
			{
				_bookmarkOutlineTex = AssetDatabase.LoadAssetAtPath<Texture2D>(
					"Packages/com.nonatomic.pkglnk/Editor/Icons/bookmark-outline.png");
			}
			if (_bookmarkFilledTex == null)
			{
				_bookmarkFilledTex = AssetDatabase.LoadAssetAtPath<Texture2D>(
					"Packages/com.nonatomic.pkglnk/Editor/Icons/bookmark-filled.png");
			}

			_bookmarkButton = new Button(() =>
			{
				_onBookmarkClicked?.Invoke(this);
			});
			_bookmarkButton.text = string.Empty;
			_bookmarkButton.tooltip = "Bookmark";
			_bookmarkButton.AddToClassList("bookmark-button");
			_bookmarkButton.style.display = DisplayStyle.None;
			_bookmarkButton.RegisterCallback<ClickEvent>(evt => evt.StopPropagation());
			header.Add(_bookmarkButton);

			_bookmarkIcon = new VisualElement();
			_bookmarkIcon.AddToClassList("bookmark-icon");
			_bookmarkIcon.pickingMode = PickingMode.Ignore;
			if (_bookmarkOutlineTex != null)
			{
				_bookmarkIcon.style.backgroundImage = new StyleBackground(_bookmarkOutlineTex);
			}
			_bookmarkButton.Add(_bookmarkIcon);

			// Image — persistent element, swap background only
			_imageArea = new VisualElement();
			_imageArea.AddToClassList("card-image-area");
			_cardBody.Add(_imageArea);

			_imageElement = new VisualElement();
			_imageElement.AddToClassList("card-image");
			_imageElement.style.display = DisplayStyle.None;
			_imageArea.Add(_imageElement);

			_placeholderIcon = new VisualElement();
			_placeholderIcon.AddToClassList("card-image-placeholder");
			var boxIcon = AssetDatabase.LoadAssetAtPath<Texture2D>(
				"Packages/com.nonatomic.pkglnk/Editor/Icons/pkglnk-box-white.png");
			if (boxIcon != null)
			{
				_placeholderIcon.style.backgroundImage = new StyleBackground(boxIcon);
			}
			_imageArea.Add(_placeholderIcon);

			// Display name
			_nameLabel = new Label();
			_nameLabel.AddToClassList("package-display-name");
			_cardBody.Add(_nameLabel);

			// Description
			_descLabel = new Label();
			_descLabel.AddToClassList("package-description");
			_cardBody.Add(_descLabel);

			// Topics — pre-allocated labels, show/hide only
			_topicsRow = new VisualElement();
			_topicsRow.AddToClassList("topic-tags-row");
			_topicsRow.style.display = DisplayStyle.None;
			_cardBody.Add(_topicsRow);

			_topicsInner = new VisualElement();
			_topicsInner.AddToClassList("topic-tags-inner");
			_topicsRow.Add(_topicsInner);

			_topicsRow.RegisterCallback<WheelEvent>(OnTopicsWheel);

			for (var i = 0; i < MaxTopics; i++)
			{
				var topicLabel = new Label();
				topicLabel.AddToClassList("topic-tag");
				topicLabel.style.display = DisplayStyle.None;
				var index = i;
				topicLabel.RegisterCallback<ClickEvent>(evt =>
				{
					evt.StopPropagation();
					_onTopicClicked?.Invoke(_topicLabels[index].text);
				});
				_topicsInner.Add(topicLabel);
				_topicLabels[i] = topicLabel;
			}

			// Footer
			var footer = new VisualElement();
			footer.AddToClassList("card-footer");
			_cardBody.Add(footer);

			_platformIcon = new VisualElement();
			_platformIcon.AddToClassList("card-platform-icon");
			footer.Add(_platformIcon);

			_repoLabel = new Label();
			_repoLabel.AddToClassList("repo-label");
			footer.Add(_repoLabel);

			_updatedLabel = new Label();
			_updatedLabel.AddToClassList("updated-label");
			_updatedLabel.text = "0 installs";
			footer.Add(_updatedLabel);

			_installButton = new Button(() => _onInstallClicked?.Invoke(this));
			_installButton.text = string.Empty;
			_installButton.tooltip = "Install";
			_installButton.AddToClassList("install-button");
			footer.Add(_installButton);

			_installButtonIcon = new VisualElement();
			_installButtonIcon.AddToClassList("install-button-icon");
			_installButtonIcon.pickingMode = PickingMode.Ignore;
			var downloadTex = AssetDatabase.LoadAssetAtPath<Texture2D>(
				"Packages/com.nonatomic.pkglnk/Editor/Icons/download-icon.png");
			if (downloadTex != null)
			{
				_installButtonIcon.style.backgroundImage = new StyleBackground(downloadTex);
			}
			_installButton.Add(_installButtonIcon);
		}

		/// <summary>
		/// Switches to ghost skeleton. Zero allocations.
		/// </summary>
		public void ShowGhost()
		{
			if (_isGhost) return;
			_isGhost = true;
			Package = null;
			_boundImageUrl = null;
			_boundAvatarOwner = null;
			_boundAvatarPlatform = null;
			_boundOwner = null;
			_boundRepo = null;
			_boundInstallCount = -1;
			CancelImageRecheck();
			_cardBody.style.display = DisplayStyle.None;
			_ghostBody.style.display = DisplayStyle.Flex;
			AddToClassList("package-card-ghost");
			RemoveFromClassList("package-card-installed");
		}

		/// <summary>
		/// Binds to package data. Zero DOM allocations — only text/style updates.
		/// </summary>
		public void Bind(PackageData pkg, int installCount, bool isBookmarked, bool showBookmark)
		{
			if (_isGhost)
			{
				_isGhost = false;
				_ghostBody.style.display = DisplayStyle.None;
				_cardBody.style.display = DisplayStyle.Flex;
				RemoveFromClassList("package-card-ghost");
			}

			Package = pkg;

			_ownerLabel.text = pkg.git_owner;

			// Owner avatar — only reload when owner/platform changes
			if (pkg.git_owner != _boundAvatarOwner || pkg.git_platform != _boundAvatarPlatform)
			{
				_boundAvatarOwner = pkg.git_owner;
				_boundAvatarPlatform = pkg.git_platform;
				_ownerAvatar.style.backgroundImage = StyleKeyword.None;

				var avatarUrl = GetAvatarUrl(pkg.git_platform, pkg.git_owner);
				if (!string.IsNullOrEmpty(avatarUrl))
				{
					ImageLoader.Load(avatarUrl, texture =>
					{
						if (texture == null || panel == null) return;
						if (Package != pkg) return;

						_ownerAvatar.style.backgroundImage = new StyleBackground(texture);
					});
				}
			}

			// Image — only reload when URL changes
			var imageUrl = pkg.card_image_url ?? string.Empty;
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
						if (Package != pkg) return;

						_imageElement.style.backgroundImage = new StyleBackground(texture);
						_imageElement.style.display = DisplayStyle.Flex;
						_placeholderIcon.style.display = DisplayStyle.None;
					});
				}
			}

			// Schedule periodic recheck for packages with no image
			if (string.IsNullOrEmpty(imageUrl))
				ScheduleImageRecheck();
			else
				CancelImageRecheck();

			_nameLabel.text = pkg.display_name;

			// Description
			if (!string.IsNullOrEmpty(pkg.description))
			{
				_descLabel.text = pkg.description;
				_descLabel.RemoveFromClassList("package-description-empty");
			}
			else
			{
				_descLabel.text = "No description";
				_descLabel.AddToClassList("package-description-empty");
			}

			// Topics — reuse pre-allocated labels
			var topicCount = pkg.topics?.Length ?? 0;
			var visibleTopics = Mathf.Min(topicCount, MaxTopics);

			for (var i = 0; i < MaxTopics; i++)
			{
				if (i < visibleTopics)
				{
					_topicLabels[i].text = pkg.topics[i];
					_topicLabels[i].style.display = DisplayStyle.Flex;
				}
				else
				{
					_topicLabels[i].style.display = DisplayStyle.None;
				}
			}

			_topicsRow.style.display = visibleTopics > 0 ? DisplayStyle.Flex : DisplayStyle.None;
			_topicsInner.style.left = 0;

			// Footer — skip string allocation when unchanged
			if (pkg.git_owner != _boundOwner || pkg.git_repo != _boundRepo)
			{
				_boundOwner = pkg.git_owner;
				_boundRepo = pkg.git_repo;
				_repoLabel.text = $"{pkg.git_owner}/{pkg.git_repo}";
				_platformIcon.style.backgroundImage = new StyleBackground(TabIcons.GetPlatformIcon(pkg.git_platform));
			}

			if (installCount != _boundInstallCount)
			{
				_boundInstallCount = installCount;
				_updatedLabel.text = installCount > 0
					? $"{FormatUtils.FormatCount(installCount)} installs"
					: "0 installs";
			}

			// Install state
			_isInstalled = PackageInstaller.IsInstalled(pkg);
			UpdateButtonDisplay();
			UpdateInstalledHighlight();

			// Bookmark state
			UpdateBookmarkState(isBookmarked);
			_bookmarkButton.style.display = showBookmark ? DisplayStyle.Flex : DisplayStyle.None;
		}

		public void UpdateInstalledState(bool isInstalled)
		{
			_isInstalled = isInstalled;
			UpdateButtonDisplay();
			UpdateInstalledHighlight();
		}

		public void UpdateInstallCount(int installCount)
		{
			_updatedLabel.text = installCount > 0
				? $"{FormatUtils.FormatCount(installCount)} installs"
				: "0 installs";
		}

		public void UpdateBookmarkState(bool isBookmarked)
		{
			_isBookmarked = isBookmarked;
			var tex = isBookmarked ? _bookmarkFilledTex : _bookmarkOutlineTex;
			if (tex != null)
			{
				_bookmarkIcon.style.backgroundImage = new StyleBackground(tex);
			}
			if (isBookmarked)
				_bookmarkButton.AddToClassList("bookmark-button-active");
			else
				_bookmarkButton.RemoveFromClassList("bookmark-button-active");
		}

		public void SetInstalling(bool installing)
		{
			if (installing)
			{
				_installButton.text = "Installing...";
				_installButton.SetEnabled(false);
				_installButton.RemoveFromClassList("installed-button");
				_installButtonIcon.style.display = DisplayStyle.None;
			}
			else
			{
				UpdateButtonDisplay();
			}
		}

		/// <summary>
		/// Updates the install button text based on the current server-reported phase.
		/// </summary>
		public void SetInstallPhaseText(InstallPhase phase)
		{
			switch (phase)
			{
				case InstallPhase.Resolving:
					_installButton.text = "Resolving...";
					_installButtonIcon.style.display = DisplayStyle.None;
					break;
				case InstallPhase.Downloading:
					_installButton.text = "Downloading...";
					_installButtonIcon.style.display = DisplayStyle.None;
					break;
				case InstallPhase.Importing:
					_installButton.text = "Importing...";
					_installButtonIcon.style.display = DisplayStyle.None;
					break;
				case InstallPhase.Complete:
					_isInstalled = true;
					UpdateButtonDisplay();
					UpdateInstalledHighlight();
					break;
			}
		}

		private void UpdateInstalledHighlight()
		{
			if (_isInstalled)
				AddToClassList("package-card-installed");
			else
				RemoveFromClassList("package-card-installed");
		}

		private void UpdateButtonDisplay()
		{
			if (_isInstalled)
			{
				_installButton.text = "Installed";
				_installButton.SetEnabled(false);
				_installButton.AddToClassList("installed-button");
				_installButtonIcon.style.display = DisplayStyle.None;
			}
			else
			{
				_installButton.text = string.Empty;
				_installButton.SetEnabled(true);
				_installButton.RemoveFromClassList("installed-button");
				_installButtonIcon.style.display = DisplayStyle.Flex;
			}
		}

		private void OnTopicsWheel(WheelEvent evt)
		{
			var innerWidth = _topicsInner.resolvedStyle.width;
			var outerWidth = _topicsRow.resolvedStyle.width;
			if (float.IsNaN(innerWidth) || float.IsNaN(outerWidth)) return;

			var maxScroll = innerWidth - outerWidth;
			if (maxScroll <= 0) return;

			var current = _topicsInner.resolvedStyle.left;
			if (float.IsNaN(current)) current = 0f;

			current -= evt.delta.y * 20f;
			current = Mathf.Clamp(current, -maxScroll, 0f);
			_topicsInner.style.left = current;

			evt.StopPropagation();
		}

		private void ScheduleImageRecheck()
		{
			if (_onImageRecheck == null) return;
			CancelImageRecheck();
			_imageRecheckTask = schedule.Execute(() =>
			{
				if (_isGhost || Package == null) return;
				if (!string.IsNullOrEmpty(Package.card_image_url)) return;
				_onImageRecheck.Invoke(this);
			}).StartingIn(ImageRecheckIntervalMs).Every(ImageRecheckIntervalMs);
		}

		private void CancelImageRecheck()
		{
			if (_imageRecheckTask == null) return;
			_imageRecheckTask.Pause();
			_imageRecheckTask = null;
		}

		private static string GetAvatarUrl(string platform, string owner)
		{
			if (string.IsNullOrEmpty(owner)) return string.Empty;

			return platform switch
			{
				"gitlab" => $"https://gitlab.com/uploads/-/system/user/avatar/{owner}/avatar.png",
				"bitbucket" => $"https://bitbucket.org/account/{owner}/avatar/40/",
				_ => $"https://github.com/{owner}.png?size=40"
			};
		}
	}
}
