using System;
using System.Collections.Generic;
using Nonatomic.PkgLnk.Editor.Api;

namespace Nonatomic.PkgLnk.Editor.PkgLnkWindow
{
	public enum InstallFilter
	{
		All,
		Installed,
		NotInstalled
	}

	public enum BookmarkFilter
	{
		All,
		Bookmarked,
		NotBookmarked
	}

	public enum VisibilityFilter
	{
		All,
		Public,
		Private
	}

	public class PackageFilterState
	{
		public InstallFilter Install { get; set; } = InstallFilter.All;
		public BookmarkFilter Bookmark { get; set; } = BookmarkFilter.All;
		public VisibilityFilter Visibility { get; set; } = VisibilityFilter.All;
		public string Platform { get; set; } = string.Empty;
		public HashSet<string> SelectedTopics { get; } = new HashSet<string>();

		public int ActiveFilterCount
		{
			get
			{
				var count = 0;
				if (Install != InstallFilter.All) count++;
				if (Bookmark != BookmarkFilter.All) count++;
				if (Visibility != VisibilityFilter.All) count++;
				if (!string.IsNullOrEmpty(Platform)) count++;
				count += SelectedTopics.Count;
				return count;
			}
		}

		public bool HasActiveFilters => ActiveFilterCount > 0;

		public void Reset()
		{
			Install = InstallFilter.All;
			Bookmark = BookmarkFilter.All;
			Visibility = VisibilityFilter.All;
			Platform = string.Empty;
			SelectedTopics.Clear();
		}

		public static bool Matches(
			PackageData pkg,
			PackageFilterState state,
			Func<PackageData, bool> isInstalled,
			Func<string, bool> isBookmarked)
		{
			if (state.Install == InstallFilter.Installed && !isInstalled(pkg))
				return false;
			if (state.Install == InstallFilter.NotInstalled && isInstalled(pkg))
				return false;

			if (state.Bookmark != BookmarkFilter.All && isBookmarked != null)
			{
				var bookmarked = isBookmarked(pkg.id);
				if (state.Bookmark == BookmarkFilter.Bookmarked && !bookmarked)
					return false;
				if (state.Bookmark == BookmarkFilter.NotBookmarked && bookmarked)
					return false;
			}

			if (!string.IsNullOrEmpty(state.Platform) &&
			    !string.Equals(pkg.git_platform, state.Platform, StringComparison.OrdinalIgnoreCase))
			{
				return false;
			}

			if (state.Visibility == VisibilityFilter.Public && pkg.is_private)
				return false;
			if (state.Visibility == VisibilityFilter.Private && !pkg.is_private)
				return false;

			if (state.SelectedTopics.Count > 0)
			{
				if (pkg.topics == null || pkg.topics.Length == 0)
					return false;

				foreach (var topic in state.SelectedTopics)
				{
					var found = false;
					foreach (var t in pkg.topics)
					{
						if (string.Equals(t, topic, StringComparison.Ordinal))
						{
							found = true;
							break;
						}
					}
					if (!found) return false;
				}
			}

			return true;
		}
	}
}
