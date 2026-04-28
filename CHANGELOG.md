# Changelog

All notable changes to this project will be documented in this file.

## [0.10.5] - 2026-04-28

### Fixed
- UPM `InstallConfirmWindow` result label now wraps and splits the success message onto two lines (slug, then status) — same fix as 0.10.4 applied to the sibling component, since long slugs would hit the same `white-space: nowrap` truncation bug.

## [0.10.4] - 2026-04-28

### Fixed
- NuGet install-confirm window's success message no longer truncates on long package ids. The result label inherits `white-space: nowrap` from `login-card-title`, which clipped "Microsoft.Bcl.AsyncInterfaces installed successfully" mid-word. Override `whiteSpace` to `Normal` and split the message onto two lines (package id on top, status below).

## [0.10.3] - 2026-04-28

### Fixed
- Post-install verification now queries `NugetForUnity.InstalledPackagesManager.InstalledPackages` to confirm the package actually landed before reporting success. Previously a silent no-op from `InstallIdentifier` (e.g. unresolvable version, missing source) would be reported as success because the bool-cast guard returned true on a null result. Now any handoff that doesn't end with the package in NFU's installed list surfaces as `HandoffFailed` with a hint pointing the user at NFU's source configuration.
- `InstallIdentifier` non-bool / null return now treated as failure rather than silent success.

### Added
- After a verified install, the freshly-installed `Assets/Packages/{Id}.{Version}/` folder is pinged in the Project window via `EditorGUIUtility.PingObject`. NuGet packages don't appear in Unity's Package Manager, so this gives the user visible confirmation of where the install landed. Falls back silently on custom NFU install locations.

## [0.10.2] - 2026-04-28

### Fixed
- NuGet handoff now invokes `NugetForUnity.NugetPackageInstaller.InstallIdentifier(INugetPackageIdentifier, bool, bool, bool)` directly, rather than searching for an "install from local file" method that doesn't exist in NuGetForUnity v4. Constructs a `NugetPackageIdentifier(id, version)` via reflection and hands it to NFU, which then resolves the install through its own configured sources.
- `Library/PkglnkCache/` is now the .nupkg download location instead of `Assets/Packages/Pkglnk/` — the cache lives in Unity's per-project Library folder where it's git-ignored and excluded from the asset pipeline. The pre-download is still useful (it's where pkglnk's analytics events come from) but it no longer pollutes the project's Assets tree.

