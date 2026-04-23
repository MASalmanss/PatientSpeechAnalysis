import { useState, useRef, useEffect, useCallback } from "react";
import DnaBackground from "./components/DnaBackground";
import PatientIdInput from "./components/PatientIdInput";
import AudioRecorder from "./components/AudioRecorder";
import AnalysisResult from "./components/AnalysisResult";
import "./App.css";

const API_URL = import.meta.env.VITE_API_URL || "http://localhost:5233";
const WS_URL = API_URL.replace(/^http/, "ws");

function App() {
  // ── Auth ────────────────────────────────────────────────────────────────────
  const [authStatus, setAuthStatus] = useState("loading"); // "loading" | "unauthenticated" | "authenticated"
  const [authError, setAuthError] = useState(null);
  const accessTokenRef = useRef(null); // in-memory only, never localStorage

  // Refresh cookie ile sessiz login dene
  const tryRefresh = useCallback(async () => {
    try {
      const res = await fetch(`${API_URL}/api/auth/refresh`, {
        method: "POST",
        credentials: "include",
      });
      if (res.ok) {
        const data = await res.json();
        accessTokenRef.current = data.accessToken;
        setAuthStatus("authenticated");
        return true;
      }
    } catch {
      // sunucuya ulaşılamıyor veya cookie yok — sessizce geç
    }
    setAuthStatus("unauthenticated");
    return false;
  }, []);

  // İlk yüklemede sessiz refresh dene
  useEffect(() => {
    tryRefresh();
  }, [tryRefresh]);

  // "Token Al" butonuna basınca
  const handleGetToken = async () => {
    setAuthError(null);
    try {
      const res = await fetch(`${API_URL}/api/auth/token`, {
        method: "POST",
        credentials: "include",
      });
      if (!res.ok) {
        const err = await res.json().catch(() => ({}));
        throw new Error(err.error || `HTTP ${res.status}`);
      }
      const data = await res.json();
      accessTokenRef.current = data.accessToken;
      setAuthStatus("authenticated");
    } catch (err) {
      setAuthError(err.message || "Token alınamadı. Sunucu çalışıyor mu?");
    }
  };

  // Çıkış
  const handleLogout = async () => {
    try {
      await fetch(`${API_URL}/api/auth/logout`, {
        method: "POST",
        credentials: "include",
      });
    } catch {
      // hata olsa bile state temizle
    }
    accessTokenRef.current = null;
    setAuthStatus("unauthenticated");
    setResult(null);
    setError(null);
    setLiveText("");
  };

  // Auth wrapper: 401 gelirse token yenilemeyi dene, sonra tekrar iste
  const authFetch = useCallback(async (url, options = {}) => {
    const token = accessTokenRef.current;
    const headers = {
      ...(options.headers || {}),
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
    };

    let res = await fetch(url, { ...options, headers, credentials: "include" });

    if (res.status === 401) {
      const refreshed = await tryRefresh();
      if (refreshed) {
        const newToken = accessTokenRef.current;
        res = await fetch(url, {
          ...options,
          headers: {
            ...(options.headers || {}),
            Authorization: `Bearer ${newToken}`,
          },
          credentials: "include",
        });
      } else {
        setAuthStatus("unauthenticated");
      }
    }

    return res;
  }, [tryRefresh]);

  // ── Ana uygulama state ───────────────────────────────────────────────────────
  const [patientId, setPatientId] = useState("");
  const [result, setResult] = useState(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState(null);
  const [wsStatus, setWsStatus] = useState("idle");
  const [liveText, setLiveText] = useState("");

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

    const token = accessTokenRef.current;
    if (!token) {
      setError("Oturum süresi dolmuş. Lütfen tekrar token alın.");
      setAuthStatus("unauthenticated");
      return false;
    }

    setLoading(true);
    setError(null);
    setResult(null);
    setLiveText("");
    setWsStatusSync("connecting");

    const wsUrl = `${WS_URL}/ws/analyze?patientId=${patientId}&token=${encodeURIComponent(token)}`;
    const ws = new WebSocket(wsUrl);
    ws.binaryType = "arraybuffer";

    ws.onopen = () => {
      setWsStatusSync("connected");
      ws.send("START");
    };

    ws.onmessage = (event) => {
      try {
        const msg = JSON.parse(event.data);
        if (msg.type === "partial") {
          setLiveText(msg.text);
        } else if (msg.type === "result") {
          setResult(msg.data);
          setLiveText("");
          setLoading(false);
          setWsStatusSync("done");
        } else if (msg.type === "error") {
          setError(msg.message);
          setLoading(false);
          setWsStatusSync("error");
        }
      } catch {
        setError("Sunucudan geçersiz yanıt alındı.");
        setLoading(false);
        setWsStatusSync("error");
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
    wsStatus === "connected"  ? "Kayıt aktarılıyor..." :
    "Transkripsiyon ve analiz ediliyor...";

  // ── Render: yükleniyor ───────────────────────────────────────────────────────
  if (authStatus === "loading") {
    return (
      <>
        <DnaBackground />
        <div className="app">
          <div className="auth-screen">
            <div className="spinner" style={{ margin: "0 auto" }}></div>
          </div>
        </div>
      </>
    );
  }

  // ── Render: giriş ekranı ─────────────────────────────────────────────────────
  if (authStatus === "unauthenticated") {
    return (
      <>
        <DnaBackground />
        <div className="app">
          <div className="auth-screen">
            <div className="glass auth-card">
              <div className="auth-icon">🔐</div>
              <h1 className="auth-title">Hasta Konuşma Analizi</h1>
              <p className="auth-subtitle">
                Sisteme erişmek için oturum başlatın.
              </p>
              <button
                className="btn btn-token"
                onClick={handleGetToken}
              >
                Token Al &amp; Giriş Yap
              </button>
              {authError && (
                <p className="auth-error">{authError}</p>
              )}
            </div>
          </div>
        </div>
      </>
    );
  }

  // ── Render: ana uygulama ─────────────────────────────────────────────────────
  return (
    <>
      <DnaBackground />
      <div className="app">
        <div className="app-content">
          <header>
            <h1>Hasta Konuşma Analizi</h1>
            <p>Sesli mesajınızı kaydedin, AI analiz etsin.</p>
            <button className="btn btn-logout" onClick={handleLogout}>
              Çıkış
            </button>
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

          {liveText && (
            <div className="glass live-transcript">
              <p className="live-label">Canlı Transkript</p>
              <p className="live-text">"{liveText}"</p>
            </div>
          )}

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
                <AnalysisResult result={result} authFetch={authFetch} />
              </div>
            </div>
          )}
        </div>
      </div>
    </>
  );
}

export default App;
