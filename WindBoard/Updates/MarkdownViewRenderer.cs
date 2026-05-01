using System;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace WindBoard.Updates
{
    /// <summary>
    /// 将 <see cref="MarkdownViewDocument"/> 渲染为原生 WinUI 控件树。
    /// </summary>
    internal static class MarkdownViewRenderer
    {
        private static readonly FontFamily CodeFontFamily = new("Consolas");

        internal static UIElement Build(MarkdownViewDocument document, Action<string>? openUrl)
        {
            var panel = new StackPanel
            {
                Spacing = 12,
            };

            foreach (MarkdownViewBlock block in document.Blocks)
            {
                panel.Children.Add(BuildBlock(block, openUrl, nestedListLevel: 0));
            }

            return panel;
        }

        private static UIElement BuildBlock(MarkdownViewBlock block, Action<string>? openUrl, int nestedListLevel)
        {
            return block switch
            {
                MarkdownHeadingBlock heading => BuildHeading(heading),
                MarkdownParagraphBlock paragraph => BuildParagraph(paragraph, openUrl),
                MarkdownListBlock list => BuildList(list, openUrl, nestedListLevel),
                MarkdownCodeBlock code => BuildCodeBlock(code),
                _ => new TextBlock
                {
                    Text = block.ToString(),
                    TextWrapping = TextWrapping.Wrap,
                },
            };
        }

        private static UIElement BuildHeading(MarkdownHeadingBlock heading)
        {
            return new TextBlock
            {
                Text = heading.Text,
                TextWrapping = TextWrapping.Wrap,
                FontWeight = FontWeights.SemiBold,
                FontSize = heading.Level switch
                {
                    1 => 24,
                    2 => 20,
                    3 => 18,
                    _ => 16,
                },
                Margin = new Thickness(0, heading.Level <= 2 ? 2 : 0, 0, 0),
            };
        }

        private static UIElement BuildParagraph(MarkdownParagraphBlock paragraph, Action<string>? openUrl)
        {
            var textBlock = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.95,
            };

            foreach (MarkdownInlinePart inline in paragraph.Inlines)
            {
                textBlock.Inlines.Add(BuildInline(inline, openUrl));
            }

            return textBlock;
        }

        private static UIElement BuildList(MarkdownListBlock list, Action<string>? openUrl, int nestedListLevel)
        {
            var listPanel = new StackPanel
            {
                Spacing = 8,
                Margin = new Thickness(nestedListLevel * 14, 0, 0, 0),
            };

            for (int i = 0; i < list.Items.Count; i++)
            {
                string marker = list.IsOrdered ? $"{i + 1}." : "•";

                var row = new Grid
                {
                    ColumnSpacing = 10,
                    VerticalAlignment = VerticalAlignment.Top,
                };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var bullet = new TextBlock
                {
                    Text = marker,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, 1, 0, 0),
                };
                Grid.SetColumn(bullet, 0);
                row.Children.Add(bullet);

                var contentPanel = new StackPanel
                {
                    Spacing = 6,
                };

                foreach (MarkdownViewBlock child in list.Items[i].Blocks)
                {
                    int childNestedLevel = child is MarkdownListBlock ? nestedListLevel + 1 : nestedListLevel;
                    contentPanel.Children.Add(BuildBlock(child, openUrl, childNestedLevel));
                }

                Grid.SetColumn(contentPanel, 1);
                row.Children.Add(contentPanel);
                listPanel.Children.Add(row);
            }

            return listPanel;
        }

        private static UIElement BuildCodeBlock(MarkdownCodeBlock code)
        {
            var textBlock = new TextBlock
            {
                Text = code.Code,
                FontFamily = CodeFontFamily,
                TextWrapping = TextWrapping.Wrap,
                IsTextSelectionEnabled = true,
            };

            return new Border
            {
                Padding = new Thickness(12, 10, 12, 10),
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                Background = new SolidColorBrush(Color.FromArgb(18, 127, 127, 127)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(40, 127, 127, 127)),
                Child = textBlock,
            };
        }

        private static Inline BuildInline(MarkdownInlinePart inline, Action<string>? openUrl)
        {
            switch (inline)
            {
                case MarkdownTextPart text:
                    return new Run
                    {
                        Text = text.Text,
                    };

                case MarkdownCodePart code:
                    return new Run
                    {
                        Text = code.Text,
                        FontFamily = CodeFontFamily,
                    };

                case MarkdownLineBreakPart:
                    return new LineBreak();

                case MarkdownStrongPart strong:
                    var bold = new Bold();
                    AppendInlineChildren(bold.Inlines, strong.Children, openUrl);
                    return bold;

                case MarkdownEmphasisPart emphasis:
                    var italic = new Italic();
                    AppendInlineChildren(italic.Inlines, emphasis.Children, openUrl);
                    return italic;

                case MarkdownLinkPart link:
                    var hyperlink = new Hyperlink();
                    hyperlink.Inlines.Add(new Run
                    {
                        Text = link.Text,
                    });
                    hyperlink.Click += (_, _) => openUrl?.Invoke(link.Url);
                    return hyperlink;

                default:
                    return new Run
                    {
                        Text = inline.ToString(),
                    };
            }
        }

        private static void AppendInlineChildren(InlineCollection target, System.Collections.Generic.IReadOnlyList<MarkdownInlinePart> children, Action<string>? openUrl)
        {
            foreach (MarkdownInlinePart child in children)
            {
                target.Add(BuildInline(child, openUrl));
            }
        }
    }
}