### Notes
- NuGetForUnity does its own redundant fetch from nuget.org when we hand it the identifier. The pkglnk pre-download captures the install event regardless. v2 (pkglnk's own .nupkg unpacker) collapses both into a single fetch.

### Added
- `Tools / PkgLnk / Diagnostics / Probe NuGetForUnity` menu item (added in 0.10.1 publishing window — listed here for completeness). Prints loaded NuGetForUnity assemblies, candidate types, and public-static methods to the Console; used to identify the API shape the installer reflects against.

## [0.10.1] - 2026-04-28

### Fixed
- NuGet installer now resolves the latest version when the web modal sends a `downloadUrl` ending in `/index.json` (versions list) rather than a fully-qualified `.nupkg` URL — fetches the versions list, picks the last entry per NuGet v3's ascending semver order, then continues into the .nupkg download

## [0.10.0] - 2026-04-28

### Added
- `/install/nuget` localhost endpoint mirroring the existing UPM `/install` route. Body shape: `{ packageId, version?, downloadUrl }`. `downloadUrl` is restricted to `pkglnk.dev/nuget/flatcontainer/*` for security, matching the UPM URL-prefix allowlist
- `NuGetPackageInstaller` — downloads the `.nupkg` via `UnityWebRequest` from pkglnk's flat container so install events flow through the same analytics pipeline as UPM clones, then hands off to NuGetForUnity via reflection (no hard reference — assembly is optional)
- `InstallNuGetConfirmWindow` — confirmation popup mirroring `InstallConfirmWindow` for browser-initiated NuGet installs, sharing the same Resolving → Downloading → Importing → Complete phase animation

### Notes
- NuGetForUnity API binding is best-effort reflection. If the assembly isn't installed, or the API has drifted from the heuristic, the `.nupkg` is left at `Assets/Packages/Pkglnk/{file}.nupkg` with a clear error message pointing the user at the manual fallback (drag into NuGet Package Manager window)

## [0.9.2] - 2026-04-25

### Changed
- Card install/installed icons replaced with crisp Lucide-style download arrow and checkmark, matching pkglnk.dev's InstallButton SVG
- Icons now sourced from PNGs rendered from canonical SVGs at 128×128, instead of hand-drawn 14×14 ASCII pixel bitmaps

### Removed
- `TabIcons.Download` and `TabIcons.Checkmark` procedural bitmap textures (replaced by `Editor/Icons/download-icon.png` and `check-icon.png`)

## [0.9.1] - 2026-04-25

### Changed
- Card install button restyled as a two-chamber pill matching pkglnk.dev directory cards: violet "Install" chamber with download icon + neutral chamber showing the install count
- Install count is now part of the install button click target instead of a separate footer label
- Removed the standalone "N installs" footer label (count moved into the button)
- Installed cards collapse the button to a single-chamber success-green "Installed" pill

## [0.9.0] - 2026-04-25

### Changed
- Theming overhauled to match the latest pkglnk.dev visual identity: neutral dark surfaces with violet accent
- Stylesheet refactored to use USS custom properties (`--pkglnk-bg`, `--pkglnk-accent`, etc.) for single-source palette tuning
- Card hover state now reads as a violet border accent instead of muddy white
- OAuth callback HTML and markdown link colour brought on-palette

### Removed
- Light (mint) and Grey theme variants — package is now single-theme to mirror pkglnk.dev
- Theme toggle button removed from header bar
- `PkgLnkWindowStylesDark.uss`, `PkgLnkWindowStylesLight.uss`, `PkgLnkWindowStylesGrey.uss` (consolidated into the base stylesheet)
- Unused brand assets: `pkglnk-box-green/orange/trans.png`, `toggle-icon-green/white/grey.png`

### Fixed
- EditMode tests failing to compile due to missing `InternalsVisibleTo` declaration on the editor assembly

## [0.8.5] - 2026-04-08

### Fixed
- Card images failing to load when source URL serves unsupported formats (GIF, WebP) without a matching file extension
- Now always prefers server-optimised PNG for card images, falling back to original URL only when no PNG is available

## [0.8.4] - 2026-04-07

### Fixed
- Download icon distortion on package card install buttons
- Download icon vertical centering within install button

## [0.8.3] - 2026-03-30

### Added
- Star on GitHub button in profile dropdown with themed styling per colour scheme
- GIF first-frame decoding for card images via GifDecoder
- PNG fallback for card images when primary URL is an unsupported format (GIF, WebP)
- Checkmark icon for installed packages replacing text label on card install button

### Fixed
- Pool size zero guard prevents layout error when no cards are allocated

## [0.8.2] - 2026-03-26

### Changed
- Scroll performance: position caching skips redundant style writes on cards that haven't moved
- Scroll performance: card width/height set once on layout instead of every scroll frame
- Scroll performance: display visibility caching avoids writes on already-visible/hidden cards
- Scroll performance: install, bookmark, and description state guards skip unchanged class/style updates
- Scroll performance: increased installed-package cache TTL from 2s to 10s

## [0.8.1] - 2026-03-25

### Changed
- Install URLs now use `https://pkglnk.dev/{slug}.git` format (removed `/track/` path segment)
- Legacy `/track/` URLs still recognised for backwards compatibility with existing installs

## [0.8.0] - 2026-03-24

### Added
- Version number display in header bar next to pkglnk.dev brand
- Automatic update detection via GitHub releases API on window open
- One-click update to latest version via highlighted version label
- Platform source icons (GitHub, GitLab) on package card footers using PNG assets
- Download icon on card install button replacing text label
- Bookmark and install button tooltips
- Periodic image recheck for packages without card images (60s interval)
- VersionUtils for semver comparison and installed version reading

### Changed
- Install button on cards now shows download icon instead of "Install" text
- Platform icons loaded from PNG assets instead of procedural bitmaps for GitHub and GitLab

## [0.7.0] - 2026-03-23

### Added
- README rendering in package detail view with markdown-to-VisualElement converter
- HTML block element support: headings, tables, pre/code, details/summary, lists, blockquotes, hr
- HTML inline element support: strong, em, code, kbd, a, br, del, sup, sub
- HTML entity decoding (amp, lt, gt, quot, nbsp)
- Readme title heading above rendered content
- Table rendering with header row styling and equal-width columns
- README cache to avoid refetching on back/forward navigation
- Grey theme with greyscale colour palette and orange logo variant
- Three-state theme toggle cycle: dark, light, grey
- Collection CRUD: create, edit, delete collections from Unity
- Add-to-collection dropdown on package detail view
- Collection form window with slug availability checking

### Changed
- Theme toggle moved into header bar alongside auth controls
- MarkdownRenderer converts HTML inline tags to markdown before processing

## [0.6.0] - 2026-03-21

### Added
- Collections tab with collection cards, detail view, and batch install with per-package progress
- Install confirmation popup for browser-initiated installs with pkglnk branding and animated progress
- Install progress tracking with phase callbacks (Resolving, Downloading, Importing, Complete)
- Multi-strategy installed package detection (PackageInfo registry, manifest.json, and Packages lock file)
- Tab icons for Directory, Collections, Bookmarks, and My Packages tabs
- Unit tests for InstallProgressTracker

### Changed
- Package description area now reserves space for 4 lines for consistent card heights
- Browser install listener uses confirmation popup instead of silent installation

### Fixed
- CORS origin matching for browser install requests (www.pkglnk.dev support)
- Tracking URL validation now accepts both pkglnk.dev and www.pkglnk.dev prefixes
- Background thread crash when checking install state from HTTP listener thread

## [0.5.0] - 2026-03-20

### Added
- Install listener for browser-to-editor communication (localhost HTTP server on port 29120)
- Website "Install in Unity" button now triggers package installation directly in the editor
- Owner avatars on package cards (GitHub, GitLab, Bitbucket)
- Search bar available on all tabs with client-side text filtering for Bookmarks and My Packages
- Close button on filter dropdown
- Install count displayed in card footer

### Changed
- Bookmark icons changed from emoji to image-based assets (bookmark-outline/filled)
- Filter dropdown made smaller and scrollable with reduced spacing
- Card images use contain scaling instead of crop to preserve readability
- Filter button moved inside search field as compact icon
- Browse tab renamed to Directory
- Profile dropdown rendered above all siblings for correct layering

### Fixed
- Image loading race condition when same URL requested multiple times (callback queuing)
- My Packages tab not loading images due to API response format mismatch
- Removed unused HandleUserPackagesResponse from API client

## [0.4.0] - 2026-03-20

### Added
- Profile dropdown menu with avatar and username as trigger button
- Account option in dropdown opens pkglnk.dev account page in browser
- Sign Out moved into profile dropdown

### Changed
- Avatar and username now displayed as a pill-shaped profile button
- Sign Out button replaced with profile dropdown menu

## [0.3.0] - 2026-03-20

### Added
- User profile picture displayed next to username in header when logged in
- Avatar loaded from GitHub OAuth profile via pkglnk.dev auth callback

## [0.2.0] - 2026-03-20

### Added
- Filter dropdown menu accessible from all tabs (Browse, Bookmarks, My Packages)
- Filter by install status (All / Installed / Not Installed)
- Filter by bookmark status (All / Bookmarked / Not Bookmarked)
- Filter by platform (GitHub, GitLab, etc.)
- Filter by visibility (Public / Private)
- Filter by topics (multi-select)
- Active filter count badge on the filter button
- Auto-prefetch when filters reduce visible results below viewport

### Fixed
- Topic tags on cards now limited to a single line instead of wrapping
- Topic tag border-radius changed from 9999px to 8px to fix oval distortion

## [0.1.0] - 2026-03-19

### Added
- Initial release
- Editor window for browsing pkglnk.dev package directory
- Package search with debounced text input
- Package detail view with install URL
- One-click package installation via Unity Package Manager
- Topic-based filtering
- Paginated results with Load More
