"use client";
import { useEffect, useState } from "react";

// Presentation only. The API independently rechecks current grants for every write.
export function useStaffAction(module: string, action: "read" | "add" | "edit" | "delete") {
  return useStaffActionAccess(module, action) === true;
}

export function useStaffActionAccess(module: string, action: "read" | "add" | "edit" | "delete", revision = 0) {
  const key = `${module}:${action}:${revision}`;
  const [result, setResult] = useState<{ key: string; allowed: boolean | "unavailable" }>();
  useEffect(() => {
    const controller = new AbortController();
    const resolve = (allowed: boolean | "unavailable") => {
      if (!controller.signal.aborted) setResult({ key, allowed });
    };
    void Promise.all([fetch("/api/v1/me", { signal: controller.signal }), fetch("/api/v1/my-access", { signal: controller.signal })]).then(async ([me, scope]) => {
      if (!me.ok || !scope.ok) { resolve("unavailable"); return; }
      const identity = await me.json(), grants = await scope.json();
      let value = identity.role === "AcademyOwner";
      if (identity.role === "AcademyAdmin") {
        if (grants.useActionGrants === true) value = Array.isArray(grants.actions) && grants.actions.some((g: Record<string, unknown>) => g.module === module && g.read === true && g[action] === true);
        else if (action !== "delete") {
          const legacy: Record<string, [string, string]> = { academy: ["structure.manage", "structure.manage"], players: ["people.manage", "people.manage"], facilities: ["facilities.read", "facilities.manage"], attendance: ["attendance.read", "attendance.manage"], subscriptions: ["subscription.read", "subscription.plan.manage"] };
          const permission = legacy[module]?.[action === "read" ? 0 : 1];
          if (permission) {
            const response = await fetch("/api/v1/my-permissions", { signal: controller.signal });
            if (!response.ok) { resolve("unavailable"); return; }
            const permissions: unknown = await response.json();
            value = Array.isArray(permissions) && permissions.includes(permission);
          }
        }
      }
      resolve(value);
    }).catch(() => resolve("unavailable"));
    return () => controller.abort();
  }, [key, module, action]);
  return result?.key === key ? result.allowed : null;
}
