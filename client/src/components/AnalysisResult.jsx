export default function AnalysisResult({ result }) {
  if (!result) return null;

  const scoreClass = result.dailyScore <= 3 ? "low" : result.dailyScore <= 5 ? "mid" : "high";

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
    </>
  );
}
