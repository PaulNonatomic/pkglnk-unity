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
		private const string DownloadDir = "Assets/Packages/Pkglnk";
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

			// If the URL points at the versions index rather than a
			// specific .nupkg, we'd need a second round-trip to resolve
			// the latest version. v1 expects callers (the web modal) to
			// pass a fully-qualified .nupkg URL — flag this explicitly so
			// the caller can't accidentally feed us an index.json.
			if (!downloadUrl.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase))
			{
				onComplete?.Invoke(
					NuGetInstallOutcome.DownloadFailed,
					"downloadUrl must point at a .nupkg file (versions resolution not yet supported)");
				return;
			}

			onPhase?.Invoke(InstallPhase.Resolving);

			DownloadNupkg(packageId, version, downloadUrl, onComplete, onPhase);
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
		/// Tries to invoke NuGetForUnity's install API via reflection.
		/// We don't take a hard reference — NuGetForUnity is an optional
		/// dependency, and its public API has shifted across major versions
		/// (v3 → v4). On failure we leave the .nupkg in place at
		/// Assets/Packages/Pkglnk/ so the user can install it manually
		/// (drag into the NuGet Package Manager window) and surface a
		/// descriptive error.
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
					$"NuGetForUnity not detected. The .nupkg has been saved to {nupkgPath} — " +
					"install NuGetForUnity (https://github.com/GlitchEnzo/NuGetForUnity) and drag the file in.");
				return;
			}

			// Try the install — this layer is best-effort. If the API has
			// drifted from what we expect, surface the error and leave the
			// .nupkg on disk for manual handling.
			try
			{
				var invoked = TryInvokeInstallByLocalFile(nfuAssembly, nupkgPath, packageId, version);
				if (invoked)
				{
					AssetDatabase.Refresh();
					onPhase?.Invoke(InstallPhase.Complete);
					onComplete?.Invoke(NuGetInstallOutcome.Success, null);
					return;
				}

				onComplete?.Invoke(
					NuGetInstallOutcome.HandoffFailed,
					$"NuGetForUnity is installed but no compatible install API was found. " +
					$"The .nupkg is at {nupkgPath} — please install it manually via the NuGet Package Manager window.");
			}
			catch (Exception ex)
			{
				Debug.LogWarning($"[PkgLnk] NuGetForUnity handoff threw: {ex.GetType().Name}: {ex.Message}");
				onComplete?.Invoke(
					NuGetInstallOutcome.HandoffFailed,
					$"NuGetForUnity install threw an error: {ex.Message}. " +
					$"The .nupkg is at {nupkgPath} for manual install.");
			}
		}

		private static Assembly FindNuGetForUnityAssembly()
		{
			// NuGetForUnity's editor assembly is typically named
			// "NugetForUnity" or "NugetForUnity.Editor" depending on
			// version. Scan loaded assemblies for either.
			return AppDomain.CurrentDomain
				.GetAssemblies()
				.FirstOrDefault(a =>
				{
					var name = a.GetName().Name;
					return name.Equals("NugetForUnity", StringComparison.OrdinalIgnoreCase) ||
					       name.Equals("NugetForUnity.Editor", StringComparison.OrdinalIgnoreCase);
				});
		}

		/// <summary>
		/// Attempts to find and invoke a public static "install from local
		/// file path" method in NuGetForUnity. Returns true if a method was
		/// found and invoked without throwing. The exact method varies by
		/// version, so we try a couple of known shapes.
		/// </summary>
		private static bool TryInvokeInstallByLocalFile(
			Assembly nfu,
			string nupkgPath,
			string packageId,
			string version)
		{
			// Candidate type names across NuGetForUnity versions. Reflection
			// over a small known set is more predictable than walking
			// every type in the assembly.
			var candidateTypes = new[]
			{
				"NugetForUnity.NugetPackageInstaller",
				"NugetForUnity.PackageInstaller",
				"NugetForUnity.NugetHelper"
			};

			foreach (var typeName in candidateTypes)
			{
				var type = nfu.GetType(typeName, throwOnError: false, ignoreCase: false);
				if (type == null) continue;

				// Try methods that take a single string (file path).
				foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
				{
					if (!IsLikelyInstallFromFileMethod(method)) continue;

#if PKGLNK_DEBUG
					Debug.Log($"[PkgLnk] Trying NuGetForUnity install method: {type.FullName}.{method.Name}");
#endif

					method.Invoke(null, new object[] { nupkgPath });
					return true;
				}
			}

			return false;
		}

		private static bool IsLikelyInstallFromFileMethod(MethodInfo method)
		{
			var name = method.Name.ToLowerInvariant();
			if (!name.Contains("install")) return false;

			var ps = method.GetParameters();
			if (ps.Length != 1) return false;
			if (ps[0].ParameterType != typeof(string)) return false;

			// Heuristic: parameter name hints "path" / "file" / "nupkg".
			// If the param name is something else (e.g. "id"), skip — that
			// method probably wants a package identifier, not a local file.
			var paramName = ps[0].Name?.ToLowerInvariant() ?? string.Empty;
			return paramName.Contains("path") ||
			       paramName.Contains("file") ||
			       paramName.Contains("nupkg");
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
