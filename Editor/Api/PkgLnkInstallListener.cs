using System;
using System.IO;
using System.Net;
using System.Threading;
using UnityEditor;
using UnityEditor.PackageManager;
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
		private const string AllowedOrigin = "https://pkglnk.dev";
		private const string AllowedUrlPrefix = "https://pkglnk.dev/track/";

		private static HttpListener _listener;
		private static Thread _listenerThread;

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

			Debug.Log($"[PkgLnk] Install listener started on port {Port}");
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

			// CORS headers on all responses
			response.AddHeader("Access-Control-Allow-Origin", AllowedOrigin);
			response.AddHeader("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
			response.AddHeader("Access-Control-Allow-Headers", "Content-Type");

			try
			{
				var path = request.Url.AbsolutePath.TrimEnd('/');

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

					default:
						SendJson(response, 404, "{\"error\":\"not_found\"}");
						break;
				}
			}
			catch (Exception ex)
			{
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
				SendJson(response, 400, "{\"error\":\"missing_url\"}");
				return;
			}

			if (!url.StartsWith(AllowedUrlPrefix, StringComparison.OrdinalIgnoreCase))
			{
				SendJson(response, 400, "{\"error\":\"invalid_url\"}");
				return;
			}

			if (PackageInstaller.IsInstalling)
			{
				SendJson(response, 409, "{\"error\":\"install_in_progress\"}");
				return;
			}

			// Dispatch install to the main thread
			EditorApplication.delayCall += () =>
			{
				Debug.Log($"[PkgLnk] Install request received from browser: {url}");
				Client.Add(url);
			};

			SendJson(response, 200, "{\"status\":\"installing\"}");
		}

		/// <summary>
		/// Minimal JSON parser for {"url":"..."} — avoids dependency on JsonUtility
		/// which requires a serializable class.
		/// </summary>
		private static string ParseUrlFromJson(string json)
		{
			if (string.IsNullOrEmpty(json)) return null;

			var marker = "\"url\"";
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
