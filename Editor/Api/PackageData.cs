using System;

namespace Nonatomic.PkgLnk.Editor.Api
{
	/// <summary>
	/// Mirrors the Package row returned by the pkglnk.dev directory API.
	/// </summary>
	[Serializable]
	public class PackageData
	{
		public string id = string.Empty;
		public string slug = string.Empty;
		public string short_code = string.Empty;
		public string display_name = string.Empty;
		public string git_platform = string.Empty;
		public string git_owner = string.Empty;
		public string git_repo = string.Empty;
		public string git_path = string.Empty;
		public string git_ref = string.Empty;
		public string description = string.Empty;
		public string package_json_name = string.Empty;
		public string[] topics = Array.Empty<string>();
		public int github_stars;
		public string card_image_url = string.Empty;
		public string card_image_png_url = string.Empty;
		public string updated_at = string.Empty;
		public string created_at = string.Empty;
		public bool is_private;
	}
}
