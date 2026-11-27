using Microsoft.UI.Xaml.Controls;
using WindBoard.Settings;

namespace WindBoard.Tests.Settings;

public sealed class AppAnnouncementPayloadParserTests
{
    private const string ValidAnnouncement = """
        {
          "id": "installer-distribution-changed",
          "severity": "warning",
          "action": "openBackupSettings",
          "text": {
            "zh-CN": { "title": "zh-title", "message": "zh-message", "actionText": "zh-action" },
            "en-US": { "title": "en-title", "message": "en-message", "actionText": "en-action" }
          }
        }
        """;

    [Fact]
    public void TryParse_ValidPayload_ReturnsAnnouncement()
    {
        bool ok = AppAnnouncementPayloadParser.TryParse(
            BuildPayload(ValidAnnouncement),
            out IReadOnlyList<AppAnnouncement> announcements,
            out string? error);

        Assert.True(ok, error);
        AppAnnouncement announcement = Assert.Single(announcements);
        Assert.Equal("installer-distribution-changed", announcement.Id);
        Assert.Equal(InfoBarSeverity.Warning, announcement.Severity);
        Assert.Equal(AppAnnouncementAction.OpenBackupSettings, announcement.Action);
        Assert.Equal("zh-title", announcement.ResolveText("zh-CN").Title);
        Assert.Equal("zh-message", announcement.ResolveText("zh-CN").Message);
        Assert.Equal("zh-action", announcement.ResolveText("zh-CN").ActionText);
        Assert.Equal("en-title", announcement.ResolveText("en-US").Title);
    }

    [Fact]
    public void TryParse_EmptyAnnouncementList_ReturnsEmptyList()
    {
        bool ok = AppAnnouncementPayloadParser.TryParse(
            BuildPayload(string.Empty),
            out IReadOnlyList<AppAnnouncement> announcements,
            out string? error);

        Assert.True(ok, error);
        Assert.Empty(announcements);
    }

