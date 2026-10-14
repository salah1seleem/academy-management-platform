"use client";
import { useEffect, useState } from "react";

export function usePayrollPermission(permission: string) {
  const [allowed, setAllowed] = useState(false);
  useEffect(() => {
    const abort = new AbortController();
    fetch("/api/v1/my-permissions", { signal: abort.signal, cache: "no-store" }).then(r => r.ok ? r.json() : []).then(p => setAllowed(Array.isArray(p) && p.includes(permission))).catch(() => { if (!abort.signal.aborted) setAllowed(false); });
    return () => abort.abort();
  }, [permission]);
  return allowed;
}
export function usePayrollManage() { return usePayrollPermission("payroll.manage"); }
