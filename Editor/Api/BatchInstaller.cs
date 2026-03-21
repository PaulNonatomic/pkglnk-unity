using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Nonatomic.PkgLnk.Editor.Api
{
	public enum BatchPhase
	{
		Installing,
		Completed,
		Failed,
		Cancelled
	}

	public class BatchProgress
	{
		public int CurrentIndex;
		public int TotalCount;
		public PackageData CurrentPackage;
		public BatchPhase Phase;
	}

	public class BatchResult
	{
		public int Installed;
		public int Failed;
		public int Skipped;
		public int Cancelled;
		public List<string> Errors = new List<string>();
	}

	/// <summary>
	/// Sequentially installs multiple packages by chaining PackageInstaller.Install() calls.
	/// Only one batch can run at a time.
	/// </summary>
	public static class BatchInstaller
	{
		private const string SessionKeyIndex = "PkgLnk_BatchIndex";
		private const string SessionKeyTotal = "PkgLnk_BatchTotal";

		private static List<PackageData> _queue;
		private static int _currentIndex;
		private static bool _isCancelled;
		private static Action<BatchProgress> _onProgress;
		private static Action<BatchResult> _onComplete;
		private static Action<PackageData, InstallPhase> _onInstallPhase;
		private static BatchResult _result;

		public static bool IsRunning => _queue != null && _currentIndex < _queue.Count && !_isCancelled;

		/// <summary>
		/// Re-attaches after domain reload if a batch was in progress.
		/// </summary>
		[InitializeOnLoadMethod]
		private static void OnEditorLoad()
		{
			var total = SessionState.GetInt(SessionKeyTotal, 0);
			if (total <= 0) return;

			var index = SessionState.GetInt(SessionKeyIndex, 0);
			Cleanup();

#if PKGLNK_DEBUG
			EditorApplication.delayCall += () =>
				Debug.Log($"[PkgLnk] Batch install was interrupted at {index}/{total} during domain reload.");
#endif
		}

		/// <summary>
		/// Begins sequential installation. Already-installed packages are skipped.
		/// Optionally pass <paramref name="onInstallPhase"/> to receive per-package
		/// server-side install phase updates.
		/// </summary>
		public static void InstallAll(
			List<PackageData> packages,
			Action<BatchProgress> onProgress,
			Action<BatchResult> onComplete,
			Action<PackageData, InstallPhase> onInstallPhase = null)
		{
			if (IsRunning)
			{
				onComplete?.Invoke(new BatchResult
				{
					Errors = new List<string> { "A batch install is already running." }
				});
				return;
			}

			_queue = new List<PackageData>();
			_result = new BatchResult();
			_onProgress = onProgress;
			_onComplete = onComplete;
			_onInstallPhase = onInstallPhase;
			_isCancelled = false;
			_currentIndex = 0;

			foreach (var pkg in packages)
			{
				if (PackageInstaller.IsInstalled(pkg))
				{
					_result.Skipped++;
				}
				else
				{
					_queue.Add(pkg);
				}
			}

			if (_queue.Count == 0)
			{
				var result = _result;
				Cleanup();
				onComplete?.Invoke(result);
				return;
			}

			SessionState.SetInt(SessionKeyTotal, _queue.Count);
			SessionState.SetInt(SessionKeyIndex, 0);
			ProcessNext();
		}

		public static void Cancel()
		{
			if (_queue == null) return;

			_isCancelled = true;
			_result.Cancelled = _queue.Count - _currentIndex;
		}

		private static void ProcessNext()
		{
			if (_isCancelled || _currentIndex >= _queue.Count)
			{
				var result = _result;
				var callback = _onComplete;
				Cleanup();
				callback?.Invoke(result);
				return;
			}

			var pkg = _queue[_currentIndex];

			_onProgress?.Invoke(new BatchProgress
			{
				CurrentIndex = _currentIndex,
				TotalCount = _queue.Count,
				CurrentPackage = pkg,
				Phase = BatchPhase.Installing
			});

			Action<InstallPhase> phaseCallback = _onInstallPhase != null
				? phase => _onInstallPhase?.Invoke(pkg, phase)
				: null;

			PackageInstaller.Install(pkg, (success, error) =>
			{
				if (success)
				{
					_result.Installed++;
					_onProgress?.Invoke(new BatchProgress
					{
						CurrentIndex = _currentIndex,
						TotalCount = _queue.Count,
						CurrentPackage = pkg,
						Phase = BatchPhase.Completed
					});
				}
				else
				{
					_result.Failed++;
					_result.Errors.Add($"{pkg.display_name}: {error}");
					_onProgress?.Invoke(new BatchProgress
					{
						CurrentIndex = _currentIndex,
						TotalCount = _queue.Count,
						CurrentPackage = pkg,
						Phase = BatchPhase.Failed
					});
				}

				_currentIndex++;
				SessionState.SetInt(SessionKeyIndex, _currentIndex);
				ProcessNext();
			}, phaseCallback);
		}

		private static void Cleanup()
		{
			_queue = null;
			_currentIndex = 0;
			_isCancelled = false;
			_onProgress = null;
			_onComplete = null;
			_onInstallPhase = null;
			_result = null;
			SessionState.EraseInt(SessionKeyIndex);
			SessionState.EraseInt(SessionKeyTotal);
		}
	}
}
