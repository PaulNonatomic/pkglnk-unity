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
	/// horizontal rules, images, inline formatting, and common HTML elements.
	/// </summary>
	public static class MarkdownRenderer
	{
		private static readonly Regex HtmlBlockStartRegex = new Regex(
			@"^<(pre|table|details|blockquote|div|p|ul|ol|section|figure|nav|header|footer|main|article|aside)[\s>/]",
			RegexOptions.IgnoreCase);

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

				var trimmed = line.Trim();

				// Horizontal rule (markdown)
				if (IsHorizontalRule(trimmed))
				{
					var hr = new VisualElement();
					hr.AddToClassList("readme-hr");
					container.Add(hr);
					i++;
					continue;
				}

				// HTML <hr> (self-closing)
				if (Regex.IsMatch(trimmed, @"^<hr\s*/?\s*>?$", RegexOptions.IgnoreCase))
				{
					var hr = new VisualElement();
					hr.AddToClassList("readme-hr");
					container.Add(hr);
					i++;
					continue;
				}

				// Heading (markdown)
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

				// HTML heading <h1>-<h6> (single line)
				var htmlH = Regex.Match(trimmed, @"^<h([1-6])[^>]*>(.*?)</h\1\s*>$", RegexOptions.IgnoreCase);
				if (htmlH.Success)
				{
					var level = int.Parse(htmlH.Groups[1].Value);
					var headingText = StripHtmlTags(htmlH.Groups[2].Value);
					var heading = new Label(FormatInline(headingText));
					heading.enableRichText = true;
					heading.AddToClassList("readme-heading");
					heading.AddToClassList($"readme-h{level}");
					container.Add(heading);
					i++;
					continue;
				}

				// Image (standalone markdown line)
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
				if (trimmed.StartsWith("<img ", StringComparison.OrdinalIgnoreCase) ||
					trimmed.StartsWith("<img>", StringComparison.OrdinalIgnoreCase))
				{
					var srcMatch = Regex.Match(trimmed, @"src=[""']([^""']+)[""']", RegexOptions.IgnoreCase);
					if (srcMatch.Success)
					{
						var imgUrl = ResolveUrl(srcMatch.Groups[1].Value, gitOwner, gitRepo, branch);
						AddImage(container, imgUrl);
						i++;
						continue;
					}
				}

				// HTML block elements (pre, table, details, div, p, ul, ol, etc.)
				if (HtmlBlockStartRegex.IsMatch(trimmed))
				{
					i = ParseHtmlBlock(lines, i, container, gitOwner, gitRepo, branch);
					continue;
				}

				// Blockquote (markdown)
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

		// ─── Block Parsers ───────────────────────────────────────────

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
				var trimmedLine = line.TrimStart();
				if (string.IsNullOrWhiteSpace(line)) break;
				if (trimmedLine.StartsWith("#")) break;
				if (trimmedLine.StartsWith("```")) break;
				if (trimmedLine.StartsWith(">")) break;
				if (IsHorizontalRule(line.Trim())) break;
				if (IsUnorderedListItem(trimmedLine)) break;
				if (IsOrderedListItem(trimmedLine)) break;
				if (trimmedLine.StartsWith("![")) break;
				if (trimmedLine.StartsWith("<img ", StringComparison.OrdinalIgnoreCase)) break;
				if (trimmedLine.StartsWith("<!--")) break;
				if (HtmlBlockStartRegex.IsMatch(trimmedLine)) break;
				if (Regex.IsMatch(trimmedLine, @"^<h[1-6][\s>]", RegexOptions.IgnoreCase)) break;
				if (Regex.IsMatch(trimmedLine, @"^<hr\s*/?\s*>?$", RegexOptions.IgnoreCase)) break;

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

						var imgHtml = Regex.Match(part, @"src=[""']([^""']+)[""']", RegexOptions.IgnoreCase);
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

		// ─── HTML Block Handling ─────────────────────────────────────

		private static int ParseHtmlBlock(string[] lines, int start, VisualElement container,
			string gitOwner, string gitRepo, string branch)
		{
			var firstLine = lines[start].TrimStart();
			var tagMatch = Regex.Match(firstLine, @"^<([a-zA-Z]+)");
			if (!tagMatch.Success) return start + 1;

			var tagName = tagMatch.Groups[1].Value.ToLowerInvariant();
			var openTag = $"<{tagName}";
			var closeTag = $"</{tagName}";

			// Collect all lines until the closing tag
			var sb = new StringBuilder();
			var i = start;
			var depth = 0;

			while (i < lines.Length)
			{
				var line = lines[i];
				var lineLower = line.ToLowerInvariant();

				depth += CountSubstring(lineLower, openTag);
				depth -= CountSubstring(lineLower, closeTag);

				if (sb.Length > 0) sb.Append('\n');
				sb.Append(line);
				i++;

				if (depth <= 0) break;
			}

			// If we never found a closing tag, also break on blank lines
			if (depth > 0)
			{
				// Reset and try simpler approach — consume until blank line
				sb.Clear();
				i = start;
				while (i < lines.Length && !string.IsNullOrWhiteSpace(lines[i]))
				{
					if (sb.Length > 0) sb.Append('\n');
					sb.Append(lines[i]);
					i++;
				}
			}

			var content = sb.ToString();

			switch (tagName)
			{
				case "pre":
					RenderHtmlPre(content, container);
					break;
				case "table":
					RenderHtmlTable(content, container, gitOwner, gitRepo, branch);
					break;
				case "details":
					RenderHtmlDetails(content, container, gitOwner, gitRepo, branch);
					break;
				case "blockquote":
					RenderHtmlBlockquote(content, container);
					break;
				case "ul":
					RenderHtmlList(content, container, ordered: false);
					break;
				case "ol":
					RenderHtmlList(content, container, ordered: true);
					break;
				default:
					RenderHtmlGenericBlock(content, container, gitOwner, gitRepo, branch);
					break;
			}

			return i;
		}

		private static void RenderHtmlPre(string html, VisualElement container)
		{
			// Strip <pre> and <code> tags, extract content
			var inner = StripOuterTag(html, "pre");
			inner = Regex.Replace(inner, @"</?code[^>]*>", "", RegexOptions.IgnoreCase);
			inner = DecodeHtmlEntities(inner.Trim());

			var codeLabel = new Label(inner);
			codeLabel.enableRichText = false;
			codeLabel.AddToClassList("readme-code-block");
			container.Add(codeLabel);
		}

		private static void RenderHtmlTable(string html, VisualElement container,
			string gitOwner, string gitRepo, string branch)
		{
			var tableContainer = new VisualElement();
			tableContainer.AddToClassList("readme-table");

			// Extract rows
			var rows = Regex.Matches(html, @"<tr[^>]*>(.*?)</tr>",
				RegexOptions.IgnoreCase | RegexOptions.Singleline);

			foreach (Match row in rows)
			{
				var rowEl = new VisualElement();
				rowEl.AddToClassList("readme-table-row");

				// Extract cells (th or td)
				var cells = Regex.Matches(row.Groups[1].Value, @"<(th|td)[^>]*>(.*?)</\1>",
					RegexOptions.IgnoreCase | RegexOptions.Singleline);

				var isHeader = false;
				foreach (Match cell in cells)
				{
					isHeader = cell.Groups[1].Value.Equals("th", StringComparison.OrdinalIgnoreCase);
					var cellText = StripHtmlTags(cell.Groups[2].Value).Trim();
					var cellLabel = new Label(FormatInline(cellText));
					cellLabel.enableRichText = true;
					cellLabel.AddToClassList(isHeader ? "readme-table-header-cell" : "readme-table-cell");
					rowEl.Add(cellLabel);
				}

				if (isHeader)
				{
					rowEl.AddToClassList("readme-table-header-row");
				}

				tableContainer.Add(rowEl);
			}

			if (tableContainer.childCount > 0)
			{
				container.Add(tableContainer);
			}
		}

		private static void RenderHtmlDetails(string html, VisualElement container,
			string gitOwner, string gitRepo, string branch)
		{
			// Extract summary
			var summaryMatch = Regex.Match(html, @"<summary[^>]*>(.*?)</summary>",
				RegexOptions.IgnoreCase | RegexOptions.Singleline);
			var summaryText = summaryMatch.Success
				? StripHtmlTags(summaryMatch.Groups[1].Value).Trim()
				: "Details";

			// Render summary as a heading-style label
			var summaryLabel = new Label(FormatInline(summaryText));
			summaryLabel.enableRichText = true;
			summaryLabel.AddToClassList("readme-heading");
			summaryLabel.AddToClassList("readme-h4");
			container.Add(summaryLabel);

			// Extract content after </summary> and before </details>
			var innerHtml = StripOuterTag(html, "details");
			innerHtml = Regex.Replace(innerHtml, @"<summary[^>]*>.*?</summary>",
				"", RegexOptions.IgnoreCase | RegexOptions.Singleline);

			RenderHtmlContent(innerHtml.Trim(), container, gitOwner, gitRepo, branch);
		}

		private static void RenderHtmlBlockquote(string html, VisualElement container)
		{
			var inner = StripOuterTag(html, "blockquote");
			var text = StripHtmlTags(inner).Trim();
			if (string.IsNullOrEmpty(text)) return;

			var quote = new Label(FormatInline(text));
			quote.enableRichText = true;
			quote.AddToClassList("readme-blockquote");
			container.Add(quote);
		}

		private static void RenderHtmlList(string html, VisualElement container, bool ordered)
		{
			var listContainer = new VisualElement();
			listContainer.AddToClassList("readme-list");

			var items = Regex.Matches(html, @"<li[^>]*>(.*?)</li>",
				RegexOptions.IgnoreCase | RegexOptions.Singleline);
			var itemNumber = 1;

			foreach (Match li in items)
			{
				var text = StripHtmlTags(li.Groups[1].Value).Trim();
				var prefix = ordered ? $"{itemNumber}. " : "\u2022 ";
				var item = new Label(FormatInline($"{prefix}{text}"));
				item.enableRichText = true;
				item.AddToClassList("readme-list-item");
				listContainer.Add(item);
				itemNumber++;
			}

			if (listContainer.childCount > 0)
			{
				container.Add(listContainer);
			}
		}

		private static void RenderHtmlGenericBlock(string html, VisualElement container,
			string gitOwner, string gitRepo, string branch)
		{
			// For div, p, section, etc. — extract images and render remaining text
			RenderHtmlContent(html, container, gitOwner, gitRepo, branch);
		}

		private static void RenderHtmlContent(string html, VisualElement container,
			string gitOwner, string gitRepo, string branch)
		{
			if (string.IsNullOrWhiteSpace(html)) return;

			var resolvedBranch = string.IsNullOrEmpty(branch) ? "main" : branch;

			// Split on images (both markdown and HTML img)
			var parts = Regex.Split(html,
				@"(!\[[^\]]*\]\([^)]+\)|<img\s[^>]*>)",
				RegexOptions.IgnoreCase);

			foreach (var part in parts)
			{
				if (string.IsNullOrWhiteSpace(part)) continue;

				// Markdown image
				var imgMd = Regex.Match(part, @"^!\[([^\]]*)\]\(([^)]+)\)$");
				if (imgMd.Success)
				{
					var imgUrl = ResolveUrl(imgMd.Groups[2].Value, gitOwner, gitRepo, resolvedBranch);
					AddImage(container, imgUrl);
					continue;
				}

				// HTML img tag
				if (part.TrimStart().StartsWith("<img", StringComparison.OrdinalIgnoreCase))
				{
					var srcMatch = Regex.Match(part, @"src=[""']([^""']+)[""']", RegexOptions.IgnoreCase);
					if (srcMatch.Success)
					{
						var imgUrl = ResolveUrl(srcMatch.Groups[1].Value, gitOwner, gitRepo, resolvedBranch);
						AddImage(container, imgUrl);
					}

					continue;
				}

				// Text content — strip HTML and render as paragraph
				var text = StripHtmlTags(part).Trim();
				if (string.IsNullOrWhiteSpace(text)) continue;

				var para = new Label(FormatInline(text));
				para.enableRichText = true;
				para.AddToClassList("readme-paragraph");
				container.Add(para);
			}
		}

		// ─── Inline Formatting ───────────────────────────────────────

		/// <summary>
		/// Converts inline markdown and HTML formatting to Unity rich text tags.
		/// </summary>
		private static string FormatInline(string text)
		{
			if (string.IsNullOrEmpty(text)) return text;

			// Convert HTML inline elements to markdown equivalents first
			text = ConvertHtmlToMarkdown(text);

			// Strip remaining unknown HTML tags (keep content)
			text = Regex.Replace(text, @"</?[a-zA-Z][a-zA-Z0-9]*(?:\s[^>]*)?>", "");

			// Decode HTML entities
			text = DecodeHtmlEntities(text);

			// Escape any remaining angle brackets that might interfere with Unity rich text
			text = text.Replace("<", "\u200B<\u200B");

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
			text = Regex.Replace(text, @"\[([^\]]+)\]\([^)]+\)", "<color=#c084fc>$1</color>");

			return text;
		}

		/// <summary>
		/// Converts common HTML inline elements to their markdown equivalents
		/// so the markdown processor can handle them uniformly.
		/// </summary>
		private static string ConvertHtmlToMarkdown(string text)
		{
			// <br> → newline
			text = Regex.Replace(text, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);

			// <strong>text</strong> → **text**
			text = Regex.Replace(text, @"<strong>(.*?)</strong>", "**$1**",
				RegexOptions.IgnoreCase | RegexOptions.Singleline);

			// <b>text</b> → **text**
			text = Regex.Replace(text, @"<b>(.*?)</b>", "**$1**",
				RegexOptions.IgnoreCase | RegexOptions.Singleline);

			// <em>text</em> → *text*
			text = Regex.Replace(text, @"<em>(.*?)</em>", "*$1*",
				RegexOptions.IgnoreCase | RegexOptions.Singleline);

			// <i>text</i> → *text*
			text = Regex.Replace(text, @"<i>(.*?)</i>", "*$1*",
				RegexOptions.IgnoreCase | RegexOptions.Singleline);

			// <code>text</code> → `text`
			text = Regex.Replace(text, @"<code>([^<]*)</code>", "`$1`", RegexOptions.IgnoreCase);

			// <kbd>text</kbd> → `text`
			text = Regex.Replace(text, @"<kbd>([^<]*)</kbd>", "`$1`", RegexOptions.IgnoreCase);

			// <a href="url">text</a> → [text](url)
			text = Regex.Replace(text, @"<a\s+href=[""']([^""']+)[""'][^>]*>([^<]*)</a>",
				"[$2]($1)", RegexOptions.IgnoreCase);

			// <del>text</del> / <s>text</s> → ~text~ (show as-is, no Unity support)
			text = Regex.Replace(text, @"<del>(.*?)</del>", "$1",
				RegexOptions.IgnoreCase | RegexOptions.Singleline);
			text = Regex.Replace(text, @"<s>(.*?)</s>", "$1",
				RegexOptions.IgnoreCase | RegexOptions.Singleline);

			// <sup>/<sub> — no Unity equivalent, just show content
			text = Regex.Replace(text, @"<sup>(.*?)</sup>", "$1",
				RegexOptions.IgnoreCase | RegexOptions.Singleline);
			text = Regex.Replace(text, @"<sub>(.*?)</sub>", "$1",
				RegexOptions.IgnoreCase | RegexOptions.Singleline);

			return text;
		}

		// ─── Image Handling ──────────────────────────────────────────

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

				var maxWidth = 600f;
				var scale = texture.width > maxWidth ? maxWidth / texture.width : 1f;
				imageElement.style.width = texture.width * scale;
				imageElement.style.height = texture.height * scale;
			});

			container.Add(imageContainer);
		}

		// ─── URL Helpers ─────────────────────────────────────────────

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

			return $"https://raw.githubusercontent.com/{gitOwner}/{gitRepo}/{branch}/{url}";
		}

		// ─── Detection Helpers ───────────────────────────────────────

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
			if (!trimmed.StartsWith("[![")) return false;
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

		// ─── String Helpers ──────────────────────────────────────────

		private static string StripHtmlTags(string html)
		{
			if (string.IsNullOrEmpty(html)) return html;
			var result = Regex.Replace(html, @"<[^>]+>", "");
			return DecodeHtmlEntities(result);
		}

		private static string StripOuterTag(string html, string tagName)
		{
			var pattern = $@"^\s*<{tagName}[^>]*>(.*)</{tagName}\s*>\s*$";
			var match = Regex.Match(html, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);
			return match.Success ? match.Groups[1].Value : html;
		}

		private static string DecodeHtmlEntities(string text)
		{
			if (string.IsNullOrEmpty(text)) return text;
			text = text.Replace("&amp;", "&");
			text = text.Replace("&lt;", "<");
			text = text.Replace("&gt;", ">");
			text = text.Replace("&quot;", "\"");
			text = text.Replace("&#39;", "'");
			text = text.Replace("&apos;", "'");
			text = text.Replace("&nbsp;", " ");
			return text;
		}

		private static int CountSubstring(string source, string substring)
		{
			var count = 0;
			var idx = 0;
			while ((idx = source.IndexOf(substring, idx, StringComparison.Ordinal)) >= 0)
			{
				count++;
				idx += substring.Length;
			}

			return count;
		}
	}
}
