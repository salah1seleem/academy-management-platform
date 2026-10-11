"use client";

import { createContext, useContext } from "react";
import { monthToDate } from "../lib/date-range";

export type DateRange = { from: string; to: string };
const DateDefaults = createContext<DateRange | null>(null);
export function DateFilterDefaults({ range, children }: { range: DateRange; children: React.ReactNode }) {
  return <DateDefaults.Provider value={range}>{children}</DateDefaults.Provider>;
}
export function useDateFilterDefaults() { return useContext(DateDefaults) ?? monthToDate(); }
