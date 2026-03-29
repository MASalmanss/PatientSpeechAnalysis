import { useState, useRef } from "react";
import DnaBackground from "./components/DnaBackground";
import PatientIdInput from "./components/PatientIdInput";
import AudioRecorder from "./components/AudioRecorder";
import AnalysisResult from "./components/AnalysisResult";
import "./App.css";

const API_URL = import.meta.env.VITE_API_URL || "http://localhost:5233";
const WS_URL = API_URL.replace(/^http/, "ws");

function App() {
  const [patientId, setPatientId] = useState("");
  const [result, setResult] = useState(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState(null);
  const [wsStatus, setWsStatus] = useState("idle");

  const wsRef = useRef(null);
  const wsStatusRef = useRef("idle");

  const setWsStatusSync = (s) => {
    wsStatusRef.current = s;
    setWsStatus(s);
  };

  const openWebSocket = () => {
    if (!patientId || parseInt(patientId) <= 0) {
      setError("Lütfen geçerli bir Hasta ID girin.");
      return false;
    }

    setLoading(true);
    setError(null);
    setResult(null);
    setWsStatusSync("connecting");

    const ws = new WebSocket(`${WS_URL}/ws/analyze?patientId=${patientId}`);
    ws.binaryType = "arraybuffer";

    ws.onopen = () => {
      setWsStatusSync("connected");
      ws.send("START");
    };

    ws.onmessage = (event) => {
      const text = event.data;
      if (typeof text === "string") {
        if (text.startsWith("ERROR:")) {
          setError(text.slice(6));
          setLoading(false);
          setWsStatusSync("error");
        } else {
          try {
            const data = JSON.parse(text);
            setResult(data);
            setWsStatusSync("done");
          } catch {
            setError("Sunucudan geçersiz yanıt alındı.");
            setWsStatusSync("error");
          } finally {
            setLoading(false);
          }
        }
      }
    };

    ws.onerror = () => {
      setError("WebSocket bağlantı hatası. Sunucuya ulaşılamıyor.");
      setLoading(false);
      setWsStatusSync("error");
    };

    ws.onclose = (event) => {
      wsRef.current = null;
      if (wsStatusRef.current !== "done" && wsStatusRef.current !== "error") {
        if (!event.wasClean) {
          setError("Bağlantı beklenmedik şekilde kapandı.");
          setLoading(false);
          setWsStatusSync("error");
        }
      }
    };

    wsRef.current = ws;
    return true;
  };

  const handleChunk = (arrayBuffer) => {
    if (wsRef.current?.readyState === WebSocket.OPEN) {
      wsRef.current.send(arrayBuffer);
    }
  };

  const handleStop = (micError) => {
    if (micError) {
      setError(micError);
      setLoading(false);
      setWsStatusSync("error");
      wsRef.current?.close();
      return;
    }
    if (wsRef.current?.readyState === WebSocket.OPEN) {
      wsRef.current.send("END");
      setWsStatusSync("processing");
    }
  };

  const loadingMessage =
    wsStatus === "connecting" ? "Bağlanıyor..." :
    wsStatus === "connected" ? "Kayıt aktarılıyor..." :
    "Transkripsiyon ve analiz ediliyor...";

  return (
    <>
      <DnaBackground />
      <div className="app">
        <div className="app-content">
          <header>
            <h1>Hasta Konuşma Analizi</h1>
            <p>Sesli mesajınızı kaydedin, AI analiz etsin.</p>
          </header>

          <PatientIdInput value={patientId} onChange={setPatientId} />

          <div className="glass audio-recorder">
            <AudioRecorder
              onStartRecording={openWebSocket}
              onChunk={handleChunk}
              onStop={handleStop}
              disabled={loading}
            />
          </div>

          {loading && (
            <div className="glass loading">
              <div className="spinner"></div>
              <p>{loadingMessage}</p>
            </div>
          )}

          {error && (
            <div className="error-message">
              <p>{error}</p>
            </div>
          )}

          {result && (
            <div className="analysis-result">
              <h2>Analiz Sonucu</h2>
              <div className="glass result-card">
                <AnalysisResult result={result} />
              </div>
            </div>
          )}
        </div>
      </div>
    </>
  );
}

export default App;
