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
			operation.completed += _ => HandleUserPackagesResponse(request, onComplete);
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

		private static void HandleUserPackagesResponse(
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
				// /api/v1/packages returns { packages: [{...total_installs}], total }
				// We map it to DirectoryResponse format for consistency
				response = JsonUtility.FromJson<DirectoryResponse>(json);
				response.installCounts = new Dictionary<string, int>();
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
