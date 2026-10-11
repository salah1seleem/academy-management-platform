"use client";

import { ReactNode, useId } from "react";
import { usePathname, useRouter, useSearchParams } from "next/navigation";

export type RecordTab = { id: string; label: string; content: ReactNode };

/** Stable URL tabs retain record context and other filters; private data is
 * still fetched by its authorized module, never granted by a query parameter. */
export function RecordTabs({ tabs, label }: { tabs: RecordTab[]; label: string }) {
  const query = useSearchParams(), path = usePathname(), router = useRouter();
  const prefix = useId();
  const selected = tabs.findIndex(tab => tab.id === query.get("tab"));
  const index = selected < 0 ? 0 : selected;
  function select(next: number, focus = false) {
    const params = new URLSearchParams(query.toString());
    params.set("tab", tabs[next].id);
    router.replace(`${path}?${params}`, { scroll: false });
    if (focus) document.getElementById(`${prefix}-${tabs[next].id}`)?.focus();
  }
  if (!tabs.length) return null;
  return <div className="record-workspace">
    <div role="tablist" aria-label={label} className="record-tabs" dir="rtl">{tabs.map((tab, i) => <button key={tab.id} id={`${prefix}-${tab.id}`} type="button" role="tab" aria-selected={i === index} aria-controls={`${prefix}-panel-${tab.id}`} tabIndex={i === index ? 0 : -1} onClick={() => select(i)} onKeyDown={event => {
      const next = event.key === "ArrowLeft" ? (i + 1) % tabs.length : event.key === "ArrowRight" ? (i + tabs.length - 1) % tabs.length : event.key === "Home" ? 0 : event.key === "End" ? tabs.length - 1 : null;
      if (next !== null) { event.preventDefault(); select(next, true); }
    }}>{tab.label}</button>)}</div>
    <div role="tabpanel" id={`${prefix}-panel-${tabs[index].id}`} aria-labelledby={`${prefix}-${tabs[index].id}`} tabIndex={0}>{tabs[index].content}</div>
  </div>;
}
