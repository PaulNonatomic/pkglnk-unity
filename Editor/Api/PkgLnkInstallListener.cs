using System;
using System.IO;
using System.Net;
using System.Threading;
using Nonatomic.PkgLnk.Editor.PkgLnkWindow;
using UnityEditor;
using UnityEngine;

namespace Nonatomic.PkgLnk.Editor.Api
{
	/// <summary>
	/// Persistent localhost HTTP server that receives install requests
	/// from the pkglnk.dev website's "Install in Unity" button.
	/// Starts automatically when the editor loads, stops on quit.
	/// </summary>
	[InitializeOnLoad]
	public static class PkgLnkInstallListener
	{
		private const int Port = 29120;
		private static readonly string[] AllowedOrigins =
		{
			"https://pkglnk.dev",
			"https://www.pkglnk.dev",
			"http://localhost:5173",
			"http://localhost:4173",
			"http://localhost:3000"
		};
		private static readonly string[] AllowedUrlPrefixes =
		{
			"https://pkglnk.dev/",
			"https://www.pkglnk.dev/",
			"https://pkglnk.dev/track/",
			"https://www.pkglnk.dev/track/"
		};
		private static readonly string[] AllowedNuGetDownloadPrefixes =
		{
			"https://pkglnk.dev/nuget/flatcontainer/",
			"https://www.pkglnk.dev/nuget/flatcontainer/"
		};

		private static HttpListener _listener;
		private static Thread _listenerThread;
		private static volatile bool _installDispatched;

		static PkgLnkInstallListener()
		{
			Start();
			EditorApplication.quitting += Stop;
		}

		private static void Start()
		{
			if (_listener != null) return;

			try
			{
				_listener = new HttpListener();
				_listener.Prefixes.Add($"http://localhost:{Port}/");
				_listener.Start();
			}
			catch (Exception ex)
			{
				Debug.LogWarning($"[PkgLnk] Install listener could not start on port {Port}: {ex.Message}");
				_listener = null;
				return;
			}

			_listenerThread = new Thread(ListenLoop) { IsBackground = true };
			_listenerThread.Start();

#if PKGLNK_DEBUG
			Debug.Log($"[PkgLnk] Install listener started on port {Port}");
#endif
		}

		private static void Stop()
		{
			try
			{
				_listener?.Stop();
				_listener?.Close();
			}
			catch
			{
				// Ignore cleanup errors
			}

			_listener = null;
			_listenerThread = null;
		}

		private static void ListenLoop()
		{
			while (_listener != null && _listener.IsListening)
			{
				try
				{
					var context = _listener.GetContext();
					HandleRequest(context);
				}
				catch (HttpListenerException)
				{
					// Listener was stopped
					break;
				}
				catch (ObjectDisposedException)
				{
					break;
				}
				catch (Exception ex)
				{
					Debug.LogWarning($"[PkgLnk] Listener error: {ex.Message}");
				}
			}
		}

