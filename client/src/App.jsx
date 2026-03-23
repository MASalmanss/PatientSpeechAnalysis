import { useState } from "react";
import PatientIdInput from "./components/PatientIdInput";
import AudioRecorder from "./components/AudioRecorder";
import AnalysisResult from "./components/AnalysisResult";
import "./App.css";

const API_URL = import.meta.env.VITE_API_URL || "http://localhost:5233";

function App() {
  const [patientId, setPatientId] = useState("");
  const [result, setResult] = useState(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState(null);

  const handleRecordingComplete = async (blob) => {
    if (!patientId || parseInt(patientId) <= 0) {
      setError("Lütfen geçerli bir Hasta ID girin.");
      return;
    }

    setLoading(true);
    setError(null);
    setResult(null);

    const formData = new FormData();
    formData.append("patientId", patientId);
    formData.append("audio", blob, "recording.webm");

    try {
      const response = await fetch(`${API_URL}/api/analyze/audio`, {
        method: "POST",
        body: formData,
      });

      const data = await response.json();

      if (!response.ok) {
        setError(data.error || "Bir hata oluştu.");
        return;
      }

      setResult(data);
    } catch (err) {
      setError("Sunucuya bağlanılamadı. API'nin çalıştığından emin olun.");
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="app">
      <header>
        <h1>Hasta Konuşma Analizi</h1>
        <p>Sesli mesajınızı kaydedin, AI analiz etsin.</p>
      </header>

      <main>
        <PatientIdInput value={patientId} onChange={setPatientId} />
        <AudioRecorder
          onRecordingComplete={handleRecordingComplete}
          disabled={loading}
        />

        {loading && (
          <div className="loading">
            <div className="spinner"></div>
            <p>Analiz ediliyor... Bu işlem biraz zaman alabilir.</p>
          </div>
        )}

        {error && (
          <div className="error-message">
            <p>{error}</p>
          </div>
        )}

        <AnalysisResult result={result} />
      </main>
    </div>
  );
}

export default App;
