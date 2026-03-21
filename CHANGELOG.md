# Changelog

All notable changes to this project will be documented in this file.

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
