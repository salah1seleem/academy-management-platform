"use client";

import { useState, type ChangeEvent, type FormEvent } from "react";
import { canonicalDigits } from "../lib/number-input";
import styles from "./login-form.module.css";

export type LoginCredentials = { email: string; password: string; code: string };
type Field = keyof LoginCredentials;
type FieldError = { field: Field; message: string };

export function LoginForm({ busy, error, platform = false, onLogin }: {
  busy: boolean;
  error: string;
  platform?: boolean;
  onLogin: (credentials: LoginCredentials) => Promise<void>;
}) {
  const [fieldError, setFieldError] = useState<FieldError>();

  function updateField(event: ChangeEvent<HTMLInputElement>) {
    const input = event.currentTarget;
    if (input.name === "code") input.value = canonicalDigits(input.value);
    if (fieldError?.field === input.name && input.validity.valid) setFieldError(undefined);
  }

  function fieldProps(field: Field) {
    const invalid = fieldError?.field === field;
    return { name: field, required: true, readOnly: busy, dir: "ltr", onChange: updateField,
      "aria-invalid": invalid || undefined, "aria-describedby": invalid ? `login-${field}-error` : undefined };
  }

  function message(field: Field) {
    return fieldError?.field === field && <span id={`login-${field}-error`} className="error-message" role="alert">{fieldError.message}</span>;
  }

  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (busy) return;
    const form = event.currentTarget;
    const email = form.elements.namedItem("email") as HTMLInputElement;
    const password = form.elements.namedItem("password") as HTMLInputElement;
    const code = form.elements.namedItem("code") as HTMLInputElement | null;
    const invalidField = [email, password, code].find(input => input && !input.validity.valid);
    if (invalidField) {
      const field = invalidField.name as Field;
      const errors = { email: email.value ? "اكتب بريدًا إلكترونيًا صحيحًا." : "اكتب بريدك الإلكتروني.",
        password: "اكتب كلمة المرور.", code: "اكتب رمز التحقق من ٦ أرقام." };
      setFieldError({ field, message: errors[field] });
      invalidField.focus();
      return;
    }
    setFieldError(undefined);
    void onLogin({ email: email.value.trim(), password: password.value, code: code?.value ?? "" });
  }

  return <form method="post" noValidate onSubmit={submit} className={styles.form}>
    <div className="field">
      <label htmlFor="login-email">البريد الإلكتروني</label>
      <input id="login-email" {...fieldProps("email")} type="email" autoComplete="username" />
      {message("email")}
    </div>
    <div className="field">
      <label htmlFor="login-password">كلمة المرور</label>
      <input id="login-password" {...fieldProps("password")} type="password" autoComplete="current-password" />
      {message("password")}
    </div>
    {platform && <div className="field">
      <label htmlFor="login-code">رمز التحقق</label>
      <input id="login-code" {...fieldProps("code")} inputMode="numeric" pattern="[0-9]{6}" maxLength={6} autoComplete="one-time-code" />
      {message("code")}
    </div>}
    {error && !fieldError && <p className="error-message" role="alert">{error}</p>}
    <button className="primary-button" disabled={busy}>{busy ? "جارٍ تسجيل الدخول…" : "تسجيل الدخول"}</button>
  </form>;
}
