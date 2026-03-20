using System;
using System.Collections.Generic;

namespace Nonatomic.PkgLnk.Editor.Api
{
	/// <summary>
	/// Response from GET /api/directory on pkglnk.dev.
	/// The installCounts dictionary is parsed separately since JsonUtility
	/// does not support Dictionary types.
	/// </summary>
	[Serializable]
	public class DirectoryResponse
	{
		public PackageData[] packages = Array.Empty<PackageData>();
		public bool hasMore;
		public int totalCount;

		/// <summary>
		/// Install counts keyed by package ID. Populated by manual JSON parsing.
		/// </summary>
		[NonSerialized]
		public Dictionary<string, int> installCounts = new Dictionary<string, int>();
	}
}
