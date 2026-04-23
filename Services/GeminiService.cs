using System.Diagnostics;
using System.Text;
using System.Text.Json;
using PatientSpeechAnalysis.Models;

namespace PatientSpeechAnalysis.Services;

public class GeminiService : IGeminiService
{
    private readonly HttpClient _httpClient;
    private readonly string _model;
    private readonly ILogger<GeminiService> _logger;

    public GeminiService(IConfiguration configuration, ILogger<GeminiService> logger)
    {
        _logger = logger;

        var apiKey = configuration["OpenRouter:ApiKey"]
            ?? throw new InvalidOperationException("OpenRouter API key bulunamadı. appsettings.json'da 'OpenRouter:ApiKey' ayarını kontrol edin.");

        _model = configuration["OpenRouter:Model"] ?? "google/gemini-2.0-flash-001";

        _httpClient = new HttpClient
        {
            BaseAddress = new Uri("https://openrouter.ai/api/v1/")
        };
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
    }

    public async Task<GeminiAnalysisResult> AnalyzeAsync(string sentence)
    {
        var sw = Stopwatch.StartNew();
        _logger.LogInformation("Analiz başlatılıyor: \"{Sentence}\"", sentence);

        var prompt = "Sen bir sağlık asistanısın. Yaşlı bir hastadan gelen aşağıdaki cümleyi analiz et.\n\n" +
            "Hasta cümlesi: \"" + sentence + "\"\n\n" +
            "Aşağıdaki JSON formatında yanıt ver. Başka hiçbir metin ekleme, sadece JSON döndür:\n" +
            "{\n" +
            "  \"mood\": \"hastanın duygu durumu (örn: mutlu, üzgün, endişeli, korkmuş, sakin, sinirli, umutsuz, ağrılı)\",\n" +
            "  \"isEmergency\": true/false,\n" +
            "  \"summary\": \"hastanın durumunun kısa özeti (max 250 kelime)\",\n" +
            "  \"dailyScore\": 1-10 arası sağlık puanı\n" +
            "}\n\n" +
            "Acil durum kriterleri (isEmergency = true):\n" +
            "- İntihar düşüncesi veya kendine zarar verme ifadesi\n" +
            "- Şiddetli ağrı, göğüs ağrısı, nefes darlığı\n" +
            "- Düşme, kaza veya yaralanma bildirimi\n" +
            "- Bilinç bulanıklığı, bayılma\n" +
            "- İlaç zehirlenmesi veya yanlış ilaç kullanımı\n" +
            "- Yardım çağrısı veya panik ifadesi\n\n" +
            "DailyScore değerlendirmesi:\n" +
            "- 1-3: Kritik/kötü durum\n" +
            "- 4-5: Endişe verici\n" +
            "- 6-7: Normal/orta\n" +
            "- 8-10: İyi/çok iyi";

        try
        {
            var requestBody = new
            {
                model = _model,
                messages = new[]
                {
                    new { role = "user", content = prompt }
                }
            };

            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var apiSw = Stopwatch.StartNew();
            var response = await _httpClient.PostAsync("chat/completions", content);
            apiSw.Stop();
            var responseBody = await response.Content.ReadAsStringAsync();

            _logger.LogInformation("OpenRouter API çağrısı: {StatusCode} ({ApiElapsed:F3}s)",
                response.StatusCode, apiSw.Elapsed.TotalSeconds);

            if (!response.IsSuccessStatusCode)
                throw new Exception($"OpenRouter API hatası: {response.StatusCode} - {responseBody}");

            using var doc = JsonDocument.Parse(responseBody);
            var text = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString()
                ?? throw new Exception("AI'dan boş yanıt alındı.");

            _logger.LogDebug("Ham yanıt: {RawResponse}", text);

            var cleanedJson = CleanJsonResponse(text);
            var result = JsonSerializer.Deserialize<GeminiAnalysisResult>(cleanedJson)
                ?? throw new Exception("AI yanıtı JSON'a dönüştürülemedi.");

            sw.Stop();
            _logger.LogInformation(
                "Analiz tamamlandı ({Elapsed:F3}s) - Mood: {Mood}, Emergency: {IsEmergency}, Score: {DailyScore}",
                sw.Elapsed.TotalSeconds, result.Mood, result.IsEmergency, result.DailyScore);

            return result;
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "Analiz HATA ({Elapsed:F3}s)", sw.Elapsed.TotalSeconds);
            throw;
        }
    }

    public async Task<SymptomExtractionResult> ExtractSymptomsAsync(string sentence)
    {
        var sw = Stopwatch.StartNew();
        _logger.LogInformation("Semptom çıkarımı başlatılıyor: \"{Sentence}\"", sentence);

        var prompt = """
            Sen bir tıbbi semptom çıkarım sistemisin.

            GÖREV:
            Hastanın serbest konuşma metnini analiz et. Metinde geçen semptomları çıkar, aşağıdaki kurallara uygun yapılandırılmış JSON formatında döndür.

            ════════════════════════════════════════
            ÇIKTI FORMATI
            ════════════════════════════════════════

            {
              "semptomlar": [
                {
                  "canonical_key": "<semptom>::<zaman>::<tetikleyici_azaltan>",
                  "semptom": "<zorunlu_kelime_listesinden>",
                  "zaman": "<zorunlu_kelime_listesinden>",
                  "sıklık": "<zorunlu_kelime_listesinden>",
                  "şiddet_seyri": "<zorunlu_kelime_listesinden>",
                  "tetikleyici_azaltan": ["<zorunlu_kelime_listesinden>"]
                }
              ]
            }

            ════════════════════════════════════════
            CANONICAL KEY KURALLARI
            ════════════════════════════════════════

            1. Format: [semptom]::[zaman]::[tetikleyici_azaltan]
            2. Tetikleyici birden fazlaysa pipe ile ayır: baş_ağrısı::sabah::taze_hava|su
            3. Tetikleyici yoksa "yok" yaz: baş_ağrısı::sabah::yok
            4. Zaman bilinmiyorsa "belirsiz" yaz: baş_ağrısı::belirsiz::yok
            5. Canonical key, aynı semptomu farklı seanslarda eşleştirmek için kullanılır.
               Aynı semptom + aynı bağlam = HER ZAMAN aynı canonical_key üretilmeli.

            ════════════════════════════════════════
            ZORUNLU KELİME LİSTELERİ
            ════════════════════════════════════════

            Yalnızca bu listelerden seç. Listede olmayan kavramı en yakın kelimeye eşle.

            ── SEMPTOM ──────────────────────────────
              baş_ağrısı, boyun_ağrısı, sırt_ağrısı, bel_ağrısı, göğüs_ağrısı,
              karın_ağrısı, eklem_ağrısı, kas_ağrısı, diş_ağrısı, kulak_ağrısı,
              boğaz_ağrısı, göz_ağrısı,
              bulantı, kusma, ishal, kabızlık, şişkinlik, hazımsızlık,
              iştahsızlık, aşırı_iştah, mide_yanması, yutma_güçlüğü,
              nefes_darlığı, öksürük, hırıltı, burun_tıkanıklığı, burun_akıntısı,
              balgam, kan_tükürme,
              baş_dönmesi, denge_bozukluğu, uyuşma, karıncalanma, titreme,
              unutkanlık, konsantrasyon_güçlüğü, bayılma, görme_bozukluğu,
              çarpıntı, nabız_düzensizliği, yüksek_tansiyon_hissi, ödem,
              yorgunluk, halsizlik, ateş, üşüme, terleme, gece_terlemesi,
              kilo_kaybı, kilo_artışı, şişme,
              sık_idrara_çıkma, yanmalı_idrara_çıkma, idrar_renginde_değişim,
              kaşıntı, döküntü, kızarıklık, sarılık, morarma,
              uyku_bozukluğu, aşırı_uyuma, anksiyete, panik, depresif_his, iritabilite,
              hareket_güçlüğü, yürüme_güçlüğü, kas_zayıflığı, tutukluk

            ── ZAMAN ────────────────────────────────
              sabah, öğlen, akşam, gece, sürekli, aktivite_sırası,
              yemek_sonrası, yemek_öncesi, belirsiz

            ── SIKLIK ───────────────────────────────
              her_gün, haftada_birkaç, ayda_birkaç, ara_sıra, ilk_kez, bugün

            ── ŞİDDET SEYRİ ─────────────────────────
              artıyor, azalıyor, sabit, geçti, dalgalı

            ── TETİKLEYİCİ / AZALTAN ───────────────
              taze_hava, su, ilaç, dinlenme, hareket, yemek, sıcak, soğuk,
              yatma, kalkma, stres, yorgunluk, soğuk_hava, sıcak_hava, yok

            ════════════════════════════════════════
            UYGULAMA KURALLARI
            ════════════════════════════════════════

            1. Metinde geçmeyen alana null yaz. Asla uydurma.
            2. Tek metinde birden fazla semptom varsa her biri ayrı obje olarak diziye ekle.
            3. Yorum, açıklama, özet yazma. Yalnızca JSON döndür.
            4. Aynı semptom aynı seansta iki farklı bağlamda geçiyorsa iki ayrı obje yaz.
            5. Hastanın kullandığı kelime listede yoksa en yakın kelimeyi seç, kendi kelimeni icat etme.
            6. JSON dışında hiçbir şey yazma. Markdown, kod bloğu, açıklama ekleme.
            7. Metinde hiç semptom yoksa: { "semptomlar": [] }

            Hasta metni: "
            """ + sentence + "\"";

        try
        {
            var requestBody = new
            {
                model = _model,
                messages = new[] { new { role = "user", content = prompt } }
            };

            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync("chat/completions", content);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                throw new Exception($"OpenRouter API hatası: {response.StatusCode} - {responseBody}");

            using var doc = JsonDocument.Parse(responseBody);
            var text = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString()
                ?? throw new Exception("AI'dan boş yanıt alındı.");

            var cleanedJson = CleanJsonResponse(text);

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
            var result = JsonSerializer.Deserialize<SymptomExtractionResult>(cleanedJson, options)
                ?? new SymptomExtractionResult();

            sw.Stop();
            _logger.LogInformation(
                "Semptom çıkarımı tamamlandı ({Elapsed:F3}s) — {Count} semptom",
                sw.Elapsed.TotalSeconds, result.Semptomlar.Count);

            return result;
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "Semptom çıkarımı HATA ({Elapsed:F3}s)", sw.Elapsed.TotalSeconds);
            return new SymptomExtractionResult(); // hata olursa boş döner, akışı kesmez
        }
    }

    private static string CleanJsonResponse(string text)
    {
        var cleaned = text.Trim();

        if (cleaned.StartsWith("```json"))
            cleaned = cleaned[7..];
        else if (cleaned.StartsWith("```"))
            cleaned = cleaned[3..];

        if (cleaned.EndsWith("```"))
            cleaned = cleaned[..^3];

        return cleaned.Trim();
    }
}
