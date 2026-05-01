using System;
using System.Collections.Generic;
using System.Text;
using Markdig.Helpers;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace WindBoard.Updates
{
    /// <summary>
    /// Markdown 轻量视图文档：
    /// - 只保留更新日志需要的常见结构；
    /// - 供单元测试与 WinUI 原生渲染共用；
    /// - 未支持语法尽量降级为纯文本。
    /// </summary>
    internal sealed record MarkdownViewDocument(IReadOnlyList<MarkdownViewBlock> Blocks);

    internal abstract record MarkdownViewBlock;

    internal sealed record MarkdownHeadingBlock(int Level, string Text) : MarkdownViewBlock;

    internal sealed record MarkdownParagraphBlock(IReadOnlyList<MarkdownInlinePart> Inlines) : MarkdownViewBlock;

    internal sealed record MarkdownListBlock(bool IsOrdered, IReadOnlyList<MarkdownListItem> Items) : MarkdownViewBlock;

    internal sealed record MarkdownCodeBlock(string Code, string? Language) : MarkdownViewBlock;

    internal sealed record MarkdownListItem(IReadOnlyList<MarkdownViewBlock> Blocks);

    internal abstract record MarkdownInlinePart;

    internal sealed record MarkdownTextPart(string Text) : MarkdownInlinePart;

    internal sealed record MarkdownStrongPart(IReadOnlyList<MarkdownInlinePart> Children) : MarkdownInlinePart;

    internal sealed record MarkdownEmphasisPart(IReadOnlyList<MarkdownInlinePart> Children) : MarkdownInlinePart;

    internal sealed record MarkdownCodePart(string Text) : MarkdownInlinePart;

    internal sealed record MarkdownLinkPart(string Text, string Url) : MarkdownInlinePart;

    internal sealed record MarkdownLineBreakPart() : MarkdownInlinePart;

    internal static class MarkdownViewDocumentBuilder
    {
        // 仅启用自动链接扩展；其它语法保持 CommonMark 默认能力，避免超出当前需要的复杂度。
        private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
            .UseAutoLinks()
            .Build();

        internal static MarkdownViewDocument Build(string markdown)
        {
            string normalizedMarkdown = NormalizeMarkdown(markdown);
            if (string.IsNullOrWhiteSpace(normalizedMarkdown))
            {
                return new MarkdownViewDocument(Array.Empty<MarkdownViewBlock>());
            }

            MarkdownDocument document = Markdown.Parse(normalizedMarkdown, Pipeline);
            var blocks = new List<MarkdownViewBlock>();

            foreach (Block block in document)
            {
                AppendBlock(blocks, block);
            }

            return new MarkdownViewDocument(blocks);
        }

        private static string NormalizeMarkdown(string markdown)
        {
            string normalized = (markdown ?? string.Empty)
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace("\r", "\n", StringComparison.Ordinal);

            if (normalized.Contains('\n'))
            {
                return normalized;
            }

            // release 元数据偶发把换行保留为转义序列；这里仅在原文没有真实换行时做最小纠正。
            return normalized
                .Replace("\\r\\n", "\n", StringComparison.Ordinal)
                .Replace("\\n", "\n", StringComparison.Ordinal)
                .Replace("\\r", "\n", StringComparison.Ordinal);
        }

        private static void AppendBlock(List<MarkdownViewBlock> output, Block block)
        {
            switch (block)
            {
                case HeadingBlock heading:
                    output.Add(new MarkdownHeadingBlock(
                        Level: Math.Clamp(heading.Level, 1, 6),
                        Text: ExtractPlainText(heading.Inline).Trim()));
                    return;

                case ParagraphBlock paragraph:
                    IReadOnlyList<MarkdownInlinePart> paragraphInlines = BuildInlineParts(paragraph.Inline);
                    if (paragraphInlines.Count > 0)
                    {
                        output.Add(new MarkdownParagraphBlock(paragraphInlines));
                    }

                    return;

                case ListBlock list:
                    output.Add(new MarkdownListBlock(list.IsOrdered, BuildListItems(list)));
                    return;

                case FencedCodeBlock fencedCode:
                    output.Add(new MarkdownCodeBlock(
                        Code: NormalizeCodeText(fencedCode.Lines.ToString()),
                        Language: ExtractCodeLanguage(fencedCode.Info)));
                    return;

                case CodeBlock codeBlock:
                    output.Add(new MarkdownCodeBlock(
                        Code: NormalizeCodeText(codeBlock.Lines.ToString()),
                        Language: null));
                    return;

                case HtmlBlock htmlBlock:
                    AppendFallbackParagraph(output, NormalizeCodeText(htmlBlock.Lines.ToString()));
                    return;

                case QuoteBlock quote:
                    foreach (Block child in quote)
                    {
                        AppendBlock(output, child);
                    }

                    return;

                default:
                    AppendFallbackParagraph(output, block.ToString());
                    return;
            }
        }

        private static IReadOnlyList<MarkdownListItem> BuildListItems(ListBlock list)
        {
            var items = new List<MarkdownListItem>();

            foreach (Block block in list)
            {
                if (block is not ListItemBlock itemBlock)
                {
                    continue;
                }

                var itemBlocks = new List<MarkdownViewBlock>();
                foreach (Block child in itemBlock)
                {
                    AppendBlock(itemBlocks, child);
                }

                if (itemBlocks.Count == 0)
                {
                    itemBlocks.Add(new MarkdownParagraphBlock(new MarkdownInlinePart[]
                    {
                        new MarkdownTextPart(string.Empty),
                    }));
                }

                items.Add(new MarkdownListItem(itemBlocks));
            }

            return items;
        }

        private static IReadOnlyList<MarkdownInlinePart> BuildInlineParts(ContainerInline? container)
        {
            if (container is null)
            {
                return Array.Empty<MarkdownInlinePart>();
            }

            var parts = new List<MarkdownInlinePart>();

            for (Inline? current = container.FirstChild; current is not null; current = current.NextSibling)
            {
                AppendInline(parts, current);
            }

            return parts;
        }

        private static void AppendInline(List<MarkdownInlinePart> output, Inline inline)
        {
            switch (inline)
            {
                case LiteralInline literal:
                    AppendText(output, literal.Content.ToString());
                    return;

                case CodeInline code:
                    output.Add(new MarkdownCodePart(code.Content));
                    return;

                case LineBreakInline:
                    output.Add(new MarkdownLineBreakPart());
                    return;

                case EmphasisInline emphasis:
                    IReadOnlyList<MarkdownInlinePart> children = BuildInlineParts(emphasis);
                    if (children.Count == 0)
                    {
                        return;
                    }

                    output.Add(emphasis.DelimiterCount >= 2
                        ? new MarkdownStrongPart(children)
                        : new MarkdownEmphasisPart(children));
                    return;

                case LinkInline link when !link.IsImage:
                    string url = (link.GetDynamicUrl?.Invoke() ?? link.Url ?? string.Empty).Trim();
                    string text = ExtractPlainText(link).Trim();
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        text = url;
                    }

                    if (string.IsNullOrWhiteSpace(url))
                    {
                        AppendText(output, text);
                        return;
                    }

                    output.Add(new MarkdownLinkPart(text, url));
                    return;

                case HtmlInline html:
                    AppendText(output, html.Tag);
                    return;

                case ContainerInline nested:
                    for (Inline? child = nested.FirstChild; child is not null; child = child.NextSibling)
                    {
                        AppendInline(output, child);
                    }

                    return;

                default:
                    AppendText(output, inline.ToString());
                    return;
            }
        }

        private static string ExtractPlainText(ContainerInline? container)
        {
            if (container is null)
            {
                return string.Empty;
            }

            var builder = new StringBuilder();
            for (Inline? current = container.FirstChild; current is not null; current = current.NextSibling)
            {
                AppendInlineText(builder, current);
            }

            return builder.ToString();
        }

        private static void AppendInlineText(StringBuilder builder, Inline inline)
        {
            switch (inline)
            {
                case LiteralInline literal:
                    builder.Append(literal.Content.ToString());
                    return;

                case CodeInline code:
                    builder.Append(code.Content);
                    return;

                case LineBreakInline:
                    builder.Append('\n');
                    return;

                case HtmlInline html:
                    builder.Append(html.Tag);
                    return;

                case LinkInline link when !link.IsImage:
                    string text = ExtractPlainText(link);
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        builder.Append(text);
                    }
                    else
                    {
                        builder.Append(link.GetDynamicUrl?.Invoke() ?? link.Url ?? string.Empty);
                    }

                    return;

                case ContainerInline nested:
                    for (Inline? child = nested.FirstChild; child is not null; child = child.NextSibling)
                    {
                        AppendInlineText(builder, child);
                    }

                    return;

                default:
                    builder.Append(inline.ToString());
                    return;
            }
        }

        private static void AppendFallbackParagraph(List<MarkdownViewBlock> output, string? text)
        {
            string normalized = (text ?? string.Empty).Trim();
            if (normalized.Length == 0)
            {
                return;
            }

            output.Add(new MarkdownParagraphBlock(new MarkdownInlinePart[]
            {
                new MarkdownTextPart(normalized),
            }));
        }

        private static void AppendText(List<MarkdownInlinePart> output, string? text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            if (output.Count > 0 && output[^1] is MarkdownTextPart last)
            {
                output[^1] = last with { Text = last.Text + text };
                return;
            }

            output.Add(new MarkdownTextPart(text));
        }

        private static string NormalizeCodeText(string? text)
        {
            return (text ?? string.Empty).TrimEnd('\r', '\n');
        }

        private static string? ExtractCodeLanguage(string? infoText)
        {
            string text = (infoText ?? string.Empty).Trim();
            if (text.Length == 0)
            {
                return null;
            }

            int separatorIndex = text.IndexOfAny([' ', '\t']);
            return separatorIndex >= 0 ? text[..separatorIndex] : text;
        }
    }
}
