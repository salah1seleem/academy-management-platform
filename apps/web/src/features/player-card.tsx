"use client";

import Image from "next/image";
import { FormEvent, useEffect, useState } from "react";
import { Printer, QrCode, RefreshCw, ShieldCheck } from "lucide-react";
import { ErrorState, LoadingState } from "../components/dashboard-shell";
import { csrfRequest } from "./dashboard-api";
import { formatDateAr } from "../lib/formatters";

type Card = { active: boolean; version: number; code?: string; pngBase64?: string; expiresAtUtc?: string; message: string; playerName?: string; playerCode?: string };
export function PlayerCardPanel({ playerId, guardian = false, name = "" }: { playerId: string; guardian?: boolean; name?: string }) {
  const [card, setCard] = useState<Card | null>(null), [error, setError] = useState(""), [busy, setBusy] = useState(false), [access, setAccess] = useState({ add: false, edit: false }), [intent, setIntent] = useState("");
  const path = guardian ? `/api/v1/guardian/children/${playerId}/card` : `/api/v1/attendance/players/${playerId}/card`;
  useEffect(() => { const abort = new AbortController(); void (async () => {
    const r = await fetch(path, { cache: "no-store", signal: abort.signal }); if (!r.ok) throw new Error("تعذر تحميل بطاقة اللاعب."); setCard(await r.json());
    if (!guardian) { const p = await fetch("/api/v1/my-permissions", { signal: abort.signal }); if (p.ok && (await p.json() as string[]).includes("attendance.manage")) { const a = await fetch("/api/v1/my-access", { signal: abort.signal }); if (a.ok) { const grants = await a.json() as { useActionGrants: boolean; actions: { module: string; read: boolean; add: boolean; edit: boolean }[] }; const attendance = grants.actions?.find(g => g.module === "attendance"); setAccess(grants.useActionGrants ? { add: attendance?.read === true && attendance.add, edit: attendance?.read === true && attendance.edit } : { add: true, edit: true }); } } }
  })().catch(e => { if (e.name !== "AbortError") setError(e.message); }); return () => abort.abort(); }, [path, guardian]);
  async function change(revoke: boolean) {
    if (!card || busy || !(revoke ? access.edit : access.add) || guardian) return; setBusy(true); setError("");
    const key = intent || crypto.randomUUID(); setIntent(key);
    try {
      await csrfRequest(`${path}${revoke ? "/revoke" : ""}`, revoke ? "PUT" : "POST", { expectedVersion: card.version }, revoke ? {} : { "Idempotency-Key": key });
      const r = await fetch(path, { cache: "no-store" }); if (!r.ok) throw new Error("تمت العملية؛ تعذر تحديث البطاقة، أعد فتح الصفحة."); setCard(await r.json()); setIntent("");
    } catch (e) { setError(e instanceof Error ? e.message : "تعذر تحديث البطاقة."); } finally { setBusy(false); }
  }
  return <section className="detail-card player-card-panel" aria-label="بطاقة اللاعب"><h2><QrCode aria-hidden="true" /> بطاقة اللاعب</h2>{error && <ErrorState message={error} />}{!card && !error ? <LoadingState /> : card && <>
    <div className="player-card-paper"><ShieldCheck aria-hidden="true" /><h3>{name || card.playerName || "بطاقة الحضور"}{card.playerCode && <small dir="ltr">{card.playerCode}</small>}</h3>{card.active && card.pngBase64 ? <Image unoptimized src={`data:image/png;base64,${card.pngBase64}`} width={256} height={256} alt="QR بطاقة اللاعب — معرّف مبهم" /> : <p>{card.message}</p>}{card.active && card.expiresAtUtc && <small>صالحة حتى {formatDateAr(card.expiresAtUtc.slice(0, 10))}</small>}</div>
    <p className="muted">البطاقة معرّف فقط. يتأكد الموظف من الطفل ويحفظ الحضور داخل الحصة. الاسم أو كود اللاعب متاحان دائمًا كبديل يدوي.</p>
    <div className="row-actions">{card.active && <button className="secondary-button" onClick={() => window.print()}><Printer aria-hidden="true" /> طباعة البطاقة</button>}{!guardian && access.add && <button className="primary-button" disabled={busy} onClick={() => void change(false)}><RefreshCw aria-hidden="true" />{card.active ? "إصدار بديل وإلغاء القديمة" : "إصدار بطاقة"}</button>}{!guardian && access.edit && card.active && <button className="danger-button" disabled={busy} onClick={() => void change(true)}>إلغاء البطاقة</button>}</div>
  </>}</section>;
}

export function PlayerCardLookup({ sessionId, onResolved }: { sessionId: string; onResolved: (code: string) => void }) {
  const [value, setValue] = useState(""), [error, setError] = useState(""), [message, setMessage] = useState(""), [busy, setBusy] = useState(false);
  async function resolve(e: FormEvent) { e.preventDefault(); if (busy || !value.trim()) return; setBusy(true); setError(""); setMessage(""); try {
    const r = await csrfRequest(`/api/v1/attendance/sessions/${sessionId}/player-card/resolve`, "POST", { code: value.trim() });
    const data = await r.json() as { player: { arabicName: string; playerCode: string }; message: string }; onResolved(data.player.playerCode); setValue(""); setMessage(`${data.player.arabicName} · ${data.message}`);
  } catch (e) { setError(e instanceof Error ? e.message : "البطاقة غير متاحة."); } finally { setBusy(false); } }
  return <details className="player-card-lookup"><summary><QrCode aria-hidden="true" /> البحث ببطاقة اللاعب</summary><form onSubmit={e => void resolve(e)}><label>محتوى QR<input value={value} onChange={e => setValue(e.target.value)} dir="ltr" autoComplete="off" spellCheck={false} maxLength={200} placeholder="امسح بقارئ متصل أو ألصق محتوى البطاقة" /></label><button className="secondary-button" disabled={busy || !value.trim()}>تحقق من البطاقة</button></form><p className="muted">القراءة تحدد اللاعب فقط؛ اختر الحالة ثم اضغط حفظ الحضور. يمكنك البحث بالاسم بدل البطاقة.</p>{error && <ErrorState message={error} />}{message && <p role="status">{message}</p>}</details>;
}
