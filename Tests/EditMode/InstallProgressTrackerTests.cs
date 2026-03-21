using System.Collections.Generic;
using Nonatomic.PkgLnk.Editor.Api;
using NUnit.Framework;

namespace Tests.EditMode
{
	[TestFixture]
	public class InstallProgressTrackerTests
	{
		[Test]
		public void GenerateInstallId_Returns16CharHex()
		{
			var id = InstallProgressTracker.GenerateInstallId();
			Assert.AreEqual(16, id.Length);
			Assert.That(id, Does.Match("^[0-9a-f]{16}$"));
		}

		[Test]
		public void GenerateInstallId_IsUnique()
		{
			var ids = new HashSet<string>();
			for (var i = 0; i < 100; i++)
			{
				ids.Add(InstallProgressTracker.GenerateInstallId());
			}
			Assert.AreEqual(100, ids.Count);
		}

		[Test]
		public void ParsePhase_Resolving()
		{
			var json = "{\"phase\":\"resolving\",\"created_at\":\"2025-01-01\",\"updated_at\":\"2025-01-01\"}";
			Assert.AreEqual(InstallPhase.Resolving, InstallProgressTracker.ParsePhase(json));
		}

		[Test]
		public void ParsePhase_Downloading()
		{
			var json = "{\"phase\":\"downloading\",\"created_at\":\"2025-01-01\",\"updated_at\":\"2025-01-01\"}";
			Assert.AreEqual(InstallPhase.Downloading, InstallProgressTracker.ParsePhase(json));
		}

		[Test]
		public void ParsePhase_Pending()
		{
			var json = "{\"phase\":\"pending\",\"created_at\":\"2025-01-01\",\"updated_at\":\"2025-01-01\"}";
			Assert.AreEqual(InstallPhase.Pending, InstallProgressTracker.ParsePhase(json));
		}

		[Test]
		public void ParsePhase_InvalidJson_ReturnsPending()
		{
			Assert.AreEqual(InstallPhase.Pending, InstallProgressTracker.ParsePhase("{}"));
			Assert.AreEqual(InstallPhase.Pending, InstallProgressTracker.ParsePhase(""));
		}

		[Test]
		public void InstallPhase_OrderingIsCorrect()
		{
			Assert.That(InstallPhase.Pending, Is.LessThan(InstallPhase.Resolving));
			Assert.That(InstallPhase.Resolving, Is.LessThan(InstallPhase.Downloading));
			Assert.That(InstallPhase.Downloading, Is.LessThan(InstallPhase.Importing));
			Assert.That(InstallPhase.Importing, Is.LessThan(InstallPhase.Complete));
		}
	}
}
