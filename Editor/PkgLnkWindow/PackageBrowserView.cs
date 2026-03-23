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
			Collections,
			Bookmarks,
			MyPackages
		}

		private enum CollectionViewMode
		{
			All,
			Mine
		}

		private const string SessionKeyActiveTab = "PkgLnk_ActiveTab";
		private const string SessionKeyDetailSlug = "PkgLnk_CollectionDetailSlug";

		private const double DebounceSeconds = 0.4;
		private const int PageSize = 40;
		private const float MinCardWidth = 280f;
		private const float CardGap = 8f;
		private const float MinCardHeight = 280f;
		private const float MaxCardHeight = 400f;
		private const float RowGap = 8f;
		private const float ContainerPadding = 8f;
		private const int BufferRows = 2;
		private const int GhostRows = 3;
		private const float PrefetchViewportMultiplier = 3f;

		// Header
		private readonly VisualElement _authRow;
		private readonly Button _profileButton;
		private readonly VisualElement _avatarImage;
		private readonly Label _usernameLabel;
		private readonly Button _signInButton;
		private readonly VisualElement _profileDropdown;
		private readonly Button _accountButton;
		private readonly Button _signOutButton;

		// Tabs
		private readonly Button _browseTab;
		private readonly Button _collectionsTab;
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

		// Collection toggle
		private readonly VisualElement _collectionToggleRow;
		private readonly Button _allCollectionsButton;
		private readonly Button _myCollectionsButton;
		private readonly Button _createCollectionButton;
		private CollectionViewMode _collectionViewMode = CollectionViewMode.All;
		private bool _myCollectionsFetched;

		// Add to Collection dropdown
		private readonly AddToCollectionDropdown _addToCollectionDropdown;

		// Content
		private readonly Label _statusLabel;
		private readonly ScrollView _scrollView;
		private readonly VisualElement _cardContainer;
		private readonly PackageDetailView _detailView;
		private readonly CollectionDetailView _collectionDetailView;
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

		// Cached delegates to avoid allocation in ApplyFilters hot path
		private readonly Func<PackageData, bool> _isInstalledDelegate = PackageInstaller.IsInstalled;
		private Func<string, bool> _isBookmarkedDelegate;

		// Collections data
		private readonly List<CollectionData> _allCollections = new List<CollectionData>();
		private readonly List<CollectionData> _filteredCollections = new List<CollectionData>();
		private readonly Dictionary<string, PackageData[]> _collectionPackagesCache = new Dictionary<string, PackageData[]>();
		private int _collectionsOffset;
		private bool _collectionsHasMore;
		private int _collectionsTotalCount;

		// Card pool — fixed size, circular reuse
		private readonly List<PackageCard> _cardPool = new List<PackageCard>();
		private readonly HashSet<int> _activePoolIndices = new HashSet<int>();

		// Collection card pool
		private readonly List<CollectionCard> _collectionCardPool = new List<CollectionCard>();
		private readonly HashSet<int> _activeCollectionPoolIndices = new HashSet<int>();
		private int _collectionPoolSize;

		// Layout state
		private int _columns = 1;
		private float _cardWidth = MinCardWidth;
		private float _cardHeight = MinCardHeight;
		private float _centerOffset;
		private int _poolSize;

		// Row-range cache — avoids re-running PositionVisibleCards when nothing changed
		private int _prevFirstRow = -1;
		private int _prevLastRow = -1;
		private int _prevCollFirstRow = -1;
		private int _prevCollLastRow = -1;

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
				"Packages/com.nonatomic.pkglnk/Editor/Icons/pkglnk-box-white.png");
			if (iconTexture != null)
			{
				logoIcon.style.backgroundImage = new StyleBackground(iconTexture);
			}
			brandRow.Add(logoIcon);

			var brandLabel = new Label("pkglnk.dev");
			brandLabel.AddToClassList("header-brand");
			brandRow.Add(brandLabel);

			_authRow = new VisualElement();
			_authRow.AddToClassList("header-auth-row");
			headerBar.Add(_authRow);

			_signInButton = new Button(OnSignInClicked);
			_signInButton.text = "Sign In";
			_signInButton.AddToClassList("sign-in-button");
			_authRow.Add(_signInButton);

			// Profile button (avatar + username, toggles dropdown)
			_profileButton = new Button(ToggleProfileDropdown);
			_profileButton.AddToClassList("profile-button");
			_profileButton.style.display = DisplayStyle.None;
			_authRow.Add(_profileButton);

			_avatarImage = new VisualElement();
			_avatarImage.AddToClassList("header-avatar");
			_profileButton.Add(_avatarImage);

			_usernameLabel = new Label();
			_usernameLabel.AddToClassList("header-username");
			_profileButton.Add(_usernameLabel);

			var chevron = new Label("\u25BE");
			chevron.AddToClassList("profile-chevron");
			_profileButton.Add(chevron);

			// Profile dropdown (added to _listView later so it paints above siblings)
			_profileDropdown = new VisualElement();
			_profileDropdown.AddToClassList("profile-dropdown");
			_profileDropdown.style.display = DisplayStyle.None;

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

			var bookmarkTex = AssetDatabase.LoadAssetAtPath<Texture2D>(
				"Packages/com.nonatomic.pkglnk/Editor/Icons/bookmark-outline.png");

			_browseTab = CreateTabButton("Directory", TabIcons.Compass, () => SwitchTab(BrowseTab.Browse));
			tabBar.Add(_browseTab);

			_collectionsTab = CreateTabButton("Collections", TabIcons.Folder, () => SwitchTab(BrowseTab.Collections));
			tabBar.Add(_collectionsTab);

			_bookmarksTab = CreateTabButton("Bookmarks", bookmarkTex, () => SwitchTab(BrowseTab.Bookmarks));
			tabBar.Add(_bookmarksTab);

			_myPackagesTab = CreateTabButton("My Packages", TabIcons.Grid, () => SwitchTab(BrowseTab.MyPackages));
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

			// Collection toggle row (All / Mine / + Create)
			_collectionToggleRow = new VisualElement();
			_collectionToggleRow.AddToClassList("collection-toggle-row");
			_collectionToggleRow.style.display = DisplayStyle.None;
			_listView.Add(_collectionToggleRow);

			_allCollectionsButton = new Button(() => SwitchCollectionMode(CollectionViewMode.All));
			_allCollectionsButton.text = "All";
			_allCollectionsButton.AddToClassList("toggle-button");
			_collectionToggleRow.Add(_allCollectionsButton);

			_myCollectionsButton = new Button(() => SwitchCollectionMode(CollectionViewMode.Mine));
			_myCollectionsButton.text = "Mine";
			_myCollectionsButton.AddToClassList("toggle-button");
			_collectionToggleRow.Add(_myCollectionsButton);

			_createCollectionButton = new Button(OnCreateCollectionClicked);
			_createCollectionButton.text = "+ Create";
			_createCollectionButton.AddToClassList("create-collection-button");
			_createCollectionButton.style.display = DisplayStyle.None;
			_collectionToggleRow.Add(_createCollectionButton);

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
			_filterDropdown.SetOnClose(CloseFilterDropdown);
			_filterDropdown.style.display = DisplayStyle.None;
			_listView.Add(_filterDropdown);

			// Profile dropdown (overlays everything)
			_listView.Add(_profileDropdown);

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
			_detailView = new PackageDetailView(OnBackToList, OnTopicClicked, OnAddToCollectionClicked);
			_detailView.style.display = DisplayStyle.None;
			Add(_detailView);

			// Collection detail view (hidden initially)
			_collectionDetailView = new CollectionDetailView(
				OnBackToList,
				OnEditCollection,
				OnDeleteCollection,
				OnRemovePackageFromCollection);
			Add(_collectionDetailView);

			// Add to Collection dropdown (hidden initially)
			_addToCollectionDropdown = new AddToCollectionDropdown(
				() => _addToCollectionDropdown.Hide(),
				OnCollectionCreatedFromDropdown);
			Add(_addToCollectionDropdown);

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
			_scrollView.contentViewport.RegisterCallback<GeometryChangedEvent>(_ => UpdateLayout());
			_scrollView.verticalScroller.valueChanged += OnScrollChanged;

			// Restore persisted tab
			var savedTab = SessionState.GetInt(SessionKeyActiveTab, 0);
			if (Enum.IsDefined(typeof(BrowseTab), savedTab))
			{
				_activeTab = (BrowseTab)savedTab;
			}

			// Initial state
			UpdateAuthUI();
			UpdateTabState();

			// Restore collection detail view if we were viewing one before domain reload
			var savedDetailSlug = SessionState.GetString(SessionKeyDetailSlug, string.Empty);
			if (!string.IsNullOrEmpty(savedDetailSlug) && _activeTab == BrowseTab.Collections)
			{
				ShowCollectionDetail(savedDetailSlug, false);
			}
			else if (_activeTab == BrowseTab.Collections)
			{
				FetchCollections(true);
			}
			else
			{
				FetchPackages(true);
			}
			FetchBookmarkIds();
		}

		public void AddToHeader(VisualElement element)
		{
			_authRow.Add(element);
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

			RefreshCollectionCardInstalledState();

			_detailView.RefreshInstalledState();
			_collectionDetailView.RefreshInstalledState();
		}

		private void RefreshCollectionCardInstalledState()
		{
			foreach (var card in _collectionCardPool)
			{
				if (card.style.display != DisplayStyle.Flex || card.Collection == null) continue;
				if (!_collectionPackagesCache.TryGetValue(card.Collection.slug, out var pkgs)) continue;
				card.UpdateInstalledState(pkgs);
			}
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

			if (_activeTab == BrowseTab.Browse)
			{
				if (_isFetching) return;
				_debounceActive = false;
				FetchPackages(true);
			}
			else if (_activeTab == BrowseTab.Collections)
			{
				if (_isFetching) return;
				_debounceActive = false;
				if (_collectionViewMode == CollectionViewMode.Mine)
				{
					ApplyCollectionFilters();
				}
				else
				{
					FetchCollections(true);
				}
			}
			else
			{
				_debounceActive = false;
				ApplyFilters();
			}
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
			_myCollectionsFetched = false;
			_allCollections.Clear();
			_filteredCollections.Clear();
			_collectionDetailView.Hide();
			_collectionViewMode = CollectionViewMode.All;
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
			_loginMessage.text = tab switch
			{
				BrowseTab.Bookmarks => "Sign in to access your bookmarks.",
				BrowseTab.Collections => "Sign in to manage your collections.",
				_ => "Sign in to access your packages."
			};
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

					if (_pendingTab == BrowseTab.Collections && _activeTab == BrowseTab.Collections)
					{
						// Already on Collections tab, just switch to Mine mode
						_collectionViewMode = CollectionViewMode.Mine;
						UpdateCollectionToggleButtons();
						FetchMyCollections();
					}
					else
					{
						SwitchTab(_pendingTab);
					}
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
			if (tab != BrowseTab.Browse && tab != BrowseTab.Collections && !PkgLnkAuth.IsLoggedIn)
			{
				ShowLoginModal(tab);
				return;
			}

			// Hide cards from previous tab
			if (_activeTab == BrowseTab.Collections)
			{
				HideAllCollectionPoolCards();
			}
			else
			{
				HideAllPoolCards();
			}

			_activeTab = tab;
			SessionState.SetInt(SessionKeyActiveTab, (int)tab);
			_currentTopic = string.Empty;
			_searchField.SetValueWithoutNotify(string.Empty);
			_currentQuery = string.Empty;
			_clearSearchButton.style.display = DisplayStyle.None;
			_debounceActive = false;

			UpdateTabState();

			if (tab == BrowseTab.Collections)
			{
				if (_collectionViewMode == CollectionViewMode.Mine)
				{
					FetchMyCollections();
				}
				else
				{
					FetchCollections(true);
				}
			}
			else
			{
				FetchPackages(true);
			}
		}

		private void UpdateTabState()
		{
			SetTabActive(_browseTab, _activeTab == BrowseTab.Browse);
			SetTabActive(_collectionsTab, _activeTab == BrowseTab.Collections);
			SetTabActive(_bookmarksTab, _activeTab == BrowseTab.Bookmarks);
			SetTabActive(_myPackagesTab, _activeTab == BrowseTab.MyPackages);

			_searchGroup.style.display = DisplayStyle.Flex;

			// Hide filter button on Collections tab (filters are package-specific)
			_filterButton.style.display = _activeTab == BrowseTab.Collections
				? DisplayStyle.None
				: DisplayStyle.Flex;

			// Collection toggle row
			_collectionToggleRow.style.display = _activeTab == BrowseTab.Collections
				? DisplayStyle.Flex
				: DisplayStyle.None;

			UpdateCollectionToggleButtons();
		}

		private static Button CreateTabButton(string label, Texture2D icon, Action onClick)
		{
			var button = new Button(onClick);
			button.text = string.Empty;
			button.AddToClassList("tab-button");

			var iconElement = new VisualElement();
			iconElement.AddToClassList("tab-icon");
			iconElement.pickingMode = PickingMode.Ignore;
			if (icon != null)
			{
				iconElement.style.backgroundImage = new StyleBackground(icon);
			}
			button.Add(iconElement);

			var labelElement = new Label(label);
			labelElement.AddToClassList("tab-label");
			labelElement.pickingMode = PickingMode.Ignore;
			button.Add(labelElement);

			return button;
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

		private void UpdateCollectionToggleButtons()
		{
			var isAll = _collectionViewMode == CollectionViewMode.All;

			if (isAll)
			{
				_allCollectionsButton.AddToClassList("toggle-button-active");
				_myCollectionsButton.RemoveFromClassList("toggle-button-active");
			}
			else
			{
				_allCollectionsButton.RemoveFromClassList("toggle-button-active");
				_myCollectionsButton.AddToClassList("toggle-button-active");
			}

			_createCollectionButton.style.display =
				!isAll && PkgLnkAuth.IsLoggedIn ? DisplayStyle.Flex : DisplayStyle.None;
		}

		private void SwitchCollectionMode(CollectionViewMode mode)
		{
			if (_collectionViewMode == mode) return;

			if (mode == CollectionViewMode.Mine && !PkgLnkAuth.IsLoggedIn)
			{
				ShowLoginModal(BrowseTab.Collections);
				return;
			}

			_collectionViewMode = mode;
			HideAllCollectionPoolCards();
			_scrollView.scrollOffset = Vector2.zero;
			_searchField.SetValueWithoutNotify(string.Empty);
			_currentQuery = string.Empty;
			_clearSearchButton.style.display = DisplayStyle.None;
			_debounceActive = false;
			UpdateCollectionToggleButtons();

			if (mode == CollectionViewMode.Mine)
			{
				FetchMyCollections();
			}
			else
			{
				FetchCollections(true);
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

			if (_activeTab == BrowseTab.Browse)
			{
				FetchPackages(true);
			}
			else if (_activeTab == BrowseTab.Collections)
			{
				if (_collectionViewMode == CollectionViewMode.Mine)
				{
					ApplyCollectionFilters();
				}
				else
				{
					FetchCollections(true);
				}
			}
			else
			{
				ApplyFilters();
			}
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

		private float RowHeight => _cardHeight + RowGap;

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
			if (_activeTab == BrowseTab.Collections)
			{
				UpdateCollectionLayout();
				return;
			}

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

			// Dynamic card height: fill viewport with whole rows
			var viewportHeight = _scrollView.contentViewport.resolvedStyle.height;
			if (float.IsNaN(viewportHeight) || viewportHeight <= 0) viewportHeight = 600f;

			var usableHeight = viewportHeight - ContainerPadding * 2;
			var fitRows = Mathf.Max(1, Mathf.FloorToInt((usableHeight + RowGap) / (MinCardHeight + RowGap)));
			_cardHeight = Mathf.Floor((usableHeight - (fitRows - 1) * RowGap) / fitRows);
			_cardHeight = Mathf.Clamp(_cardHeight, MinCardHeight, MaxCardHeight);

			var totalRows = TotalVirtualRows();
			var totalHeight = totalRows > 0 ? totalRows * RowHeight - RowGap + ContainerPadding * 2 : 0;
			_cardContainer.style.height = totalHeight;
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
			if (_activeTab == BrowseTab.Collections)
			{
				PositionVisibleCollectionCards();
				return;
			}

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
			if (float.IsNaN(viewportHeight) || viewportHeight <= 0) viewportHeight = 600f;

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
			var showBookmark = PkgLnkAuth.IsLoggedIn;

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
					card.style.height = _cardHeight;
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

			var scrollOffset = _scrollView.verticalScroller.value;
			var viewportHeight = _scrollView.contentViewport.resolvedStyle.height;
			if (float.IsNaN(viewportHeight) || viewportHeight <= 0) return;

			// Prefetch: trigger next page well before the user reaches loaded data boundary.
			if (!_isFetching && _activeTab == BrowseTab.Browse && _hasMore)
			{
				var dataRows = _filteredPackages.Count > 0
					? Mathf.CeilToInt((float)_filteredPackages.Count / Mathf.Max(1, _columns))
					: 0;
				var dataBottom = dataRows * RowHeight + ContainerPadding;
				var viewportBottom = scrollOffset + viewportHeight;
				var distanceToDataEnd = dataBottom - viewportBottom;

				if (distanceToDataEnd < viewportHeight * PrefetchViewportMultiplier)
				{
					_currentPage++;
					FetchPackages(false);
				}
			}
			else if (!_isFetching && _activeTab == BrowseTab.Collections && _collectionsHasMore)
			{
				var dataRows = _filteredCollections.Count > 0
					? Mathf.CeilToInt((float)_filteredCollections.Count / Mathf.Max(1, _columns))
					: 0;
				var collectionCardHeight = CollectionCardHeight + RowGap;
				var dataBottom = dataRows * collectionCardHeight + ContainerPadding;
				var viewportBottom = scrollOffset + viewportHeight;
				var distanceToDataEnd = dataBottom - viewportBottom;

				if (distanceToDataEnd < viewportHeight * PrefetchViewportMultiplier)
				{
					FetchCollections(false);
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
			_collectionDetailView.Hide();
			SessionState.EraseString(SessionKeyDetailSlug);
			_listView.style.display = DisplayStyle.Flex;
			RefreshInstalledState();

			// Invalidate row caches so positioning re-runs with existing layout dimensions
			_prevFirstRow = -1;
			_prevLastRow = -1;
			_prevCollFirstRow = -1;
			_prevCollLastRow = -1;
			PositionVisibleCards();
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

			var packageId = card.Package.id;

			PackageInstaller.Install(card.Package, (success, error) =>
			{
				card.SetInstalling(false);

				if (success)
				{
					PackageInstaller.InvalidateInstalledCache();
					card.UpdateInstalledState(true);

					_installCounts.TryGetValue(packageId, out var currentCount);
					_installCounts[packageId] = currentCount + 1;
					card.UpdateInstallCount(currentCount + 1);
				}
				else
				{
					Debug.LogError($"[PkgLnk] Failed to install {card.Package.display_name}: {error}");
				}
			}, phase => card.SetInstallPhaseText(phase));
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

			var query = _currentQuery ?? string.Empty;
			var hasQuery = query.Length > 0;

			// Browse tab: server already filters by query, skip client-side query filter
			var applyClientQuery = hasQuery && _activeTab != BrowseTab.Browse;

			_isBookmarkedDelegate ??= id => _bookmarkedIds.Contains(id);
			var isBookmarked = _bookmarksFetched ? _isBookmarkedDelegate : null;

			foreach (var pkg in _allPackages)
			{
				if (!PackageFilterState.Matches(pkg, _filterState, _isInstalledDelegate, isBookmarked))
					continue;

				if (applyClientQuery && !MatchesQuery(pkg, query))
					continue;

				_filteredPackages.Add(pkg);
			}

			RebuildFilterOptions();
			UpdateFilterBadge();

			if (_filteredPackages.Count == 0 && _allPackages.Count > 0 && (_filterState.HasActiveFilters || hasQuery))
			{
				HideAllPoolCards();
				ShowStatus(hasQuery && !_filterState.HasActiveFilters
					? "No packages match your search."
					: "No packages match the current filters.");
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

		private static bool MatchesQuery(PackageData pkg, string query)
		{
			if (FieldMatchesQuery(pkg.display_name, query)) return true;
			if (FieldMatchesQuery(pkg.description, query)) return true;
			if (FieldMatchesQuery(pkg.git_owner, query)) return true;
			if (FieldMatchesQuery(pkg.slug, query)) return true;
			if (FieldMatchesQuery(pkg.package_json_name, query)) return true;
			return false;
		}

		private static bool FieldMatchesQuery(string field, string query)
		{
			if (string.IsNullOrEmpty(field)) return false;

			// Substring match
			if (field.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
				return true;

			// Acronym match — query letters match first letter of consecutive words
			// e.g. "VSM" matches "Visual State Machine"
			var words = field.Split(new[] { ' ', '-', '_', '.' }, StringSplitOptions.RemoveEmptyEntries);
			if (query.Length <= words.Length)
			{
				var acronymMatch = true;
				for (var i = 0; i < query.Length; i++)
				{
					if (!char.ToUpperInvariant(words[i][0]).Equals(char.ToUpperInvariant(query[i])))
					{
						acronymMatch = false;
						break;
					}
				}
				if (acronymMatch) return true;
			}

			return false;
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

		// ─── Collections ────────────────────────────────────────────────

		private const float CollectionCardHeight = 200f;
		private const int CollectionPageSize = 20;

		private float CollectionRowHeight => CollectionCardHeight + RowGap;

		private void FetchCollections(bool freshSearch)
		{
			if (_isFetching) return;

			if (freshSearch)
			{
				_collectionsOffset = 0;
				_allCollections.Clear();
				_filteredCollections.Clear();
				_scrollView.scrollOffset = Vector2.zero;
				HideStatus();
			}

			_isFetching = true;

			if (freshSearch)
			{
				UpdateCollectionLayout();
			}
			else
			{
				var totalRows = TotalCollectionVirtualRows();
				var totalHeight = totalRows > 0 ? totalRows * CollectionRowHeight - RowGap + ContainerPadding * 2 : 0;
				_cardContainer.style.height = totalHeight;
				_prevCollFirstRow = -1;
				_prevCollLastRow = -1;
			}

			PkgLnkApiClient.FetchCollections(
				_currentQuery,
				string.Empty,
				_collectionsOffset,
				CollectionPageSize,
				OnCollectionsResponse);
		}

		private void OnCollectionsResponse(CollectionsResponse response, string error)
		{
			_isFetching = false;

			if (error != null)
			{
				HideAllCollectionPoolCards();
				ShowStatus($"Error: {error}");
				return;
			}

			if (response == null)
			{
				HideAllCollectionPoolCards();
				ShowStatus("No response received.");
				return;
			}

			_collectionsTotalCount = response.totalCount;

			foreach (var col in response.collections)
			{
				_allCollections.Add(col);
			}

			_collectionsOffset = _allCollections.Count;
			_collectionsHasMore = _allCollections.Count < _collectionsTotalCount;

			if (_allCollections.Count == 0)
			{
				HideAllCollectionPoolCards();
				ShowStatus("No collections found.");
				_cardContainer.style.height = 0;
				return;
			}

			ApplyCollectionFilters();
		}

		private void ApplyCollectionFilters()
		{
			_filteredCollections.Clear();

			var query = _currentQuery?.Trim() ?? string.Empty;
			var hasQuery = query.Length > 0;

			foreach (var col in _allCollections)
			{
				if (hasQuery && !MatchesCollectionQuery(col, query))
					continue;

				_filteredCollections.Add(col);
			}

			if (_filteredCollections.Count == 0 && _allCollections.Count > 0 && hasQuery)
			{
				HideAllCollectionPoolCards();
				ShowStatus("No collections match your search.");
				_cardContainer.style.height = 0;
			}
			else if (_filteredCollections.Count > 0)
			{
				HideStatus();
				UpdateCollectionLayout();
			}
			else
			{
				UpdateCollectionLayout();
			}
		}

		private static bool MatchesCollectionQuery(CollectionData col, string query)
		{
			if (!string.IsNullOrEmpty(col.name) &&
			    col.name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
				return true;
			if (!string.IsNullOrEmpty(col.description) &&
			    col.description.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
				return true;
			if (!string.IsNullOrEmpty(col.slug) &&
			    col.slug.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
				return true;
			if (!string.IsNullOrEmpty(col.owner_username) &&
			    col.owner_username.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
				return true;
			return false;
		}

		private int TotalCollectionVirtualRows()
		{
			var dataRows = _filteredCollections.Count > 0
				? Mathf.CeilToInt((float)_filteredCollections.Count / Mathf.Max(1, _columns))
				: 0;

			if (_collectionsHasMore || _isFetching) return dataRows + GhostRows;
			if (_filteredCollections.Count == 0 && _isFetching) return GhostRows;
			return dataRows;
		}

		private void UpdateCollectionLayout()
		{
			var containerWidth = _scrollView.contentViewport.resolvedStyle.width;
			if (float.IsNaN(containerWidth) || containerWidth <= 0) return;

			_prevCollFirstRow = -1;
			_prevCollLastRow = -1;

			var availableWidth = containerWidth - ContainerPadding * 2;
			_columns = Mathf.Max(1, Mathf.FloorToInt(availableWidth / (MinCardWidth + CardGap)));
			_cardWidth = Mathf.Floor((availableWidth - (_columns - 1) * CardGap) / _columns);

			var totalGridWidth = _columns * _cardWidth + (_columns - 1) * CardGap;
			_centerOffset = ContainerPadding + Mathf.Floor((availableWidth - totalGridWidth) / 2f);

			var totalRows = TotalCollectionVirtualRows();
			var totalHeight = totalRows > 0 ? totalRows * CollectionRowHeight - RowGap + ContainerPadding * 2 : 0;
			_cardContainer.style.height = totalHeight;

			var viewportHeight = _scrollView.contentViewport.resolvedStyle.height;
			if (float.IsNaN(viewportHeight) || viewportHeight <= 0) viewportHeight = 600f;

			var visibleRowCount = Mathf.CeilToInt(viewportHeight / CollectionRowHeight) + BufferRows * 2;
			var neededPool = visibleRowCount * _columns;

			if (neededPool > _collectionPoolSize)
			{
				EnsureCollectionPoolSize(neededPool);
				_collectionPoolSize = neededPool;
			}

			PositionVisibleCollectionCards();
		}

		private void PositionVisibleCollectionCards()
		{
			var totalRows = TotalCollectionVirtualRows();
			if (totalRows == 0)
			{
				HideAllCollectionPoolCards();
				_prevCollFirstRow = -1;
				_prevCollLastRow = -1;
				return;
			}

			var scrollOffset = _scrollView.verticalScroller.value;
			var viewportHeight = _scrollView.contentViewport.resolvedStyle.height;
			if (float.IsNaN(viewportHeight) || viewportHeight <= 0) viewportHeight = 600f;

			var rowHeight = CollectionRowHeight;
			var firstRow = Mathf.Max(0,
				Mathf.FloorToInt((scrollOffset - ContainerPadding) / rowHeight) - BufferRows);
			var lastRow = Mathf.Min(totalRows - 1,
				Mathf.CeilToInt((scrollOffset + viewportHeight - ContainerPadding) / rowHeight) + BufferRows);

			if (firstRow == _prevCollFirstRow && lastRow == _prevCollLastRow) return;
			_prevCollFirstRow = firstRow;
			_prevCollLastRow = lastRow;

			_activeCollectionPoolIndices.Clear();
			var totalVirtualSlots = totalRows * _columns;

			for (var row = firstRow; row <= lastRow; row++)
			{
				for (var col = 0; col < _columns; col++)
				{
					var dataIndex = row * _columns + col;
					if (dataIndex >= totalVirtualSlots) break;

					var poolIndex = dataIndex % _collectionPoolSize;
					if (poolIndex >= _collectionCardPool.Count) break;

					_activeCollectionPoolIndices.Add(poolIndex);
					var card = _collectionCardPool[poolIndex];

					if (dataIndex < _filteredCollections.Count)
					{
						var colData = _filteredCollections[dataIndex];
						if (card.Collection != colData)
						{
							card.Bind(colData);
							if (_collectionPackagesCache.TryGetValue(colData.slug, out var pkgs))
							{
								card.UpdateInstalledState(pkgs);
							}
						}
					}
					else
					{
						card.ShowGhost();
					}

					card.style.top = ContainerPadding + row * rowHeight;
					card.style.left = _centerOffset + col * (_cardWidth + CardGap);
					card.style.width = _cardWidth;
					card.style.height = CollectionCardHeight;
					card.style.display = DisplayStyle.Flex;
				}
			}

			for (var i = 0; i < _collectionCardPool.Count; i++)
			{
				if (!_activeCollectionPoolIndices.Contains(i))
				{
					_collectionCardPool[i].style.display = DisplayStyle.None;
				}
			}
		}

		private void EnsureCollectionPoolSize(int needed)
		{
			while (_collectionCardPool.Count < needed)
			{
				var card = new CollectionCard(OnCollectionCardClicked, OnCollectionInstallClicked);
				card.style.display = DisplayStyle.None;
				_collectionCardPool.Add(card);
				_cardContainer.Add(card);
			}
		}

		private void HideAllCollectionPoolCards()
		{
			foreach (var card in _collectionCardPool)
			{
				card.style.display = DisplayStyle.None;
			}
		}

		private void OnCollectionCardClicked(CollectionCard card)
		{
			if (card.Collection == null) return;
			ShowCollectionDetail(card.Collection.slug, false);
		}

		private void OnCollectionInstallClicked(CollectionCard card)
		{
			if (card.Collection == null || BatchInstaller.IsRunning || PackageInstaller.IsInstalling) return;
			ShowCollectionDetail(card.Collection.slug, true);
		}

		private void ShowCollectionDetail(string slug, bool autoInstall)
		{
			_listView.style.display = DisplayStyle.None;
			_collectionDetailView.ShowLoading();
			SessionState.SetString(SessionKeyDetailSlug, slug);

			PkgLnkApiClient.FetchCollection(slug, (response, error) =>
			{
				if (error != null)
				{
					_collectionDetailView.ShowError($"Error: {error}");
					return;
				}

				if (response == null)
				{
					_collectionDetailView.ShowError("No response received.");
					return;
				}

				_collectionPackagesCache[slug] = response.packages;

				var isOwner = PkgLnkAuth.IsLoggedIn &&
				              !string.IsNullOrEmpty(response.collection.user_id) &&
				              response.collection.user_id == PkgLnkAuth.UserId;

				if (autoInstall)
				{
					_collectionDetailView.ShowAndInstallAll(response.collection, response.packages, isOwner);
				}
				else
				{
					_collectionDetailView.Show(response.collection, response.packages, isOwner);
				}
			});
		}

		// ─── My Collections ─────────────────────────────────────────────

		private void FetchMyCollections()
		{
			if (!PkgLnkAuth.IsLoggedIn) return;

			_isFetching = true;
			_allCollections.Clear();
			_filteredCollections.Clear();
			_scrollView.scrollOffset = Vector2.zero;
			HideStatus();
			HideAllCollectionPoolCards();
			UpdateCollectionLayout();

			PkgLnkApiClient.FetchMyCollections(PkgLnkAuth.Token, (response, error) =>
			{
				_isFetching = false;

				if (error != null)
				{
					HideAllCollectionPoolCards();

					if (error.StartsWith("403"))
					{
						ShowStatus("Token needs refreshed permissions. Please sign out and sign in again.");
					}
					else
					{
						ShowStatus($"Error: {error}");
					}
					return;
				}

				if (response == null || response.collections == null)
				{
					HideAllCollectionPoolCards();
					ShowStatus("No response received.");
					return;
				}

				_myCollectionsFetched = true;
				_collectionsHasMore = false;

				foreach (var col in response.collections)
				{
					_allCollections.Add(col);
				}

				_collectionsTotalCount = _allCollections.Count;

				if (_allCollections.Count == 0)
				{
					HideAllCollectionPoolCards();
					ShowStatus("No collections yet. Create one to get started!");
					_cardContainer.style.height = 0;
					return;
				}

				ApplyCollectionFilters();
			});
		}

		// ─── Collection CRUD Handlers ───────────────────────────────────

		private void OnCreateCollectionClicked()
		{
			CollectionFormWindow.ShowCreate(newCollection =>
			{
				if (_collectionViewMode == CollectionViewMode.Mine)
				{
					FetchMyCollections();
				}
			});
		}

		private void OnEditCollection(CollectionData collection)
		{
			CollectionFormWindow.ShowEdit(collection, updatedCollection =>
			{
				// Refresh the detail view
				ShowCollectionDetail(updatedCollection.slug, false);

				// Refresh list if in Mine mode
				if (_collectionViewMode == CollectionViewMode.Mine)
				{
					_myCollectionsFetched = false;
				}
			});
		}

		private void OnDeleteCollection(CollectionData collection)
		{
			PkgLnkApiClient.DeleteCollection(PkgLnkAuth.Token, collection.slug, (response, error) =>
			{
				if (error != null)
				{
					if (error.StartsWith("403"))
					{
						_collectionDetailView.ShowError("Token needs refreshed permissions. Please sign out and sign in again.");
					}
					else
					{
						_collectionDetailView.ShowError($"Failed to delete: {error}");
					}
					return;
				}

				// Back to list and refresh
				OnBackToList();
				if (_collectionViewMode == CollectionViewMode.Mine)
				{
					FetchMyCollections();
				}
				else
				{
					FetchCollections(true);
				}
			});
		}

		private void OnRemovePackageFromCollection(CollectionData collection, PackageData package)
		{
			PkgLnkApiClient.RemovePackageFromCollection(
				PkgLnkAuth.Token,
				collection.slug,
				package.id,
				(response, error) =>
				{
					if (error != null)
					{
						_collectionDetailView.ShowError($"Failed to remove package: {error}");
						return;
					}

					// Refresh the detail view
					ShowCollectionDetail(collection.slug, false);
				});
		}

		private void OnAddToCollectionClicked(PackageData package)
		{
			if (!PkgLnkAuth.IsLoggedIn)
			{
				ShowLoginModal(BrowseTab.Collections);
				return;
			}

			_addToCollectionDropdown.Show(package);
		}

		private void OnCollectionCreatedFromDropdown(CollectionData newCollection)
		{
			_myCollectionsFetched = false;
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
