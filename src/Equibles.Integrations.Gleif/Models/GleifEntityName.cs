namespace Equibles.Integrations.Gleif.Models;

public class GleifEntityName
{
    public string Name { get; set; }
    public string Language { get; set; }

    // GLEIF's name type, such as TRADING_OR_OPERATING_NAME or AUTO_ASCII_TRANSLITERATED_LEGAL_NAME.
    public string Type { get; set; }
    public bool IsTransliterated { get; set; }
}
