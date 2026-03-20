# PkgLnk for Unity

Browse, search, and install Unity packages from [pkglnk.dev](https://pkglnk.dev) — directly inside the Unity Editor.

<!-- Screenshot: Full PkgLnk window showing the package grid with cards, search bar, and filter options -->
<!-- Suggested size: 900×550, captured from Unity Editor with the PkgLnk window open -->
![PkgLnk Window](Screenshots/pkglnk-window.png)

## Features

### Package Browser

A responsive card grid with infinite scroll for browsing the full pkglnk.dev directory. Cards display the package image (or a placeholder icon), name, description, topic tags, star count, and install count. Installed packages are highlighted with a green border for quick identification.

<!-- Screenshot: Close-up of the card grid showing a mix of installed (green border) and non-installed packages -->
<!-- Suggested size: 700×450 -->
![Package Grid](Screenshots/package-grid.png)

### Search

Real-time debounced search filters packages as you type. Results update automatically with smooth skeleton loading states.

<!-- Screenshot: The search bar with a query entered and filtered results below -->
<!-- Suggested size: 700×350 -->
![Search](Screenshots/search.png)

### Filters

A multi-criteria filter system lets you narrow packages by:

- **Install status** — All, Installed, Not Installed
- **Bookmark status** — All, Bookmarked, Not Bookmarked
- **Visibility** — All, Public, Private
- **Platform** — GitHub, GitLab, Bitbucket
- **Topics** — Select one or more topic tags

Active filter count is shown as a badge on the filter button. Filters compose with search for precise results.

<!-- Screenshot: The filter dropdown open, showing the available filter categories and some active selections -->
<!-- Suggested size: 400×500 -->
![Filters](Screenshots/filters.png)

### Package Details

Click any card to view full package details including description, repository owner, platform, star count, install count, last updated date, package name, and topic tags. Clickable topic tags navigate back to the grid filtered by that topic.

<!-- Screenshot: The package detail view for a specific package, showing all metadata and the install button -->
<!-- Suggested size: 700×500 -->
![Package Detail](Screenshots/package-detail.png)

### One-Click Install

Install any package directly into your project through the Unity Package Manager. The install button shows real-time status — whether a package is already installed, currently installing, or available to install.

### Bookmarks

Sign in to bookmark packages for quick access. Toggle bookmarks directly from cards using the star icon. The Bookmarks tab shows all your bookmarked packages in one place.

<!-- Screenshot: Cards showing the bookmark star icon, with one or two bookmarked (filled star) -->
<!-- Suggested size: 500×300 -->
![Bookmarks](Screenshots/bookmarks.png)

### Sign In

Browser-based OAuth authentication through pkglnk.dev. Sign in with GitHub, GitLab, or Bitbucket to access bookmarks and manage your packages. The sign-in flow opens your system browser and returns the session to Unity automatically.

<!-- Screenshot: The login modal that appears when accessing Bookmarks or My Packages while signed out -->
<!-- Suggested size: 400×300 -->
![Sign In Modal](Screenshots/sign-in-modal.png)

### Dark & Light Themes

Fully themed for both Unity Pro (dark) and Unity Personal (light) editor skins using dedicated USS stylesheets.

<!-- Screenshot: Side-by-side comparison of the PkgLnk window in dark and light themes -->
<!-- Suggested size: 900×450 (two windows composited side by side) -->
![Themes](Screenshots/themes.png)

## Installation

### Via Unity Package Manager (recommended)

1. Open **Window > Package Manager**
2. Click **+** → **Add package from git URL...**
3. Enter:

```
https://github.com/PaulNonatomic/pkglnk-unity.git
```

### Via manifest.json

Add the following to your project's `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.nonatomic.pkglnk": "https://github.com/PaulNonatomic/pkglnk-unity.git"
  }
}
```

### Specific Version

To pin a specific version, append the tag:

```
https://github.com/PaulNonatomic/pkglnk-unity.git#v0.2.0
```

## Usage

Open the window via the menu:

**Tools → PkgLnk → PkgLnk Window**

From there you can:

1. **Browse** — Scroll through the full package directory
2. **Search** — Type in the search bar to filter by name or description
3. **Filter** — Click the filter button to narrow results by status, platform, or topic
4. **View details** — Click any card to see full package information
5. **Install** — Click the install button on a card or in the detail view
6. **Sign in** — Click "Sign In" in the header to authenticate via your browser
7. **Bookmark** — Click the star icon on any card to save it to your bookmarks
8. **My Packages** — View packages you own (requires sign-in)

## Requirements

- Unity **2022.3** or later
- Internet connection for browsing and installing packages

## Architecture

The package is editor-only and lives entirely under `Editor/`:

```
Editor/
├── Api/                    # API client, data models, auth, package installer
├── PkgLnkWindow/           # UI Toolkit views, card grid, filters, detail view
├── Utils/                  # Image loader, date/format utilities
├── Icons/                  # Package icons (logo, placeholder)
└── Styles/                 # USS stylesheets (dark + light themes)
```

Key implementation details:

- **Virtualized grid** — A fixed pool of card elements is circularly reused as the user scrolls, providing zero-allocation scrolling regardless of dataset size
- **Prefetch-ahead** — Data is fetched 3 viewports ahead of the scroll position for seamless infinite scroll
- **Ghost cards** — Skeleton loading states are shown for cards beyond loaded data
- **Optimistic UI** — Bookmark toggles update immediately while the API call completes in the background

## License

MIT License — see [LICENSE](LICENSE) for details.

## Links

- [pkglnk.dev](https://pkglnk.dev) — Package directory website
- [Issues](https://github.com/PaulNonatomic/pkglnk-unity/issues) — Bug reports and feature requests
- [Nonatomic](https://nonatomic.co.uk/) — Author
