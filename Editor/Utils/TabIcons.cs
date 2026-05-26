using UnityEditor;
using UnityEngine;

namespace Nonatomic.PkgLnk.Editor.Utils
{
	/// <summary>
	/// Generates simple 14x14 white-on-transparent icon textures at runtime
	/// for the tab bar, matching the pkglnk.dev mobile navigation icons.
	/// </summary>
	public static class TabIcons
	{
		private static Texture2D _compass;
		private static Texture2D _folder;
		private static Texture2D _grid;
		private static Texture2D _sun;
		private static Texture2D _moon;
		private static Texture2D _github;
		private static Texture2D _gitlab;
		private static Texture2D _bitbucket;

		public static Texture2D Compass => _compass ??= Generate(CompassBitmap);
		public static Texture2D Folder => _folder ??= Generate(FolderBitmap);
		public static Texture2D Grid => _grid ??= Generate(GridBitmap);
		public static Texture2D Sun => _sun ??= Generate(SunBitmap);
		public static Texture2D Moon => _moon ??= Generate(MoonBitmap);
		public static Texture2D GitHub => _github ??= LoadAsset("source-icon-github.png");
		public static Texture2D GitLab => _gitlab ??= LoadAsset("source-icon-gitlab.png");
		public static Texture2D Bitbucket => _bitbucket ??= Generate(BitbucketBitmap);

		public static Texture2D GetPlatformIcon(string platform)
		{
			return platform switch
			{
				"gitlab" => GitLab,
				"bitbucket" => Bitbucket,
				_ => GitHub
			};
		}

		// Compass icon (Directory) — circle with diamond, matches pkglnk.dev desktop nav
		private static readonly string[] CompassBitmap =
		{
			"....######....",
			"...#......#...",
			"..#........#..",
			".#....##....#.",
			"#....#..#....#",
			"#...#....#...#",
			"#..#......#..#",
			"#...#....#...#",
			"#....#..#....#",
			".#....##....#.",
			"..#........#..",
			"...#......#...",
			"....######....",
			"..............",
		};

		// Folder outline (Collections)
		private static readonly string[] FolderBitmap =
		{
			"..............",
			"..............",
			".#####........",
			"##...########.",
			"#...........#.",
			"#...........#.",
			"#...........#.",
			"#...........#.",
			"#...........#.",
			"#...........#.",
			"#...........#.",
			"#############.",
			"..............",
			"..............",
		};

		// 2x2 grid of squares (My Packages)
		private static readonly string[] GridBitmap =
		{
			"..............",
			".#####.#####..",
			".#...#.#...#..",
			".#...#.#...#..",
			".#...#.#...#..",
			".#####.#####..",
			"..............",
			".#####.#####..",
			".#...#.#...#..",
			".#...#.#...#..",
			".#...#.#...#..",
			".#####.#####..",
			"..............",
			"..............",
		};

		// Sun icon (light mode indicator) — circle with rays
		private static readonly string[] SunBitmap =
		{
			"......#.......",
			"..#...#...#...",
			"...#..#..#....",
			"......#.......",
			"...#######....",
			"..##.....##...",
			".##.......##..",
			"###.......###.",
			".##.......##..",
			"..##.....##...",
			"...#######....",
			"......#.......",
			"...#..#..#....",
			"..#...#...#...",
		};

		// Moon icon (dark mode indicator) — crescent
		private static readonly string[] MoonBitmap =
		{
			"..............",
			"....######....",
			"...##....##...",
			"..##......#...",
			".##.......#...",
			".#........#...",
			"##.......##...",
			"##.......##...",
			".#........#...",
			".##.......#...",
			"..##......#...",
			"...##....##...",
			"....######....",
			"..............",
		};

		// Bitbucket bucket
		private static readonly string[] BitbucketBitmap =
		{
			"..............",
			".############.",
			".#..........#.",
			"..#........#..",
			"..#........#..",
			"..#........#..",
			"...#......#...",
			"...#......#...",
			"...#......#...",
			"....#....#....",
			"....#....#....",
			".....####.....",
			"......##......",
			"..............",
		};

		private static Texture2D LoadAsset(string filename)
		{
			return AssetDatabase.LoadAssetAtPath<Texture2D>(
				$"Packages/com.nonatomic.pkglnk/Editor/Icons/{filename}");
		}

		private static Texture2D Generate(string[] bitmap)
		{
			var size = bitmap.Length;
			var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
			{
				filterMode = FilterMode.Bilinear,
				wrapMode = TextureWrapMode.Clamp
			};

			var white = Color.white;
			var clear = new Color(0, 0, 0, 0);

			for (var y = 0; y < size; y++)
			{
				var row = bitmap[size - 1 - y]; // Flip Y (Texture2D is bottom-up)
				for (var x = 0; x < size && x < row.Length; x++)
				{
					tex.SetPixel(x, y, row[x] == '#' ? white : clear);
				}
			}

			tex.Apply();
			return tex;
		}
	}
}
