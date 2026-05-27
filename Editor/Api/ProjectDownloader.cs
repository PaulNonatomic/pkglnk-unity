using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace Nonatomic.PkgLnk.Editor.Api
{
	/// <summary>
	/// Downloads a project listing as a repo archive, extracts it into a
	/// user-chosen parent directory, and reports progress via the editor's
	/// non-blocking <see cref="Progress"/> API (bottom-right background tasks).
	///
	/// Flow:
	///   1. Open folder picker, default to the parent of the current project root.
	///   2. Abort with a dialog if the target folder already exists.
	///   3. POST /api/projects/{id}/track-download to resolve the archive URL
	///      (the server constructs the codeload URL and logs the download).
	///   4. Stream the archive to a temp file (UnityWebRequest + DownloadHandlerFile).
	///   5. Extract entry-by-entry into a temp dir alongside the target so the
	///      final Move is intra-volume (a rename, not a copy).
	///   6. Move the single top-level extracted folder into place.
	///   7. Offer "Reveal in Explorer" on success.
	/// </summary>
	public static class ProjectDownloader
	{
		private const string TrackDownloadBase = "https://pkglnk.dev/api/projects";
		private const string UserAgent = "pkglnk-unity/0.11";

		// Hard timeouts so a stalled connection can't hang forever. UnityWebRequest
		// defaults to 0 (no timeout). The POST is tiny; the archive may be MBs of zip.
		private const int TrackDownloadTimeoutSeconds = 30;
		private const int ArchiveDownloadTimeoutSeconds = 600;

		private sealed class InFlightJob
		{
			public string DisplayName;
			public UnityWebRequest Request;
			public int ProgressId;
			public string TempArchive;
			public string TempExtract;
			public Action<bool, string, string> OnComplete;
		}

		// Track every active download so a domain reload (script recompile,
		// play-mode toggle, …) can cleanly abort them all instead of leaving
		// orphaned Progress entries and dead closures behind.
		private static readonly List<InFlightJob> InFlight = new List<InFlightJob>();

		[InitializeOnLoadMethod]
		private static void RegisterReloadHandler()
		{
			AssemblyReloadEvents.beforeAssemblyReload -= AbortOnReload;
			AssemblyReloadEvents.beforeAssemblyReload += AbortOnReload;
		}

		private static void AbortOnReload()
		{
			if (InFlight.Count == 0) return;

			Debug.LogWarning($"[PkgLnk] Aborting {InFlight.Count} project download(s) — Unity editor is reloading.");

			// Snapshot so we can iterate while jobs remove themselves.
			var snapshot = InFlight.ToArray();
			InFlight.Clear();

			foreach (var job in snapshot)
			{
				try { job.Request?.Abort(); } catch { /* best-effort */ }
				try { Progress.Finish(job.ProgressId, Progress.Status.Failed); } catch { /* best-effort */ }
				CleanupTemp(job.TempArchive, job.TempExtract);
				job.OnComplete?.Invoke(false, "Aborted by editor reload.", null);
			}
		}

		private static void RegisterJob(InFlightJob job)
		{
			InFlight.Add(job);
		}

		private static void RetireJob(InFlightJob job)
		{
			InFlight.Remove(job);
		}

		/// <summary>
		/// Kicks off the project download flow. Non-blocking; <paramref name="onComplete"/>
		/// fires (success, errorOrNull, installedPathOrNull) after the user-facing flow ends.
		/// </summary>
		public static void Download(PackageData pkg, Action<bool, string, string> onComplete = null)
		{
			if (pkg == null)
			{
				onComplete?.Invoke(false, "No package data.", null);
				return;
			}

			// Folder picker defaults to the parent of the current project root.
			// Application.dataPath is "<root>/Assets", so the project root is its
			// directory, and the parent of the project root is one above that.
			var projectRoot = Path.GetDirectoryName(Application.dataPath);
			var defaultDir = string.IsNullOrEmpty(projectRoot)
				? string.Empty
				: Path.GetDirectoryName(projectRoot) ?? projectRoot;

			var chosenDir = EditorUtility.OpenFolderPanel(
				$"Choose where to download {pkg.display_name}",
				defaultDir,
				string.Empty);

			if (string.IsNullOrEmpty(chosenDir))
			{
				onComplete?.Invoke(false, "Cancelled.", null);
				return;
			}

			var folderName = string.IsNullOrEmpty(pkg.git_repo) ? pkg.slug : pkg.git_repo;
			if (string.IsNullOrEmpty(folderName))
			{
				EditorUtility.DisplayDialog(
					"Download Failed",
					"Project has no repo name or slug; cannot determine target folder.",
					"OK");
				onComplete?.Invoke(false, "Missing folder name.", null);
				return;
			}

			var targetPath = Path.Combine(chosenDir, folderName);
			if (Directory.Exists(targetPath))
			{
				EditorUtility.DisplayDialog(
					"Folder Already Exists",
					$"A folder already exists at:\n\n{targetPath}\n\nRemove or rename it and try again.",
					"OK");
				onComplete?.Invoke(false, "Folder already exists.", null);
				return;
			}

			// Codeload archives (GitHub/GitLab/Bitbucket) are served chunked with
			// no Content-Length header, so UnityWebRequest.downloadProgress sits
			// at 0 until the response completes. Use Indefinite so the bar
			// visibly pulses; the description carries the live byte count.
			var progressId = Progress.Start(
				$"Downloading {pkg.display_name}",
				"Requesting archive URL…",
				Progress.Options.Indefinite);

			var cancelled = false;
			Progress.RegisterCancelCallback(progressId, () =>
			{
				cancelled = true;
				return true;
			});

			var job = new InFlightJob
			{
				DisplayName = pkg.display_name,
				ProgressId = progressId,
				OnComplete = onComplete,
			};
			RegisterJob(job);

			FetchArchiveUrl(pkg.id, job, (archiveUrl, error) =>
			{
				if (cancelled)
				{
					RetireJob(job);
					Progress.Remove(progressId);
					Debug.Log($"[PkgLnk] Project download cancelled by user: {pkg.display_name}");
					onComplete?.Invoke(false, "Cancelled.", null);
					return;
				}

				if (!string.IsNullOrEmpty(error) || string.IsNullOrEmpty(archiveUrl))
				{
					RetireJob(job);
					Progress.Finish(progressId, Progress.Status.Failed);
					Debug.LogError($"[PkgLnk] Could not resolve archive URL for {pkg.display_name}: {error}");
					EditorUtility.DisplayDialog(
						"Download Failed",
						$"Could not resolve archive URL.\n\n{error}",
						"OK");
					onComplete?.Invoke(false, error ?? "Empty archive URL.", null);
					return;
				}

				DownloadAndExtract(pkg, archiveUrl, targetPath, job, () => cancelled, onComplete);
			});
		}

		// ─── Track-download endpoint ─────────────────────────────────────────

		private static void FetchArchiveUrl(string projectId, InFlightJob job, Action<string, string> onComplete)
		{
			var url = $"{TrackDownloadBase}/{Uri.EscapeDataString(projectId)}/track-download";
			var request = new UnityWebRequest(url, "POST");
			request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes("{}"));
			request.downloadHandler = new DownloadHandlerBuffer();
			request.SetRequestHeader("Content-Type", "application/json");
			request.SetRequestHeader("User-Agent", UserAgent);
			request.timeout = TrackDownloadTimeoutSeconds;

			job.Request = request;

			var op = request.SendWebRequest();
			op.completed += _ =>
			{
				// If the reload handler has already aborted us, the closure may
				// fire one last time — bail rather than running the success path.
				if (job.Request != request) { try { request.Dispose(); } catch { } return; }

				if (request.result != UnityWebRequest.Result.Success)
				{
					var err = request.error ?? "request failed";
					request.Dispose();
					job.Request = null;
					onComplete?.Invoke(null, err);
					return;
				}

				var text = request.downloadHandler.text;
				request.Dispose();
				job.Request = null;
				onComplete?.Invoke(ParseArchiveUrl(text), null);
			};
		}

		// Minimal scan for { "url": "..." }. Avoids pulling in a JSON dependency
		// for a single-field response.
		private static string ParseArchiveUrl(string json)
		{
			if (string.IsNullOrEmpty(json)) return null;
			const string key = "\"url\"";
			var i = json.IndexOf(key, StringComparison.Ordinal);
			if (i < 0) return null;
			i = json.IndexOf('"', i + key.Length);
			if (i < 0) return null;
			var start = i + 1;
			var end = json.IndexOf('"', start);
			if (end < 0) return null;
			return json.Substring(start, end - start).Replace("\\/", "/").Replace("\\\\", "\\");
		}

		// ─── Download + extract pipeline ─────────────────────────────────────

		private static void DownloadAndExtract(
			PackageData pkg,
			string archiveUrl,
			string targetPath,
			InFlightJob job,
			Func<bool> isCancelled,
			Action<bool, string, string> onComplete)
		{
			var progressId = job.ProgressId;

			// Extract directly under the target's parent so the final move is an
			// intra-volume rename rather than a cross-volume copy.
			var targetParent = Path.GetDirectoryName(targetPath);
			if (string.IsNullOrEmpty(targetParent))
			{
				RetireJob(job);
				Progress.Finish(progressId, Progress.Status.Failed);
				Debug.LogError($"[PkgLnk] Invalid target path for {pkg.display_name}: {targetPath}");
				onComplete?.Invoke(false, "Invalid target path.", null);
				return;
			}

			Directory.CreateDirectory(targetParent);
			var tempArchive = Path.Combine(Path.GetTempPath(), $"pkglnk-{Guid.NewGuid():N}.zip");
			var tempExtract = Path.Combine(targetParent, $".pkglnk-tmp-{Guid.NewGuid():N}");

			job.TempArchive = tempArchive;
			job.TempExtract = tempExtract;

			var request = new UnityWebRequest(archiveUrl, "GET");
			request.downloadHandler = new DownloadHandlerFile(tempArchive) { removeFileOnAbort = true };
			request.SetRequestHeader("User-Agent", UserAgent);
			request.timeout = ArchiveDownloadTimeoutSeconds;

			job.Request = request;

			Progress.SetDescription(progressId, "Downloading archive…");

			var op = request.SendWebRequest();

			// Poll download progress from editor-update so the description stays
			// live. Codeload responses are chunked (no Content-Length), so
			// downloadProgress doesn't move — use downloadedBytes instead.
			EditorApplication.CallbackFunction progressTick = null;
			progressTick = () =>
			{
				if (isCancelled())
				{
					EditorApplication.update -= progressTick;
					request.Abort();
					return;
				}
				if (!request.isDone)
				{
					Progress.SetDescription(progressId, $"Downloading… {FormatBytes(request.downloadedBytes)}");
				}
			};
			EditorApplication.update += progressTick;

			op.completed += _ =>
			{
				EditorApplication.update -= progressTick;

				// Reload handler already aborted us — bail without re-running cleanup.
				if (job.Request != request) { try { request.Dispose(); } catch { } return; }

				if (isCancelled())
				{
					request.Dispose();
					job.Request = null;
					RetireJob(job);
					CleanupTemp(tempArchive, tempExtract);
					Progress.Remove(progressId);
					Debug.Log($"[PkgLnk] Project download cancelled by user: {pkg.display_name}");
					onComplete?.Invoke(false, "Cancelled.", null);
					return;
				}

				if (request.result != UnityWebRequest.Result.Success)
				{
					var err = request.error ?? "download failed";
					request.Dispose();
					job.Request = null;
					RetireJob(job);
					CleanupTemp(tempArchive, tempExtract);
					Progress.Finish(progressId, Progress.Status.Failed);
					Debug.LogError($"[PkgLnk] Project download failed for {pkg.display_name}: {err}");
					EditorUtility.DisplayDialog("Download Failed", $"Download error: {err}", "OK");
					onComplete?.Invoke(false, err, null);
					return;
				}

				request.Dispose();
				job.Request = null;

				try
				{
					Progress.SetDescription(progressId, "Extracting…");

					Directory.CreateDirectory(tempExtract);
					ExtractZipWithProgress(tempArchive, tempExtract, progressId, isCancelled);

					if (isCancelled())
					{
						RetireJob(job);
						CleanupTemp(tempArchive, tempExtract);
						Progress.Remove(progressId);
						Debug.Log($"[PkgLnk] Project download cancelled during extract: {pkg.display_name}");
						onComplete?.Invoke(false, "Cancelled.", null);
						return;
					}

					// Codeload archives wrap their contents in a single top-level
					// <repo>-<ref>/ folder. Lift that to be the target directly.
					var topLevel = FindSingleTopLevel(tempExtract) ?? tempExtract;

					if (Directory.Exists(targetPath))
					{
						RetireJob(job);
						CleanupTemp(tempArchive, tempExtract);
						Progress.Finish(progressId, Progress.Status.Failed);
						Debug.LogError($"[PkgLnk] Folder appeared at {targetPath} while downloading {pkg.display_name}.");
						EditorUtility.DisplayDialog(
							"Folder Already Exists",
							$"A folder appeared at {targetPath} while downloading. Aborting.",
							"OK");
						onComplete?.Invoke(false, "Folder already exists.", null);
						return;
					}

					Directory.Move(topLevel, targetPath);
					CleanupTemp(tempArchive, tempExtract);
					RetireJob(job);

					Progress.SetDescription(progressId, "Done");
					Progress.Finish(progressId, Progress.Status.Succeeded);
					Debug.Log($"[PkgLnk] Project downloaded: {pkg.display_name} → {targetPath}");

					var reveal = EditorUtility.DisplayDialog(
						"Project Downloaded",
						$"{pkg.display_name} downloaded to:\n\n{targetPath}",
						"Reveal in Explorer",
						"Close");
					if (reveal)
					{
						EditorUtility.RevealInFinder(targetPath);
					}

					onComplete?.Invoke(true, null, targetPath);
				}
				catch (Exception ex)
				{
					RetireJob(job);
					CleanupTemp(tempArchive, tempExtract);
					Progress.Finish(progressId, Progress.Status.Failed);
					Debug.LogError($"[PkgLnk] Extract failed for {pkg.display_name}: {ex}");
					EditorUtility.DisplayDialog("Extract Failed", ex.Message, "OK");
					onComplete?.Invoke(false, ex.Message, null);
				}
			};
		}

		private static void ExtractZipWithProgress(
			string archivePath,
			string destinationDir,
			int progressId,
			Func<bool> isCancelled)
		{
			using var archive = ZipFile.OpenRead(archivePath);
			var total = archive.Entries.Count;
			if (total == 0) return;

			var destFull = Path.GetFullPath(destinationDir) + Path.DirectorySeparatorChar;
			var done = 0;

			foreach (var entry in archive.Entries)
			{
				if (isCancelled()) return;

				// Zip-slip guard: ensure the resolved entry path stays inside destDir.
				var combined = Path.Combine(destinationDir, entry.FullName);
				var fullPath = Path.GetFullPath(combined);
				if (!fullPath.StartsWith(destFull, StringComparison.OrdinalIgnoreCase))
				{
					throw new IOException($"Zip entry escapes destination: {entry.FullName}");
				}

				if (entry.FullName.EndsWith("/", StringComparison.Ordinal))
				{
					Directory.CreateDirectory(fullPath);
				}
				else
				{
					var parent = Path.GetDirectoryName(fullPath);
					if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
					entry.ExtractToFile(fullPath, overwrite: true);
				}

				done++;
				Progress.SetDescription(progressId, $"Extracting… {done}/{total}");
			}
		}

		private static string FindSingleTopLevel(string dir)
		{
			var entries = Directory.GetFileSystemEntries(dir);
			if (entries.Length != 1) return null;
			return Directory.Exists(entries[0]) ? entries[0] : null;
		}

		private static void CleanupTemp(string archive, string extractDir)
		{
			try { if (File.Exists(archive)) File.Delete(archive); } catch { /* best-effort */ }
			try { if (Directory.Exists(extractDir)) Directory.Delete(extractDir, true); } catch { /* best-effort */ }
		}

		private static string FormatBytes(ulong bytes)
		{
			if (bytes < 1024UL) return $"{bytes} B";
			if (bytes < 1024UL * 1024UL) return $"{bytes / 1024.0:F1} KB";
			if (bytes < 1024UL * 1024UL * 1024UL) return $"{bytes / (1024.0 * 1024.0):F1} MB";
			return $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
		}
	}
}
