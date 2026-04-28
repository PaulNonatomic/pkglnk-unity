using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace Nonatomic.PkgLnk.Editor.Api
{
	/// <summary>
	/// Result of an attempted NuGet install handoff.
	/// </summary>
	public enum NuGetInstallOutcome
	{
		Success,
		DownloadFailed,
		NuGetForUnityNotFound,
		HandoffFailed
	}

	/// <summary>
	/// Installs a NuGet package by routing through pkglnk's flat container
	/// proxy (so install analytics flow through pkglnk.dev) and handing off
	/// to NuGetForUnity for the actual install. NuGetForUnity is detected
	/// and invoked via reflection so this assembly takes no hard dependency
	/// — if it isn't installed, we save the downloaded .nupkg to a known
	/// location and surface a clear error to the caller.
	///
	/// The "download via pkglnk → handoff to installer" split is the v1
	/// architecture; a v2 ships pkglnk's own .nupkg unpacker so the
	/// NuGetForUnity dependency drops entirely.
	/// </summary>
	public static class NuGetPackageInstaller
	{
		private const string PkglnkAllowedHost = "pkglnk.dev";
		// Library/ is Unity's per-project cache directory — excluded from
		// version control + asset pipeline, so the .nupkg never becomes a
		// tracked project asset. The download is purely for analytics
		// (bytes flowing through pkglnk) + as a fallback if NuGetForUnity
		// reflection fails; NFU's own install does its own fetch.
		private const string DownloadDir = "Library/PkglnkCache";
		private const string UserAgent = "pkglnk-unity/0.1 (+https://pkglnk.dev)";

		/// <summary>
		/// Downloads the .nupkg from pkglnk's flat container then hands it
		/// to NuGetForUnity. <paramref name="onComplete"/> receives the
		/// outcome plus an optional error message. Phase reporting via
		/// <paramref name="onPhase"/> mirrors the UPM install button flow:
		/// Resolving → Downloading → Importing → Complete.
		/// </summary>
		public static void Install(
			string packageId,
			string version,
			string downloadUrl,
			Action<NuGetInstallOutcome, string> onComplete,
			Action<InstallPhase> onPhase = null)
		{
			if (string.IsNullOrEmpty(packageId))
			{
				onComplete?.Invoke(NuGetInstallOutcome.DownloadFailed, "packageId is required");
				return;
			}

			if (string.IsNullOrEmpty(downloadUrl))
			{
				onComplete?.Invoke(NuGetInstallOutcome.DownloadFailed, "downloadUrl is required");
				return;
			}

			// Security: only fetch from pkglnk.dev. Without this, a hostile
			// website could ask us to download from arbitrary hosts.
			if (!IsPkglnkUrl(downloadUrl))
			{
				onComplete?.Invoke(
					NuGetInstallOutcome.DownloadFailed,
					$"Refusing to download from non-pkglnk host: {downloadUrl}");
				return;
			}

			onPhase?.Invoke(InstallPhase.Resolving);

			// Two URL shapes are accepted:
			//   - …/{id}/{version}/{id}.{version}.nupkg → download directly
			//   - …/{id}/index.json → fetch versions list, pick latest,
			//                         then build the .nupkg URL
			// The web modal sends the index.json shape when no version is
			// pinned (the common case for v1), so we resolve here rather
			// than burdening every caller with the version lookup.
			if (downloadUrl.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase))
			{
				DownloadNupkg(packageId, version, downloadUrl, onComplete, onPhase);
				return;
			}

			if (downloadUrl.EndsWith("/index.json", StringComparison.OrdinalIgnoreCase))
			{
				ResolveLatestVersionThenDownload(packageId, downloadUrl, onComplete, onPhase);
				return;
			}

			onComplete?.Invoke(
				NuGetInstallOutcome.DownloadFailed,
				$"Unrecognised downloadUrl shape: {downloadUrl}");
		}

		[Serializable]
		private class VersionsList
		{
			public string[] versions;
		}

		/// <summary>
		/// Fetches /flatcontainer/{id}/index.json (versions list per the
		/// NuGet v3 protocol — ascending semver order, last entry is
		/// latest), then constructs the .nupkg URL and continues to
		/// DownloadNupkg. We picked NuGet's "last entry" rule rather than
		/// doing semver parsing because every package on api.nuget.org
		/// follows the spec and over-engineering version comparison adds
		/// no value.
		/// </summary>
		private static void ResolveLatestVersionThenDownload(
			string packageId,
			string indexUrl,
			Action<NuGetInstallOutcome, string> onComplete,
			Action<InstallPhase> onPhase)
		{
			var request = UnityWebRequest.Get(indexUrl);
			request.SetRequestHeader("User-Agent", UserAgent);
			request.SetRequestHeader("Accept", "application/json");

			var op = request.SendWebRequest();
			op.completed += _ =>
			{
				try
				{
					if (request.result != UnityWebRequest.Result.Success)
					{
						onComplete?.Invoke(
							NuGetInstallOutcome.DownloadFailed,
							$"Versions list failed ({request.responseCode}): {request.error}");
						return;
					}

					var json = request.downloadHandler.text;
					var parsed = JsonUtility.FromJson<VersionsList>(json);
					var version = parsed?.versions != null && parsed.versions.Length > 0
						? parsed.versions[parsed.versions.Length - 1]
						: null;

					if (string.IsNullOrEmpty(version))
					{
						onComplete?.Invoke(
							NuGetInstallOutcome.DownloadFailed,
							"Could not parse latest version from versions list");
						return;
					}

					// Build the .nupkg URL. The flat container path
					// uses lowercased id and version per protocol.
					var basePrefix = indexUrl.Substring(0, indexUrl.LastIndexOf('/') + 1);
					var lowerId = packageId.ToLowerInvariant();
					var lowerVer = version.ToLowerInvariant();
					var nupkgUrl = $"{basePrefix}{lowerVer}/{lowerId}.{lowerVer}.nupkg";

#if PKGLNK_DEBUG
					Debug.Log($"[PkgLnk] Resolved {packageId} latest version: {version} → {nupkgUrl}");
#endif

					DownloadNupkg(packageId, version, nupkgUrl, onComplete, onPhase);
				}
				finally
				{
					request.Dispose();
				}
			};
		}

		private static bool IsPkglnkUrl(string url)
		{
			if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
			return uri.Scheme == "https" &&
			       (uri.Host == PkglnkAllowedHost || uri.Host == $"www.{PkglnkAllowedHost}");
		}

		private static void DownloadNupkg(
			string packageId,
			string version,
			string downloadUrl,
			Action<NuGetInstallOutcome, string> onComplete,
			Action<InstallPhase> onPhase)
		{
			onPhase?.Invoke(InstallPhase.Downloading);

			Directory.CreateDirectory(DownloadDir);

			var fileName = SanitiseFileName(
				$"{packageId}.{(string.IsNullOrEmpty(version) ? "latest" : version)}.nupkg");
			var savePath = Path.Combine(DownloadDir, fileName);

			var request = UnityWebRequest.Get(downloadUrl);
			request.SetRequestHeader("User-Agent", UserAgent);
			request.downloadHandler = new DownloadHandlerFile(savePath) { removeFileOnAbort = true };

			var op = request.SendWebRequest();
			op.completed += _ =>
			{
				try
				{
					if (request.result != UnityWebRequest.Result.Success)
					{
						onComplete?.Invoke(
							NuGetInstallOutcome.DownloadFailed,
							$"Download failed ({request.responseCode}): {request.error}");
						return;
					}

#if PKGLNK_DEBUG
					Debug.Log($"[PkgLnk] Downloaded {packageId} → {savePath} ({request.downloadedBytes} bytes)");
#endif

					HandoffToNuGetForUnity(packageId, version, savePath, onComplete, onPhase);
				}
				finally
				{
					request.Dispose();
				}
			};
		}

		/// <summary>
		/// Hands off to NuGetForUnity's <c>NugetPackageInstaller.InstallIdentifier</c>
		/// API via reflection — found via the diagnostic probe. NFU takes a
		/// package identifier (not a file path), so it does its own fetch
		/// from whatever sources are configured (typically nuget.org). Our
		/// pre-download through pkglnk's flat container has already
		/// produced the analytics row; NFU's redundant download is the
		/// price we pay until v2 ships pkglnk's own .nupkg unpacker.
		///
		/// We don't take a hard reference because NuGetForUnity is an
		/// optional dependency. If the assembly is missing, or the API
		/// has drifted, the .nupkg is still on disk at
		/// Library/PkglnkCache/ for manual fallback.
		/// </summary>
		private static void HandoffToNuGetForUnity(
			string packageId,
			string version,
			string nupkgPath,
			Action<NuGetInstallOutcome, string> onComplete,
			Action<InstallPhase> onPhase)
		{
			onPhase?.Invoke(InstallPhase.Importing);

			var nfuAssembly = FindNuGetForUnityAssembly();
			if (nfuAssembly == null)
			{
				onComplete?.Invoke(
					NuGetInstallOutcome.NuGetForUnityNotFound,
					"NuGetForUnity not detected. Install it from " +
					"https://github.com/GlitchEnzo/NuGetForUnity to enable one-click installs.");
				return;
			}

			try
			{
				var invoked = TryInvokeInstallIdentifier(nfuAssembly, packageId, version, out var error);
				if (invoked)
				{
					AssetDatabase.Refresh();
					onPhase?.Invoke(InstallPhase.Complete);
					onComplete?.Invoke(NuGetInstallOutcome.Success, null);
					return;
				}

				onComplete?.Invoke(
					NuGetInstallOutcome.HandoffFailed,
					error ??
					"NuGetForUnity is installed but the InstallIdentifier API couldn't be invoked. " +
					"Run Tools / PkgLnk / Diagnostics / Probe NuGetForUnity and report the output.");
			}
			catch (Exception ex)
			{
				Debug.LogWarning($"[PkgLnk] NuGetForUnity handoff threw: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
				onComplete?.Invoke(
					NuGetInstallOutcome.HandoffFailed,
					$"NuGetForUnity install threw: {ex.Message}");
			}
		}

		private static Assembly FindNuGetForUnityAssembly()
		{
			// The editor assembly is named "NugetForUnity" (lowercase 'g'
			// after Nu). The PluginAPI sub-assembly exists too but doesn't
			// contain the installer entry point.
			return AppDomain.CurrentDomain
				.GetAssemblies()
				.FirstOrDefault(a => a.GetName().Name.Equals("NugetForUnity", StringComparison.OrdinalIgnoreCase));
		}

		/// <summary>
		/// Constructs a <c>NugetPackageIdentifier(id, version)</c> and
		/// invokes <c>NugetPackageInstaller.InstallIdentifier(identifier,
		/// refreshAssets:true, isSlimRestoreInstall:false,
		/// allowUpdateForExplicitlyInstalled:false)</c>. Both type names +
		/// the method signature were captured from the v0.10.1 diagnostic
		/// probe against NuGetForUnity v4.5.0. If a future NFU version
		/// breaks this, the probe will surface the new shape.
		/// </summary>
		private static bool TryInvokeInstallIdentifier(
			Assembly nfu,
			string packageId,
			string version,
			out string error)
		{
			error = null;

			var identifierType = nfu.GetType("NugetForUnity.Models.NugetPackageIdentifier",
				throwOnError: false, ignoreCase: false);
			if (identifierType == null)
			{
				error = "NugetForUnity.Models.NugetPackageIdentifier type not found in NuGetForUnity assembly.";
				return false;
			}

			var identifierInterface = nfu.GetType("NugetForUnity.Models.INugetPackageIdentifier",
				throwOnError: false, ignoreCase: false);
			if (identifierInterface == null)
			{
				error = "NugetForUnity.Models.INugetPackageIdentifier interface not found.";
				return false;
			}

			// NugetPackageIdentifier(string id, string version) is the v4
			// constructor. If a future version drops it we'll need to
			// inspect alternatives via the probe.
			var ctor = identifierType.GetConstructor(new[] { typeof(string), typeof(string) });
			if (ctor == null)
			{
				error = "NugetPackageIdentifier(string, string) constructor not found.";
				return false;
			}

			var identifier = ctor.Invoke(new object[] { packageId, version });

			var installerType = nfu.GetType("NugetForUnity.NugetPackageInstaller",
				throwOnError: false, ignoreCase: false);
			if (installerType == null)
			{
				error = "NugetForUnity.NugetPackageInstaller type not found.";
				return false;
			}

			var installMethod = installerType.GetMethod(
				"InstallIdentifier",
				BindingFlags.Public | BindingFlags.Static,
				binder: null,
				types: new[] { identifierInterface, typeof(bool), typeof(bool), typeof(bool) },
				modifiers: null);

			if (installMethod == null)
			{
				error = "NugetPackageInstaller.InstallIdentifier(INugetPackageIdentifier, bool, bool, bool) method not found.";
				return false;
			}

#if PKGLNK_DEBUG
			Debug.Log($"[PkgLnk] Invoking {installerType.FullName}.InstallIdentifier({packageId}, {version})");
#endif

			// refreshAssets:true so NFU triggers AssetDatabase.Refresh() itself.
			// isSlimRestoreInstall:false → full install (deps resolved).
			// allowUpdateForExplicitlyInstalled:false → don't bump pinned packages.
			var result = installMethod.Invoke(null, new object[] { identifier, true, false, false });

			// Method returns bool; treat false as a soft failure (NFU
			// itself decided not to proceed) but don't throw.
			if (result is bool ok && !ok)
			{
				error = "NuGetForUnity declined to install (InstallIdentifier returned false). " +
				        "Check the Console for NuGetForUnity logs.";
				return false;
			}

			return true;
		}

		private static string SanitiseFileName(string name)
		{
			var invalid = Path.GetInvalidFileNameChars();
			var safe = new System.Text.StringBuilder(name.Length);
			foreach (var c in name)
			{
				safe.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
			}
			return safe.ToString();
		}
	}
}