    [Fact]
    public void TryParse_SchemaVersionMismatch_RejectsWholePayload()
    {
        bool ok = AppAnnouncementPayloadParser.TryParse(
            BuildPayload(ValidAnnouncement, schemaVersion: "2"),
            out IReadOnlyList<AppAnnouncement> announcements,
            out string? error);

        Assert.False(ok);
        Assert.Empty(announcements);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void TryParse_DuplicateIds_RejectsWholePayload()
    {
        string payload = BuildPayload($"{CreateAnnouncementJson("same-id")},{CreateAnnouncementJson("same-id")}");

        bool ok = AppAnnouncementPayloadParser.TryParse(payload, out IReadOnlyList<AppAnnouncement> announcements, out string? error);

        Assert.False(ok);
        Assert.Empty(announcements);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void TryParse_InvalidIdCharacters_RejectsWholePayload()
    {
        string payload = BuildPayload(CreateAnnouncementJson("Invalid Id"));

        bool ok = AppAnnouncementPayloadParser.TryParse(payload, out _, out string? error);

        Assert.False(ok);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void TryParse_UnknownSeverity_RejectsWholePayload()
    {
        const string announcement = """
            {
              "id": "announcement-one",
              "severity": "critical",
              "text": { "zh-CN": { "title": "title", "message": "message" } }
            }
            """;

        bool ok = AppAnnouncementPayloadParser.TryParse(BuildPayload(announcement), out _, out string? error);

        Assert.False(ok);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void TryParse_UnknownAction_RejectsWholePayload()
    {
        const string announcement = """
            {
              "id": "announcement-one",
              "severity": "warning",
              "action": "openUrl",
              "text": { "zh-CN": { "title": "title", "message": "message", "actionText": "action" } }
            }
            """;

        bool ok = AppAnnouncementPayloadParser.TryParse(BuildPayload(announcement), out _, out string? error);

        Assert.False(ok);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void TryParse_MissingDefaultLanguage_RejectsWholePayload()
    {
        const string announcement = """
            {
              "id": "announcement-one",
              "severity": "warning",
              "text": { "en-US": { "title": "title", "message": "message" } }
            }
            """;

        bool ok = AppAnnouncementPayloadParser.TryParse(BuildPayload(announcement), out _, out string? error);

        Assert.False(ok);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void TryParse_EmptyTitle_RejectsWholePayload()
    {
        bool ok = AppAnnouncementPayloadParser.TryParse(
            BuildPayload(CreateAnnouncementJson("announcement-one", title: string.Empty)),
            out _,
            out string? error);

        Assert.False(ok);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void TryParse_EmptyMessage_RejectsWholePayload()
    {
        bool ok = AppAnnouncementPayloadParser.TryParse(
            BuildPayload(CreateAnnouncementJson("announcement-one", message: string.Empty)),
            out _,
            out string? error);

        Assert.False(ok);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void TryParse_NullAnnouncementItem_RejectsWholePayload()
    {
        bool ok = AppAnnouncementPayloadParser.TryParse(BuildPayload("null"), out _, out string? error);

        Assert.False(ok);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void TryParse_TooManyLanguages_RejectsWholePayload()
    {
        string[] languages = Enumerable.Range(0, AppAnnouncementPayloadParser.MaxLanguageCount)
            .Select(index => $"\"x-{index}\"")
            .Append("\"zh-CN\"")
            .ToArray();
        string texts = string.Join(
            ",",
            languages.Select(language => $"{language}: {{ \"title\": \"title\", \"message\": \"message\" }}"));
        string announcement = $$"""
            {
              "id": "announcement-one",
              "severity": "warning",
              "text": { {{texts}} }
            }
            """;

        bool ok = AppAnnouncementPayloadParser.TryParse(BuildPayload(announcement), out _, out string? error);

        Assert.False(ok);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void TryParse_TooLongActionText_RejectsWholePayload()
    {
        string actionText = new('a', AppAnnouncementPayloadParser.MaxActionTextLength + 1);
        string announcement = $$"""
            {
              "id": "announcement-one",
              "severity": "warning",
              "action": "openBackupSettings",
              "text": { "zh-CN": { "title": "title", "message": "message", "actionText": "{{actionText}}" } }
            }
            """;

        bool ok = AppAnnouncementPayloadParser.TryParse(BuildPayload(announcement), out _, out string? error);

        Assert.False(ok);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void TryParse_TooLongTitle_RejectsWholePayload()
    {
        string title = new('a', AppAnnouncementPayloadParser.MaxTitleLength + 1);

        bool ok = AppAnnouncementPayloadParser.TryParse(
            BuildPayload(CreateAnnouncementJson("announcement-one", title: title)),
            out _,
            out string? error);

        Assert.False(ok);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void TryParse_TooLongMessage_RejectsWholePayload()
    {
        string message = new('a', AppAnnouncementPayloadParser.MaxMessageLength + 1);

        bool ok = AppAnnouncementPayloadParser.TryParse(
            BuildPayload(CreateAnnouncementJson("announcement-one", message: message)),
            out _,
            out string? error);

        Assert.False(ok);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void TryParse_TooManyAnnouncements_RejectsWholePayload()
    {
        string items = string.Join(
            ",",
            Enumerable.Range(0, AppAnnouncementPayloadParser.MaxAnnouncementCount + 1)
                .Select(index => CreateAnnouncementJson($"announcement-{index}")));

        bool ok = AppAnnouncementPayloadParser.TryParse(BuildPayload(items), out _, out string? error);

        Assert.False(ok);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void TryParse_UnknownFields_AreIgnored()
    {
        const string announcement = """
            {
              "id": "announcement-one",
              "severity": "warning",
              "futureField": "ignored",
              "text": {
                "zh-CN": { "title": "title", "message": "message", "futureText": true }
              }
            }
            """;
        string payload = $$"""
            {
              "schemaVersion": 1,
              "futureRoot": { "anything": true },
              "announcements": [{{announcement}}]
            }
            """;

        bool ok = AppAnnouncementPayloadParser.TryParse(payload, out IReadOnlyList<AppAnnouncement> announcements, out string? error);

        Assert.True(ok, error);
        Assert.Single(announcements);
    }

    [Fact]
    public void TryParse_ActionWithoutActionText_RejectsWholePayload()
    {
        const string announcement = """
            {
              "id": "announcement-one",
              "severity": "warning",
              "action": "openBackupSettings",
              "text": { "zh-CN": { "title": "title", "message": "message" } }
            }
            """;

        bool ok = AppAnnouncementPayloadParser.TryParse(BuildPayload(announcement), out _, out string? error);

        Assert.False(ok);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void ResolveText_PrefersExactCulture()
    {
        AppAnnouncement announcement = ParseSingle(ValidAnnouncement);

        AppAnnouncementText text = announcement.ResolveText("en-US");

        Assert.Equal("en-title", text.Title);
    }

    [Fact]
    public void ResolveText_FallsBackToParentLanguage()
    {
        const string announcement = """
            {
              "id": "announcement-one",
              "severity": "warning",
              "text": {
                "zh": { "title": "zh-title", "message": "zh-message" },
                "zh-CN": { "title": "zh-cn-title", "message": "zh-cn-message" }
              }
            }
            """;

        AppAnnouncement parsed = ParseSingle(announcement);

        Assert.Equal("zh-title", parsed.ResolveText("zh-Hans-CN").Title);
    }

    [Fact]
    public void ResolveText_FallsBackToDefaultLanguage_WhenCultureMissing()
    {
        AppAnnouncement announcement = ParseSingle(ValidAnnouncement);

        Assert.Equal("zh-title", announcement.ResolveText("zh-Hans-CN").Title);
        Assert.Equal("zh-title", announcement.ResolveText("fr-FR").Title);
    }

    [Fact]
    public void ResolveText_NormalizesUnderscoreAndIgnoresCase()
    {
        AppAnnouncement announcement = ParseSingle(ValidAnnouncement);

        Assert.Equal("en-title", announcement.ResolveText("en_US").Title);
        Assert.Equal("en-title", announcement.ResolveText("EN-us").Title);
    }

    private static AppAnnouncement ParseSingle(string announcementJson)
    {
        bool ok = AppAnnouncementPayloadParser.TryParse(
            BuildPayload(announcementJson),
            out IReadOnlyList<AppAnnouncement> announcements,
            out string? error);

        Assert.True(ok, error);
        return Assert.Single(announcements);
    }

    private static string CreateAnnouncementJson(
        string id,
        string title = "title",
        string message = "message")
    {
        return $$"""
            {
              "id": "{{id}}",
              "severity": "warning",
              "text": {
                "zh-CN": { "title": "{{title}}", "message": "{{message}}" }
              }
            }
            """;
    }

    private static string BuildPayload(string announcementsJson, string schemaVersion = "1")
    {
        return $$"""
            {
              "schemaVersion": {{schemaVersion}},
              "announcements": [{{announcementsJson}}]
            }
            """;
    }
}
