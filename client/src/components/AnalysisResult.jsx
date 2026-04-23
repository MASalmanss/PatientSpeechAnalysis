import { useState, useRef } from "react";

const API_URL = import.meta.env.VITE_API_URL || "http://localhost:5233";

export default function AnalysisResult({ result, authFetch }) {
  if (!result) return null;

  const [isPlaying, setIsPlaying] = useState(false);
  const [ttsLoading, setTtsLoading] = useState(false);
  const [ttsError, setTtsError] = useState(null);
  const audioRef = useRef(null);

  const scoreClass = result.dailyScore <= 3 ? "low" : result.dailyScore <= 5 ? "mid" : "high";

  const handleListen = async () => {
    // Çalıyorsa durdur
    if (isPlaying) {
      audioRef.current?.pause();
      setIsPlaying(false);
      return;
    }

    setTtsError(null);
    setTtsLoading(true);

    try {
      const fetchFn = authFetch ?? fetch;
      const response = await fetchFn(`${API_URL}/api/tts`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ text: result.summary }),
      });

      if (!response.ok) {
        const err = await response.json().catch(() => ({ error: "Bilinmeyen hata" }));
        throw new Error(err.error || `HTTP ${response.status}`);
      }

      const blob = await response.blob();
      const url = URL.createObjectURL(blob);
      const audio = new Audio(url);
      audioRef.current = audio;

      audio.onended = () => {
        setIsPlaying(false);
        URL.revokeObjectURL(url);
      };

      audio.onerror = () => {
        setIsPlaying(false);
        setTtsError("Ses oynatılamadı.");
        URL.revokeObjectURL(url);
      };

      await audio.play();
      setIsPlaying(true);
    } catch (err) {
      setTtsError(err.message || "Ses sentezi başarısız oldu.");
    } finally {
      setTtsLoading(false);
    }
  };

  return (
    <>
      <div className="result-row">
        <span className="label">Transkript</span>
        <span className="value transcript">"{result.patientSentence}"</span>
      </div>

      <div className="result-row">
        <span className="label">Duygu Durumu</span>
        <span className="value mood">{result.mood}</span>
      </div>

      <div className="result-row">
        <span className="label">Acil Durum</span>
        <span className={`value emergency ${result.isEmergency ? "danger" : "safe"}`}>
          {result.isEmergency ? "⚠ ACİL" : "✓ Normal"}
        </span>
      </div>

      <div className="result-row">
        <span className="label">Günlük Skor</span>
        <span className={`value score score-${scoreClass}`}>
          {result.dailyScore} / 10
        </span>
      </div>

      <div className="result-row summary">
        <span className="label">Özet</span>
        <p className="value">{result.summary}</p>
      </div>

      <div className="result-row tts-row">
        <button
          className={`btn btn-listen ${isPlaying ? "btn-listen--playing" : ""}`}
          onClick={handleListen}
          disabled={ttsLoading || !result.summary}
        >
          {ttsLoading ? "Yükleniyor..." : isPlaying ? "⏹ Durdur" : "🔊 Dinle"}
        </button>
        {ttsError && <span className="tts-error">{ttsError}</span>}
      </div>
    </>
  );
}