		private static void HandleRequest(HttpListenerContext context)
		{
			var request = context.Request;
			var response = context.Response;

			// CORS headers on all responses — echo back origin if allowed
			var requestOrigin = request.Headers["Origin"] ?? string.Empty;
			var matchedOrigin = string.Empty;
			foreach (var allowed in AllowedOrigins)
			{
				if (string.Equals(requestOrigin, allowed, StringComparison.OrdinalIgnoreCase))
				{
					matchedOrigin = allowed;
					break;
				}
			}

			if (!string.IsNullOrEmpty(matchedOrigin))
			{
				response.AddHeader("Access-Control-Allow-Origin", matchedOrigin);
			}

			response.AddHeader("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
			response.AddHeader("Access-Control-Allow-Headers", "Content-Type");

			try
			{
				var path = request.Url.AbsolutePath.TrimEnd('/');

#if PKGLNK_DEBUG
				Debug.Log($"[PkgLnk] HTTP {request.HttpMethod} {path} from {request.RemoteEndPoint}");
#endif

				if (request.HttpMethod == "OPTIONS")
				{
					response.StatusCode = 204;
					response.Close();
					return;
				}

				switch (path)
				{
					case "/ping":
						SendJson(response, 200, "{\"status\":\"ok\"}");
						break;

					case "/install":
						HandleInstall(request, response);
						break;

					case "/install/nuget":
						HandleNuGetInstall(request, response);
						break;

					default:
						SendJson(response, 404, "{\"error\":\"not_found\"}");
						break;
				}
			}
			catch (Exception ex)
			{
				Debug.LogError($"[PkgLnk] Request handler error: {ex.Message}\n{ex.StackTrace}");
				try
				{
					SendJson(response, 500, $"{{\"error\":\"{EscapeJson(ex.Message)}\"}}");
				}
				catch
				{
					// Response may already be closed
				}
			}
		}

		private static void HandleInstall(HttpListenerRequest request, HttpListenerResponse response)
		{
			if (request.HttpMethod != "POST")
			{
				SendJson(response, 405, "{\"error\":\"method_not_allowed\"}");
				return;
			}

			string body;
			using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
			{
				body = reader.ReadToEnd();
			}

			var url = ParseUrlFromJson(body);

			if (string.IsNullOrEmpty(url))
			{
				Debug.LogWarning("[PkgLnk] Could not parse URL from request body");
				SendJson(response, 400, "{\"error\":\"missing_url\"}");
				return;
			}

			var urlAllowed = false;
			foreach (var prefix in AllowedUrlPrefixes)
			{
				if (url.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
				{
					urlAllowed = true;
					break;
				}
			}

			if (!urlAllowed)
			{
				Debug.LogWarning($"[PkgLnk] URL rejected — does not match any allowed prefix: {url}");
				SendJson(response, 400, "{\"error\":\"invalid_url\"}");
				return;
			}

			if (_installDispatched)
			{
				Debug.LogWarning("[PkgLnk] Rejecting — another install already in progress");
				SendJson(response, 409, "{\"error\":\"install_in_progress\"}");
				return;
			}

			// Show confirmation window on the main thread
			_installDispatched = true;
			EditorApplication.delayCall += () =>
			{
				InstallConfirmWindow.Show(url);
				_installDispatched = false;
			};

			SendJson(response, 200, "{\"status\":\"installing\"}");
		}

		/// <summary>
		/// Handles POST /install/nuget. Body shape:
		///   { "packageId": "...", "version": "...", "downloadUrl": "..." }
		/// downloadUrl must be on pkglnk's flat container — same security
		/// rule as the UPM /install endpoint, just a different prefix list.
		/// </summary>
		private static void HandleNuGetInstall(HttpListenerRequest request, HttpListenerResponse response)
		{
			if (request.HttpMethod != "POST")
			{
				SendJson(response, 405, "{\"error\":\"method_not_allowed\"}");
				return;
			}

			string body;
			using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
			{
				body = reader.ReadToEnd();
			}

			var packageId = ParseStringFromJson(body, "packageId");
			var version = ParseStringFromJson(body, "version");
			var downloadUrl = ParseStringFromJson(body, "downloadUrl");

			if (string.IsNullOrEmpty(packageId) || string.IsNullOrEmpty(downloadUrl))
			{
				Debug.LogWarning("[PkgLnk] NuGet install request missing packageId or downloadUrl");
				SendJson(response, 400, "{\"error\":\"missing_fields\"}");
				return;
			}

			var allowed = false;
			foreach (var prefix in AllowedNuGetDownloadPrefixes)
			{
				if (downloadUrl.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
				{
					allowed = true;
					break;
				}
			}

			if (!allowed)
			{
				Debug.LogWarning($"[PkgLnk] NuGet downloadUrl rejected: {downloadUrl}");
				SendJson(response, 400, "{\"error\":\"invalid_download_url\"}");
				return;
			}

			if (_installDispatched)
			{
				Debug.LogWarning("[PkgLnk] Rejecting NuGet install — another install already in progress");
				SendJson(response, 409, "{\"error\":\"install_in_progress\"}");
				return;
			}

			_installDispatched = true;
			EditorApplication.delayCall += () =>
			{
				InstallNuGetConfirmWindow.Show(packageId, version ?? string.Empty, downloadUrl);
				_installDispatched = false;
			};

			SendJson(response, 200, "{\"status\":\"installing\"}");
		}

		/// <summary>
		/// Minimal JSON parser for {"url":"..."} — avoids dependency on JsonUtility
		/// which requires a serializable class.
		/// </summary>
		private static string ParseUrlFromJson(string json)
		{
			return ParseStringFromJson(json, "url");
		}

		/// <summary>
		/// Extracts a string field by name from a flat JSON object. Tolerates
		/// arbitrary whitespace and field ordering. Returns null if the field
		/// isn't present or its value isn't a string.
		/// </summary>
		private static string ParseStringFromJson(string json, string fieldName)
		{
			if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(fieldName)) return null;

			var marker = "\"" + fieldName + "\"";
			var idx = json.IndexOf(marker, StringComparison.Ordinal);
			if (idx < 0) return null;

			idx = json.IndexOf(':', idx + marker.Length);
			if (idx < 0) return null;

			var startQuote = json.IndexOf('"', idx + 1);
			if (startQuote < 0) return null;

			var endQuote = json.IndexOf('"', startQuote + 1);
			if (endQuote < 0) return null;

			return json.Substring(startQuote + 1, endQuote - startQuote - 1);
		}

		private static void SendJson(HttpListenerResponse response, int statusCode, string json)
		{
			response.StatusCode = statusCode;
			response.ContentType = "application/json";
			var buffer = System.Text.Encoding.UTF8.GetBytes(json);
			response.ContentLength64 = buffer.Length;
			response.OutputStream.Write(buffer, 0, buffer.Length);
			response.OutputStream.Close();
		}

		private static string EscapeJson(string value)
		{
			return value?.Replace("\\", "\\\\").Replace("\"", "\\\"") ?? string.Empty;
		}
	}
}
