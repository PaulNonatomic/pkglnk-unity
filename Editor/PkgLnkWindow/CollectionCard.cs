using System;
using Nonatomic.PkgLnk.Editor.Api;
using Nonatomic.PkgLnk.Editor.Utils;
using UnityEngine;
using UnityEngine.UIElements;

namespace Nonatomic.PkgLnk.Editor.PkgLnkWindow
{
	/// <summary>
	/// Poolable collection card with zero-allocation rebinding.
	/// Follows the same ghost skeleton pattern as PackageCard.
	/// </summary>
	public class CollectionCard : VisualElement
	{
		private const int MaxTags = 6;

		public CollectionData Collection { get; private set; }

		private readonly Action<CollectionCard> _onClicked;
		private readonly Action<CollectionCard> _onInstallClicked;

		// Ghost skeleton
		private readonly VisualElement _ghostBody;

		// Real content
		private readonly VisualElement _cardBody;
		private readonly VisualElement _ownerAvatar;
		private readonly Label _ownerLabel;
		private readonly Label _nameLabel;
		private readonly Label _descLabel;
		private readonly Label _packageCountLabel;
		private readonly VisualElement _tagsRow;
		private readonly Label[] _tagLabels = new Label[MaxTags];
		private readonly Button _installButton;

		private string _boundAvatarUrl;
		private bool _isGhost = true;

		public CollectionCard(Action<CollectionCard> onClicked, Action<CollectionCard> onInstallClicked)
		{
			_onClicked = onClicked;
			_onInstallClicked = onInstallClicked;

			AddToClassList("collection-card");
			AddToClassList("collection-card-ghost");

			// ─── Ghost skeleton ─────────────────────────────────────
			_ghostBody = new VisualElement();
			_ghostBody.AddToClassList("ghost-body");
			Add(_ghostBody);

			var ghostHeader = new VisualElement();
			ghostHeader.AddToClassList("ghost-line");
			ghostHeader.AddToClassList("ghost-line-short");
			_ghostBody.Add(ghostHeader);

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
			ghostFooter.AddToClassList("ghost-line-short");
			_ghostBody.Add(ghostFooter);

			// ─── Real card body ─────────────────────────────────────
			_cardBody = new VisualElement();
			_cardBody.AddToClassList("card-body");
			_cardBody.style.display = DisplayStyle.None;
			_cardBody.RegisterCallback<ClickEvent>(evt =>
			{
				if (IsChildOf(evt.target as VisualElement, _installButton)) return;
				_onClicked?.Invoke(this);
			});
			Add(_cardBody);

			// Owner row
			var ownerRow = new VisualElement();
			ownerRow.AddToClassList("owner-row");
			_cardBody.Add(ownerRow);

			_ownerAvatar = new VisualElement();
			_ownerAvatar.AddToClassList("owner-avatar");
			ownerRow.Add(_ownerAvatar);

			_ownerLabel = new Label();
			_ownerLabel.AddToClassList("owner-name");
			ownerRow.Add(_ownerLabel);

			// Collection name
			_nameLabel = new Label();
			_nameLabel.AddToClassList("collection-name");
			_cardBody.Add(_nameLabel);

			// Description
			_descLabel = new Label();
			_descLabel.AddToClassList("collection-description");
			_cardBody.Add(_descLabel);

			// Tags — pre-allocated, show/hide only
			_tagsRow = new VisualElement();
			_tagsRow.AddToClassList("topic-tags-row");
			_tagsRow.style.display = DisplayStyle.None;
			_cardBody.Add(_tagsRow);

			for (var i = 0; i < MaxTags; i++)
			{
				var tagLabel = new Label();
				tagLabel.AddToClassList("topic-tag");
				tagLabel.style.display = DisplayStyle.None;
				_tagsRow.Add(tagLabel);
				_tagLabels[i] = tagLabel;
			}

			// Footer with package count + install button
			var footer = new VisualElement();
			footer.AddToClassList("card-footer");
			_cardBody.Add(footer);

			_packageCountLabel = new Label();
			_packageCountLabel.AddToClassList("collection-package-count");
			footer.Add(_packageCountLabel);

			_installButton = new Button(() => _onInstallClicked?.Invoke(this));
			_installButton.text = "Install";
			_installButton.AddToClassList("install-button");
			_installButton.AddToClassList("collection-install-button");
			footer.Add(_installButton);
		}

