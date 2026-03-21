# PkgLnk for Unity

Browse, search, and install Unity packages from [pkglnk.dev](https://pkglnk.dev) — directly inside the Unity Editor.

<img width="696" height="810" alt="Unity_B8ndJpeVNi" src="https://github.com/user-attachments/assets/6cce9b15-b0e7-46e8-bac4-688f11cd03b4" />

## Features

### Package Browser

A responsive card grid with infinite scroll for browsing the full pkglnk.dev directory. Cards display the package image (or a placeholder icon), name, description, topic tags, star count, and install count. Installed packages are highlighted with a green border for quick identification.

### Search

Real-time debounced search filters packages as you type. Results update automatically with smooth skeleton loading states.

<img width="691" height="460" alt="Unity_TJyStBB7bP" src="https://github.com/user-attachments/assets/0376cecb-1796-4ba8-aa55-ac1e76504681" />

### Filters

A multi-criteria filter system lets you narrow packages by:

- **Install status** — All, Installed, Not Installed
- **Bookmark status** — All, Bookmarked, Not Bookmarked
- **Visibility** — All, Public, Private
- **Platform** — GitHub, GitLab, Bitbucket
- **Topics** — Select one or more topic tags

Active filter count is shown as a badge on the filter button. Filters compose with search for precise results.

<img width="691" height="240" alt="Unity_dDLHeVyz79" src="https://github.com/user-attachments/assets/785af6a0-b04d-4e73-9e58-cb1449fc8e73" />

### Package Details

Click any card to view full package details including description, repository owner, platform, star count, install count, last updated date, package name, and topic tags. Clickable topic tags navigate back to the grid filtered by that topic.

<img width="692" height="421" alt="Unity_NCU4cNPTmp" src="https://github.com/user-attachments/assets/f3bedb55-029a-4eec-ab52-04fdcb5215f4" />

### One-Click Install

Install any package directly into your project through the Unity Package Manager. The install button shows real-time status — whether a package is already installed, currently installing, or available to install.

### Bookmarks

Sign in to bookmark packages for quick access. Toggle bookmarks directly from cards using the star icon. The Bookmarks tab shows all your bookmarked packages in one place.

<img width="696" height="810" alt="Unity_O9aygJ2DYH" src="https://github.com/user-attachments/assets/074b5d97-069e-4d94-b002-b9caf669b26c" />

### Sign In

Browser-based OAuth authentication through pkglnk.dev. Sign in with GitHub, GitLab, or Bitbucket to access bookmarks and manage your packages. The sign-in flow opens your system browser and returns the session to Unity automatically.

<img width="863" height="760" alt="chrome_r8jhTHjf4C" src="https://github.com/user-attachments/assets/d5c3275f-9f71-4171-a7af-8f221337fc93" />

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
