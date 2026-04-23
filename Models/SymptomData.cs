using System.Text.Json.Serialization;

namespace PatientSpeechAnalysis.Models;

public class SymptomData
{
    [JsonPropertyName("canonical_key")]
    public string CanonicalKey { get; set; } = "";

    [JsonPropertyName("semptom")]
    public string Semptom { get; set; } = "";

    [JsonPropertyName("zaman")]
    public string? Zaman { get; set; }

    [JsonPropertyName("sıklık")]
    public string? Siklik { get; set; }

    [JsonPropertyName("şiddet_seyri")]
    public string? SiddetSeyri { get; set; }

    [JsonPropertyName("tetikleyici_azaltan")]
    public List<string>? TetikleyiciAzaltan { get; set; }
}

public class SymptomExtractionResult
{
    [JsonPropertyName("semptomlar")]
    public List<SymptomData> Semptomlar { get; set; } = [];
}
