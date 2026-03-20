using Nonatomic.PkgLnk.Editor.Api;
using NUnit.Framework;
using UnityEngine;

namespace Tests.EditMode
{
	[TestFixture]
	public class DirectoryResponseParsingTests
	{
		[Test]
		public void ParseInstallCounts_ValidJson_ReturnsDictionary()
		{
			var json = "{\"packages\":[],\"installCounts\":{\"abc-123\":42,\"def-456\":7},\"hasMore\":false,\"totalCount\":0}";
			var counts = PkgLnkApiClient.ParseInstallCounts(json);

			Assert.AreEqual(2, counts.Count);
			Assert.AreEqual(42, counts["abc-123"]);
			Assert.AreEqual(7, counts["def-456"]);
		}

		[Test]
		public void ParseInstallCounts_EmptyObject_ReturnsEmptyDictionary()
		{
			var json = "{\"packages\":[],\"installCounts\":{},\"hasMore\":false,\"totalCount\":0}";
			var counts = PkgLnkApiClient.ParseInstallCounts(json);

			Assert.AreEqual(0, counts.Count);
		}

		[Test]
		public void ParseInstallCounts_MissingField_ReturnsEmptyDictionary()
		{
			var json = "{\"packages\":[],\"hasMore\":false,\"totalCount\":0}";
			var counts = PkgLnkApiClient.ParseInstallCounts(json);

			Assert.AreEqual(0, counts.Count);
		}

		[Test]
		public void ParseInstallCounts_SingleEntry_ReturnsCorrectValue()
		{
			var json = "{\"installCounts\":{\"pkg-id-1\":100}}";
			var counts = PkgLnkApiClient.ParseInstallCounts(json);

			Assert.AreEqual(1, counts.Count);
			Assert.AreEqual(100, counts["pkg-id-1"]);
		}

		[Test]
		public void DirectoryResponse_Deserialization_ParsesBasicFields()
		{
			var json = "{\"packages\":[{\"id\":\"123\",\"slug\":\"test-pkg\",\"display_name\":\"Test Package\",\"git_platform\":\"github\",\"git_owner\":\"owner\",\"git_repo\":\"repo\",\"description\":\"A test\",\"topics\":[\"ui\",\"tools\"],\"github_stars\":5,\"updated_at\":\"2026-01-01T00:00:00Z\"}],\"hasMore\":true,\"totalCount\":50}";
			var response = JsonUtility.FromJson<DirectoryResponse>(json);

			Assert.IsTrue(response.hasMore);
			Assert.AreEqual(50, response.totalCount);
			Assert.AreEqual(1, response.packages.Length);

			var pkg = response.packages[0];
			Assert.AreEqual("123", pkg.id);
			Assert.AreEqual("test-pkg", pkg.slug);
			Assert.AreEqual("Test Package", pkg.display_name);
			Assert.AreEqual("github", pkg.git_platform);
			Assert.AreEqual("owner", pkg.git_owner);
			Assert.AreEqual("repo", pkg.git_repo);
			Assert.AreEqual("A test", pkg.description);
			Assert.AreEqual(5, pkg.github_stars);
			Assert.AreEqual(2, pkg.topics.Length);
			Assert.AreEqual("ui", pkg.topics[0]);
		}
	}
}
