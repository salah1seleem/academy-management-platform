"use client";

import Image from "next/image";
import { useState } from "react";
import { ErrorState } from "./dashboard-shell";

export function PaymentProofPreview({ url, description, linkLabel, onReady }: {
  url: string;
  description: string;
  linkLabel: string;
  onReady: (ready: boolean) => void;
}) {
  const [status, setStatus] = useState<"loading" | "ready" | "failed">("loading");

  function finish(ready: boolean) {
    setStatus(ready ? "ready" : "failed");
    onReady(ready);
  }

  return <div className="payment-proof-preview">
    <a className="button button-secondary" href={url} target="_blank" rel="noreferrer">{linkLabel}</a>
    {status === "loading" && <p role="status">جارٍ تحميل صورة الإثبات…</p>}
    {status === "failed" ? <>
      <ErrorState message="تعذر فتح صورة إثبات الدفع." />
      <button className="secondary-button" type="button" onClick={() => setStatus("loading")}>إعادة تحميل الصورة</button>
    </> : <Image src={url} unoptimized loading="eager" width={480} height={320} alt={description} hidden={status !== "ready"}
      onLoad={event => finish(event.currentTarget.naturalWidth > 0)} onError={() => finish(false)} />}
  </div>;
}
