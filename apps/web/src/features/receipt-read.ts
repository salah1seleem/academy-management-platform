"use client";

import { useEffect, useState } from "react";

export function receiptAmount(value: unknown) {
  const amount = typeof value === "string" && /^\d+(?:\.\d{1,2})?$/.test(value.trim()) ? Number(value) : value;
  return typeof amount === "number" && Number.isFinite(amount) && amount >= 0 ? amount : null;
}

export function useReceiptRead<T>(path: string | null, validate?: (value: unknown) => value is T) {
  const [revision, setRevision] = useState(0);
  const [result, setResult] = useState<{ key: string; data?: T; lastData?: T; error?: "unavailable" | "network" }>();
  const key = `${path}:${revision}`;
  useEffect(() => {
    if (path === null) return;
    const request = new AbortController();
    void fetch(path, { signal: request.signal, cache: "no-store" }).then(async response => {
      if ([401, 403, 404].includes(response.status)) {
        if (!request.signal.aborted) setResult({ key, error: "unavailable" });
        return;
      }
      if (!response.ok) throw new Error();
      const value: unknown = await response.json();
      if (!value || typeof value !== "object" || Array.isArray(value) || (validate && !validate(value))) throw new Error();
      if (!request.signal.aborted) setResult({ key, data: value as T, lastData: value as T });
    }).catch(() => {
      if (!request.signal.aborted) setResult(previous => ({ key, error: "network", lastData: previous?.lastData }));
    });
    return () => request.abort();
  }, [key, path, validate]);
  return { current: result?.key === key ? result : undefined, lastData: result?.lastData, retry: () => setRevision(value => value + 1) };
}
