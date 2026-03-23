import { useReactMediaRecorder } from "react-media-recorder";

export default function AudioRecorder({ onRecordingComplete, disabled }) {
  const { status, startRecording, stopRecording } = useReactMediaRecorder({
    audio: true,
    mediaRecorderOptions: { mimeType: "audio/webm" },
    onStop: (blobUrl, blob) => {
      onRecordingComplete(blob);
    },
  });

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
