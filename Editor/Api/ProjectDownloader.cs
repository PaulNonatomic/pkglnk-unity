using System;
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

			var progressId = Progress.Start(
				$"Downloading {pkg.display_name}",
				"Requesting archive URL…");

			var cancelled = false;
			Progress.RegisterCancelCallback(progressId, () =>
			{
				cancelled = true;
				return true;
			});

			FetchArchiveUrl(pkg.id, (archiveUrl, error) =>
			{
				if (cancelled)
				{
					Progress.Remove(progressId);
					onComplete?.Invoke(false, "Cancelled.", null);
					return;
				}

				if (!string.IsNullOrEmpty(error) || string.IsNullOrEmpty(archiveUrl))
				{
					Progress.Finish(progressId, Progress.Status.Failed);
					EditorUtility.DisplayDialog(
						"Download Failed",
						$"Could not resolve archive URL.\n\n{error}",
						"OK");
					onComplete?.Invoke(false, error ?? "Empty archive URL.", null);
					return;
				}

				DownloadAndExtract(pkg, archiveUrl, targetPath, progressId, () => cancelled, onComplete);
			});
		}

		// ─── Track-download endpoint ─────────────────────────────────────────

		private static void FetchArchiveUrl(string projectId, Action<string, string> onComplete)
		{
			var url = $"{TrackDownloadBase}/{Uri.EscapeDataString(projectId)}/track-download";
			var request = new UnityWebRequest(url, "POST");
			request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes("{}"));
			request.downloadHandler = new DownloadHandlerBuffer();
			request.SetRequestHeader("Content-Type", "application/json");
			request.SetRequestHeader("User-Agent", UserAgent);

			var op = request.SendWebRequest();
			op.completed += _ =>
			{
				if (request.result != UnityWebRequest.Result.Success)
				{
					var err = request.error ?? "request failed";
					request.Dispose();
					onComplete?.Invoke(null, err);
					return;
				}

				var text = request.downloadHandler.text;
				request.Dispose();
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
			int progressId,
			Func<bool> isCancelled,
			Action<bool, string, string> onComplete)
		{
			// Extract directly under the target's parent so the final move is an
			// intra-volume rename rather than a cross-volume copy.
			var targetParent = Path.GetDirectoryName(targetPath);
			if (string.IsNullOrEmpty(targetParent))
			{
				Progress.Finish(progressId, Progress.Status.Failed);
				onComplete?.Invoke(false, "Invalid target path.", null);
				return;
			}

			Directory.CreateDirectory(targetParent);
			var tempArchive = Path.Combine(Path.GetTempPath(), $"pkglnk-{Guid.NewGuid():N}.zip");
			var tempExtract = Path.Combine(targetParent, $".pkglnk-tmp-{Guid.NewGuid():N}");

			var request = new UnityWebRequest(archiveUrl, "GET");
			request.downloadHandler = new DownloadHandlerFile(tempArchive) { removeFileOnAbort = true };
			request.SetRequestHeader("User-Agent", UserAgent);

			Progress.SetDescription(progressId, "Downloading archive…");
			Progress.Report(progressId, 0f);

			var op = request.SendWebRequest();

			// Poll download progress from editor-update so Progress stays live.
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
					var p = request.downloadProgress;
					Progress.Report(progressId, p * 0.85f, $"Downloading… {Mathf.RoundToInt(p * 100f)}%");
				}
			};
			EditorApplication.update += progressTick;

			op.completed += _ =>
			{
				EditorApplication.update -= progressTick;

				if (isCancelled())
				{
					request.Dispose();
					CleanupTemp(tempArchive, tempExtract);
					Progress.Remove(progressId);
					onComplete?.Invoke(false, "Cancelled.", null);
					return;
				}

				if (request.result != UnityWebRequest.Result.Success)
				{
					var err = request.error ?? "download failed";
					request.Dispose();
					CleanupTemp(tempArchive, tempExtract);
					Progress.Finish(progressId, Progress.Status.Failed);
					EditorUtility.DisplayDialog("Download Failed", $"Download error: {err}", "OK");
					onComplete?.Invoke(false, err, null);
					return;
				}

				request.Dispose();

				try
				{
					Progress.SetDescription(progressId, "Extracting…");
					Progress.Report(progressId, 0.86f);

					Directory.CreateDirectory(tempExtract);
					ExtractZipWithProgress(tempArchive, tempExtract, progressId, isCancelled);

					if (isCancelled())
					{
						CleanupTemp(tempArchive, tempExtract);
						Progress.Remove(progressId);
						onComplete?.Invoke(false, "Cancelled.", null);
						return;
					}

					// Codeload archives wrap their contents in a single top-level
					// <repo>-<ref>/ folder. Lift that to be the target directly.
					var topLevel = FindSingleTopLevel(tempExtract) ?? tempExtract;

					if (Directory.Exists(targetPath))
					{
						CleanupTemp(tempArchive, tempExtract);
						Progress.Finish(progressId, Progress.Status.Failed);
						EditorUtility.DisplayDialog(
							"Folder Already Exists",
							$"A folder appeared at {targetPath} while downloading. Aborting.",
							"OK");
						onComplete?.Invoke(false, "Folder already exists.", null);
						return;
					}

					Directory.Move(topLevel, targetPath);
					CleanupTemp(tempArchive, tempExtract);

					Progress.Report(progressId, 1f, "Done");
					Progress.Finish(progressId, Progress.Status.Succeeded);

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
					CleanupTemp(tempArchive, tempExtract);
					Progress.Finish(progressId, Progress.Status.Failed);
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
				var p = 0.86f + (done / (float)total) * 0.14f;
				Progress.Report(progressId, p, $"Extracting… {done}/{total}");
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
	}
}
