using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using FluxTick.Core.Application;

namespace FluxTick.App.Localization;

/// <summary>
/// Looks up interface text in the active language and tells bound controls when the language changes.
/// </summary>
public sealed class Loc : INotifyPropertyChanged
{
    /// <summary>
    /// The culture name of English, which doubles as the fallback language.
    /// </summary>
    public const string English = "en";

    /// <summary>
    /// The culture name of Simplified Chinese.
    /// </summary>
    public const string SimplifiedChinese = "zh-CN";

    /// <summary>
    /// The tables of all languages, loaded once.
    /// </summary>
    private static readonly Dictionary<string, IReadOnlyDictionary<string, string>> Tables = new(StringComparer.OrdinalIgnoreCase)
    {
        [English] = Load("en"),
        [SimplifiedChinese] = Load("zh-CN"),
    };

    /// <summary>
    /// The culture of the operating system, captured before the application overrides the UI culture.
    /// </summary>
    private static readonly CultureInfo SystemCulture = Services.MacSystemLanguage.TryGetPreferred() ?? CultureInfo.CurrentUICulture;

    /// <summary>
    /// The table of the active language.
    /// </summary>
    private IReadOnlyDictionary<string, string> _table = Tables[English];

    /// <summary>
    /// Initializes a new instance of the <see cref="Loc"/> class. The application only uses <see cref="Instance"/>;
    /// tests create their own to look texts up without touching it.
    /// </summary>
    internal Loc()
    {
    }

    /// <summary>
    /// Gets the instance that XAML bindings and view models use.
    /// </summary>
    public static Loc Instance { get; } = new();

    /// <summary>
    /// Gets the keys of one language, used to check that the languages stay in step.
    /// </summary>
    /// <param name="language">
    /// The culture name of the language.
    /// </param>
    /// <returns>
    /// The keys defined for that language.
    /// </returns>
    public static IReadOnlyCollection<string> KeysOf(string language) => Tables[language].Keys.ToArray();

    /// <summary>
    /// Raised when a property changes. The indexer reports <c>Item[]</c>, which refreshes every bound text.
    /// </summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Raised after the active language changed, for view models that build text in code.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Gets the culture name of the active language.
    /// </summary>
    public string Language { get; private set; } = English;

    /// <summary>
    /// Gets the text for a key. Falls back to English, then to the key itself.
    /// </summary>
    /// <param name="key">
    /// The text key.
    /// </param>
    public string this[string key] => Find(key) ?? key;

    /// <summary>
    /// Looks a key up in the active language, then in English.
    /// </summary>
    /// <param name="key">
    /// The text key.
    /// </param>
    /// <returns>
    /// The text, or <see langword="null"/> when neither language defines the key.
    /// </returns>
    public string? Find(string key) =>
        _table.TryGetValue(key, out var text) ? text : Tables[English].GetValueOrDefault(key);

    /// <summary>
    /// Gets the text for a key and fills in its placeholders.
    /// </summary>
    /// <param name="key">
    /// The text key.
    /// </param>
    /// <param name="args">
    /// The values for <c>{0}</c>, <c>{1}</c> and so on.
    /// </param>
    /// <returns>
    /// The formatted text.
    /// </returns>
    public static string Get(string key, params object?[] args)
    {
        var text = Instance[key];
        return args.Length == 0 ? text : string.Format(CultureInfo.CurrentCulture, text, args);
    }

    /// <summary>
    /// Works out which language a setting stands for.
    /// </summary>
    /// <param name="mode">
    /// The configured language.
    /// </param>
    /// <param name="systemCulture">
    /// The culture of the system, used for <see cref="LanguageMode.Auto"/>.
    /// </param>
    /// <returns>
    /// The culture name of the language to show.
    /// </returns>
    public static string Resolve(LanguageMode mode, CultureInfo systemCulture) => mode switch
    {
        LanguageMode.ZhCN => SimplifiedChinese,
        LanguageMode.En => English,
        _ => systemCulture.TwoLetterISOLanguageName.Equals("zh", StringComparison.OrdinalIgnoreCase) ? SimplifiedChinese : English,
    };

    /// <summary>
    /// Switches to the language a setting stands for, and sets the UI culture to match.
    /// </summary>
    /// <param name="mode">
    /// The configured language.
    /// </param>
    public void Apply(LanguageMode mode) => Apply(mode, SystemCulture);

    /// <summary>
    /// Switches to the language a setting stands for, with an explicit system culture.
    /// </summary>
    /// <param name="mode">
    /// The configured language.
    /// </param>
    /// <param name="systemCulture">
    /// The culture of the system, used for <see cref="LanguageMode.Auto"/>.
    /// </param>
    public void Apply(LanguageMode mode, CultureInfo systemCulture)
    {
        var language = Resolve(mode, systemCulture);
        var culture = CultureInfo.GetCultureInfo(language);
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentUICulture = culture;
        Select(language);
    }

    /// <summary>
    /// Switches the language of the texts without touching the UI culture.
    /// </summary>
    /// <param name="language">
    /// The culture name of the language.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the language changed.
    /// </returns>
    internal bool Select(string language)
    {
        if (language == Language)
        {
            return false;
        }

        Language = language;
        _table = Tables[language];
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Language)));
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>
    /// Reads one embedded string table.
    /// </summary>
    /// <param name="name">
    /// The language name used in the resource name.
    /// </param>
    /// <returns>
    /// The key and text pairs.
    /// </returns>
    private static Dictionary<string, string> Load(string name)
    {
        using var stream = typeof(Loc).Assembly.GetManifestResourceStream($"FluxTick.Strings.{name}.json")
            ?? throw new InvalidOperationException($"Missing string table {name}.");
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!;
    }
}
