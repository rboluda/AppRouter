using System.Globalization;

namespace AppRouter;

/// <summary>
/// Traducción de la interfaz. Las claves son el propio texto en español; <see cref="Strings"/>
/// tiene su versión en inglés y chino simplificado. Un texto sin traducir se muestra en español.
/// </summary>
public static partial class L
{
    public const string Spanish = "es", English = "en", Chinese = "zh";

    /// <summary>Idiomas disponibles, con su nombre en ese idioma.</summary>
    public static readonly (string Code, string Name)[] Languages =
    [
        (Spanish, "Español"),
        (English, "English"),
        (Chinese, "简体中文"),
    ];

    public static string Lang { get; private set; } = Spanish;

    /// <summary>Fuente de la interfaz: Segoe UI no tiene caracteres chinos, así que en chino se usa la de Windows para chino.</summary>
    public static string FontName => Lang == Chinese ? "Microsoft YaHei UI" : "Segoe UI";

    /// <summary>Fija el idioma; null = el del sistema (español, chino o, si no, inglés).</summary>
    public static void Set(string? code) => Lang = code is Spanish or English or Chinese ? code : FromSystem();

    static string FromSystem() => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName switch
    {
        "es" => Spanish,
        "zh" => Chinese,
        _ => English,
    };

    public static string T(string spanish)
    {
        if (Lang == Spanish || !Strings.TryGetValue(spanish, out var t)) return spanish;
        return (Lang == English ? t.En : t.Zh) is { Length: > 0 } s ? s : spanish;
    }

    public static string F(string spanishFormat, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, T(spanishFormat), args);
}
