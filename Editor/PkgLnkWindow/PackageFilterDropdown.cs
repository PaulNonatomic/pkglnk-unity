using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Nonatomic.PkgLnk.Editor.PkgLnkWindow
{
	public class PackageFilterDropdown : VisualElement
	{
		private readonly PackageFilterState _state;
		private readonly Action _onChanged;

		// Install
		private readonly Button _installAll;
		private readonly Button _installInstalled;
		private readonly Button _installNotInstalled;

		// Bookmark
		private readonly VisualElement _bookmarkSection;
		private readonly Button _bookmarkAll;
		private readonly Button _bookmarkBookmarked;
		private readonly Button _bookmarkNotBookmarked;
		private readonly Label _bookmarkHint;

		// Platform
		private readonly VisualElement _platformChipRow;
		private readonly Button _platformAll;
		private readonly List<Button> _platformChips = new List<Button>();

		// Visibility
		private readonly Button _visibilityAll;
		private readonly Button _visibilityPublic;
		private readonly Button _visibilityPrivate;

		// Topics
		private readonly VisualElement _topicChipRow;
		private readonly VisualElement _topicSection;
		private readonly List<Button> _topicChips = new List<Button>();

		public PackageFilterDropdown(PackageFilterState state, Action onChanged)
		{
			_state = state;
			_onChanged = onChanged;

			AddToClassList("filter-dropdown");

			// Header
			var header = new VisualElement();
			header.AddToClassList("filter-dropdown-header");
			Add(header);

			var title = new Label("Filters");
			title.AddToClassList("filter-dropdown-title");
			header.Add(title);

			var clearButton = new Button(OnClearAll);
			clearButton.text = "Clear All";
			clearButton.AddToClassList("filter-clear-button");
			header.Add(clearButton);

			// Install Status
			var installSection = CreateSection("Install Status");
			Add(installSection);

			var installRow = new VisualElement();
			installRow.AddToClassList("filter-chip-row");
			installSection.Add(installRow);

			_installAll = CreateChip("All", installRow, () => SetInstall(InstallFilter.All));
			_installInstalled = CreateChip("Installed", installRow, () => SetInstall(InstallFilter.Installed));
			_installNotInstalled = CreateChip("Not Installed", installRow, () => SetInstall(InstallFilter.NotInstalled));

			// Bookmark Status
			_bookmarkSection = CreateSection("Bookmark Status");
			Add(_bookmarkSection);

			var bookmarkRow = new VisualElement();
			bookmarkRow.AddToClassList("filter-chip-row");
			_bookmarkSection.Add(bookmarkRow);

			_bookmarkAll = CreateChip("All", bookmarkRow, () => SetBookmark(BookmarkFilter.All));
			_bookmarkBookmarked = CreateChip("Bookmarked", bookmarkRow, () => SetBookmark(BookmarkFilter.Bookmarked));
			_bookmarkNotBookmarked = CreateChip("Not Bookmarked", bookmarkRow, () => SetBookmark(BookmarkFilter.NotBookmarked));

			_bookmarkHint = new Label("Sign in to filter by bookmarks");
			_bookmarkHint.AddToClassList("filter-section-hint");
			_bookmarkHint.style.display = DisplayStyle.None;
			_bookmarkSection.Add(_bookmarkHint);

			// Platform
			var platformSection = CreateSection("Platform");
			Add(platformSection);

			_platformChipRow = new VisualElement();
			_platformChipRow.AddToClassList("filter-chip-row");
			platformSection.Add(_platformChipRow);

			_platformAll = CreateChip("All", _platformChipRow, () => SetPlatform(string.Empty));

			// Visibility
			var visibilitySection = CreateSection("Visibility");
			Add(visibilitySection);

			var visibilityRow = new VisualElement();
			visibilityRow.AddToClassList("filter-chip-row");
			visibilitySection.Add(visibilityRow);

			_visibilityAll = CreateChip("All", visibilityRow, () => SetVisibility(VisibilityFilter.All));
			_visibilityPublic = CreateChip("Public", visibilityRow, () => SetVisibility(VisibilityFilter.Public));
			_visibilityPrivate = CreateChip("Private", visibilityRow, () => SetVisibility(VisibilityFilter.Private));

			// Topics
			_topicSection = CreateSection("Topics");
			Add(_topicSection);

			_topicChipRow = new VisualElement();
			_topicChipRow.AddToClassList("filter-chip-row");
			_topicSection.Add(_topicChipRow);

			Refresh();
		}

		public void UpdateAvailablePlatforms(IEnumerable<string> platforms)
		{
			foreach (var chip in _platformChips)
			{
				_platformChipRow.Remove(chip);
			}
			_platformChips.Clear();

			foreach (var platform in platforms)
			{
				var captured = platform;
				var chip = CreateChip(FormatPlatform(platform), _platformChipRow, () => SetPlatform(captured));
				chip.userData = captured;
				_platformChips.Add(chip);
			}

			RefreshPlatformChips();
		}

		public void UpdateAvailableTopics(IEnumerable<string> topics)
		{
			foreach (var chip in _topicChips)
			{
				_topicChipRow.Remove(chip);
			}
			_topicChips.Clear();

			foreach (var topic in topics)
			{
				var captured = topic;
				var chip = CreateChip(topic, _topicChipRow, () => ToggleTopic(captured));
				chip.userData = captured;
				_topicChips.Add(chip);
			}

			_topicSection.style.display = _topicChips.Count > 0
				? DisplayStyle.Flex
				: DisplayStyle.None;

			RefreshTopicChips();
		}

		public void UpdateBookmarkEnabled(bool isLoggedIn)
		{
			_bookmarkAll.SetEnabled(isLoggedIn);
			_bookmarkBookmarked.SetEnabled(isLoggedIn);
			_bookmarkNotBookmarked.SetEnabled(isLoggedIn);

			if (!isLoggedIn)
			{
				_bookmarkAll.AddToClassList("filter-chip-disabled");
				_bookmarkBookmarked.AddToClassList("filter-chip-disabled");
				_bookmarkNotBookmarked.AddToClassList("filter-chip-disabled");
				_bookmarkHint.style.display = DisplayStyle.Flex;
			}
			else
			{
				_bookmarkAll.RemoveFromClassList("filter-chip-disabled");
				_bookmarkBookmarked.RemoveFromClassList("filter-chip-disabled");
				_bookmarkNotBookmarked.RemoveFromClassList("filter-chip-disabled");
				_bookmarkHint.style.display = DisplayStyle.None;
			}
		}

		public void Refresh()
		{
			RefreshInstallChips();
			RefreshBookmarkChips();
			RefreshPlatformChips();
			RefreshVisibilityChips();
			RefreshTopicChips();
		}

		private void OnClearAll()
		{
			_state.Reset();
			Refresh();
			_onChanged?.Invoke();
		}

		private void SetInstall(InstallFilter value)
		{
			_state.Install = value;
			RefreshInstallChips();
			_onChanged?.Invoke();
		}

		private void SetBookmark(BookmarkFilter value)
		{
			_state.Bookmark = value;
			RefreshBookmarkChips();
			_onChanged?.Invoke();
		}

		private void SetPlatform(string value)
		{
			_state.Platform = value;
			RefreshPlatformChips();
			_onChanged?.Invoke();
		}

		private void SetVisibility(VisibilityFilter value)
		{
			_state.Visibility = value;
			RefreshVisibilityChips();
			_onChanged?.Invoke();
		}

		private void ToggleTopic(string topic)
		{
			if (!_state.SelectedTopics.Remove(topic))
			{
				_state.SelectedTopics.Add(topic);
			}
			RefreshTopicChips();
			_onChanged?.Invoke();
		}

		private void RefreshInstallChips()
		{
			SetChipActive(_installAll, _state.Install == InstallFilter.All);
			SetChipActive(_installInstalled, _state.Install == InstallFilter.Installed);
			SetChipActive(_installNotInstalled, _state.Install == InstallFilter.NotInstalled);
		}

		private void RefreshBookmarkChips()
		{
			SetChipActive(_bookmarkAll, _state.Bookmark == BookmarkFilter.All);
			SetChipActive(_bookmarkBookmarked, _state.Bookmark == BookmarkFilter.Bookmarked);
			SetChipActive(_bookmarkNotBookmarked, _state.Bookmark == BookmarkFilter.NotBookmarked);
		}

		private void RefreshPlatformChips()
		{
			SetChipActive(_platformAll, string.IsNullOrEmpty(_state.Platform));

			foreach (var chip in _platformChips)
			{
				var platform = chip.userData as string;
				SetChipActive(chip, string.Equals(platform, _state.Platform, StringComparison.OrdinalIgnoreCase));
			}
		}

		private void RefreshVisibilityChips()
		{
			SetChipActive(_visibilityAll, _state.Visibility == VisibilityFilter.All);
			SetChipActive(_visibilityPublic, _state.Visibility == VisibilityFilter.Public);
			SetChipActive(_visibilityPrivate, _state.Visibility == VisibilityFilter.Private);
		}

		private void RefreshTopicChips()
		{
			foreach (var chip in _topicChips)
			{
				var topic = chip.userData as string;
				SetChipActive(chip, topic != null && _state.SelectedTopics.Contains(topic));
			}
		}

		private static void SetChipActive(Button chip, bool active)
		{
			if (active)
			{
				chip.AddToClassList("filter-chip-active");
			}
			else
			{
				chip.RemoveFromClassList("filter-chip-active");
			}
		}

		private static Button CreateChip(string label, VisualElement parent, Action onClick)
		{
			var chip = new Button(onClick);
			chip.text = label;
			chip.AddToClassList("filter-chip");
			chip.userData = label;
			parent.Add(chip);
			return chip;
		}

		private static VisualElement CreateSection(string label)
		{
			var section = new VisualElement();
			section.AddToClassList("filter-section");

			var sectionLabel = new Label(label);
			sectionLabel.AddToClassList("filter-section-label");
			section.Add(sectionLabel);

			return section;
		}

		private static string FormatPlatform(string platform)
		{
			if (string.IsNullOrEmpty(platform)) return platform;
			return char.ToUpper(platform[0]) + platform.Substring(1);
		}
	}
}
