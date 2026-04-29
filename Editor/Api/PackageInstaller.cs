using System;
using System.Collections.Generic;
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
		private static Action<InstallPhase> _onProgress;
		private static bool _waitingForImport;
		private static double _importWaitStart;
		private static bool _sawCompiling;
		private const double ImportWarmupDelay = 0.5;

		/// <summary>
		/// Re-attaches after domain reload if an install was in progress.
		/// </summary>
		[InitializeOnLoadMethod]
		private static void OnEditorLoad()
		{
			var slug = SessionState.GetString(SessionKeySlug, string.Empty);
			if (string.IsNullOrEmpty(slug)) return;

			SessionState.EraseString(SessionKeySlug);
#if PKGLNK_DEBUG
			EditorApplication.delayCall += () =>
				Debug.Log($"[PkgLnk] Install of '{slug}' may have completed during domain reload. " +
				          "Check Package Manager to confirm.");
#endif
		}

		/// <summary>
		/// Builds the UPM git install URL for a package.
		/// Format: https://pkglnk.dev/{slug}.git[?path={path}&install={id}][#{ref}]
		///
		/// The optional <paramref name="installId"/> is appended as an
		/// `install` query param so the proxy can attach the resulting
		/// install analytics row to the session row by primary key.
		/// The proxy strips it before forwarding to the upstream Git
		/// host so it never leaks to GitHub/GitLab/Bitbucket.
		/// </summary>
		public static string BuildInstallUrl(PackageData pkg, string installId = null)
		{
			var sb = new StringBuilder($"https://pkglnk.dev/{pkg.slug}.git");

			var hasQuery = false;
			if (!string.IsNullOrEmpty(pkg.git_path))
			{
				sb.Append($"?path={Uri.EscapeDataString(pkg.git_path)}");
				hasQuery = true;
			}

			if (!string.IsNullOrEmpty(installId))
			{
				sb.Append(hasQuery ? '&' : '?');
				sb.Append("install=");
				sb.Append(Uri.EscapeDataString(installId));
				hasQuery = true;
			}

			if (!string.IsNullOrEmpty(pkg.git_ref))
			{
				sb.Append($"#{pkg.git_ref}");
			}

			return sb.ToString();
		}

		private static HashSet<string> _installedNamesCache;
		private static HashSet<string> _installedSlugsCache;
		private static HashSet<string> _installedReposCache;
		private static double _installedCacheTime;
		private const double InstalledCacheTtl = 10.0;
		private const string PkgLnkUrlPrefix = "https://pkglnk.dev/";
		private const string LegacyTrackUrlPrefix = "https://pkglnk.dev/track/";
		private const string GitHubUrlPrefix = "https://github.com/";

		/// <summary>
		/// Returns true if the package is already installed.
		/// Matches by package_json_name, pkglnk tracking slug, or git owner/repo.
		/// </summary>
		public static bool IsInstalled(PackageData pkg)
		{
			RefreshInstalledCache();

			if (!string.IsNullOrEmpty(pkg.package_json_name) &&
			    _installedNamesCache.Contains(pkg.package_json_name))
			{
				return true;
			}

			if (!string.IsNullOrEmpty(pkg.slug) &&
			    _installedSlugsCache.Contains(pkg.slug))
			{
				return true;
			}

			if (!string.IsNullOrEmpty(pkg.git_owner) && !string.IsNullOrEmpty(pkg.git_repo) &&
			    _installedReposCache.Contains($"{pkg.git_owner}/{pkg.git_repo}"))
			{
				return true;
			}

			return false;
		}

		/// <summary>Forces the installed-packages cache to refresh on next check.</summary>
		public static void InvalidateInstalledCache()
		{
			_installedCacheTime = 0;
		}

		private static void RefreshInstalledCache()
		{
			var now = EditorApplication.timeSinceStartup;
			if (_installedNamesCache != null && now - _installedCacheTime < InstalledCacheTtl) return;

			_installedNamesCache ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			_installedSlugsCache ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			_installedReposCache ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			_installedNamesCache.Clear();
			_installedSlugsCache.Clear();
			_installedReposCache.Clear();

			var installed = PackageInfo.GetAllRegisteredPackages();
			foreach (var info in installed)
			{
				_installedNamesCache.Add(info.name);
				ParseGitUrl(info.packageId);
			}

			_installedCacheTime = now;
		}

		private static void ParseGitUrl(string packageId)
		{
			var atIndex = packageId.IndexOf('@');
			if (atIndex < 0) return;

			var url = packageId.Substring(atIndex + 1);

			if (url.StartsWith(LegacyTrackUrlPrefix, StringComparison.OrdinalIgnoreCase))
			{
				var slug = StripGitSuffix(url.Substring(LegacyTrackUrlPrefix.Length));
				if (!string.IsNullOrEmpty(slug)) _installedSlugsCache.Add(slug);
			}
			else if (url.StartsWith(PkgLnkUrlPrefix, StringComparison.OrdinalIgnoreCase))
			{
				var slug = StripGitSuffix(url.Substring(PkgLnkUrlPrefix.Length));
				if (!string.IsNullOrEmpty(slug) && !slug.Contains("/")) _installedSlugsCache.Add(slug);
			}

			if (url.StartsWith(GitHubUrlPrefix, StringComparison.OrdinalIgnoreCase))
			{
				var path = StripGitSuffix(url.Substring(GitHubUrlPrefix.Length));
				if (!string.IsNullOrEmpty(path) && path.Contains("/"))
				{
					_installedReposCache.Add(path);
				}
			}
		}

		private static string StripGitSuffix(string value)
		{
			if (value.EndsWith(".git")) value = value.Substring(0, value.Length - 4);
			var q = value.IndexOf('?');
			if (q >= 0) value = value.Substring(0, q);
			var h = value.IndexOf('#');
			if (h >= 0) value = value.Substring(0, h);
			return value;
		}

		/// <summary>Returns true if any install is currently in progress (including import wait).</summary>
		public static bool IsInstalling =>
			_waitingForImport || !string.IsNullOrEmpty(SessionState.GetString(SessionKeySlug, string.Empty));

		/// <summary>
		/// Begins installation. <paramref name="onComplete"/> receives (success, errorMessage).
		/// Optionally pass <paramref name="onProgress"/> to receive real-time phase updates
		/// from the pkglnk.dev tracking proxy.
		/// <paramref name="source"/> identifies which UI surface
		/// triggered the install (default <see cref="InstallSource.PkglnkUnityWindow"/>);
		/// the localhost listener that handles deep-links from pkglnk.dev
		/// passes <see cref="InstallSource.PkglnkWeb"/>. Source flows
		/// through to the server's install_sessions row and onto the
		/// resulting install analytics row, so per-source breakdowns
		/// in pkglnk.dev's analytics panel are accurate.
		/// Only call when <see cref="IsInstalling"/> is false.
		/// </summary>
		public static void Install(
			PackageData pkg,
			Action<bool, string> onComplete,
			Action<InstallPhase> onProgress = null,
			string source = InstallSource.PkglnkUnityWindow)
		{
			if (IsInstalling)
			{
				onComplete?.Invoke(false, "Another install is already in progress.");
				return;
			}

			// Generate the install_id up-front so we can append it to
			// the UPM Git URL as ?install=<id>. The proxy reads that
			// param to attach the resulting install row to the
			// install_session by primary key — far more robust than
			// the legacy IP-hash join.
			var installId = InstallProgressTracker.GenerateInstallId();

			var url = BuildInstallUrl(pkg, installId);
			_onComplete = onComplete;
			_onProgress = onProgress;
			_pendingRequest = Client.Add(url);

			SessionState.SetString(SessionKeySlug, pkg.slug);

			// Notify the server of the session regardless of whether the
			// caller wants live progress updates; the server uses the
			// session row to stamp `source` onto the resulting install
			// analytics row, which is independent of polling.
			InstallProgressTracker.NotifyInstallStart(pkg.slug, installId, source);
			if (_onProgress != null)
			{
				InstallProgressTracker.StartTracking(installId, _onProgress);
			}

			EditorApplication.update += PollInstall;
		}

		private static void PollInstall()
		{
			// Phase 1: wait for Client.Add to complete
			if (_pendingRequest != null)
			{
				if (!_pendingRequest.IsCompleted) return;

				var request = _pendingRequest;
				_pendingRequest = null;
				SessionState.EraseString(SessionKeySlug);

				if (request.Status == StatusCode.Success)
				{
					var hadProgress = _onProgress != null;
					if (hadProgress)
					{
						InstallProgressTracker.StopWithComplete();
						_onProgress?.Invoke(InstallPhase.Importing);
					}

#if PKGLNK_DEBUG
					Debug.Log($"[PkgLnk] Package added: {request.Result.packageId} — awaiting import");
#endif

					_waitingForImport = true;
					_importWaitStart = EditorApplication.timeSinceStartup;
					_sawCompiling = EditorApplication.isCompiling;
					return;
				}

				// Failed
				EditorApplication.update -= PollInstall;
				var hadProg = _onProgress != null;
				_onProgress = null;
				if (hadProg) InstallProgressTracker.Stop();

				var error = request.Error?.message ?? "Unknown error";
				Debug.LogError($"[PkgLnk] Install failed: {error}");

				var callback = _onComplete;
				_onComplete = null;
				callback?.Invoke(false, error);
				return;
			}

			// Phase 2: wait for import/compilation to finish
			if (_waitingForImport)
			{
				if (EditorApplication.isCompiling)
				{
					_sawCompiling = true;
					return;
				}

				// Compilation finished
				if (_sawCompiling)
				{
					FinishSuccess();
					return;
				}

				// Haven't seen compilation yet — wait warmup period
				var elapsed = EditorApplication.timeSinceStartup - _importWaitStart;
				if (elapsed < ImportWarmupDelay) return;

				FinishSuccess();
			}
		}

		private static void FinishSuccess()
		{
			_waitingForImport = false;
			_sawCompiling = false;
			EditorApplication.update -= PollInstall;

			var callback = _onComplete;
			_onComplete = null;
			_onProgress = null;

			callback?.Invoke(true, null);
		}
	}
}
