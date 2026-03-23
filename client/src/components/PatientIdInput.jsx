export default function PatientIdInput({ value, onChange }) {
  return (
    <div className="patient-id-input">
      <label htmlFor="patientId">Hasta ID</label>
      <input
        id="patientId"
        type="number"
        min="1"
        placeholder="Hasta numarası girin"
        value={value}
        onChange={(e) => onChange(e.target.value)}
      />
    </div>
  );
}
