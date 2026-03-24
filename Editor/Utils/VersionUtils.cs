using System;
using UnityEditor.PackageManager;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace Nonatomic.PkgLnk.Editor.Utils
{
	/// <summary>
	/// Utilities for reading the installed package version and comparing semver strings.
	/// </summary>
	public static class VersionUtils
	{
		private const string PackageName = "com.nonatomic.pkglnk";

		/// <summary>
		/// Returns the installed version of this package, or null if not found.
		/// </summary>
		public static string GetInstalledVersion()
		{
			var info = PackageInfo.FindForAssetPath("Packages/com.nonatomic.pkglnk/package.json");
			return info?.version;
		}

		/// <summary>
		/// Returns true if <paramref name="latest"/> is a higher semver than <paramref name="current"/>.
		/// </summary>
		public static bool IsNewer(string latest, string current)
		{
			if (string.IsNullOrEmpty(latest) || string.IsNullOrEmpty(current)) return false;

			var latestParts = ParseVersion(latest);
			var currentParts = ParseVersion(current);
			if (latestParts == null || currentParts == null) return false;

			for (var i = 0; i < 3; i++)
			{
				if (latestParts[i] > currentParts[i]) return true;
				if (latestParts[i] < currentParts[i]) return false;
			}

			return false;
		}

		/// <summary>
		/// Strips a leading 'v' or 'V' from a version string.
		/// </summary>
		public static string StripPrefix(string version)
		{
			if (string.IsNullOrEmpty(version)) return version;
			if (version[0] == 'v' || version[0] == 'V') return version.Substring(1);
			return version;
		}

		private static int[] ParseVersion(string version)
		{
			version = StripPrefix(version);

			// Strip any pre-release suffix (e.g., "1.2.3-beta")
			var hyphen = version.IndexOf('-');
			if (hyphen >= 0) version = version.Substring(0, hyphen);

			var parts = version.Split('.');
			if (parts.Length < 3) return null;

			var result = new int[3];
			for (var i = 0; i < 3; i++)
			{
				if (!int.TryParse(parts[i], out result[i])) return null;
			}

			return result;
		}
	}
}
