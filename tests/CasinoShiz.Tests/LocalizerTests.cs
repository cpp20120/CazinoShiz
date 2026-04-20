using BotFramework.Host;
using BotFramework.Host.Composition;
using BotFramework.Sdk;
using Microsoft.Extensions.Options;
using Xunit;

namespace CasinoShiz.Tests;

public class LocalizerTests
{
    private static Localizer MakeLocalizer(
        Dictionary<string, Dictionary<string, string>>? locales = null,
        string defaultCulture = "ru")
    {
        var loaded = new LoadedModules(
            [],
            locales ?? new Dictionary<string, Dictionary<string, string>>(),
            [],
            []);
        var options = Options.Create(new BotFrameworkOptions { DefaultCulture = defaultCulture });
        return new Localizer(loaded, options);
    }

    private static Dictionary<string, Dictionary<string, string>> RuLocale(params (string key, string value)[] entries)
    {
        var dict = entries.ToDictionary(e => e.key, e => e.value);
        return new Dictionary<string, Dictionary<string, string>> { ["ru"] = dict };
    }

    // ── Get — basic lookup ───────────────────────────────────────────────────

    [Fact]
    public void Get_ExistingKey_ReturnsTranslation()
    {
        var loc = MakeLocalizer(RuLocale(("poker.welcome", "Добро пожаловать")));
        Assert.Equal("Добро пожаловать", loc.Get("poker", "welcome"));
    }

    [Fact]
    public void Get_MissingKey_ReturnsScopedKey()
    {
        var loc = MakeLocalizer();
        Assert.Equal("poker.missing_key", loc.Get("poker", "missing_key"));
    }

    [Fact]
    public void Get_FallsBackToDefaultCulture()
    {
        var locales = new Dictionary<string, Dictionary<string, string>>
        {
            ["ru"] = new() { ["poker.welcome"] = "Привет" }
        };
        var loc = MakeLocalizer(locales);
        Assert.Equal("Привет", loc.Get("poker", "welcome", "en"));
    }

    [Fact]
    public void Get_ExplicitCultureWhenPresent_UsesThatCulture()
    {
        var locales = new Dictionary<string, Dictionary<string, string>>
        {
            ["ru"] = new() { ["poker.hi"] = "Привет" },
            ["en"] = new() { ["poker.hi"] = "Hello" },
        };
        var loc = MakeLocalizer(locales);
        Assert.Equal("Hello", loc.Get("poker", "hi", "en"));
    }

    [Fact]
    public void Get_DefaultCultureUsedWhenNoCultureHint()
    {
        var loc = MakeLocalizer(RuLocale(("poker.hi", "Привет")));
        Assert.Equal("Привет", loc.Get("poker", "hi"));
    }

    // ── GetPlural — Russian plural rules ─────────────────────────────────────

    private static Localizer MakePluralLocalizer(string moduleId = "m") =>
        MakeLocalizer(new Dictionary<string, Dictionary<string, string>>
        {
            ["ru"] = new()
            {
                [$"{moduleId}.coins.one"] = "монета",
                [$"{moduleId}.coins.few"] = "монеты",
                [$"{moduleId}.coins.many"] = "монет",
            }
        });

    [Theory]
    [InlineData(1, "монета")]
    [InlineData(21, "монета")]
    [InlineData(101, "монета")]
    public void GetPlural_RussianOne_UsesOneForm(int count, string expected)
    {
        var loc = MakePluralLocalizer();
        Assert.Equal(expected, loc.GetPlural("m", "coins", count));
    }

    [Theory]
    [InlineData(2, "монеты")]
    [InlineData(3, "монеты")]
    [InlineData(4, "монеты")]
    [InlineData(22, "монеты")]
    [InlineData(24, "монеты")]
    public void GetPlural_RussianFew_UsesFewForm(int count, string expected)
    {
        var loc = MakePluralLocalizer();
        Assert.Equal(expected, loc.GetPlural("m", "coins", count));
    }

    [Theory]
    [InlineData(0, "монет")]
    [InlineData(5, "монет")]
    [InlineData(10, "монет")]
    [InlineData(11, "монет")]   // teens always many
    [InlineData(12, "монет")]
    [InlineData(19, "монет")]
    [InlineData(20, "монет")]
    [InlineData(100, "монет")]
    public void GetPlural_RussianMany_UsesManyForm(int count, string expected)
    {
        var loc = MakePluralLocalizer();
        Assert.Equal(expected, loc.GetPlural("m", "coins", count));
    }

    [Theory]
    [InlineData(1, "монета")]
    [InlineData(2, "монет")]   // English: not-1 → many
    public void GetPlural_EnglishCulture_TwoFormRule(int count, string expected)
    {
        // en culture: 1 → one, everything else → many
        var locales = new Dictionary<string, Dictionary<string, string>>
        {
            ["en"] = new()
            {
                ["m.coins.one"] = "монета",
                ["m.coins.many"] = "монет",
            }
        };
        var loc = MakeLocalizer(locales);
        Assert.Equal(expected, loc.GetPlural("m", "coins", count, "en"));
    }
}
