import { useState, useRef } from "react";

export default function AudioRecorder({ onStartRecording, onChunk, onStop, disabled }) {
  const [status, setStatus] = useState("idle"); // "idle" | "recording" | "stopped"
  const mediaRecorderRef = useRef(null);
  const streamRef = useRef(null);

  const startRecording = async () => {
    const ok = onStartRecording?.();
    if (ok === false) return;

    let stream;
    try {
      stream = await navigator.mediaDevices.getUserMedia({ audio: true });
    } catch (err) {
      onStop?.(`Mikrofon erişimi reddedildi: ${err.message}`);
      return;
    }

    streamRef.current = stream;
    const recorder = new MediaRecorder(stream, { mimeType: "audio/webm" });

    recorder.ondataavailable = (e) => {
      if (e.data && e.data.size > 0) {
        e.data.arrayBuffer().then((buf) => onChunk?.(buf));
      }
    };

    recorder.onstop = () => {
      stream.getTracks().forEach((t) => t.stop());
      setStatus("stopped");
      onStop?.();
    };

    mediaRecorderRef.current = recorder;
    recorder.start(250);
    setStatus("recording");
  };

  const stopRecording = () => {
    mediaRecorderRef.current?.stop();
  };

  const isRecording = status === "recording";

  return (
    <div className="audio-recorder">
      <div className={`status-indicator ${isRecording ? "recording" : ""}`}>
        {status === "idle" && "Kayda hazır"}
        {status === "recording" && "Kayıt yapılıyor..."}
        {status === "stopped" && "Kayıt tamamlandı"}
      </div>
      <div className="recorder-buttons">
        {!isRecording ? (
          <button
            onClick={startRecording}
            disabled={disabled}
            className="btn btn-record"
          >
            Kaydet
          </button>
        ) : (
          <button onClick={stopRecording} className="btn btn-stop">
            Durdur
          </button>
        )}
      </div>
    </div>
  );
}
