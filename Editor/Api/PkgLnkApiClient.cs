using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace Nonatomic.PkgLnk.Editor.Api
{
	/// <summary>
	/// HTTP client for the pkglnk.dev public directory API.
	/// Uses UnityWebRequest with completion callbacks on the main thread.
	/// </summary>
	public static class PkgLnkApiClient
	{
		private const string BaseUrl = "https://pkglnk.dev/api/directory";
		private const string CollectionsUrl = "https://pkglnk.dev/api/collections";
		private const string ApiV1Url = "https://pkglnk.dev/api/v1";
		private const string UserAgent = "pkglnk-unity/0.1";

		/// <summary>
		/// Fetches a page of packages from the directory.
		/// <paramref name="onComplete"/> receives (response, errorMessage).
		/// On success errorMessage is null; on failure response is null.
		/// </summary>
		public static void FetchDirectory(
			string query,
			string topic,
			int page,
			int limit,
			Action<DirectoryResponse, string> onComplete)
		{
			var url = BuildUrl(query, topic, page, limit);
			var request = UnityWebRequest.Get(url);
			request.SetRequestHeader("User-Agent", UserAgent);

			var operation = request.SendWebRequest();
			operation.completed += _ => HandleResponse(request, onComplete);
		}

		private static void HandleResponse(
			UnityWebRequest request,
			Action<DirectoryResponse, string> onComplete)
		{
			if (request.result != UnityWebRequest.Result.Success)
			{
				var error = request.error;
				request.Dispose();
				onComplete?.Invoke(null, error);
				return;
			}

			DirectoryResponse response;

			try
			{
				var json = request.downloadHandler.text;
				response = ParseResponse(json);
			}
			catch (Exception ex)
			{
				onComplete?.Invoke(null, $"Parse error: {ex.Message}");
				return;
			}
			finally
			{
				request.Dispose();
			}

			onComplete?.Invoke(response, null);
		}

		private static DirectoryResponse ParseResponse(string json)
		{
			var response = JsonUtility.FromJson<DirectoryResponse>(json);
			response.installCounts = ParseInstallCounts(json);
			return response;
		}

		/// <summary>
		/// Manually parses the installCounts JSON object since JsonUtility
		/// does not support Dictionary types.
		/// Expected format: "installCounts":{"uuid1":42,"uuid2":7}
		/// </summary>
		internal static Dictionary<string, int> ParseInstallCounts(string json)
		{
			var result = new Dictionary<string, int>();
			var startMarker = "\"installCounts\":{";
			var start = json.IndexOf(startMarker, StringComparison.Ordinal);
			if (start < 0) return result;

			start += startMarker.Length;
			var end = json.IndexOf('}', start);
			if (end < 0) return result;

			var block = json.Substring(start, end - start).Trim();
			if (string.IsNullOrEmpty(block)) return result;

			foreach (var pair in block.Split(','))
			{
				var colonIdx = pair.IndexOf(':');
				if (colonIdx < 0) continue;

				var key = pair.Substring(0, colonIdx).Trim().Trim('"');
				var valueStr = pair.Substring(colonIdx + 1).Trim();

				if (int.TryParse(valueStr, out var val))
				{
					result[key] = val;
				}
			}

			return result;
		}

		/// <summary>
		/// Fetches the user's bookmarked packages. Requires a valid API token.
		/// Response format matches DirectoryResponse (packages + installCounts).
		/// </summary>
		public static void FetchBookmarks(string token, Action<DirectoryResponse, string> onComplete)
		{
			var request = UnityWebRequest.Get($"{ApiV1Url}/bookmarks");
			request.SetRequestHeader("User-Agent", UserAgent);
			request.SetRequestHeader("Authorization", $"Bearer {token}");

			var operation = request.SendWebRequest();
			operation.completed += _ => HandleResponse(request, onComplete);
		}

		/// <summary>
		/// Fetches the user's own packages. Requires a valid API token.
		/// Response format: { packages: [...], total: int }
		/// </summary>
		public static void FetchUserPackages(string token, Action<DirectoryResponse, string> onComplete)
		{
			var request = UnityWebRequest.Get($"{ApiV1Url}/packages");
			request.SetRequestHeader("User-Agent", UserAgent);
			request.SetRequestHeader("Authorization", $"Bearer {token}");

			var operation = request.SendWebRequest();
			operation.completed += _ => HandleResponse(request, onComplete);
		}

		/// <summary>
		/// Toggles a bookmark on/off for a package. Requires a valid API token.
		/// </summary>
		public static void ToggleBookmark(string token, string packageId, Action<bool, string> onComplete)
		{
			var body = $"{{\"packageId\":\"{packageId}\"}}";
			var request = new UnityWebRequest($"{ApiV1Url}/bookmarks", "POST");
			request.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(body));
			request.downloadHandler = new DownloadHandlerBuffer();
			request.SetRequestHeader("Content-Type", "application/json");
			request.SetRequestHeader("User-Agent", UserAgent);
			request.SetRequestHeader("Authorization", $"Bearer {token}");

			var operation = request.SendWebRequest();
			operation.completed += _ =>
			{
				if (request.result != UnityWebRequest.Result.Success)
				{
					var error = request.error;
					request.Dispose();
					onComplete?.Invoke(false, error);
					return;
				}

				request.Dispose();
				onComplete?.Invoke(true, null);
			};
		}

		// ─── Collections ────────────────────────────────────────────────

		/// <summary>
		/// Fetches a page of public collections.
		/// </summary>
		public static void FetchCollections(
			string query,
			string tag,
			int offset,
			int limit,
			Action<CollectionsResponse, string> onComplete)
		{
			var url = $"{CollectionsUrl}?offset={offset}&limit={limit}";

			if (!string.IsNullOrWhiteSpace(query))
			{
				url += $"&q={Uri.EscapeDataString(query)}";
			}

			if (!string.IsNullOrWhiteSpace(tag))
			{
				url += $"&tag={Uri.EscapeDataString(tag)}";
			}

			var request = UnityWebRequest.Get(url);
			request.SetRequestHeader("User-Agent", UserAgent);

			var operation = request.SendWebRequest();
			operation.completed += _ => HandleJsonResponse(request, onComplete);
		}

		/// <summary>
		/// Fetches a single collection by slug, including its packages.
		/// </summary>
		public static void FetchCollection(
			string slug,
			Action<CollectionDetailResponse, string> onComplete)
		{
			var url = $"{CollectionsUrl}/{Uri.EscapeDataString(slug)}";
			var request = UnityWebRequest.Get(url);
			request.SetRequestHeader("User-Agent", UserAgent);

			var operation = request.SendWebRequest();
			operation.completed += _ => HandleJsonResponse(request, onComplete);
		}

		// ─── Authenticated Collection CRUD ─────────────────────────────

		/// <summary>
		/// Fetches the authenticated user's own collections.
		/// </summary>
		public static void FetchMyCollections(string token, Action<CollectionsResponse, string> onComplete)
		{
			var request = UnityWebRequest.Get($"{ApiV1Url}/collections");
			request.SetRequestHeader("User-Agent", UserAgent);
			request.SetRequestHeader("Authorization", $"Bearer {token}");

			var operation = request.SendWebRequest();
			operation.completed += _ => HandleJsonResponse(request, onComplete);
		}

		/// <summary>
		/// Creates a new collection.
		/// </summary>
		public static void CreateCollection(
			string token,
			string slug,
			string name,
			string description,
			Action<CollectionMutationResponse, string> onComplete)
		{
			var body = $"{{\"slug\":\"{EscapeJson(slug)}\",\"name\":\"{EscapeJson(name)}\",\"description\":\"{EscapeJson(description)}\"}}";
			SendAuthenticatedRequest("POST", $"{ApiV1Url}/collections", token, body, onComplete);
		}

		/// <summary>
		/// Updates an existing collection.
		/// </summary>
		public static void UpdateCollection(
			string token,
			string slug,
			string name,
			string description,
			Action<CollectionMutationResponse, string> onComplete)
		{
			var body = $"{{\"name\":\"{EscapeJson(name)}\",\"description\":\"{EscapeJson(description)}\"}}";
			SendAuthenticatedRequest("PUT", $"{ApiV1Url}/collections/{Uri.EscapeDataString(slug)}", token, body, onComplete);
		}

		/// <summary>
		/// Deletes a collection by slug.
		/// </summary>
		public static void DeleteCollection(
			string token,
			string slug,
			Action<CollectionSuccessResponse, string> onComplete)
		{
			var url = $"{ApiV1Url}/collections/{Uri.EscapeDataString(slug)}";
			var request = new UnityWebRequest(url, "DELETE");
			request.downloadHandler = new DownloadHandlerBuffer();
			request.SetRequestHeader("User-Agent", UserAgent);
			request.SetRequestHeader("Authorization", $"Bearer {token}");

			var operation = request.SendWebRequest();
			operation.completed += _ => HandleAuthenticatedResponse(request, onComplete);
		}

		/// <summary>
		/// Adds a package to a collection.
		/// </summary>
		public static void AddPackageToCollection(
			string token,
			string collectionSlug,
			string packageId,
			Action<CollectionSuccessResponse, string> onComplete)
		{
			var body = $"{{\"packageId\":\"{EscapeJson(packageId)}\"}}";
			SendAuthenticatedRequest("POST", $"{ApiV1Url}/collections/{Uri.EscapeDataString(collectionSlug)}/packages", token, body, onComplete);
		}

		/// <summary>
		/// Removes a package from a collection.
		/// </summary>
		public static void RemovePackageFromCollection(
			string token,
			string collectionSlug,
			string packageId,
			Action<CollectionSuccessResponse, string> onComplete)
		{
			var body = $"{{\"packageId\":\"{EscapeJson(packageId)}\"}}";
			var url = $"{ApiV1Url}/collections/{Uri.EscapeDataString(collectionSlug)}/packages";
			var request = new UnityWebRequest(url, "DELETE");
			request.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(body));
			request.downloadHandler = new DownloadHandlerBuffer();
			request.SetRequestHeader("Content-Type", "application/json");
			request.SetRequestHeader("User-Agent", UserAgent);
			request.SetRequestHeader("Authorization", $"Bearer {token}");

			var operation = request.SendWebRequest();
			operation.completed += _ => HandleAuthenticatedResponse(request, onComplete);
		}

		/// <summary>
		/// Checks if a slug is available for a collection.
		/// </summary>
		public static void CheckSlugAvailability(
			string slug,
			Action<bool, string> onComplete)
		{
			var url = $"https://pkglnk.dev/api/check-availability?slug={Uri.EscapeDataString(slug)}&type=collection";
			var request = UnityWebRequest.Get(url);
			request.SetRequestHeader("User-Agent", UserAgent);

			var operation = request.SendWebRequest();
			operation.completed += _ =>
			{
				if (request.result != UnityWebRequest.Result.Success)
				{
					var error = request.error;
					request.Dispose();
					onComplete?.Invoke(false, error);
					return;
				}

				try
				{
					var json = request.downloadHandler.text;
					// Simple parse: look for "available":true or "available":false
					var available = json.Contains("\"available\":true");
					request.Dispose();
					onComplete?.Invoke(available, null);
				}
				catch (Exception ex)
				{
					request.Dispose();
					onComplete?.Invoke(false, ex.Message);
				}
			};
		}

		private static void SendAuthenticatedRequest<T>(
			string method,
			string url,
			string token,
			string jsonBody,
			Action<T, string> onComplete)
		{
			var request = new UnityWebRequest(url, method);
			request.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(jsonBody));
			request.downloadHandler = new DownloadHandlerBuffer();
			request.SetRequestHeader("Content-Type", "application/json");
			request.SetRequestHeader("User-Agent", UserAgent);
			request.SetRequestHeader("Authorization", $"Bearer {token}");

			var operation = request.SendWebRequest();
			operation.completed += _ => HandleAuthenticatedResponse(request, onComplete);
		}

		private static void HandleAuthenticatedResponse<T>(
			UnityWebRequest request,
			Action<T, string> onComplete)
		{
			if (request.result != UnityWebRequest.Result.Success)
			{
				// Include response code for scope/permission detection
				var error = $"{request.responseCode}: {request.error}";

				// Try to extract server error message
				try
				{
					var body = request.downloadHandler?.text;
					if (!string.IsNullOrEmpty(body) && body.Contains("\"message\""))
					{
						var msgStart = body.IndexOf("\"message\":\"", StringComparison.Ordinal);
						if (msgStart >= 0)
						{
							msgStart += 11;
							var msgEnd = body.IndexOf("\"", msgStart, StringComparison.Ordinal);
							if (msgEnd > msgStart)
							{
								error = $"{request.responseCode}: {body.Substring(msgStart, msgEnd - msgStart)}";
							}
						}
					}
				}
				catch
				{
					// Use the default error
				}

				request.Dispose();
				onComplete?.Invoke(default, error);
				return;
			}

			T response;

			try
			{
				var json = request.downloadHandler.text;
				response = JsonUtility.FromJson<T>(json);
			}
			catch (Exception ex)
			{
				onComplete?.Invoke(default, $"Parse error: {ex.Message}");
				return;
			}
			finally
			{
				request.Dispose();
			}

			onComplete?.Invoke(response, null);
		}

		private static string EscapeJson(string s)
		{
			if (string.IsNullOrEmpty(s)) return string.Empty;
			return s.Replace("\\", "\\\\")
				.Replace("\"", "\\\"")
				.Replace("\n", "\\n")
				.Replace("\r", "\\r")
				.Replace("\t", "\\t");
		}

		private static void HandleJsonResponse<T>(
			UnityWebRequest request,
			Action<T, string> onComplete)
		{
			if (request.result != UnityWebRequest.Result.Success)
			{
				var error = request.error;
				request.Dispose();
				onComplete?.Invoke(default, error);
				return;
			}

			T response;

			try
			{
				var json = request.downloadHandler.text;
				response = JsonUtility.FromJson<T>(json);
			}
			catch (Exception ex)
			{
				onComplete?.Invoke(default, $"Parse error: {ex.Message}");
				return;
			}
			finally
			{
				request.Dispose();
			}

			onComplete?.Invoke(response, null);
		}

		// ─── README ─────────────────────────────────────────────────────

		/// <summary>
		/// Fetches the raw README markdown from GitHub for a package.
		/// Only works for GitHub-hosted packages.
		/// </summary>
		public static void FetchReadme(
			string gitOwner,
			string gitRepo,
			Action<string, string> onComplete)
		{
			var url = $"https://api.github.com/repos/{Uri.EscapeDataString(gitOwner)}/{Uri.EscapeDataString(gitRepo)}/readme";
			var request = UnityWebRequest.Get(url);
			request.SetRequestHeader("User-Agent", UserAgent);
			request.SetRequestHeader("Accept", "application/vnd.github.v3.raw");

			var operation = request.SendWebRequest();
			operation.completed += _ =>
			{
				if (request.result != UnityWebRequest.Result.Success)
				{
					var error = $"Error: {request.error}";
					request.Dispose();
					onComplete?.Invoke(null, error);
					return;
				}

				var text = request.downloadHandler.text;
				request.Dispose();
				onComplete?.Invoke(text, null);
			};
		}

		private static string BuildUrl(string query, string topic, int page, int limit)
		{
			var url = $"{BaseUrl}?page={page}&limit={limit}";

			if (!string.IsNullOrWhiteSpace(query))
			{
				url += $"&q={Uri.EscapeDataString(query)}";
			}

			if (!string.IsNullOrWhiteSpace(topic))
			{
				url += $"&topic={Uri.EscapeDataString(topic)}";
			}

			return url;
		}
	}
}
