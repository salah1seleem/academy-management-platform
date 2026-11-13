"use client";
import { useEffect, useRef, useState, useSyncExternalStore, type FormEvent } from "react";
import Link from "next/link";
import { Button, ConfirmationDialog, ContextHelp, ErrorState, LoadingState, PageHeader } from "../components/dashboard-shell";
import { csrfRequest, DashboardRequestError } from "./dashboard-api";
import styles from "./staff-passkeys.module.css";

type Key = {
  id: string;
  name: string;
  isActive: boolean;
  createdAtUtc: string;
  lastVerifiedAtUtc?: string | null;
  revokedAtUtc?: string | null;
  mayBeSynced: boolean;
};
type List = { configured: boolean; items: Key[] };
type Purpose = "register" | "verify";
type EncodedCredential = Omit<PublicKeyCredentialDescriptor, "id"> & { id: string };
type CreateJson = Omit<PublicKeyCredentialCreationOptions, "challenge" | "user" | "excludeCredentials"> & {
  challenge: string;
  user: Omit<PublicKeyCredentialUserEntity, "id"> & { id: string };
  excludeCredentials?: EncodedCredential[];
};
type GetJson = Omit<PublicKeyCredentialRequestOptions, "challenge" | "allowCredentials"> & {
  challenge: string;
  allowCredentials?: EncodedCredential[];
};

const root = "/api/v1/staff/passkeys";

export function decodePasskeyBytes(value: string): ArrayBuffer {
  return Uint8Array.from(atob(value.replace(/-/g, "+").replace(/_/g, "/")), c => c.charCodeAt(0)).buffer;
}

function encoded(value: ArrayBuffer): string {
  return btoa(String.fromCharCode(...new Uint8Array(value))).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/g, "");
}

export async function browserPasskey(purpose: Purpose, options: CreateJson | GetJson, signal?: AbortSignal): Promise<unknown> {
  let credential: PublicKeyCredential | null;
  if (purpose === "register") {
    const { challenge, user, excludeCredentials, ...rest } = options as CreateJson;
    credential = await navigator.credentials.create({
      signal,
      publicKey: {
        ...rest,
        challenge: decodePasskeyBytes(challenge),
        user: { ...user, id: decodePasskeyBytes(user.id) },
        excludeCredentials: excludeCredentials?.map(key => ({ ...key, id: decodePasskeyBytes(key.id) })),
      },
    }) as PublicKeyCredential | null;
  } else {
    const { challenge, allowCredentials, ...rest } = options as GetJson;
    credential = await navigator.credentials.get({
      signal,
      publicKey: {
        ...rest,
        challenge: decodePasskeyBytes(challenge),
        allowCredentials: allowCredentials?.map(key => ({ ...key, id: decodePasskeyBytes(key.id) })),
      },
    }) as PublicKeyCredential | null;
  }
  if (!credential) throw new DOMException("لم تكتمل وسيلة التحقق.", "NotAllowedError");
  const common = {
    id: credential.id,
    rawId: encoded(credential.rawId),
    type: credential.type,
    extensions: credential.getClientExtensionResults(),
    authenticatorAttachment: credential.authenticatorAttachment,
  };
  if (purpose === "register") {
    const response = credential.response as AuthenticatorAttestationResponse;
    return { ...common, response: {
      clientDataJSON: encoded(response.clientDataJSON),
      attestationObject: encoded(response.attestationObject),
      transports: response.getTransports?.() ?? [],
    } };
  }
  const response = credential.response as AuthenticatorAssertionResponse;
  return { ...common, response: {
    clientDataJSON: encoded(response.clientDataJSON),
    authenticatorData: encoded(response.authenticatorData),
    signature: encoded(response.signature),
    userHandle: response.userHandle ? encoded(response.userHandle) : null,
  } };
}

type Recovery = { action: Purpose | "revoke"; id?: string; name?: string };
type Activity = "read" | Purpose | "revoke";

const readError = "تعذر تحميل وسائل التحقق.";
const checkError = "تعذر التحقق من الحالة. أعد قراءة القائمة قبل المحاولة.";
const updated = "تم تحديث القائمة. راجع الوسائل المحفوظة قبل المحاولة مرة أخرى.";
const dateOnly = new Intl.DateTimeFormat("ar-EG-u-nu-latn", { dateStyle: "medium" });
const dateAndTime = new Intl.DateTimeFormat("ar-EG-u-nu-latn", { dateStyle: "medium", timeStyle: "short" });

