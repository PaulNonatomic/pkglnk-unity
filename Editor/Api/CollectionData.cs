using System;

namespace Nonatomic.PkgLnk.Editor.Api
{
	/// <summary>
	/// Mirrors the CollectionWithCount type returned by the pkglnk.dev collections API.
	/// </summary>
	[Serializable]
	public class CollectionData
	{
		public string id = string.Empty;
		public string slug = string.Empty;
		public string name = string.Empty;
		public string description = string.Empty;
		public string[] tags = Array.Empty<string>();
		public int package_count;
		public string owner_username = string.Empty;
		public string owner_avatar_url = string.Empty;
		public string created_at = string.Empty;
		public string updated_at = string.Empty;
	}

	/// <summary>
	/// Response from GET /api/collections on pkglnk.dev.
	/// </summary>
	[Serializable]
	public class CollectionsResponse
	{
		public CollectionData[] collections = Array.Empty<CollectionData>();
		public int totalCount;
	}

	/// <summary>
	/// Response from GET /api/collections/{slug} on pkglnk.dev.
	/// </summary>
	[Serializable]
	public class CollectionDetailResponse
	{
		public CollectionData collection;
		public PackageData[] packages = Array.Empty<PackageData>();
	}
}
