"use client";
import { useEffect, useState } from "react";

// Presentation only. The API independently rechecks current grants for every write.
export function useStaffAction(module: string, action: "add" | "edit" | "delete") {
  const [allowed, setAllowed] = useState(false);
  useEffect(() => {
    const controller = new AbortController();
    void Promise.all([fetch("/api/v1/me", { signal: controller.signal }), fetch("/api/v1/my-access", { signal: controller.signal })]).then(async ([me, scope]) => {
      if (!me.ok || !scope.ok) return;
      const identity = await me.json(), grants = await scope.json();
      if (!controller.signal.aborted) setAllowed(identity.role === "AcademyOwner" || identity.role === "AcademyAdmin" && grants.useActionGrants === true && Array.isArray(grants.actions) && grants.actions.some((g: Record<string, unknown>) => g.module === module && g.read === true && g[action] === true));
    }).catch(() => { if (!controller.signal.aborted) setAllowed(false); });
    return () => controller.abort();
  }, [module, action]);
  return allowed;
}
