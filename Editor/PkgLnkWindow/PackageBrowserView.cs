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
	/// Main browser view with a virtualized, zero-allocation card grid.
	/// A fixed pool of card elements is circularly reused as the user scrolls.
	/// Cards beyond loaded data display as ghost skeletons.
	/// </summary>
	public class PackageBrowserView : VisualElement
	{
		private enum BrowseTab
		{
			Browse,
			Bookmarks,
			MyPackages
		}

		private const double DebounceSeconds = 0.4;
		private const int PageSize = 40;
		private const float MinCardWidth = 280f;
		private const float CardGap = 8f;
		private const float FixedCardHeight = 300f;
		private const float RowGap = 8f;
		private const float ContainerPadding = 8f;
		private const int BufferRows = 2;
		private const int GhostRows = 3;
		private const float PrefetchViewportMultiplier = 3f;

		// Header
		private readonly Button _profileButton;
		private readonly VisualElement _avatarImage;
		private readonly Label _usernameLabel;
		private readonly Button _signInButton;
		private readonly VisualElement _profileDropdown;
		private readonly Button _accountButton;
		private readonly Button _signOutButton;

		// Tabs
		private readonly Button _browseTab;
		private readonly Button _bookmarksTab;
		private readonly Button _myPackagesTab;

		// Search & Filter
		private readonly VisualElement _searchRow;
		private readonly VisualElement _searchGroup;
		private readonly TextField _searchField;
		private readonly Button _clearSearchButton;
		private readonly Button _filterButton;
		private readonly Label _filterBadge;
		private readonly PackageFilterDropdown _filterDropdown;
		private readonly PackageFilterState _filterState = new PackageFilterState();

		// Content
		private readonly Label _statusLabel;
		private readonly ScrollView _scrollView;
		private readonly VisualElement _cardContainer;
		private readonly PackageDetailView _detailView;
		private readonly VisualElement _listView;

		// Login modal
		private readonly VisualElement _loginOverlay;
		private readonly Label _loginMessage;
		private readonly Button _loginButton;
		private BrowseTab _pendingTab;

		// Data
		private readonly List<PackageData> _allPackages = new List<PackageData>();
		private readonly List<PackageData> _filteredPackages = new List<PackageData>();
		private readonly Dictionary<string, int> _installCounts = new Dictionary<string, int>();
		private readonly HashSet<string> _bookmarkedIds = new HashSet<string>();
		private bool _bookmarksFetched;
		private int _consecutiveEmptyFilterFetches;

		// Card pool — fixed size, circular reuse
		private readonly List<PackageCard> _cardPool = new List<PackageCard>();
		private readonly HashSet<int> _activePoolIndices = new HashSet<int>();

		// Layout state
		private int _columns = 1;
		private float _cardWidth = MinCardWidth;
		private float _centerOffset;
		private int _poolSize;

		// Row-range cache — avoids re-running PositionVisibleCards when nothing changed
		private int _prevFirstRow = -1;
		private int _prevLastRow = -1;

		// Paging
		private string _currentQuery = string.Empty;
		private string _currentTopic = string.Empty;
		private int _currentPage = 1;
		private bool _hasMore;
		private int _totalCount;
		private bool _isFetching;
		private double _searchDebounceTime;
		private bool _debounceActive;
		private BrowseTab _activeTab = BrowseTab.Browse;

		public PackageBrowserView()
		{
			AddToClassList("package-browser");

			_listView = new VisualElement();
			_listView.AddToClassList("list-view");
			Add(_listView);

			// Header bar
			var headerBar = new VisualElement();
			headerBar.AddToClassList("header-bar");
			_listView.Add(headerBar);

			var brandRow = new VisualElement();
			brandRow.AddToClassList("header-brand-row");
			headerBar.Add(brandRow);

			var logoIcon = new VisualElement();
			logoIcon.AddToClassList("header-logo");
			var iconTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(
				"Packages/com.nonatomic.pkglnk/Editor/Icons/pkglnk-box-green.png");
			if (iconTexture != null)
			{
				logoIcon.style.backgroundImage = new StyleBackground(iconTexture);
			}
			brandRow.Add(logoIcon);

			var brandLabel = new Label("pkglnk.dev");
			brandLabel.AddToClassList("header-brand");
			brandRow.Add(brandLabel);

			var authRow = new VisualElement();
			authRow.AddToClassList("header-auth-row");
			headerBar.Add(authRow);

			_signInButton = new Button(OnSignInClicked);
			_signInButton.text = "Sign In";
			_signInButton.AddToClassList("sign-in-button");
			authRow.Add(_signInButton);

			// Profile button (avatar + username, toggles dropdown)
			_profileButton = new Button(ToggleProfileDropdown);
			_profileButton.AddToClassList("profile-button");
			_profileButton.style.display = DisplayStyle.None;
			authRow.Add(_profileButton);

			_avatarImage = new VisualElement();
			_avatarImage.AddToClassList("header-avatar");
			_profileButton.Add(_avatarImage);

			_usernameLabel = new Label();
			_usernameLabel.AddToClassList("header-username");
			_profileButton.Add(_usernameLabel);

			var chevron = new Label("\u25BE");
			chevron.AddToClassList("profile-chevron");
			_profileButton.Add(chevron);

			// Profile dropdown
			_profileDropdown = new VisualElement();
			_profileDropdown.AddToClassList("profile-dropdown");
			_profileDropdown.style.display = DisplayStyle.None;
			authRow.Add(_profileDropdown);

			_accountButton = new Button(OnAccountClicked);
			_accountButton.text = "Account";
			_accountButton.AddToClassList("profile-dropdown-item");
			_profileDropdown.Add(_accountButton);

			_signOutButton = new Button(OnSignOutClicked);
			_signOutButton.text = "Sign Out";
			_signOutButton.AddToClassList("profile-dropdown-item");
			_signOutButton.AddToClassList("profile-dropdown-item-danger");
			_profileDropdown.Add(_signOutButton);

			// Tab bar
			var tabBar = new VisualElement();
			tabBar.AddToClassList("tab-bar");
			_listView.Add(tabBar);

			_browseTab = new Button(() => SwitchTab(BrowseTab.Browse));
			_browseTab.text = "Browse";
			_browseTab.AddToClassList("tab-button");
			tabBar.Add(_browseTab);

			_bookmarksTab = new Button(() => SwitchTab(BrowseTab.Bookmarks));
			_bookmarksTab.text = "Bookmarks";
			_bookmarksTab.AddToClassList("tab-button");
			tabBar.Add(_bookmarksTab);

			_myPackagesTab = new Button(() => SwitchTab(BrowseTab.MyPackages));
			_myPackagesTab.text = "My Packages";
			_myPackagesTab.AddToClassList("tab-button");
			tabBar.Add(_myPackagesTab);

			// Toolbar row (search + filter)
			_searchRow = new VisualElement();
			_searchRow.AddToClassList("search-row");
			_listView.Add(_searchRow);

			_searchGroup = new VisualElement();
			_searchGroup.AddToClassList("search-group");
			_searchRow.Add(_searchGroup);

			_filterButton = new Button(ToggleFilterDropdown);
			_filterButton.text = "\u2261";
			_filterButton.AddToClassList("filter-button");
			_searchGroup.Add(_filterButton);

			_searchField = new TextField();
			_searchField.AddToClassList("search-field");
			_searchField.RegisterValueChangedCallback(OnSearchChanged);
			_searchGroup.Add(_searchField);

			_clearSearchButton = new Button(ClearSearch);
			_clearSearchButton.text = "\u00d7";
			_clearSearchButton.AddToClassList("clear-search-button");
			_clearSearchButton.style.display = DisplayStyle.None;
			_searchGroup.Add(_clearSearchButton);

			_filterBadge = new Label();
			_filterBadge.AddToClassList("filter-badge");
			_filterBadge.style.display = DisplayStyle.None;
			_filterButton.Add(_filterBadge);

			// Status label
			_statusLabel = new Label();
			_statusLabel.AddToClassList("status-label");
			_statusLabel.style.display = DisplayStyle.None;
			_listView.Add(_statusLabel);

			// Scroll view with virtual card container
			_scrollView = new ScrollView(ScrollViewMode.Vertical);
			_scrollView.AddToClassList("package-list-scroll");
			_listView.Add(_scrollView);

			_cardContainer = new VisualElement();
			_cardContainer.AddToClassList("package-card-container");
			_scrollView.Add(_cardContainer);

			// Filter dropdown (overlays card grid)
			_filterDropdown = new PackageFilterDropdown(_filterState, OnFiltersChanged);
			_filterDropdown.style.display = DisplayStyle.None;
			_listView.Add(_filterDropdown);

			// Click outside dropdown to dismiss
			_listView.RegisterCallback<ClickEvent>(evt =>
			{
				if (_filterDropdown.style.display != DisplayStyle.Flex) return;
				if (evt.target is not VisualElement target) return;
				if (_filterDropdown.Contains(target) || target == _filterDropdown) return;
				if (_filterButton.Contains(target) || target == _filterButton) return;
				CloseFilterDropdown();
			});

			// Detail view (hidden initially)
			_detailView = new PackageDetailView(OnBackToList, OnTopicClicked);
			_detailView.style.display = DisplayStyle.None;
			Add(_detailView);

			// Login modal (hidden initially)
			_loginOverlay = new VisualElement();
			_loginOverlay.AddToClassList("login-overlay");
			_loginOverlay.style.display = DisplayStyle.None;
			_loginOverlay.RegisterCallback<ClickEvent>(evt =>
			{
				if (evt.target == _loginOverlay) DismissLoginModal();
			});
			Add(_loginOverlay);

			var loginCard = new VisualElement();
			loginCard.AddToClassList("login-card");
			_loginOverlay.Add(loginCard);

			var loginTitle = new Label("Sign in required");
			loginTitle.AddToClassList("login-card-title");
			loginCard.Add(loginTitle);

			_loginMessage = new Label();
			_loginMessage.AddToClassList("login-card-message");
			loginCard.Add(_loginMessage);

			_loginButton = new Button(OnLoginModalSignIn);
			_loginButton.text = "Sign In";
			_loginButton.AddToClassList("login-card-button");
			loginCard.Add(_loginButton);

			var cancelButton = new Button(DismissLoginModal);
			cancelButton.text = "Cancel";
			cancelButton.AddToClassList("login-card-cancel");
			loginCard.Add(cancelButton);

			// Click outside profile dropdown to dismiss
			RegisterCallback<ClickEvent>(evt =>
			{
				if (_profileDropdown.style.display != DisplayStyle.Flex) return;
				if (evt.target is not VisualElement target) return;
				if (_profileDropdown.Contains(target) || target == _profileDropdown) return;
				if (_profileButton.Contains(target) || target == _profileButton) return;
				CloseProfileDropdown();
			});

			// Lifecycle
			RegisterCallback<AttachToPanelEvent>(OnAttachToPanel);
			RegisterCallback<DetachFromPanelEvent>(OnDetachFromPanel);

			// Layout and scroll
			_scrollView.RegisterCallback<GeometryChangedEvent>(_ => UpdateLayout());
			_scrollView.verticalScroller.valueChanged += OnScrollChanged;

			// Initial state
			UpdateAuthUI();
			UpdateTabState();
			FetchPackages(true);
			FetchBookmarkIds();
		}

		public void RefreshInstalledState()
		{
			foreach (var card in _cardPool)
			{
				if (card.style.display == DisplayStyle.Flex && card.Package != null)
				{
					card.UpdateInstalledState(PackageInstaller.IsInstalled(card.Package));
				}
			}

			_detailView.RefreshInstalledState();
		}

		private void OnAttachToPanel(AttachToPanelEvent evt)
		{
			EditorApplication.update += OnEditorUpdate;
		}

		private void OnDetachFromPanel(DetachFromPanelEvent evt)
		{
			EditorApplication.update -= OnEditorUpdate;
			UnregisterCallback<AttachToPanelEvent>(OnAttachToPanel);
			UnregisterCallback<DetachFromPanelEvent>(OnDetachFromPanel);
		}

		private void OnEditorUpdate()
		{
			if (!_debounceActive) return;
			if (EditorApplication.timeSinceStartup - _searchDebounceTime < DebounceSeconds) return;

			_debounceActive = false;
			FetchPackages(true);
		}

		// ─── Auth ───────────────────────────────────────────────────────

		private void OnSignInClicked()
		{
			_signInButton.SetEnabled(false);
			_signInButton.text = "Signing in...";

			PkgLnkAuth.Login((success, error) =>
			{
				_signInButton.SetEnabled(true);
				_signInButton.text = "Sign In";

				if (!success)
				{
					Debug.LogWarning($"[PkgLnk] Sign in failed: {error}");
				}

				UpdateAuthUI();
				UpdateTabState();

				if (success)
				{
					FetchBookmarkIds();
					if (_activeTab != BrowseTab.Browse)
					{
						FetchPackages(true);
					}
				}
			});
		}

		private void OnSignOutClicked()
		{
			CloseProfileDropdown();
			PkgLnkAuth.Logout();
			_bookmarkedIds.Clear();
			_bookmarksFetched = false;
			_filterState.Bookmark = BookmarkFilter.All;
			_filterDropdown.Refresh();
			UpdateFilterBadge();
			UpdateAuthUI();

			if (_activeTab != BrowseTab.Browse)
			{
				SwitchTab(BrowseTab.Browse);
			}
			else
			{
				UpdateTabState();
			}
		}

		private void UpdateAuthUI()
		{
			var loggedIn = PkgLnkAuth.IsLoggedIn;

			_signInButton.style.display = loggedIn ? DisplayStyle.None : DisplayStyle.Flex;
			_profileButton.style.display = loggedIn ? DisplayStyle.Flex : DisplayStyle.None;
			_profileDropdown.style.display = DisplayStyle.None;

			if (loggedIn)
			{
				var username = PkgLnkAuth.Username;
				_usernameLabel.text = string.IsNullOrEmpty(username) ? string.Empty : username;
				LoadAvatar();
			}
			else
			{
				_avatarImage.style.backgroundImage = StyleKeyword.None;
			}
		}

		private void LoadAvatar()
		{
			var avatarUrl = PkgLnkAuth.AvatarUrl;
			if (string.IsNullOrEmpty(avatarUrl))
			{
				_avatarImage.style.display = DisplayStyle.None;
				return;
			}

			ImageLoader.Load(avatarUrl, texture =>
			{
				if (texture != null)
				{
					_avatarImage.style.backgroundImage = new StyleBackground(texture);
					_avatarImage.style.display = DisplayStyle.Flex;
				}
				else
				{
					_avatarImage.style.display = DisplayStyle.None;
				}
			});
		}

		private void ToggleProfileDropdown()
		{
			var isVisible = _profileDropdown.style.display == DisplayStyle.Flex;
			_profileDropdown.style.display = isVisible ? DisplayStyle.None : DisplayStyle.Flex;
		}

		private void CloseProfileDropdown()
		{
			_profileDropdown.style.display = DisplayStyle.None;
		}

		private void OnAccountClicked()
		{
			CloseProfileDropdown();
			Application.OpenURL("https://pkglnk.dev/account");
		}

		// ─── Login Modal ────────────────────────────────────────────────

		private void ShowLoginModal(BrowseTab tab)
		{
			_pendingTab = tab;
			_loginMessage.text = tab == BrowseTab.Bookmarks
				? "Sign in to access your bookmarks."
				: "Sign in to access your packages.";
			_loginButton.text = "Sign In";
			_loginButton.SetEnabled(true);
			_loginOverlay.style.display = DisplayStyle.Flex;
		}

		private void DismissLoginModal()
		{
			_loginOverlay.style.display = DisplayStyle.None;
		}

		private void OnLoginModalSignIn()
		{
			_loginButton.SetEnabled(false);
			_loginButton.text = "Signing in...";

			PkgLnkAuth.Login((success, error) =>
			{
				_loginButton.SetEnabled(true);
				_loginButton.text = "Sign In";

				if (success)
				{
					DismissLoginModal();
					UpdateAuthUI();
					FetchBookmarkIds();
					SwitchTab(_pendingTab);
				}
				else
				{
					Debug.LogWarning($"[PkgLnk] Sign in failed: {error}");
				}
			});
		}

		// ─── Tabs ───────────────────────────────────────────────────────

		private void SwitchTab(BrowseTab tab)
		{
			if (_activeTab == tab) return;

			// Auth-required tabs show login modal when not signed in
			if (tab != BrowseTab.Browse && !PkgLnkAuth.IsLoggedIn)
			{
				ShowLoginModal(tab);
				return;
			}

			_activeTab = tab;
			_currentTopic = string.Empty;
			_searchField.SetValueWithoutNotify(string.Empty);
			_currentQuery = string.Empty;
			_clearSearchButton.style.display = DisplayStyle.None;
			_debounceActive = false;

			UpdateTabState();
			FetchPackages(true);
		}

		private void UpdateTabState()
		{
			SetTabActive(_browseTab, _activeTab == BrowseTab.Browse);
			SetTabActive(_bookmarksTab, _activeTab == BrowseTab.Bookmarks);
			SetTabActive(_myPackagesTab, _activeTab == BrowseTab.MyPackages);

			_searchGroup.style.display = DisplayStyle.Flex;
		}

		private void SetTabActive(Button tab, bool active)
		{
			if (active)
			{
				tab.AddToClassList("tab-button-active");
			}
			else
			{
				tab.RemoveFromClassList("tab-button-active");
			}
		}

		// ─── Search ─────────────────────────────────────────────────────

		private void OnSearchChanged(ChangeEvent<string> evt)
		{
			_currentQuery = evt.newValue;
			_searchDebounceTime = EditorApplication.timeSinceStartup;
			_debounceActive = true;

			_clearSearchButton.style.display = string.IsNullOrEmpty(_currentQuery)
				? DisplayStyle.None
				: DisplayStyle.Flex;
		}

		private void ClearSearch()
		{
			_searchField.SetValueWithoutNotify(string.Empty);
			_currentQuery = string.Empty;
			_clearSearchButton.style.display = DisplayStyle.None;
			_debounceActive = false;
			FetchPackages(true);
		}

		// ─── Data Fetching ──────────────────────────────────────────────

		private void FetchPackages(bool freshSearch)
		{
			if (_isFetching) return;

			if (freshSearch)
			{
				_currentPage = 1;
				_allPackages.Clear();
				_filteredPackages.Clear();
				_installCounts.Clear();
				_consecutiveEmptyFilterFetches = 0;
				_scrollView.scrollOffset = Vector2.zero;
				HideStatus();
			}

			_isFetching = true;

			if (freshSearch)
			{
				// Full layout — shows ghosts for empty data
				UpdateLayout();
			}
			else
			{
				// Append: just extend container height for ghost rows, invalidate cache
				var totalRows = TotalVirtualRows();
				var totalHeight = totalRows > 0 ? totalRows * RowHeight - RowGap + ContainerPadding * 2 : 0;
				_cardContainer.style.height = totalHeight;
				_prevFirstRow = -1;
				_prevLastRow = -1;
			}

			switch (_activeTab)
			{
				case BrowseTab.Browse:
					PkgLnkApiClient.FetchDirectory(
						_currentQuery,
						_currentTopic,
						_currentPage,
						PageSize,
						(response, error) => OnFetchComplete(response, error, freshSearch));
					break;

				case BrowseTab.Bookmarks:
					if (!PkgLnkAuth.IsLoggedIn)
					{
						_isFetching = false;
						ShowStatus("Sign in to see your bookmarks.");
						return;
					}
					PkgLnkApiClient.FetchBookmarks(
						PkgLnkAuth.Token,
						(response, error) => OnFetchComplete(response, error, freshSearch));
					break;

				case BrowseTab.MyPackages:
					if (!PkgLnkAuth.IsLoggedIn)
					{
						_isFetching = false;
						ShowStatus("Sign in to see your packages.");
						return;
					}
					PkgLnkApiClient.FetchUserPackages(
						PkgLnkAuth.Token,
						(response, error) => OnFetchComplete(response, error, freshSearch));
					break;
			}
		}

		private void OnFetchComplete(DirectoryResponse response, string error, bool freshSearch)
		{
			_isFetching = false;

			if (error != null)
			{
				HideAllPoolCards();
				ShowStatus($"Error: {error}");
				return;
			}

			if (response == null)
			{
				HideAllPoolCards();
				ShowStatus("No response received.");
				return;
			}

			_hasMore = response.hasMore;
			_totalCount = response.totalCount;

			if (response.installCounts != null)
			{
				foreach (var kvp in response.installCounts)
				{
					_installCounts[kvp.Key] = kvp.Value;
				}
			}

			foreach (var pkg in response.packages)
			{
				_allPackages.Add(pkg);
			}

			if (_allPackages.Count == 0)
			{
				HideAllPoolCards();
				var emptyMessage = _activeTab switch
				{
					BrowseTab.Bookmarks => "No bookmarked packages.",
					BrowseTab.MyPackages => "No packages found.",
					_ => "No packages found."
				};
				ShowStatus(emptyMessage);
				_cardContainer.style.height = 0;
				return;
			}

			var prevFilteredCount = _filteredPackages.Count;
			ApplyFilters();

			if (_filteredPackages.Count == prevFilteredCount && _filterState.HasActiveFilters)
			{
				_consecutiveEmptyFilterFetches++;
			}
			else
			{
				_consecutiveEmptyFilterFetches = 0;
			}

			TryPrefetchForFilters();
		}

		// ─── Virtual Grid ───────────────────────────────────────────────

		private float RowHeight => FixedCardHeight + RowGap;

		private int TotalVirtualRows()
		{
			var dataRows = _filteredPackages.Count > 0
				? Mathf.CeilToInt((float)_filteredPackages.Count / Mathf.Max(1, _columns))
				: 0;

			// Extend with ghost rows when more data is pending
			if (_hasMore || _isFetching) return dataRows + GhostRows;
			if (_filteredPackages.Count == 0 && _isFetching) return GhostRows;
			return dataRows;
		}

		private void UpdateLayout()
		{
			var containerWidth = _scrollView.contentViewport.resolvedStyle.width;
			if (float.IsNaN(containerWidth) || containerWidth <= 0) return;

			// Invalidate row-range cache so PositionVisibleCards runs fresh
			_prevFirstRow = -1;
			_prevLastRow = -1;

			var availableWidth = containerWidth - ContainerPadding * 2;
			_columns = Mathf.Max(1, Mathf.FloorToInt(availableWidth / (MinCardWidth + CardGap)));
			_cardWidth = Mathf.Floor((availableWidth - (_columns - 1) * CardGap) / _columns);

			var totalGridWidth = _columns * _cardWidth + (_columns - 1) * CardGap;
			_centerOffset = ContainerPadding + Mathf.Floor((availableWidth - totalGridWidth) / 2f);

			var totalRows = TotalVirtualRows();
			var totalHeight = totalRows > 0 ? totalRows * RowHeight - RowGap + ContainerPadding * 2 : 0;
			_cardContainer.style.height = totalHeight;

			// Pool size = viewport rows + buffer, clamped to what we need
			var viewportHeight = _scrollView.contentViewport.resolvedStyle.height;
			if (float.IsNaN(viewportHeight) || viewportHeight <= 0) viewportHeight = 600f;
			var visibleRowCount = Mathf.CeilToInt(viewportHeight / RowHeight) + BufferRows * 2;
			var neededPool = visibleRowCount * _columns;

			if (neededPool > _poolSize)
			{
				EnsurePoolSize(neededPool);
				_poolSize = neededPool;
			}

			PositionVisibleCards();
		}

		private void PositionVisibleCards()
		{
			var totalRows = TotalVirtualRows();
			if (totalRows == 0)
			{
				HideAllPoolCards();
				_prevFirstRow = -1;
				_prevLastRow = -1;
				return;
			}

			var scrollOffset = _scrollView.verticalScroller.value;
			var viewportHeight = _scrollView.contentViewport.resolvedStyle.height;
			if (float.IsNaN(viewportHeight) || viewportHeight <= 0) return;

			var firstRow = Mathf.Max(0,
				Mathf.FloorToInt((scrollOffset - ContainerPadding) / RowHeight) - BufferRows);
			var lastRow = Mathf.Min(totalRows - 1,
				Mathf.CeilToInt((scrollOffset + viewportHeight - ContainerPadding) / RowHeight) + BufferRows);

			// Early-out: nothing changed since last frame
			if (firstRow == _prevFirstRow && lastRow == _prevLastRow) return;
			_prevFirstRow = firstRow;
			_prevLastRow = lastRow;

			// Track which pool slots are in use this frame
			_activePoolIndices.Clear();
			var totalVirtualSlots = totalRows * _columns;

			for (var row = firstRow; row <= lastRow; row++)
			{
				for (var col = 0; col < _columns; col++)
				{
					var dataIndex = row * _columns + col;
					if (dataIndex >= totalVirtualSlots) break;

					// Circular pool mapping
					var poolIndex = dataIndex % _poolSize;
					if (poolIndex >= _cardPool.Count) break;

					_activePoolIndices.Add(poolIndex);
					var card = _cardPool[poolIndex];

					// Bind real data or show ghost — skip if already correct
					if (dataIndex < _filteredPackages.Count)
					{
						var pkg = _filteredPackages[dataIndex];
						if (card.Package != pkg)
						{
							_installCounts.TryGetValue(pkg.id, out var installCount);
							var isBookmarked = _bookmarkedIds.Contains(pkg.id);
							var showBookmark = PkgLnkAuth.IsLoggedIn;
							card.Bind(pkg, installCount, isBookmarked, showBookmark);
						}
					}
					else
					{
						card.ShowGhost();
					}

					card.style.top = ContainerPadding + row * RowHeight;
					card.style.left = _centerOffset + col * (_cardWidth + CardGap);
					card.style.width = _cardWidth;
					card.style.display = DisplayStyle.Flex;
				}
			}

			// Hide pool cards not in use
			for (var i = 0; i < _cardPool.Count; i++)
			{
				if (!_activePoolIndices.Contains(i))
				{
					_cardPool[i].style.display = DisplayStyle.None;
				}
			}
		}

		private void OnScrollChanged(float value)
		{
			PositionVisibleCards();

			// Prefetch: trigger next page well before the user reaches loaded data boundary.
			// Measure distance from viewport bottom to the last loaded data row (not ghost rows).
			if (!_isFetching && _hasMore && _activeTab == BrowseTab.Browse)
			{
				var scrollOffset = _scrollView.verticalScroller.value;
				var viewportHeight = _scrollView.contentViewport.resolvedStyle.height;

				if (!float.IsNaN(viewportHeight) && viewportHeight > 0)
				{
					var dataRows = _filteredPackages.Count > 0
						? Mathf.CeilToInt((float)_filteredPackages.Count / Mathf.Max(1, _columns))
						: 0;
					var dataBottom = dataRows * RowHeight + ContainerPadding;
					var viewportBottom = scrollOffset + viewportHeight;
					var distanceToDataEnd = dataBottom - viewportBottom;

					// Fetch when within N viewports of the data boundary
					if (distanceToDataEnd < viewportHeight * PrefetchViewportMultiplier)
					{
						_currentPage++;
						FetchPackages(false);
					}
				}
			}
		}

		private void EnsurePoolSize(int needed)
		{
			while (_cardPool.Count < needed)
			{
				var card = new PackageCard(OnCardClicked, OnTopicClicked, OnInstallClicked, OnBookmarkClicked);
				card.style.display = DisplayStyle.None;
				_cardPool.Add(card);
				_cardContainer.Add(card);
			}
		}

		private void HideAllPoolCards()
		{
			foreach (var card in _cardPool)
			{
				card.style.display = DisplayStyle.None;
			}
		}

		// ─── UI Helpers ─────────────────────────────────────────────────

		private void ShowStatus(string message)
		{
			_statusLabel.text = message;
			_statusLabel.style.display = DisplayStyle.Flex;
		}

		private void HideStatus()
		{
			_statusLabel.style.display = DisplayStyle.None;
		}

		// ─── Navigation ─────────────────────────────────────────────────

		private void OnCardClicked(PackageCard card)
		{
			_installCounts.TryGetValue(card.Package.id, out var installCount);
			_detailView.Show(card.Package, installCount);
			_listView.style.display = DisplayStyle.None;
			_detailView.style.display = DisplayStyle.Flex;
		}

		private void OnBackToList()
		{
			_detailView.style.display = DisplayStyle.None;
			_listView.style.display = DisplayStyle.Flex;
			RefreshInstalledState();
		}

		private void OnTopicClicked(string topic)
		{
			_detailView.style.display = DisplayStyle.None;
			_listView.style.display = DisplayStyle.Flex;

			if (_activeTab != BrowseTab.Browse)
			{
				SwitchTab(BrowseTab.Browse);
			}

			_currentTopic = topic;
			_searchField.SetValueWithoutNotify(string.Empty);
			_currentQuery = string.Empty;
			_clearSearchButton.style.display = DisplayStyle.None;
			_debounceActive = false;
			FetchPackages(true);
		}

		private void OnInstallClicked(PackageCard card)
		{
			if (PackageInstaller.IsInstalling) return;

			card.SetInstalling(true);

			PackageInstaller.Install(card.Package, (success, error) =>
			{
				card.SetInstalling(false);

				if (success)
				{
					card.UpdateInstalledState(true);
				}
				else
				{
					Debug.LogError($"[PkgLnk] Failed to install {card.Package.display_name}: {error}");
				}
			});
		}

		private void OnBookmarkClicked(PackageCard card)
		{
			if (!PkgLnkAuth.IsLoggedIn)
			{
				ShowLoginModal(BrowseTab.Bookmarks);
				return;
			}

			if (card.Package == null) return;

			var packageId = card.Package.id;
			var wasBookmarked = _bookmarkedIds.Contains(packageId);

			// Optimistic update
			if (wasBookmarked)
				_bookmarkedIds.Remove(packageId);
			else
				_bookmarkedIds.Add(packageId);
			card.UpdateBookmarkState(!wasBookmarked);

			PkgLnkApiClient.ToggleBookmark(PkgLnkAuth.Token, packageId, (success, error) =>
			{
				if (!success)
				{
					// Revert on failure
					if (wasBookmarked)
						_bookmarkedIds.Add(packageId);
					else
						_bookmarkedIds.Remove(packageId);
					card.UpdateBookmarkState(wasBookmarked);
					Debug.LogWarning($"[PkgLnk] Failed to toggle bookmark: {error}");
				}
			});
		}

		// ─── Filters ────────────────────────────────────────────────────

		private const int MaxConsecutiveEmptyFetches = 3;

		private void ApplyFilters()
		{
			_filteredPackages.Clear();

			Func<PackageData, bool> isInstalled = PackageInstaller.IsInstalled;
			Func<string, bool> isBookmarked = _bookmarksFetched
				? id => _bookmarkedIds.Contains(id)
				: (Func<string, bool>)null;

			foreach (var pkg in _allPackages)
			{
				if (PackageFilterState.Matches(pkg, _filterState, isInstalled, isBookmarked))
				{
					_filteredPackages.Add(pkg);
				}
			}

			RebuildFilterOptions();
			UpdateFilterBadge();

			if (_filteredPackages.Count == 0 && _allPackages.Count > 0 && _filterState.HasActiveFilters)
			{
				HideAllPoolCards();
				ShowStatus("No packages match the current filters.");
				_cardContainer.style.height = 0;
			}
			else if (_filteredPackages.Count > 0)
			{
				HideStatus();
				UpdateLayout();
			}
			else
			{
				UpdateLayout();
			}
		}

		private void RebuildFilterOptions()
		{
			var platforms = new HashSet<string>();
			var topics = new HashSet<string>();

			foreach (var pkg in _allPackages)
			{
				if (!string.IsNullOrEmpty(pkg.git_platform))
				{
					platforms.Add(pkg.git_platform);
				}

				if (pkg.topics == null) continue;
				foreach (var topic in pkg.topics)
				{
					topics.Add(topic);
				}
			}

			_filterDropdown.UpdateAvailablePlatforms(platforms);
			_filterDropdown.UpdateAvailableTopics(topics);
			_filterDropdown.UpdateBookmarkEnabled(PkgLnkAuth.IsLoggedIn);
		}

		private void OnFiltersChanged()
		{
			_consecutiveEmptyFilterFetches = 0;
			ApplyFilters();
			TryPrefetchForFilters();
		}

		private void ToggleFilterDropdown()
		{
			var isVisible = _filterDropdown.style.display == DisplayStyle.Flex;
			if (isVisible)
			{
				CloseFilterDropdown();
			}
			else
			{
				_filterDropdown.UpdateBookmarkEnabled(PkgLnkAuth.IsLoggedIn);
				_filterDropdown.style.display = DisplayStyle.Flex;
				_filterButton.AddToClassList("filter-button-active");
			}
		}

		private void CloseFilterDropdown()
		{
			_filterDropdown.style.display = DisplayStyle.None;
			_filterButton.RemoveFromClassList("filter-button-active");
		}

		private void UpdateFilterBadge()
		{
			var count = _filterState.ActiveFilterCount;
			if (count > 0)
			{
				_filterBadge.text = count.ToString();
				_filterBadge.style.display = DisplayStyle.Flex;
			}
			else
			{
				_filterBadge.style.display = DisplayStyle.None;
			}
		}

		private void TryPrefetchForFilters()
		{
			if (!_filterState.HasActiveFilters) return;
			if (_isFetching || !_hasMore) return;
			if (_consecutiveEmptyFilterFetches >= MaxConsecutiveEmptyFetches) return;

			var viewportHeight = _scrollView.contentViewport.resolvedStyle.height;
			if (float.IsNaN(viewportHeight) || viewportHeight <= 0) return;

			var filteredRows = _filteredPackages.Count > 0
				? Mathf.CeilToInt((float)_filteredPackages.Count / Mathf.Max(1, _columns))
				: 0;
			var filteredHeight = filteredRows * RowHeight;

			if (filteredHeight < viewportHeight)
			{
				_currentPage++;
				FetchPackages(false);
			}
		}

		// ─── Bookmark State ─────────────────────────────────────────────

		private void FetchBookmarkIds()
		{
			if (!PkgLnkAuth.IsLoggedIn || _bookmarksFetched) return;

			PkgLnkApiClient.FetchBookmarks(PkgLnkAuth.Token, (response, error) =>
			{
				if (error != null || response == null) return;

				_bookmarkedIds.Clear();
				foreach (var pkg in response.packages)
				{
					_bookmarkedIds.Add(pkg.id);
				}

				_bookmarksFetched = true;

				// Refresh visible cards with bookmark state
				_prevFirstRow = -1;
				_prevLastRow = -1;
				PositionVisibleCards();
			});
		}
	}
}
