namespace Nonatomic.PkgLnk.Editor.Utils
{
	/// <summary>
	/// Utility for formatting numbers for display.
	/// </summary>
	public static class FormatUtils
	{
		public static string FormatCount(int count)
		{
			if (count >= 1_000_000) return $"{count / 1_000_000.0:0.#}M";
			if (count >= 1_000) return $"{count / 1_000.0:0.#}k";
			return count.ToString();
		}
	}
}
