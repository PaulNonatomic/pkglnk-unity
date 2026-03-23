using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine.UIElements;

namespace Nonatomic.PkgLnk.Editor.Utils
{
	/// <summary>
	/// Converts a markdown string into a tree of UI Toolkit VisualElements.
	/// Supports headings, paragraphs, fenced code blocks, lists, blockquotes,
	/// horizontal rules, images, and inline formatting (bold, italic, code, links).
	/// </summary>
	public static class MarkdownRenderer
	{
		/// <summary>
		/// Renders markdown text into a VisualElement container.
		/// Relative image URLs are resolved against the given GitHub repo.
		/// </summary>
		public static VisualElement Render(string markdown, string gitOwner, string gitRepo, string gitRef)
		{
			var container = new VisualElement();
			container.AddToClassList("readme-content");

			if (string.IsNullOrEmpty(markdown)) return container;

			var branch = string.IsNullOrEmpty(gitRef) ? "main" : gitRef;
			var lines = markdown.Replace("\r\n", "\n").Split('\n');
			var i = 0;

			while (i < lines.Length)
			{
				var line = lines[i];

				// Fenced code block
				if (line.TrimStart().StartsWith("```"))
				{
					i = ParseCodeBlock(lines, i, container);
					continue;
				}

				// HTML comment (skip)
				if (line.TrimStart().StartsWith("<!--"))
				{
					i = SkipHtmlComment(lines, i);
					continue;
				}

				// Blank line
				if (string.IsNullOrWhiteSpace(line))
				{
					i++;
					continue;
				}

				// Horizontal rule
				var trimmed = line.Trim();
				if (IsHorizontalRule(trimmed))
				{
					var hr = new VisualElement();
					hr.AddToClassList("readme-hr");
					container.Add(hr);
					i++;
					continue;
				}

				// Heading
				if (trimmed.StartsWith("#"))
				{
					var level = 0;
					while (level < trimmed.Length && level < 6 && trimmed[level] == '#') level++;
					if (level > 0 && level < trimmed.Length && trimmed[level] == ' ')
					{
						var headingText = trimmed.Substring(level + 1).TrimEnd('#', ' ');
						var heading = new Label(FormatInline(headingText));
						heading.enableRichText = true;
						heading.AddToClassList("readme-heading");
						heading.AddToClassList($"readme-h{level}");
						container.Add(heading);
						i++;
						continue;
					}
				}

				// Image (standalone line)
				if (trimmed.StartsWith("!["))
				{
					var imgMatch = Regex.Match(trimmed, @"^!\[([^\]]*)\]\(([^)]+)\)");
					if (imgMatch.Success)
					{
						var imgUrl = ResolveUrl(imgMatch.Groups[2].Value, gitOwner, gitRepo, branch);
						AddImage(container, imgUrl);
						i++;
						continue;
					}
				}

				// HTML img tag (standalone line)
				if (trimmed.StartsWith("<img ", StringComparison.OrdinalIgnoreCase))
				{
					var srcMatch = Regex.Match(trimmed, @"src=""([^""]+)""", RegexOptions.IgnoreCase);
					if (srcMatch.Success)
					{
						var imgUrl = ResolveUrl(srcMatch.Groups[1].Value, gitOwner, gitRepo, branch);
						AddImage(container, imgUrl);
						i++;
						continue;
					}
				}

				// Blockquote
				if (trimmed.StartsWith(">"))
				{
					i = ParseBlockquote(lines, i, container);
					continue;
				}

				// Unordered list
				if (IsUnorderedListItem(trimmed))
				{
					i = ParseList(lines, i, container, ordered: false, gitOwner, gitRepo, branch);
					continue;
				}

				// Ordered list
				if (IsOrderedListItem(trimmed))
				{
					i = ParseList(lines, i, container, ordered: true, gitOwner, gitRepo, branch);
					continue;
				}

				// Badge/shield line (skip lines that are only badge images/links)
				if (IsBadgeLine(trimmed))
				{
					i++;
					continue;
				}

				// Paragraph — collect contiguous non-blank lines
				i = ParseParagraph(lines, i, container, gitOwner, gitRepo, branch);
			}

			return container;
		}

		private static int ParseCodeBlock(string[] lines, int start, VisualElement container)
		{
			var sb = new StringBuilder();
			var i = start + 1;

			while (i < lines.Length)
			{
				if (lines[i].TrimStart().StartsWith("```"))
				{
					i++;
					break;
				}

				if (sb.Length > 0) sb.Append('\n');
				sb.Append(lines[i]);
				i++;
			}

			var codeLabel = new Label(sb.ToString());
			codeLabel.enableRichText = false;
			codeLabel.AddToClassList("readme-code-block");
			container.Add(codeLabel);
			return i;
		}

