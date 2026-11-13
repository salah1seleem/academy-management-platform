"use client";

import { FormEvent, useEffect, useRef, useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { ErrorState, LoadingState, PageHeader } from "../components/dashboard-shell";
import { egyptianStaffMobile } from "../lib/number-input";
import { csrfRequest } from "./dashboard-api";

type Group = { id: string; arabicName: string };
export function CoachCreate() {
  const router = useRouter();
  const [groups, setGroups] = useState<Group[] | null>(null);
  const [error, setError] = useState("");
  const [phoneError, setPhoneError] = useState("");
  const [groupLoadFailed, setGroupLoadFailed] = useState(false);
  const [attempt, setAttempt] = useState(0);
  const [busy, setBusy] = useState(false);
  const phone = useRef<HTMLInputElement>(null);
  const submitting = useRef(false);
  useEffect(() => {
    const controller = new AbortController();
    fetch("/api/v1/manage/structure/options", { signal: controller.signal })
      .then(response => { if (!response.ok) throw new Error(); return response.json(); })
      .then((options: { groups: Group[] }) => setGroups(options.groups))
      .catch(() => { if (!controller.signal.aborted) setGroupLoadFailed(true); });
    return () => controller.abort();
  }, [attempt]);
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (submitting.current) return;
    const data = new FormData(event.currentTarget);
    const number = egyptianStaffMobile(String(data.get("phoneNumber") ?? ""));
    if (!number) {
      setError("");
      setPhoneError("اكتب رقم موبايل مصري صحيح.");
      phone.current?.focus();
      return;
    }
    submitting.current = true; setBusy(true); setError("");
    try {
      await csrfRequest("/api/v1/manage/coaches", "POST", {
        displayName: data.get("displayName"), phoneNumber: number, email: data.get("email") || null,
        isActive: data.get("isActive") === "on", groupIds: data.getAll("groupIds"),
      });
      router.push("/dashboard/academy/coaches");
    } catch (caught) { setError(caught instanceof Error ? caught.message : "تعذر إنشاء المدرب."); }
    finally { submitting.current = false; setBusy(false); }
  }
  return <><PageHeader title="إضافة مدرب" context="الأكاديمية / المدربون / إضافة مدرب" />
    {error && <ErrorState message={error} />}
    {groupLoadFailed ? <section className="detail-card">
      <ErrorState message="تعذر تحميل المجموعات." />
      <div className="form-actions">
        <button type="button" className="primary-button" onClick={() => { setGroupLoadFailed(false); setAttempt(attempt + 1); }}>إعادة المحاولة</button>
        <Link className="secondary-button" href="/dashboard/academy/coaches">إلغاء</Link>
      </div>
    </section> : !groups ? <LoadingState /> : <form className="form-shell coach-create" onSubmit={event => void submit(event)}>
      <fieldset disabled={busy}><legend>بيانات المدرب</legend>
        <label>الاسم بالعربية<input name="displayName" required maxLength={160} autoComplete="name" /></label>
        <div className="coach-phone-field">
          <label htmlFor="coach-phone">رقم الهاتف المصري</label>
          <input id="coach-phone" ref={phone} name="phoneNumber" required type="tel" dir="ltr" autoComplete="tel"
            aria-invalid={Boolean(phoneError)} aria-describedby={phoneError ? "coach-phone-error coach-phone-hint" : "coach-phone-hint"}
            onChange={() => setPhoneError("")} />
          {phoneError && <small id="coach-phone-error" role="alert" className="coach-field-error">{phoneError}</small>}
          <small id="coach-phone-hint">مثل <bdi dir="ltr">01012345678</bdi> أو <bdi dir="ltr">+201012345678</bdi>. يستخدمه المدرب للدخول برمز تحقق.</small>
        </div>
        <label>البريد الإلكتروني — اختياري<input name="email" type="email" dir="ltr" maxLength={256} autoComplete="email" /></label>
        <label className="coach-check"><input name="isActive" type="checkbox" defaultChecked />حساب نشط داخل الأكاديمية</label>
        <details>
          <summary>الربط بحساب مدرب موجود</summary>
          <p>أدخل الاسم والهاتف المسجّلين في الحساب. إذا أضفت بريدًا إلكترونيًا، استخدم البريد المسجّل أيضًا.</p>
        </details>
      </fieldset>
      <fieldset disabled={busy}><legend>المجموعات — اختياري</legend>
        {groups.length === 0 ? <p>لا توجد مجموعات متاحة. يمكنك إسناد المدرب لاحقًا.</p> : groups.map(group => <label className="coach-check" key={group.id}><input type="checkbox" name="groupIds" value={group.id} />{group.arabicName}</label>)}
      </fieldset>
      <div className="form-actions"><button className="primary-button" disabled={busy}>{busy ? "جارٍ الحفظ…" : "حفظ المدرب"}</button><button className="secondary-button" type="button" disabled={busy} onClick={() => router.push("/dashboard/academy/coaches")}>إلغاء</button></div>
    </form>}
  </>;
}
