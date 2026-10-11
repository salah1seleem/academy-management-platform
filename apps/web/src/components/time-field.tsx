"use client";

import { useState } from "react";

// Submit an unambiguous 24-hour value; keep the visible control independent of
// browser-native time inputs, whose segments are difficult to use in RTL.
export function TimeField({ name, label, defaultValue = "", required = false }: { name: string; label: string; defaultValue?: string; required?: boolean }) {
  const match = /^(\d{2}):(\d{2})/.exec(defaultValue);
  const [hour, setHour] = useState(match ? String(Number(match[1]) % 12 || 12) : "");
  const [minute, setMinute] = useState(match?.[2] ?? "");
  const [period, setPeriod] = useState(match ? Number(match[1]) >= 12 ? "pm" : "am" : "");
  const value = hour && minute && period ? `${String(Number(hour) % 12 + (period === "pm" ? 12 : 0)).padStart(2, "0")}:${minute}` : "";
  const mustComplete = required || Boolean(hour || minute || period);
  return <fieldset className="time-field"><legend>{label}</legend><input type="hidden" name={name} value={value} />
    <div className="time-field-controls">
      <select aria-label={`${label} — الساعة`} required={mustComplete} value={hour} onChange={e => setHour(e.target.value)}><option value="">الساعة</option>{Array.from({ length: 12 }, (_, i) => i + 1).map(h => <option key={h} value={h}>{h}</option>)}</select>
      <span aria-hidden="true">:</span>
      <select aria-label={`${label} — الدقيقة`} required={mustComplete} value={minute} onChange={e => setMinute(e.target.value)}><option value="">الدقيقة</option>{Array.from({ length: 60 }, (_, i) => String(i).padStart(2, "0")).map(m => <option key={m} value={m}>{m}</option>)}</select>
      <select aria-label={`${label} — الفترة`} required={mustComplete} value={period} onChange={e => setPeriod(e.target.value)}><option value="">الفترة</option><option value="am">صباحًا</option><option value="pm">مساءً</option></select>
    </div>
    {!required && <button className="time-field-clear" type="button" onClick={() => { setHour(""); setMinute(""); setPeriod(""); }}>مسح الوقت</button>}
  </fieldset>;
}
