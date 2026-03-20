using System;
using System.Text;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace Nonatomic.PkgLnk.Editor.Api
{
	/// <summary>
	/// Wraps Unity Package Manager Client.Add() with install-progress
	/// tracking that survives domain reloads via SessionState.
	/// </summary>
	public static class PackageInstaller
	{
		private const string SessionKeySlug = "PkgLnk_InstallingSlug";

		private static AddRequest _pendingRequest;
		private static Action<bool, string> _onComplete;

		/// <summary>
		/// Re-attaches after domain reload if an install was in progress.
		/// </summary>
		[InitializeOnLoadMethod]
		private static void OnEditorLoad()
		{
			var slug = SessionState.GetString(SessionKeySlug, string.Empty);
			if (string.IsNullOrEmpty(slug)) return;

			SessionState.EraseString(SessionKeySlug);
			EditorApplication.delayCall += () =>
				Debug.Log($"[PkgLnk] Install of '{slug}' may have completed during domain reload. " +
				          "Check Package Manager to confirm.");
		}

		/// <summary>
		/// Builds the UPM git install URL for a package.
		/// Format: https://pkglnk.dev/track/{slug}.git[?path={git_path}][#{git_ref}]
		/// </summary>
		public static string BuildInstallUrl(PackageData pkg)
		{
			var sb = new StringBuilder($"https://pkglnk.dev/track/{pkg.slug}.git");

			if (!string.IsNullOrEmpty(pkg.git_path))
			{
				sb.Append($"?path={Uri.EscapeDataString(pkg.git_path)}");
			}

			if (!string.IsNullOrEmpty(pkg.git_ref))
			{
				sb.Append($"#{pkg.git_ref}");
			}

			return sb.ToString();
		}

		/// <summary>
		/// Returns true if the package is already installed, matched by package_json_name.
		/// </summary>
		public static bool IsInstalled(PackageData pkg)
		{
			if (string.IsNullOrEmpty(pkg.package_json_name)) return false;

			var installed = PackageInfo.GetAllRegisteredPackages();
			foreach (var info in installed)
			{
				if (string.Equals(info.name, pkg.package_json_name, StringComparison.OrdinalIgnoreCase))
				{
					return true;
				}
			}

			return false;
		}

		/// <summary>Returns true if any install is currently in progress.</summary>
		public static bool IsInstalling =>
			!string.IsNullOrEmpty(SessionState.GetString(SessionKeySlug, string.Empty));

		/// <summary>
		/// Begins installation. <paramref name="onComplete"/> receives (success, errorMessage).
		/// Only call when <see cref="IsInstalling"/> is false.
		/// </summary>
		public static void Install(PackageData pkg, Action<bool, string> onComplete)
		{
			if (IsInstalling)
			{
				onComplete?.Invoke(false, "Another install is already in progress.");
				return;
			}

			var url = BuildInstallUrl(pkg);
			_onComplete = onComplete;
			_pendingRequest = Client.Add(url);

			SessionState.SetString(SessionKeySlug, pkg.slug);
			EditorApplication.update += PollInstall;
		}

		private static void PollInstall()
		{
			if (_pendingRequest == null)
			{
				EditorApplication.update -= PollInstall;
				return;
			}

			if (!_pendingRequest.IsCompleted) return;

			EditorApplication.update -= PollInstall;
			SessionState.EraseString(SessionKeySlug);

			var request = _pendingRequest;
			_pendingRequest = null;

			var callback = _onComplete;
			_onComplete = null;

			if (request.Status == StatusCode.Success)
			{
				Debug.Log($"[PkgLnk] Installed: {request.Result.packageId}");
				callback?.Invoke(true, null);
			}
			else
			{
				var error = request.Error?.message ?? "Unknown error";
				Debug.LogError($"[PkgLnk] Install failed: {error}");
				callback?.Invoke(false, error);
			}
		}
	}
}
