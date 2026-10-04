"use client";

import { FormEvent, useEffect, useRef, useState } from "react";
import { useRouter } from "next/navigation";
import { ErrorState, LoadingState, PageHeader } from "../components/dashboard-shell";
import { csrfRequest } from "./dashboard-api";

type Group = { id: string; arabicName: string };
export function CoachCreate() {
  const router = useRouter();
  const [groups, setGroups] = useState<Group[] | null>(null);
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);
  const submitting = useRef(false);
  useEffect(() => {
    const controller = new AbortController();
    fetch("/api/v1/manage/structure/options", { signal: controller.signal })
      .then(response => { if (!response.ok) throw new Error(); return response.json(); })
      .then((options: { groups: Group[] }) => setGroups(options.groups))
      .catch(() => { if (!controller.signal.aborted) setError("تعذر تحميل المجموعات. أعد تحميل الصفحة للمحاولة."); });
    return () => controller.abort();
  }, []);
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (submitting.current) return;
    submitting.current = true; setBusy(true); setError("");
    const data = new FormData(event.currentTarget);
    try {
      const response = await csrfRequest("/api/v1/manage/coaches", "POST", {
        displayName: data.get("displayName"), phoneNumber: data.get("phoneNumber"), email: data.get("email") || null,
        isActive: data.get("isActive") === "on", groupIds: data.getAll("groupIds"),
      });
      const result = await response.json() as { membershipId: string };
      router.push(`/dashboard/academy/coaches/${result.membershipId}`);
    } catch (caught) { setError(caught instanceof Error ? caught.message : "تعذر إنشاء المدرب."); }
    finally { submitting.current = false; setBusy(false); }
  }
  return <><PageHeader title="إضافة مدرب" context="الأكاديمية / المدربون / إضافة مدرب" />
    {error && <ErrorState message={error} />}
    {!groups ? (!error && <LoadingState />) : <form className="form-shell coach-create" onSubmit={event => void submit(event)}>
      <fieldset disabled={busy}><legend>حساب المدرب الفردي</legend>
        <label>الاسم بالعربية<input name="displayName" required maxLength={160} autoComplete="name" /></label>
        <label>رقم الهاتف المصري<input name="phoneNumber" required type="tel" dir="ltr" autoComplete="tel" placeholder="01012345678" /></label>
        <label>البريد الإلكتروني — اختياري<input name="email" type="email" dir="ltr" maxLength={256} autoComplete="email" /></label>
        <label className="coach-check"><input name="isActive" type="checkbox" defaultChecked />حساب نشط داخل الأكاديمية</label>
        <p>الدخول إلى تطبيق المدرب بالهاتف ورمز التحقق، دون كلمة مرور مشتركة. الرسائل الحقيقية تحتاج مزود SMS؛ الرمز الثابت لبيئة Demo فقط.</p>
        <p>عند ربط هوية موجودة يجب تطابق بيانات صاحب الحساب؛ لن نغيّر اسمه أو هاتفه أو كلمة مروره.</p>
      </fieldset>
      <fieldset disabled={busy}><legend>المجموعات — اختياري</legend>
        {groups.length === 0 ? <p>لا توجد مجموعات متاحة. يمكنك إسناد المدرب لاحقًا.</p> : groups.map(group => <label className="coach-check" key={group.id}><input type="checkbox" name="groupIds" value={group.id} />{group.arabicName}</label>)}
      </fieldset>
      <div className="form-actions"><button className="primary-button" disabled={busy}>{busy ? "جارٍ الحفظ…" : "حفظ المدرب"}</button><button className="secondary-button" type="button" disabled={busy} onClick={() => router.push("/dashboard/academy/coaches")}>إلغاء</button></div>
    </form>}
  </>;
}
