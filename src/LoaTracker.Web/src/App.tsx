import { FormEvent, useEffect, useState } from "react";
import { Link, Route, Routes, useNavigate, useParams } from "react-router-dom";
import { ApiError, getLoaStatus, LoaStatus, uploadLoa } from "./api";

export function App() {
  return (
    <div className="page">
      <header className="masthead">
        <p className="eyebrow">Letter of authority</p>
        <h1><Link to="/">LoA Tracker</Link></h1>
        <p className="lede">Send a letter of authority and follow it with the tracking id.</p>
      </header>
      <main>
        <Routes>
          <Route path="/" element={<SubmitPage />} />
          <Route path="/status/:trackingId" element={<StatusPage />} />
        </Routes>
      </main>
    </div>
  );
}

function SubmitPage() {
  const navigate = useNavigate();
  const [adviserName, setAdviserName] = useState("");
  const [memberEmail, setMemberEmail] = useState("");
  const [schemeName, setSchemeName] = useState("");
  const [file, setFile] = useState<File | null>(null);
  const [lookupId, setLookupId] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);

  async function onSubmit(event: FormEvent) {
    event.preventDefault();
    setError(null);

    if (!file) {
      setError("Choose the letter of authority file.");
      return;
    }

    setSubmitting(true);
    try {
      const result = await uploadLoa({ adviserName, memberEmail, schemeName, file });
      navigate(`/status/${result.trackingId}`);
    } catch (caught) {
      setError(caught instanceof ApiError ? caught.message : "The upload failed.");
    } finally {
      setSubmitting(false);
    }
  }

  function onLookup(event: FormEvent) {
    event.preventDefault();
    const trackingId = lookupId.trim();
    if (trackingId) {
      navigate(`/status/${trackingId}`);
    }
  }

  return (
    <div className="stack">
      <section className="card">
        <h2>Upload a letter</h2>
        <form className="form" onSubmit={onSubmit}>
          <label>
            Adviser name
            <input value={adviserName} onChange={(event) => setAdviserName(event.target.value)} required />
          </label>
          <label>
            Member email
            <input type="email" value={memberEmail} onChange={(event) => setMemberEmail(event.target.value)} required />
          </label>
          <label>
            Scheme name
            <input value={schemeName} onChange={(event) => setSchemeName(event.target.value)} />
          </label>
          <label className="file">
            Letter of authority
            <input
              type="file"
              accept=".pdf,.png,.jpg,.jpeg,application/pdf,image/png,image/jpeg"
              onChange={(event) => setFile(event.target.files?.[0] ?? null)}
              required
            />
            <span>{file ? file.name : "PDF, PNG, or JPEG. 10 MB maximum."}</span>
          </label>
          {error ? <p className="error" role="alert">{error}</p> : null}
          <button type="submit" disabled={submitting}>{submitting ? "Uploading…" : "Submit letter"}</button>
        </form>
      </section>

      <section className="card">
        <h2>Check a tracking id</h2>
        <form className="form lookup" onSubmit={onLookup}>
          <label>
            Tracking id
            <input value={lookupId} onChange={(event) => setLookupId(event.target.value)} required />
          </label>
          <button type="submit">Show status</button>
        </form>
      </section>
    </div>
  );
}

function StatusPage() {
  const { trackingId = "" } = useParams();
  const [status, setStatus] = useState<LoaStatus | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let active = true;

    async function load() {
      try {
        const next = await getLoaStatus(trackingId);
        if (active) {
          setStatus(next);
          setError(null);
        }
      } catch (caught) {
        if (active) {
          setError(caught instanceof ApiError ? caught.message : "The status could not be loaded.");
        }
      }
    }

    void load();
    const timer = window.setInterval(() => void load(), 5000);
    return () => {
      active = false;
      window.clearInterval(timer);
    };
  }, [trackingId]);

  const documentName = status?.history.find((item) => item.documentFileName)?.documentFileName;

  return (
    <section className="card">
      <p className="eyebrow">Tracking id</p>
      <p className="tracking">{trackingId}</p>
      {error ? <p className="error" role="alert">{error}</p> : null}
      {status ? (
        <>
          <p className="status-line">Current status <strong>{status.currentStatus}</strong></p>
          {documentName ? (
            <p><a href={`/loa/${encodeURIComponent(trackingId)}/document`}>Download {documentName}</a></p>
          ) : null}
          <table>
            <thead>
              <tr>
                <th>When</th>
                <th>Status</th>
                <th>Adviser</th>
                <th>Member</th>
                <th>Scheme</th>
              </tr>
            </thead>
            <tbody>
              {status.history.map((item, index) => (
                <tr key={`${item.timestamp ?? "event"}-${index}`}>
                  <td>{formatWhen(item.timestamp)}</td>
                  <td>{item.status}</td>
                  <td>{item.adviserName}</td>
                  <td>{item.mememberEmail}</td>
                  <td>{item.schemeName}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </>
      ) : error ? null : <p>Loading status…</p>}
    </section>
  );
}

function formatWhen(value?: string) {
  if (!value) {
    return "—";
  }
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString();
}
