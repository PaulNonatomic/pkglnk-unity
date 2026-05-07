using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace Nonatomic.PkgLnk.Editor.Utils
{
	/// <summary>
	/// Async texture loader with bounded LRU caching for card images and avatars.
	///
	/// Cached textures are flagged HideAndDontSave|DontUnloadUnusedAsset so a
	/// Resources.UnloadUnusedAssets() sweep (triggered by play-mode toggles,
	/// asset imports, scene loads, etc.) doesn't destroy them while the
	/// dictionary still holds references — which previously caused images to
	/// "disappear" intermittently.
	///
	/// Cache is cleared before each assembly reload so post-reload lookups
	/// don't return stale references to destroyed UnityEngine.Objects.
	/// </summary>
	public static class ImageLoader
	{
		private const int MaxCacheSize = 256;

		private static readonly Dictionary<string, Texture2D> Cache = new Dictionary<string, Texture2D>();
		private static readonly LinkedList<string> CacheOrder = new LinkedList<string>();
		private static readonly Dictionary<string, List<Action<Texture2D>>> Pending = new Dictionary<string, List<Action<Texture2D>>>();

		[InitializeOnLoadMethod]
		private static void RegisterReloadHandler()
		{
			AssemblyReloadEvents.beforeAssemblyReload -= ClearCache;
			AssemblyReloadEvents.beforeAssemblyReload += ClearCache;
		}

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
				if (cached != null)
				{
					// LRU bump
					CacheOrder.Remove(url);
					CacheOrder.AddLast(url);
					onLoaded?.Invoke(cached);
					return;
				}

				// Stale entry — texture was destroyed despite our hideFlags. Drop and re-fetch.
				Cache.Remove(url);
				CacheOrder.Remove(url);
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
				// Prevent Resources.UnloadUnusedAssets() and serialization passes
				// from destroying our cached textures.
				texture.hideFlags = HideFlags.HideAndDontSave | HideFlags.DontUnloadUnusedAsset;
				AddToCache(url, texture);
			}

			if (!Pending.TryGetValue(url, out var pending)) return;
			Pending.Remove(url);
			foreach (var cb in pending) cb?.Invoke(texture);
		}

		private static void AddToCache(string url, Texture2D texture)
		{
			if (Cache.ContainsKey(url))
			{
				// Refresh existing entry to most-recently-used.
				CacheOrder.Remove(url);
				CacheOrder.AddLast(url);
				Cache[url] = texture;
				return;
			}

			Cache[url] = texture;
			CacheOrder.AddLast(url);

			while (CacheOrder.Count > MaxCacheSize)
			{
				var oldest = CacheOrder.First;
				if (oldest == null) break;
				CacheOrder.RemoveFirst();
				if (Cache.TryGetValue(oldest.Value, out var evicted))
				{
					Cache.Remove(oldest.Value);
					if (evicted != null) UnityEngine.Object.DestroyImmediate(evicted);
				}
			}
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

		/// <summary>Clears the entire image cache and destroys every cached texture.</summary>
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
			CacheOrder.Clear();
		}
	}
}
