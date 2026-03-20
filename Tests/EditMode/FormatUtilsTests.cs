using Nonatomic.PkgLnk.Editor.Utils;
using NUnit.Framework;

namespace Tests.EditMode
{
	[TestFixture]
	public class FormatUtilsTests
	{
		[Test]
		public void FormatCount_Zero_ReturnsZero()
		{
			Assert.AreEqual("0", FormatUtils.FormatCount(0));
		}

		[Test]
		public void FormatCount_Under1000_ReturnsExactNumber()
		{
			Assert.AreEqual("999", FormatUtils.FormatCount(999));
			Assert.AreEqual("42", FormatUtils.FormatCount(42));
		}

		[Test]
		public void FormatCount_Thousands_ReturnsK()
		{
			Assert.AreEqual("1k", FormatUtils.FormatCount(1000));
			Assert.AreEqual("1.5k", FormatUtils.FormatCount(1500));
			Assert.AreEqual("10k", FormatUtils.FormatCount(10000));
		}

		[Test]
		public void FormatCount_Millions_ReturnsM()
		{
			Assert.AreEqual("1M", FormatUtils.FormatCount(1000000));
			Assert.AreEqual("2.5M", FormatUtils.FormatCount(2500000));
		}
	}
}