		private static int SkipHtmlComment(string[] lines, int start)
		{
			var i = start;
			while (i < lines.Length)
			{
				if (lines[i].Contains("-->"))
				{
					i++;
					break;
				}

				i++;
			}

			return i;
		}

		private static int ParseBlockquote(string[] lines, int start, VisualElement container)
		{
			var sb = new StringBuilder();
			var i = start;

			while (i < lines.Length)
			{
				var line = lines[i].TrimStart();
				if (!line.StartsWith(">") && !string.IsNullOrWhiteSpace(lines[i])) break;
				if (line.StartsWith(">"))
				{
					line = line.Substring(1).TrimStart();
				}

				if (sb.Length > 0) sb.Append(' ');
				sb.Append(line);
				i++;
			}

			var quote = new Label(FormatInline(sb.ToString()));
			quote.enableRichText = true;
			quote.AddToClassList("readme-blockquote");
			container.Add(quote);
			return i;
		}

		private static int ParseList(string[] lines, int start, VisualElement container, bool ordered,
			string gitOwner, string gitRepo, string branch)
		{
			var listContainer = new VisualElement();
			listContainer.AddToClassList("readme-list");
			var i = start;
			var itemNumber = 1;

			while (i < lines.Length)
			{
				var line = lines[i];
				var trimmed = line.TrimStart();

				if (string.IsNullOrWhiteSpace(line))
				{
					// Check if next line continues the list
					if (i + 1 < lines.Length)
					{
						var next = lines[i + 1].TrimStart();
						if ((ordered && IsOrderedListItem(next)) || (!ordered && IsUnorderedListItem(next)))
						{
							i++;
							continue;
						}
					}

					break;
				}

				if (ordered && IsOrderedListItem(trimmed))
				{
					var dotIdx = trimmed.IndexOf('.');
					var text = trimmed.Substring(dotIdx + 1).TrimStart();
					var item = new Label(FormatInline($"{itemNumber}. {text}"));
					item.enableRichText = true;
					item.AddToClassList("readme-list-item");
					listContainer.Add(item);
					itemNumber++;
					i++;
				}
				else if (!ordered && IsUnorderedListItem(trimmed))
				{
					var text = trimmed.Substring(2);
					var item = new Label(FormatInline($"\u2022 {text}"));
					item.enableRichText = true;
					item.AddToClassList("readme-list-item");
					listContainer.Add(item);
					i++;
				}
				else
				{
					break;
				}
			}

			container.Add(listContainer);
			return i;
		}

		private static int ParseParagraph(string[] lines, int start, VisualElement container,
			string gitOwner, string gitRepo, string branch)
		{
			var sb = new StringBuilder();
			var i = start;

			while (i < lines.Length)
			{
				var line = lines[i];
				if (string.IsNullOrWhiteSpace(line)) break;
				if (line.TrimStart().StartsWith("#")) break;
				if (line.TrimStart().StartsWith("```")) break;
				if (line.TrimStart().StartsWith(">")) break;
				if (IsHorizontalRule(line.Trim())) break;
				if (IsUnorderedListItem(line.TrimStart())) break;
				if (IsOrderedListItem(line.TrimStart())) break;
				if (line.TrimStart().StartsWith("![")) break;
				if (line.TrimStart().StartsWith("<img ", StringComparison.OrdinalIgnoreCase)) break;
				if (line.TrimStart().StartsWith("<!--")) break;

				if (sb.Length > 0) sb.Append(' ');
				sb.Append(line.Trim());
				i++;
			}

			if (sb.Length > 0)
			{
				var text = sb.ToString();

				// Check for inline images within the paragraph
				if (text.Contains("![") || Regex.IsMatch(text, @"<img\s", RegexOptions.IgnoreCase))
				{
					// Extract and render inline images separately
					var parts = Regex.Split(text, @"(!\[[^\]]*\]\([^)]+\)|<img\s[^>]+>)");
					foreach (var part in parts)
					{
						if (string.IsNullOrWhiteSpace(part)) continue;

						var imgMd = Regex.Match(part, @"^!\[([^\]]*)\]\(([^)]+)\)$");
						if (imgMd.Success)
						{
							var imgUrl = ResolveUrl(imgMd.Groups[2].Value, gitOwner, gitRepo, branch);
							AddImage(container, imgUrl);
							continue;
						}

						var imgHtml = Regex.Match(part, @"src=""([^""]+)""", RegexOptions.IgnoreCase);
						if (part.TrimStart().StartsWith("<img", StringComparison.OrdinalIgnoreCase) && imgHtml.Success)
						{
							var imgUrl = ResolveUrl(imgHtml.Groups[1].Value, gitOwner, gitRepo, branch);
							AddImage(container, imgUrl);
							continue;
						}

						var para = new Label(FormatInline(part.Trim()));
						para.enableRichText = true;
						para.AddToClassList("readme-paragraph");
						container.Add(para);
					}
				}
				else
				{
					var para = new Label(FormatInline(text));
					para.enableRichText = true;
					para.AddToClassList("readme-paragraph");
					container.Add(para);
				}
			}

			return i;
		}