function formatDate(value: string | null | undefined, format: Intl.DateTimeFormat) {
  const time = value ? Date.parse(value) : NaN;
  return Number.isFinite(time) ? format.format(time) : null;
}

async function readMethods(signal: AbortSignal): Promise<List> {
  const response = await fetch(root + "/", { cache: "no-store", signal });
  if (!response.ok) throw new Error();
  const value = await response.json();
  if (typeof value?.configured !== "boolean" || !Array.isArray(value.items) ||
      value.items.some((item: Key) => typeof item?.id !== "string" || typeof item.name !== "string" || typeof item.isActive !== "boolean")) {
    throw new Error();
  }
  return value;
}

function resultMessage(target: Recovery, list: List) {
  const item = target.id ? list.items.find(item => item.id === target.id) : undefined;
  if (target.action === "revoke") {
    if (!item) return null;
    return item.isActive ? `ما زالت وسيلة «${item.name}» فعّالة.` : `تم إيقاف وسيلة «${item.name}».`;
  }
  if (!item?.isActive) return updated;
  return target.action === "register" ? `تمت إضافة وسيلة «${item.name}».` : `تم التحقق من وسيلة «${item.name}».`;
}

export function StaffPasskeySettings() {
  const [data, setData] = useState<List | null>(null);
  const [name, setName] = useState("");
  const [nameError, setNameError] = useState("");
  const [busy, setBusy] = useState<Activity | null>(null);
  const [listFailed, setListFailed] = useState(false);
  const [error, setError] = useState("");
  const [message, setMessage] = useState("");
  const [revoke, setRevoke] = useState<Key | null>(null);
  const [recovery, setRecovery] = useState<Recovery | null>(null);
  const scope = useRef<AbortController | null>(null);
  const nameField = useRef<HTMLInputElement>(null);
  const checkButton = useRef<HTMLButtonElement>(null);
  const result = useRef<HTMLParagraphElement>(null);
  const supported = useSyncExternalStore(
    () => () => {},
    () => window.isSecureContext && typeof PublicKeyCredential !== "undefined" && !!navigator.credentials,
    () => false,
  );

  useEffect(() => {
    const controller = new AbortController();
    scope.current = controller;
    void readMethods(controller.signal).then(list => {
      if (!controller.signal.aborted) setData(list);
    }).catch(() => {
      if (!controller.signal.aborted) setListFailed(true);
    });
    return () => controller.abort();
  }, []);

  useEffect(() => {
    if (!busy && recovery) checkButton.current?.focus();
    else if (!busy && message) result.current?.focus();
  }, [busy, recovery, message]);

  async function refresh(signal: AbortSignal, target?: Recovery) {
    try {
      const list = await readMethods(signal);
      if (signal.aborted) return;
      setData(list);
      setListFailed(false);
      if (target) {
        const confirmed = resultMessage(target, list);
        if (!confirmed) {
          setRecovery(target);
          setError("الوسيلة غير ظاهرة في القائمة. أعد التحقق من حالتها.");
          return;
        }
        setRecovery(null);
        setMessage(confirmed);
        if (target.action === "register" && target.id) setName("");
      }
    } catch {
      if (signal.aborted) return;
      setListFailed(true);
      if (target) {
        setRecovery(target);
        setError(checkError);
      }
    }
  }

  async function reload() {
    const signal = scope.current?.signal;
    if (!signal || busy) return;
    setBusy("read");
    setError("");
    setMessage("");
    await refresh(signal, recovery ?? undefined);
    if (!signal.aborted) setBusy(null);
  }

  async function ceremony(purpose: Purpose, event?: FormEvent<HTMLFormElement>) {
    event?.preventDefault();
    const signal = scope.current?.signal;
    if (!signal || busy || recovery || listFailed || !data?.configured || !supported) return;
    const active = data.items.filter(item => item.isActive).length;
    if (purpose === "verify" && active === 0 || purpose === "register" && active >= 5) return;
    const trimmedName = name.trim();
    if (purpose === "register" && (trimmedName.length < 2 || trimmedName.length > 80)) {
      setNameError("اكتب اسمًا من حرفين إلى ٨٠ حرفًا.");
      nameField.current?.focus();
      return;
    }
    setBusy(purpose);
    setError("");
    setMessage("");
    let completing = false;
    try {
      const challenge = await (await csrfRequest(`${root}/${purpose}/options`, "POST",
        purpose === "register" ? { name: trimmedName } : {}, {}, { announce: false })).json();
      if (signal.aborted) return;
      if (typeof challenge?.challengeId !== "string" || String(challenge.purpose).toLowerCase() !== purpose || !challenge.options) throw new Error();
      const credential = await browserPasskey(purpose, challenge.options, signal);
      if (signal.aborted) return;
      completing = true;
      const saved = await (await csrfRequest(`${root}/${purpose}/complete`, "POST", {
        challengeId: challenge.challengeId, credential,
      }, {}, { announce: false })).json();
      if (signal.aborted) return;
      if (saved?.verified !== true || saved.attendanceRecorded !== false ||
          String(saved.purpose).toLowerCase() !== purpose || typeof saved.credential?.id !== "string") throw new Error();
      await refresh(signal, { action: purpose, id: saved.credential.id });
    } catch (cause) {
      if (signal.aborted) return;
      if (completing) {
        setRecovery({ action: purpose });
        setError("تعذر تأكيد نتيجة المحاولة. تحقّق من الوسائل المحفوظة.");
      } else if (cause instanceof DOMException && ["NotAllowedError", "AbortError"].includes(cause.name)) {
        setError("لم يكتمل التحقق على الجهاز. يمكنك المحاولة مرة أخرى.");
      } else {
        setError(cause instanceof DashboardRequestError && cause.status < 500
          ? cause.message
          : "تعذر بدء التحقق. حاول مرة أخرى.");
      }
    } finally {
      if (!signal.aborted) setBusy(null);
    }
  }

  async function stop() {
    const signal = scope.current?.signal;
    if (!signal || !revoke || busy || recovery || listFailed) return;
    const target: Recovery = { action: "revoke", id: revoke.id, name: revoke.name };
    setBusy("revoke");
    setError("");
    setMessage("");
    try {
      const saved = await (await csrfRequest(`${root}/credentials/${revoke.id}/revoke`, "POST", {}, {}, { announce: false })).json();
      if (signal.aborted) return;
      if (saved?.id !== revoke.id || saved.isActive !== false) throw new Error();
      await refresh(signal, target);
    } catch {
      if (!signal.aborted) {
        setRecovery(target);
        setError("تعذر تأكيد الإيقاف. تحقّق من حالة الوسيلة.");
      }
    } finally {
      if (!signal.aborted) {
        setRevoke(null);
        setBusy(null);
      }
    }
  }

  const locked = !!busy || !!recovery || listFailed;
  const active = data?.items.filter(item => item.isActive).length ?? 0;
  return <div className={styles.screen}>
    <PageHeader title="وسائل التحقق الخاصة بي" context="أمان الحساب" />
    <section className={styles.page}>
      <div className={styles.links}>
        <Link href="/dashboard/account/attendance">حضوري وانصرافي</Link>
        <ContextHelp title="التحقق والخصوصية">
          <p>قد تستخدم بصمة الإصبع أو الوجه أو رمز الجهاز. يمكن أن تتزامن الوسيلة بين أجهزتك. لا نخزّن بصمات أو صور وجوه.</p>
          <p>إضافة الوسيلة أو تجربتها لا تسجّل حضورًا.</p>
        </ContextHelp>
      </div>
      {error && <ErrorState message={error} />}
      {message && <p ref={result} tabIndex={-1} className={styles.notice} role="status">{message}</p>}
      {recovery && <button ref={checkButton} type="button" className="button button-secondary" disabled={!!busy} onClick={() => void reload()}>
        {busy === "read" ? "جارٍ التحقق…" : recovery.name ? `تحقّق من حالة «${recovery.name}»` : "تحقّق من الوسائل المحفوظة"}
      </button>}
      {!data && !listFailed && <LoadingState message="جارٍ تحميل وسائل التحقق…" />}
      {listFailed && !recovery && <div className={styles.panel}>
        <ErrorState message={readError} />
        <Button variant="secondary" disabled={!!busy} onClick={() => void reload()}>{busy === "read" ? "جارٍ التحميل…" : "إعادة التحميل"}</Button>
      </div>}
      {data && <>
        {!data.configured && <p className={styles.panel}>إضافة وسيلة تحقق غير متاحة حاليًا. تواصل مع المسؤول عن إعدادها. يمكنك طلب إثبات حضور للمراجعة من تطبيق الموظف.</p>}
        {data.configured && !supported && <p className={styles.panel}>هذا المتصفح أو الاتصال لا يدعم إضافة وسيلة تحقق. يمكنك طلب إثبات حضور للمراجعة من تطبيق الموظف.</p>}
        {data.configured && supported && <div className={styles.panel}>
          {active < 5 ? <form className={styles.form} noValidate onSubmit={event => void ceremony("register", event)}>
            <label htmlFor="staff-passkey-name">اسم الوسيلة</label>
            <input id="staff-passkey-name" ref={nameField} value={name} placeholder="هاتف العمل" required minLength={2} maxLength={80} disabled={locked}
              aria-invalid={!!nameError || undefined} aria-describedby={nameError ? "staff-passkey-name-error" : undefined}
              onChange={event => { setName(event.target.value); setNameError(""); }} />
            {nameError && <p id="staff-passkey-name-error" className={styles.fieldError} role="alert">{nameError}</p>}
            <Button type="submit" disabled={locked}>{busy === "register" ? "جارٍ إضافة الوسيلة…" : "إضافة وسيلة تحقق"}</Button>
          </form> : <p>لديك خمس وسائل فعّالة. أوقف إحدى الوسائل قبل إضافة وسيلة أخرى.</p>}
          {active > 0 && <Button variant="secondary" disabled={locked} onClick={() => void ceremony("verify")}>
            {busy === "verify" ? "جارٍ التحقق…" : "تجربة وسيلة تحقق"}
          </Button>}
        </div>}
        <section className={styles.panel} aria-labelledby="staff-passkey-saved">
          <div className={styles.listHeader}>
            <h2 id="staff-passkey-saved">الوسائل المحفوظة</h2>
            {!recovery && !listFailed && <Button variant="ghost" disabled={!!busy} onClick={() => void reload()}>
              {busy === "read" ? "جارٍ التحديث…" : "تحديث القائمة"}
            </Button>}
          </div>
          {!data.items.length && !listFailed && <p>لا توجد وسائل تحقق محفوظة لهذا الحساب.</p>}
          {data.items.length > 0 && <ul className={styles.list}>
            {data.items.map(item => {
              const created = formatDate(item.createdAtUtc, dateOnly);
              const verified = formatDate(item.lastVerifiedAtUtc, dateAndTime);
              const revoked = formatDate(item.revokedAtUtc, dateOnly);
              return <li key={item.id}>
                <div className={styles.listHeader}>
                  <strong>{item.name}</strong>
                  <span className={item.isActive ? styles.active : styles.inactive}>{item.isActive ? "فعّالة" : "متوقفة"}</span>
                </div>
                {created && <p>تاريخ الإضافة {created}</p>}
                {verified && <p>آخر تحقق {verified}</p>}
                {revoked && <p>تاريخ الإيقاف {revoked}</p>}
                {item.mayBeSynced && <p>قد تتزامن بين أجهزتك.</p>}
                {item.isActive && <Button variant="secondary" disabled={locked} onClick={() => setRevoke(item)}>إيقاف {item.name}</Button>}
              </li>;
            })}
          </ul>}
        </section>
      </>}
    </section>
    <ConfirmationDialog open={!!revoke} title={revoke ? `إيقاف وسيلة «${revoke.name}»؟` : ""}
      message="ستبقى الوسيلة في السجل. لا يمكن إعادة تفعيلها بعد الإيقاف." confirmLabel="تأكيد الإيقاف"
      busy={busy === "revoke"} busyLabel="جارٍ إيقاف الوسيلة…" onConfirm={() => void stop()} onCancel={() => setRevoke(null)} />
  </div>;
}
