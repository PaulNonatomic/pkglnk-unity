using Nonatomic.PkgLnk.Editor.Api;
using NUnit.Framework;

namespace Tests.EditMode
{
	[TestFixture]
	public class PackageInstallerTests
	{
		[Test]
		public void BuildInstallUrl_BasicSlug_ReturnsCorrectUrl()
		{
			var pkg = new PackageData { slug = "my-package" };
			var url = PackageInstaller.BuildInstallUrl(pkg);
			Assert.AreEqual("https://pkglnk.dev/track/my-package.git", url);
		}

		[Test]
		public void BuildInstallUrl_WithGitPath_IncludesEncodedPath()
		{
			var pkg = new PackageData
			{
				slug = "mono-repo-pkg",
				git_path = "Packages/com.example.pkg"
			};

			var url = PackageInstaller.BuildInstallUrl(pkg);
			Assert.That(url, Does.StartWith("https://pkglnk.dev/track/mono-repo-pkg.git?path="));
			Assert.That(url, Does.Contain("Packages"));
		}

		[Test]
		public void BuildInstallUrl_WithGitRef_IncludesFragment()
		{
			var pkg = new PackageData
			{
				slug = "versioned-pkg",
				git_ref = "v1.0.0"
			};

			var url = PackageInstaller.BuildInstallUrl(pkg);
			Assert.AreEqual("https://pkglnk.dev/track/versioned-pkg.git#v1.0.0", url);
		}

		[Test]
		public void BuildInstallUrl_WithPathAndRef_IncludesBoth()
		{
			var pkg = new PackageData
			{
				slug = "full-pkg",
				git_path = "src/Package",
				git_ref = "main"
			};

			var url = PackageInstaller.BuildInstallUrl(pkg);
			Assert.That(url, Does.StartWith("https://pkglnk.dev/track/full-pkg.git?path="));
			Assert.That(url, Does.EndWith("#main"));
		}

		[Test]
		public void BuildInstallUrl_EmptyPathAndRef_NoQueryOrFragment()
		{
			var pkg = new PackageData
			{
				slug = "simple",
				git_path = "",
				git_ref = ""
			};

			var url = PackageInstaller.BuildInstallUrl(pkg);
			Assert.AreEqual("https://pkglnk.dev/track/simple.git", url);
		}
	}
}
