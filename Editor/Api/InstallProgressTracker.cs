using System;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace Nonatomic.PkgLnk.Editor.Api
{
	/// <summary>
	/// Install progress phases reported by the pkglnk.dev tracking proxy.
	/// </summary>
	public enum InstallPhase
	{
		Pending,
		Resolving,
		Downloading,
		Importing,
		Complete
	}

	/// <summary>
	/// Install-source identifiers that pkglnk.dev validates against.
	/// Values mirror the server-side enum in /api/v1/install-start;
	/// adding a new source requires updates on both sides.
	/// </summary>
	public static class InstallSource
	{
		/// <summary>User clicked Install on pkglnk.dev — the website's
		/// modal forwarded the request to us via the localhost listener.</summary>
		public const string PkglnkWeb = "pkglnk-web";

		/// <summary>User clicked install on a card inside pkglnk-unity's
		/// own editor browser window — direct in-editor flow.</summary>
		public const string PkglnkUnityWindow = "pkglnk-unity-window";
	}

	/// <summary>
	/// Tracks real-time install progress by polling the pkglnk.dev server.
	/// The server updates phase as git protocol events arrive at the tracking proxy.
	/// </summary>
	public static class InstallProgressTracker
	{
		private const string ApiUrl = "https://pkglnk.dev/api/v1";
		private const string UserAgent = "pkglnk-unity/0.1";
		private const double PollInterval = 2.0;

		private static string _activeInstallId;
		private static Action<InstallPhase> _onPhaseChanged;
		private static InstallPhase _lastPhase;
		private static double _lastPollTime;
		private static bool _polling;

		/// <summary>
		/// Generates a crypto-random 16-character hex string for use as an install session ID.
		/// </summary>
		public static string GenerateInstallId()
		{
			var bytes = new byte[8];
			using (var rng = RandomNumberGenerator.Create())
			{
				rng.GetBytes(bytes);
			}

			var sb = new StringBuilder(16);
			foreach (var b in bytes)
			{
				sb.Append(b.ToString("x2"));
			}

			return sb.ToString();
		}

		/// <summary>
		/// Notifies the server that an install is starting. Fire-and-forget.
		///
		/// <paramref name="source"/> tells the server which surface
		/// triggered the install — either <see cref="InstallSource.PkglnkWeb"/>
		/// (the user clicked Install on pkglnk.dev and the request was
		/// forwarded here via the localhost listener) or
		/// <see cref="InstallSource.PkglnkUnityWindow"/> (the user
		/// browsed packages inside pkglnk-unity's editor window).
		/// Defaults to <see cref="InstallSource.PkglnkUnityWindow"/>.
		/// </summary>
		public static void NotifyInstallStart(string slug, string installId, string source = InstallSource.PkglnkUnityWindow)
		{
			// Tiny manual JSON encode keeps the dependency footprint
			// down. Source values are server-side validated against an
			// enum so we don't need to escape them here.
			var body = $"{{\"slug\":\"{slug}\",\"install_id\":\"{installId}\",\"source\":\"{source}\"}}";
			var request = new UnityWebRequest($"{ApiUrl}/install-start", "POST");
			request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
			request.downloadHandler = new DownloadHandlerBuffer();
			request.SetRequestHeader("Content-Type", "application/json");
			request.SetRequestHeader("User-Agent", UserAgent);

			var operation = request.SendWebRequest();
			operation.completed += _ =>
			{
				if (request.result != UnityWebRequest.Result.Success)
				{
					Debug.LogWarning($"[PkgLnk] Install-start notification failed: {request.error}");
				}
				request.Dispose();
			};
		}

		/// <summary>
		/// Begins polling the server for phase changes.
		/// Calls <paramref name="onPhaseChanged"/> on the main thread when the phase advances.
		/// </summary>
		public static void StartTracking(string installId, Action<InstallPhase> onPhaseChanged)
		{
			Stop();

			_activeInstallId = installId;
			_onPhaseChanged = onPhaseChanged;
			_lastPhase = InstallPhase.Pending;
			_lastPollTime = 0;
			_polling = false;

			EditorApplication.update += PollProgress;
		}

		/// <summary>
		/// Stops tracking and fires a final Complete phase.
		/// </summary>
		public static void StopWithComplete()
		{
			var callback = _onPhaseChanged;
			Stop();
			callback?.Invoke(InstallPhase.Complete);
		}

		/// <summary>
		/// Stops tracking without firing Complete.
		/// </summary>
		public static void Stop()
		{
			EditorApplication.update -= PollProgress;
			_activeInstallId = null;
			_onPhaseChanged = null;
			_polling = false;
		}

		private static void PollProgress()
		{
			if (_activeInstallId == null)
			{
				EditorApplication.update -= PollProgress;
				return;
			}

			if (_polling) return;

			var now = EditorApplication.timeSinceStartup;
			if (now - _lastPollTime < PollInterval) return;

			_lastPollTime = now;
			_polling = true;

			var request = UnityWebRequest.Get($"{ApiUrl}/install-progress/{_activeInstallId}");
			request.SetRequestHeader("User-Agent", UserAgent);

			var operation = request.SendWebRequest();
			operation.completed += _ =>
			{
				_polling = false;

				if (_activeInstallId == null)
				{
					request.Dispose();
					return;
				}

				if (request.result == UnityWebRequest.Result.Success)
				{
					var phase = ParsePhase(request.downloadHandler.text);
					if (phase > _lastPhase)
					{
						_lastPhase = phase;
						_onPhaseChanged?.Invoke(phase);
					}
				}

				request.Dispose();
			};
		}

		/// <summary>
		/// Parses the phase field from a JSON response string.
		/// Expected format: {"phase":"resolving",...}
		/// </summary>
		internal static InstallPhase ParsePhase(string json)
		{
			var marker = "\"phase\":\"";
			var start = json.IndexOf(marker, StringComparison.Ordinal);
			if (start < 0) return InstallPhase.Pending;

			start += marker.Length;
			var end = json.IndexOf('"', start);
			if (end < 0) return InstallPhase.Pending;

			var value = json.Substring(start, end - start);

			switch (value)
			{
				case "resolving":
					return InstallPhase.Resolving;
				case "downloading":
					return InstallPhase.Downloading;
				default:
					return InstallPhase.Pending;
			}
		}
	}
}
