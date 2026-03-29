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

			if (IsGifUrl(url))
			{
				LoadGif(url);
			}
			else
			{
				LoadTexture(url);
			}
		}

		private static void LoadTexture(string url)
		{
			UnityWebRequest request;
			UnityWebRequestAsyncOperation operation;

			try
			{
				request = UnityWebRequestTexture.GetTexture(url);
				operation = request.SendWebRequest();
			}
			catch (Exception ex)
			{
				FinishPending(url, null);
				Debug.LogWarning($"[PkgLnk] Failed to load image {url}: {ex.Message}");
				return;
			}

			operation.completed += _ =>
			{
				Texture2D texture = null;

				if (request.result == UnityWebRequest.Result.Success)
				{
					texture = DownloadHandlerTexture.GetContent(request);
				}

				request.Dispose();
				FinishPending(url, texture);
			};
		}

		private static void LoadGif(string url)
		{
			UnityWebRequest request;
			UnityWebRequestAsyncOperation operation;

			try
			{
				request = UnityWebRequest.Get(url);
				operation = request.SendWebRequest();
			}
			catch (Exception ex)
			{
				FinishPending(url, null);
				Debug.LogWarning($"[PkgLnk] Failed to load GIF {url}: {ex.Message}");
				return;
			}

			operation.completed += _ =>
			{
				Texture2D texture = null;

				if (request.result == UnityWebRequest.Result.Success)
				{
					texture = GifDecoder.DecodeFirstFrame(request.downloadHandler.data);
				}

				request.Dispose();
				FinishPending(url, texture);
			};
		}

		private static void FinishPending(string url, Texture2D texture)
		{
			if (texture != null)
			{
				Cache[url] = texture;
			}

			if (!Pending.TryGetValue(url, out var pending)) return;
			Pending.Remove(url);
			foreach (var cb in pending) cb?.Invoke(texture);
		}

		private static bool IsGifUrl(string url)
		{
			var end = url.Length;
			var q = url.IndexOf('?');
			if (q >= 0) end = q;
			var h = url.IndexOf('#');
			if (h >= 0 && h < end) end = h;
			return end >= 4 && url.Substring(end - 4, 4).Equals(".gif", StringComparison.OrdinalIgnoreCase);
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
