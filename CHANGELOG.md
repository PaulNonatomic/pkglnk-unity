# Changelog

All notable changes to this project will be documented in this file.

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
