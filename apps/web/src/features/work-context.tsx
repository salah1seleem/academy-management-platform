"use client";

import Link from "next/link";
import { useEffect, useRef, useState } from "react";
import { Button, ErrorState, LoadingState } from "../components/dashboard-shell";
import { operationLoginPath, operationReturnPath } from "../lib/operation-return";
import { useWebAppearance } from "../lib/web-appearance";
import { webAuthRequest } from "../lib/web-auth-request";
import { csrfRequest, DashboardRequestError } from "./dashboard-api";
import styles from "./work-context.module.css";

type ContextKey = { academyId: string; membershipId: string };
type Context = ContextKey & {
  academyName: string;
  role: string;
  contextName?: string | null;
  allBranches: boolean;
  allSports: boolean;
  branchNames: string[];
  sportNames: string[];
};
type LoadState = { status: "loading" | "error" | "expired" } | { status: "ready"; items: Context[] };
type Issue = { action: "verify" | "refresh" | "retry"; message: string };

function isStaff(role: string) {
  return role === "AcademyOwner" || role === "AcademyAdmin";
}

function matches(current: ContextKey | null, item: ContextKey) {
  return current?.academyId === item.academyId && current.membershipId === item.membershipId;
}

async function activeContext(signal?: AbortSignal): Promise<ContextKey | null> {
  const response = await fetch("/api/v1/me", { cache: "no-store", signal });
  if (response.status === 403) return null;
  if (!response.ok) throw new DashboardRequestError("تعذر التحقق من مساحة العمل الحالية.", response.status);
  const session = await response.json();
  if (typeof session?.role !== "string") throw new Error();
  if (!isStaff(session.role)) return null;
  if (typeof session.academyId !== "string" || typeof session.membershipId !== "string") throw new Error();
  return { academyId: session.academyId, membershipId: session.membershipId };
}

