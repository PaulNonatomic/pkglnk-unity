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
		private static readonly HashSet<string> InFlight = new HashSet<string>();

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

			if (InFlight.Contains(url)) return;
			InFlight.Add(url);

			UnityWebRequest request;
			UnityWebRequestAsyncOperation operation;

			try
			{
				request = UnityWebRequestTexture.GetTexture(url);
				operation = request.SendWebRequest();
			}
			catch (Exception ex)
			{
				InFlight.Remove(url);
				Debug.LogWarning($"[PkgLnk] Failed to load image {url}: {ex.Message}");
				onLoaded?.Invoke(null);
				return;
			}
			operation.completed += _ =>
			{
				InFlight.Remove(url);

				if (request.result == UnityWebRequest.Result.Success)
				{
					var texture = DownloadHandlerTexture.GetContent(request);
					if (texture != null)
					{
						Cache[url] = texture;
						onLoaded?.Invoke(texture);
					}
					else
					{
						onLoaded?.Invoke(null);
					}
				}
				else
				{
					onLoaded?.Invoke(null);
				}

				request.Dispose();
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
