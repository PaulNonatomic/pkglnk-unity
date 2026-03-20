using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace Nonatomic.PkgLnk.Editor.Utils
{
	/// <summary>
	/// Async texture loader with in-memory caching for card images and avatars.
	/// </summary>
	public static class ImageLoader
	{
		private static readonly Dictionary<string, Texture2D> Cache = new Dictionary<string, Texture2D>();
		private static readonly Dictionary<string, List<Action<Texture2D>>> Pending = new Dictionary<string, List<Action<Texture2D>>>();

		/// <summary>
		/// Loads a texture from a URL. Returns cached texture immediately if available.
		/// The callback fires on the main thread.
		/// </summary>
		public static void Load(string url, Action<Texture2D> onLoaded)
		{
			if (string.IsNullOrEmpty(url))
			{
				onLoaded?.Invoke(null);
				return;
			}

			// Upgrade insecure URLs — Unity blocks HTTP by default
			if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
			{
				url = "https://" + url.Substring(7);
			}

			if (Cache.TryGetValue(url, out var cached))
			{
				onLoaded?.Invoke(cached);
				return;
			}

			// If already in flight, queue the callback for when it completes
			if (Pending.TryGetValue(url, out var callbacks))
			{
				callbacks.Add(onLoaded);
				return;
			}

			Pending[url] = new List<Action<Texture2D>> { onLoaded };

			UnityWebRequest request;
			UnityWebRequestAsyncOperation operation;

			try
			{
				request = UnityWebRequestTexture.GetTexture(url);
				operation = request.SendWebRequest();
			}
			catch (Exception ex)
			{
				var pending = Pending[url];
				Pending.Remove(url);
				Debug.LogWarning($"[PkgLnk] Failed to load image {url}: {ex.Message}");
				foreach (var cb in pending) cb?.Invoke(null);
				return;
			}

			var capturedUrl = url;
			operation.completed += _ =>
			{
				Texture2D texture = null;

				if (request.result == UnityWebRequest.Result.Success)
				{
					texture = DownloadHandlerTexture.GetContent(request);
					if (texture != null)
					{
						Cache[capturedUrl] = texture;
					}
				}

				request.Dispose();

				if (Pending.TryGetValue(capturedUrl, out var pending))
				{
					Pending.Remove(capturedUrl);
					foreach (var cb in pending) cb?.Invoke(texture);
				}
			};
		}

		/// <summary>Clears the entire image cache.</summary>
		public static void ClearCache()
		{
			foreach (var texture in Cache.Values)
			{
				if (texture != null)
				{
					UnityEngine.Object.DestroyImmediate(texture);
				}
			}

			Cache.Clear();
		}
	}
}