export function WorkContextChooser({ returnTo, navigate }: { returnTo?: string; navigate?: () => void }) {
  const mode = useWebAppearance();
  const destination = operationReturnPath(returnTo ?? null) ?? "/dashboard";
  const loginHref = operationLoginPath(destination);
  const [load, setLoad] = useState<LoadState>({ status: "loading" });
  const [readVersion, setReadVersion] = useState(0);
  const [current, setCurrent] = useState<ContextKey | null>(null);
  const [selected, setSelected] = useState("");
  const [issue, setIssue] = useState<Issue>();
  const [busy, setBusy] = useState<"opening" | "verifying" | "logout" | null>(null);
  const [logoutError, setLogoutError] = useState("");
  const selectionStarted = useRef(false);

  useEffect(() => {
    const controller = new AbortController();
    const signal = controller.signal;
    void (async () => {
      try {
        const response = await fetch("/api/v1/my-academies", { cache: "no-store", signal });
        if (signal.aborted) return;
        if (response.status === 401) { setLoad({ status: "expired" }); return; }
        if (!response.ok) throw new Error();
        const data: Context[] = await response.json();
        if (!Array.isArray(data)) throw new Error();
        if (!signal.aborted) setLoad({ status: "ready", items: data.filter(item => isStaff(item.role)) });
      } catch {
        if (!signal.aborted) setLoad({ status: "error" });
      }
    })();
    void activeContext(signal).then(item => {
      if (!signal.aborted && !selectionStarted.current) setCurrent(item);
    }).catch(() => {});
    return () => controller.abort();
  }, [readVersion]);

  const chosen = load.status === "ready" ? load.items.find(item => item.membershipId === selected) : undefined;

  function refresh() {
    selectionStarted.current = false;
    setLoad({ status: "loading" });
    setCurrent(null);
    setSelected("");
    setIssue(undefined);
    setReadVersion(version => version + 1);
  }

  function openWorkspace() {
    // A full navigation drops all React state scoped to the previous membership.
    if (navigate) navigate();
    else window.location.replace(destination);
  }

  async function open() {
    if (!chosen || busy || issue?.action === "verify" || issue?.action === "refresh") return;
    selectionStarted.current = true;
    setBusy("opening");
    setIssue(undefined);
    setLogoutError("");
    try {
      const response = await csrfRequest("/api/v1/session/academy", "POST", {
        academyId: chosen.academyId,
        membershipId: chosen.membershipId,
      }, {}, { announce: false });
      if (response.status !== 204) throw new Error();
      openWorkspace();
    } catch (error) {
      const status = error instanceof DashboardRequestError ? error.status : undefined;
      if (status === 401) {
        setLoad({ status: "expired" });
      } else if (status === 403 || status === 404 || status === 409) {
        setIssue({ action: "refresh", message: "قد يكون هذا الاختيار تغيّر. حدّث الاختيارات قبل المتابعة." });
      } else if (status === 429) {
        setIssue({ action: "retry", message: "طلبات كثيرة في وقت قصير. انتظر قليلًا ثم أعد المحاولة." });
      } else {
        setIssue({ action: "verify", message: "تعذر تأكيد فتح مساحة العمل. تحقّق من المساحة الحالية." });
      }
      setBusy(null);
    }
  }

  async function verify() {
    if (!chosen || busy) return;
    setBusy("verifying");
    try {
      const confirmed = await activeContext();
      setCurrent(confirmed);
      if (matches(confirmed, chosen)) { openWorkspace(); return; }
      setIssue({ action: "retry", message: "مساحة العمل المطلوبة غير مفتوحة. يمكنك المحاولة مجددًا أو اختيار مساحة أخرى." });
    } catch (error) {
      if (error instanceof DashboardRequestError && error.status === 401) {
        setLoad({ status: "expired" });
      } else {
        setIssue({ action: "verify", message: "تعذر التحقق من مساحة العمل الحالية. أعد التحقق." });
      }
    }
    setBusy(null);
  }

  async function logout() {
    if (busy) return;
    setBusy("logout");
    setLogoutError("");
    try {
      const response = await webAuthRequest("/api/v1/auth", "logout");
      if (!response.ok && response.status !== 401) throw new Error();
      window.location.replace(loginHref);
    } catch {
      setLogoutError("تعذر تسجيل الخروج. حاول مرة أخرى.");
      setBusy(null);
    }
  }

  const needsRecovery = issue?.action === "verify" || issue?.action === "refresh";
  return <main className={`dashboard-shell ${styles.shell}`} data-workspace-design="v5" data-mode={mode} data-brand-primary="#D71935">
    <section className={styles.chooser} aria-labelledby="work-context-title">
      <h1 id="work-context-title">اختر مساحة العمل</h1>
      {load.status === "loading" && <LoadingState message="جارٍ تحميل مساحات العمل…" />}
      {load.status === "expired" && <>
        <ErrorState message="انتهت الجلسة. سجّل الدخول مرة أخرى." />
        <Link className={styles.link} href={loginHref}>تسجيل الدخول</Link>
      </>}
      {load.status === "error" && <>
        <ErrorState message="تعذر تحميل مساحات العمل." />
        <Button variant="secondary" disabled={!!busy} onClick={refresh}>إعادة التحميل</Button>
      </>}
      {load.status === "ready" && (load.items.length === 0
        ? <p className={styles.empty}>لا توجد مساحات عمل متاحة لهذا الحساب.</p>
        : <>
          <fieldset className={styles.options} disabled={!!busy || needsRecovery}>
            <legend className="sr-only">مساحات العمل المتاحة</legend>
            {load.items.map(item => {
              const id = `context-${item.membershipId}`;
              return <label className={styles.option} key={item.membershipId}>
                <input type="radio" name="context" value={item.membershipId} checked={selected === item.membershipId}
                  aria-labelledby={`${id}-name ${id}-academy`} aria-describedby={`${id}-branches ${id}-sports`}
                  onChange={() => { setSelected(item.membershipId); setIssue(undefined); }} />
                <span className={styles.details}>
                  <strong id={`${id}-name`}>{item.contextName || (item.role === "AcademyOwner" ? "مالك الأكاديمية" : "الإدارة الأساسية")}</strong>
                  <span id={`${id}-academy`}>{item.academyName}</span>
                  {matches(current, item) && <span className={styles.current}>مساحة العمل الحالية</span>}
                  <span className={styles.scope}>
                    <span id={`${id}-branches`}>الفروع: {item.allBranches ? "كل الفروع" : item.branchNames.join("، ") || "لا توجد فروع مسندة"}</span>
                    <span id={`${id}-sports`}>الرياضات: {item.allSports ? "كل الرياضات" : item.sportNames.join("، ") || "لا توجد رياضات مسندة"}</span>
                  </span>
                </span>
              </label>;
            })}
          </fieldset>
          {issue && <ErrorState message={issue.message} />}
          {issue?.action === "verify"
            ? <Button variant="secondary" disabled={!!busy} onClick={() => void verify()}>{busy === "verifying" ? "جارٍ التحقق…" : "تحقّق من المساحة الحالية"}</Button>
            : issue?.action === "refresh"
              ? <Button variant="secondary" disabled={!!busy} onClick={refresh}>تحديث الاختيارات</Button>
              : <Button disabled={!chosen || !!busy} onClick={() => void open()}>{busy === "opening" ? "جارٍ فتح مساحة العمل…" : "فتح مساحة العمل"}</Button>}
        </>)}
      {load.status !== "expired" && <>
        {logoutError && <ErrorState message={logoutError} />}
        <footer className={styles.actions}>
          {current && !busy && !needsRecovery && <Link className={styles.link} href={destination}>متابعة بالمساحة الحالية</Link>}
          <Button variant="ghost" disabled={!!busy} onClick={() => void logout()}>{busy === "logout" ? "جارٍ تسجيل الخروج…" : "تسجيل الخروج"}</Button>
        </footer>
      </>}
    </section>
  </main>;
}
