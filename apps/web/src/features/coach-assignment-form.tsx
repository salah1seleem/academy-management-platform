"use client";

import { FormEvent, useState } from "react";
import { ErrorState, LoadingState } from "../components/dashboard-shell";
import { announceSaveSuccess, csrfRequest, DashboardRequestError } from "./dashboard-api";

class AssignmentRejected extends Error {}

type Item = { id: string; arabicName: string; branchId?: string };
type Props = {
  options: { coaches: Item[]; groups: Item[] };
  loaded: boolean;
  optionsError?: string;
  coachId?: string;
  groupId?: string;
  branchId?: string;
  onSaved: () => void;
  onCancel: () => void;
};

export function CoachAssignmentForm({ options, loaded, optionsError, coachId = "", groupId = "", branchId = "", onSaved, onCancel }: Props) {
  const [coach, setCoach] = useState(coachId), [group, setGroup] = useState(groupId);
  const [busy, setBusy] = useState(false), [error, setError] = useState("");
  const coaches = coachId ? options.coaches.filter(c => c.id === coachId) : options.coaches;
  const groups = options.groups.filter(g => (!groupId || g.id === groupId) && (!branchId || g.branchId === branchId));
  const valid = coaches.some(c => c.id === coach) && groups.some(g => g.id === group);
  async function save(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (busy || !valid) return;
    const selectedCoach = coach, selectedGroup = group;
    setBusy(true); setError("");
    try {
      const path = `/api/v1/manage/groups/${encodeURIComponent(selectedGroup)}/coaches`;
      const response = await csrfRequest(path, "POST", { membershipId: selectedCoach }, {}, { announce: false });
      const id: unknown = await response.json();
      const read = await fetch(`${path}/${encodeURIComponent(selectedCoach)}`, { cache: "no-store" });
      if (!read.ok) throw new AssignmentRejected("لم يتأكد حفظ الإسناد. أعد المحاولة بنفس الاختيار أو راجع المجموعة.");
      const stored = await read.json() as { id: string; trainingGroupId: string; academyMembershipId: string; isActive: boolean };
      if (typeof id !== "string" || stored.id !== id || stored.trainingGroupId !== selectedGroup || stored.academyMembershipId !== selectedCoach || stored.isActive !== true) {
        throw new AssignmentRejected("لم يتطابق الإسناد المحفوظ مع الاختيار. حدّث الصفحة وراجع المجموعة.");
      }
      announceSaveSuccess("تم حفظ الإسناد والتحقق منه.");
      onSaved();
    } catch (caught) {
      setError(caught instanceof AssignmentRejected || caught instanceof DashboardRequestError ? caught.message : "تعذر حفظ الإسناد. راجع الاتصال وحاول مجددًا.");
    } finally { setBusy(false); }
  }
  if (optionsError) return <ErrorState message={optionsError} />;
  if (!loaded) return <LoadingState />;
  return <form className="form-shell" onSubmit={event => void save(event)}>
    <label>المدرب<select name="membershipId" value={coach} disabled={busy} required onChange={e => { setCoach(e.target.value); setError(""); }}><option value="">اختر المدرب</option>{coaches.map(c => <option key={c.id} value={c.id}>{c.arabicName}</option>)}</select></label>
    <label>المجموعة<select name="groupId" value={group} disabled={busy} required onChange={e => { setGroup(e.target.value); setError(""); }}><option value="">اختر المجموعة</option>{groups.map(g => <option key={g.id} value={g.id}>{g.arabicName}</option>)}</select></label>
    {coaches.length === 0 && <p role="alert">المدرب المحدد غير متاح ضمن صلاحياتك. ارجع للقائمة واختر حسابًا نشطًا.</p>}
    {groups.length === 0 && <p role="alert">لا توجد مجموعة فعالة متاحة في النطاق المختار.</p>}
    {error && <ErrorState message={error} />}
    <div className="form-actions"><button className="primary-button" disabled={busy || !valid}>{busy ? "جارٍ الحفظ والتحقق…" : "حفظ"}</button><button type="button" disabled={busy} onClick={onCancel}>إلغاء</button></div>
  </form>;
}
