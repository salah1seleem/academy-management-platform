"use client";
import { useEffect, useState } from "react";

export function usePayrollManage() {
  const [allowed, setAllowed] = useState(false);
  useEffect(() => {
    const abort = new AbortController();
    fetch("/api/v1/my-permissions", { signal: abort.signal }).then(r => r.ok ? r.json() : []).then(p => setAllowed(Array.isArray(p) && p.includes("payroll.manage"))).catch(() => { if (!abort.signal.aborted) setAllowed(false); });
    return () => abort.abort();
  }, []);
  return allowed;
}