		/// <summary>
		/// Converts inline markdown formatting to Unity rich text tags.
		/// </summary>
		private static string FormatInline(string text)
		{
			if (string.IsNullOrEmpty(text)) return text;

			// Escape existing rich text tags from the markdown content
			text = text.Replace("<", "\u200B<\u200B");

			// Restore known tags we'll generate
			// (We process our tags after escaping)

			// Inline code: `code` → <b><color=#8a8a8a>code</color></b>
			text = Regex.Replace(text, @"`([^`]+)`", "<b><color=#8a8a8a>$1</color></b>");

			// Bold + italic: ***text*** or ___text___
			text = Regex.Replace(text, @"\*\*\*(.+?)\*\*\*", "<b><i>$1</i></b>");
			text = Regex.Replace(text, @"___(.+?)___", "<b><i>$1</i></b>");

			// Bold: **text** or __text__
			text = Regex.Replace(text, @"\*\*(.+?)\*\*", "<b>$1</b>");
			text = Regex.Replace(text, @"__(.+?)__", "<b>$1</b>");

			// Italic: *text* or _text_ (but not inside words for underscores)
			text = Regex.Replace(text, @"(?<!\w)\*([^*]+)\*(?!\w)", "<i>$1</i>");
			text = Regex.Replace(text, @"(?<!\w)_([^_]+)_(?!\w)", "<i>$1</i>");

			// Links: [text](url) → colored text
			text = Regex.Replace(text, @"\[([^\]]+)\]\([^)]+\)", "<color=#10b981>$1</color>");

			return text;
		}

		private static void AddImage(VisualElement container, string url)
		{
			if (string.IsNullOrEmpty(url)) return;

			// Skip badge/shield images
			if (IsBadgeUrl(url)) return;

			var imageContainer = new VisualElement();
			imageContainer.AddToClassList("readme-image-container");

			var imageElement = new VisualElement();
			imageElement.AddToClassList("readme-image");
			imageContainer.Add(imageElement);

			ImageLoader.Load(url, texture =>
			{
				if (texture == null) return;
				imageElement.style.backgroundImage = new StyleBackground(texture);

				// Set aspect ratio based on loaded texture
				var maxWidth = 600f;
				var scale = texture.width > maxWidth ? maxWidth / texture.width : 1f;
				imageElement.style.width = texture.width * scale;
				imageElement.style.height = texture.height * scale;
			});

			container.Add(imageContainer);
		}

		private static string ResolveUrl(string url, string gitOwner, string gitRepo, string branch)
		{
			if (string.IsNullOrEmpty(url)) return url;

			// Already absolute
			if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
				url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
			{
				return url;
			}

			// Strip leading ./
			if (url.StartsWith("./")) url = url.Substring(2);

			// Build raw GitHub URL
			return $"https://raw.githubusercontent.com/{gitOwner}/{gitRepo}/{branch}/{url}";
		}

		private static bool IsHorizontalRule(string trimmed)
		{
			if (trimmed.Length < 3) return false;
			var stripped = trimmed.Replace(" ", "");
			if (stripped.Length < 3) return false;
			var ch = stripped[0];
			if (ch != '-' && ch != '*' && ch != '_') return false;
			foreach (var c in stripped)
			{
				if (c != ch) return false;
			}

			return true;
		}

		private static bool IsUnorderedListItem(string trimmed)
		{
			return (trimmed.StartsWith("- ") || trimmed.StartsWith("* ") || trimmed.StartsWith("+ "));
		}

		private static bool IsOrderedListItem(string trimmed)
		{
			var dotIdx = trimmed.IndexOf('.');
			if (dotIdx <= 0 || dotIdx >= trimmed.Length - 1) return false;
			if (trimmed[dotIdx + 1] != ' ') return false;
			for (var j = 0; j < dotIdx; j++)
			{
				if (!char.IsDigit(trimmed[j])) return false;
			}

			return true;
		}

		private static bool IsBadgeLine(string trimmed)
		{
			// Lines that are only badge images wrapped in links
			if (!trimmed.StartsWith("[![")) return false;
			// Must end with a closing link paren
			return trimmed.EndsWith(")");
		}

		private static bool IsBadgeUrl(string url)
		{
			if (string.IsNullOrEmpty(url)) return true;
			var lower = url.ToLowerInvariant();
			return lower.Contains("shields.io") ||
				   lower.Contains("badge") ||
				   lower.Contains("travis-ci") ||
				   lower.Contains("codecov.io") ||
				   lower.Contains("github.com/workflows") ||
				   lower.Contains("img.shields.io");
		}
	}
}
