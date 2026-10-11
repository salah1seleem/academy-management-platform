"use client";
import { useEffect, useState } from "react";
import { csrfRequest } from "./dashboard-api";
export function PlayerGroupChange({ enrollmentId, groupId }: { enrollmentId: string; groupId: string }) {
  const [groups, setGroups] = useState<{ id: string; arabicName: string; sportId: string; branchId: string; registrationFormat?: string }[]>([]); const [selected, setSelected] = useState(groupId); const [message, setMessage] = useState(""); const [busy, setBusy] = useState(false);
  useEffect(() => { fetch("/api/v1/manage/structure/options").then(r => r.json()).then(o => { const original = o.groups.find((g: { id: string }) => g.id === groupId); setGroups(o.groups.filter((g: { sportId: string; branchId: string; registrationFormat?: string }) => !original || g.sportId === original.sportId && g.branchId === original.branchId && (g.registrationFormat ?? "Group") === (original.registrationFormat ?? "Group"))); }).catch(() => setMessage("تعذر تحميل المجموعات.")); }, [groupId]);
  async function move() { setBusy(true); try { await csrfRequest(`/api/v1/people/enrollments/${enrollmentId}/group`, "PUT", { groupId: selected }); window.location.reload(); } catch (caught) { setMessage(caught instanceof Error ? caught.message : "تعذر تغيير المجموعة."); } finally { setBusy(false); } }
  return <details><summary>تعديل المجموعة</summary><label>المجموعة<select aria-label="تعديل المجموعة" value={selected} onChange={e => setSelected(e.target.value)}>{groups.map(g => <option value={g.id} key={g.id}>{g.arabicName}</option>)}</select></label><button disabled={busy || selected === groupId} onClick={() => void move()}>حفظ المجموعة</button>{message && <p role="alert">{message}</p>}</details>;
}
