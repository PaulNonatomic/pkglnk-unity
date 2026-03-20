using System;
using System.Globalization;

namespace Nonatomic.PkgLnk.Editor.Utils
{
	/// <summary>
	/// Utility for formatting dates as relative time strings.
	/// </summary>
	public static class DateUtils
	{
		public static string FormatRelative(string isoDateString)
		{
			if (string.IsNullOrEmpty(isoDateString)) return string.Empty;

			if (!DateTime.TryParse(
				    isoDateString,
				    CultureInfo.InvariantCulture,
				    DateTimeStyles.RoundtripKind,
				    out var date))
			{
				return isoDateString;
			}

			var span = DateTime.UtcNow - date.ToUniversalTime();
			var days = (int)span.TotalDays;

			if (days < 0) return "Just now";
			if (days == 0) return "Today";
			if (days == 1) return "Yesterday";
			if (days < 7) return $"{days} days ago";
			if (days < 30) return $"{days / 7} week{(days / 7 == 1 ? "" : "s")} ago";
			if (days < 365) return $"{days / 30} month{(days / 30 == 1 ? "" : "s")} ago";
			return $"{days / 365} year{(days / 365 == 1 ? "" : "s")} ago";
		}
	}
}