		private static bool IsChildOf(VisualElement target, VisualElement parent)
		{
			while (target != null)
			{
				if (target == parent) return true;
				target = target.parent;
			}
			return false;
		}

		public void ShowGhost()
		{
			if (_isGhost) return;
			_isGhost = true;
			Collection = null;
			_boundAvatarUrl = null;
			_cardBody.style.display = DisplayStyle.None;
			_ghostBody.style.display = DisplayStyle.Flex;
			AddToClassList("collection-card-ghost");
			ResetInstallButton();
		}

		public void SetInstalling(bool installing)
		{
			if (installing)
			{
				_installButton.text = "Installing...";
				_installButton.SetEnabled(false);
				_installButton.RemoveFromClassList("installed-button");
			}
			else
			{
				ResetInstallButton();
			}
		}

		/// <summary>
		/// Checks whether all packages in the collection are installed and
		/// updates the install button to show "All Installed" (greyed out) or "Install".
		/// </summary>
		public void UpdateInstalledState(PackageData[] packages)
		{
			if (packages == null || packages.Length == 0)
			{
				ResetInstallButton();
				return;
			}

			var allInstalled = true;
			foreach (var pkg in packages)
			{
				if (PackageInstaller.IsInstalled(pkg))  continue;
				allInstalled = false;
				break;
			}

			if (allInstalled)
			{
				_installButton.text = "All Installed";
				_installButton.SetEnabled(false);
				_installButton.AddToClassList("installed-button");
			}
			else
			{
				ResetInstallButton();
			}
		}

		private void ResetInstallButton()
		{
			_installButton.text = "Install";
			_installButton.SetEnabled(true);
			_installButton.RemoveFromClassList("installed-button");
		}

		public void Bind(CollectionData collection)
		{
			if (_isGhost)
			{
				_isGhost = false;
				_ghostBody.style.display = DisplayStyle.None;
				_cardBody.style.display = DisplayStyle.Flex;
				RemoveFromClassList("collection-card-ghost");
			}

			Collection = collection;

			_ownerLabel.text = collection.owner_username;
			_nameLabel.text = collection.name;

			if (!string.IsNullOrEmpty(collection.description))
			{
				_descLabel.text = collection.description;
				_descLabel.RemoveFromClassList("package-description-empty");
			}
			else
			{
				_descLabel.text = "No description";
				_descLabel.AddToClassList("package-description-empty");
			}

			_packageCountLabel.text = collection.package_count == 1
				? "1 package"
				: $"{collection.package_count} packages";

			// Owner avatar
			var avatarUrl = !string.IsNullOrEmpty(collection.owner_avatar_url)
				? collection.owner_avatar_url
				: !string.IsNullOrEmpty(collection.owner_username)
					? $"https://github.com/{collection.owner_username}.png?size=40"
					: string.Empty;

			if (avatarUrl != _boundAvatarUrl)
			{
				_boundAvatarUrl = avatarUrl;
				_ownerAvatar.style.backgroundImage = StyleKeyword.None;

				if (!string.IsNullOrEmpty(avatarUrl))
				{
					ImageLoader.Load(avatarUrl, texture =>
					{
						if (texture == null || panel == null) return;
						if (Collection != collection) return;
						_ownerAvatar.style.backgroundImage = new StyleBackground(texture);
					});
				}
			}

			// Tags
			var tagCount = collection.tags?.Length ?? 0;
			var visibleTags = Mathf.Min(tagCount, MaxTags);

			for (var i = 0; i < MaxTags; i++)
			{
				if (i < visibleTags)
				{
					_tagLabels[i].text = collection.tags[i];
					_tagLabels[i].style.display = DisplayStyle.Flex;
				}
				else
				{
					_tagLabels[i].style.display = DisplayStyle.None;
				}
			}

			_tagsRow.style.display = visibleTags > 0 ? DisplayStyle.Flex : DisplayStyle.None;
		}
	}
}
