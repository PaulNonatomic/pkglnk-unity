using System;
using System.Net;
using System.Threading;
using UnityEditor;
using UnityEngine;

namespace Nonatomic.PkgLnk.Editor.Api
{
	/// <summary>
	/// Handles browser-based OAuth login for pkglnk.dev.
	/// Opens the system browser, listens on localhost for the token callback.
	/// </summary>
	public static class PkgLnkAuth
	{
		private const string EditorPrefsTokenKey = "PkgLnk_ApiToken";
		private const string EditorPrefsUsernameKey = "PkgLnk_Username";
		private const string AuthStartUrl = "https://pkglnk.dev/auth/unity-start";
		private const float TimeoutSeconds = 120f;

		private static HttpListener _listener;
		private static Thread _listenerThread;
		private static bool _isLoggingIn;
		private static Action<bool, string> _loginCallback;

		/// <summary>The stored API token, or empty if not logged in.</summary>
		public static string Token
		{
			get => EditorPrefs.GetString(EditorPrefsTokenKey, string.Empty);
			private set => EditorPrefs.SetString(EditorPrefsTokenKey, value);
		}

		/// <summary>The logged-in username.</summary>
		public static string Username
		{
			get => EditorPrefs.GetString(EditorPrefsUsernameKey, string.Empty);
			private set => EditorPrefs.SetString(EditorPrefsUsernameKey, value);
		}

		/// <summary>True if a valid token is stored.</summary>
		public static bool IsLoggedIn => !string.IsNullOrEmpty(Token);

		/// <summary>True if a login flow is currently in progress.</summary>
		public static bool IsLoggingIn => _isLoggingIn;

		/// <summary>
		/// Starts the OAuth login flow.
		/// Opens the system browser and waits for the callback.
		/// </summary>
		public static void Login(Action<bool, string> onComplete)
		{
			if (_isLoggingIn)
			{
				onComplete?.Invoke(false, "Login already in progress.");
				return;
			}

			_isLoggingIn = true;
			_loginCallback = onComplete;

			var port = GetAvailablePort();
			var prefix = $"http://localhost:{port}/";

			try
			{
				_listener = new HttpListener();
				_listener.Prefixes.Add(prefix);
				_listener.Start();
			}
			catch (Exception ex)
			{
				_isLoggingIn = false;
				onComplete?.Invoke(false, $"Failed to start listener: {ex.Message}");
				return;
			}

			_listenerThread = new Thread(() => ListenForCallback(port))
			{
				IsBackground = true
			};
			_listenerThread.Start();

			var loginUrl = $"{AuthStartUrl}?port={port}";
			Application.OpenURL(loginUrl);

			// Set a timeout to clean up if no callback received
			EditorApplication.delayCall += () => ScheduleTimeout(port);
		}

		/// <summary>Clears the stored token and username.</summary>
		public static void Logout()
		{
			EditorPrefs.DeleteKey(EditorPrefsTokenKey);
			EditorPrefs.DeleteKey(EditorPrefsUsernameKey);
		}

		private static void ListenForCallback(int port)
		{
			try
			{
				var context = _listener.GetContext();
				var request = context.Request;
				var response = context.Response;

				var token = request.QueryString["token"];
				var username = request.QueryString["username"];
				var error = request.QueryString["error"];

				string responseHtml;

				if (!string.IsNullOrEmpty(error))
				{
					responseHtml = BuildResponseHtml(false, $"Authentication failed: {error}");
					SendResponse(response, responseHtml);
					CompleteLogin(false, error);
				}
				else if (!string.IsNullOrEmpty(token))
				{
					responseHtml = BuildResponseHtml(true, $"Signed in as {username ?? "user"}");
					SendResponse(response, responseHtml);
					CompleteLogin(true, null, token, username ?? string.Empty);
				}
				else
				{
					responseHtml = BuildResponseHtml(false, "No token received.");
					SendResponse(response, responseHtml);
					CompleteLogin(false, "No token received in callback.");
				}
			}
			catch (HttpListenerException)
			{
				// Listener was stopped (timeout or manual cancel)
			}
			catch (ObjectDisposedException)
			{
				// Listener was disposed
			}
			catch (Exception ex)
			{
				CompleteLogin(false, $"Listener error: {ex.Message}");
			}
			finally
			{
				StopListener();
			}
		}

		private static void CompleteLogin(bool success, string error, string token = null, string username = null)
		{
			EditorApplication.delayCall += () =>
			{
				_isLoggingIn = false;

				if (success && !string.IsNullOrEmpty(token))
				{
					Token = token;
					Username = username ?? string.Empty;
					Debug.Log($"[PkgLnk] Logged in as {username}");
				}

				var callback = _loginCallback;
				_loginCallback = null;
				callback?.Invoke(success, error);
			};
		}

		private static void ScheduleTimeout(int port)
		{
			var startTime = EditorApplication.timeSinceStartup;

			void CheckTimeout()
			{
				if (!_isLoggingIn)
				{
					EditorApplication.update -= CheckTimeout;
					return;
				}

				if (EditorApplication.timeSinceStartup - startTime > TimeoutSeconds)
				{
					EditorApplication.update -= CheckTimeout;
					StopListener();
					CompleteLogin(false, "Login timed out. Please try again.");
				}
			}

			EditorApplication.update += CheckTimeout;
		}

		private static void StopListener()
		{
			try
			{
				if (_listener != null && _listener.IsListening)
				{
					_listener.Stop();
					_listener.Close();
				}
			}
			catch
			{
				// Ignore cleanup errors
			}

			_listener = null;
		}

		private static void SendResponse(HttpListenerResponse response, string html)
		{
			var buffer = System.Text.Encoding.UTF8.GetBytes(html);
			response.ContentType = "text/html; charset=utf-8";
			response.ContentLength64 = buffer.Length;
			response.OutputStream.Write(buffer, 0, buffer.Length);
			response.OutputStream.Close();
		}

		private static string BuildResponseHtml(bool success, string message)
		{
			var color = success ? "#10b981" : "#f87171";
			var title = success ? "Connected!" : "Error";
			return $@"<!DOCTYPE html>
<html>
<head><title>PkgLnk - Unity Editor</title>
<style>
body {{ font-family: sans-serif; background: #022c22; color: #ecfdf5; display: flex; justify-content: center; align-items: center; min-height: 100vh; margin: 0; }}
.card {{ background: #064e3b; border: 1px solid rgba(255,255,255,0.08); border-radius: 12px; padding: 32px; max-width: 400px; text-align: center; }}
h1 {{ color: {color}; }}
p {{ color: #a7f3d0; }}
</style>
</head>
<body><div class='card'><h1>{title}</h1><p>{message}</p><p style='color:#6ee7b7;font-size:13px'>You can close this tab and return to Unity.</p></div></body>
</html>";
		}

		private static int GetAvailablePort()
		{
			var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
			listener.Start();
			var port = ((IPEndPoint)listener.LocalEndpoint).Port;
			listener.Stop();
			return port;
		}
	}
}
