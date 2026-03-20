using System;
using Nonatomic.PkgLnk.Editor.Utils;
using NUnit.Framework;

namespace Tests.EditMode
{
	[TestFixture]
	public class DateUtilsTests
	{
		[Test]
		public void FormatRelative_Today_ReturnsToday()
		{
			var now = DateTime.UtcNow.ToString("O");
			Assert.AreEqual("Today", DateUtils.FormatRelative(now));
		}

		[Test]
		public void FormatRelative_Yesterday_ReturnsYesterday()
		{
			var yesterday = DateTime.UtcNow.AddDays(-1).ToString("O");
			Assert.AreEqual("Yesterday", DateUtils.FormatRelative(yesterday));
		}

		[Test]
		public void FormatRelative_ThreeDaysAgo_ReturnsDaysAgo()
		{
			var date = DateTime.UtcNow.AddDays(-3).ToString("O");
			Assert.AreEqual("3 days ago", DateUtils.FormatRelative(date));
		}

		[Test]
		public void FormatRelative_TwoWeeksAgo_ReturnsWeeksAgo()
		{
			var date = DateTime.UtcNow.AddDays(-14).ToString("O");
			Assert.AreEqual("2 weeks ago", DateUtils.FormatRelative(date));
		}

		[Test]
		public void FormatRelative_ThreeMonthsAgo_ReturnsMonthsAgo()
		{
			var date = DateTime.UtcNow.AddDays(-90).ToString("O");
			Assert.AreEqual("3 months ago", DateUtils.FormatRelative(date));
		}

		[Test]
		public void FormatRelative_OneYearAgo_ReturnsYearAgo()
		{
			var date = DateTime.UtcNow.AddDays(-365).ToString("O");
			Assert.AreEqual("1 year ago", DateUtils.FormatRelative(date));
		}

		[Test]
		public void FormatRelative_NullOrEmpty_ReturnsEmpty()
		{
			Assert.AreEqual(string.Empty, DateUtils.FormatRelative(null));
			Assert.AreEqual(string.Empty, DateUtils.FormatRelative(string.Empty));
		}

		[Test]
		public void FormatRelative_InvalidDate_ReturnsOriginalString()
		{
			Assert.AreEqual("not-a-date", DateUtils.FormatRelative("not-a-date"));
		}
	}
}
