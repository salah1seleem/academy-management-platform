"use client";

import { useState } from "react";
import styles from "./weekdays-field.module.css";

export const trainingWeek = [
  { value: 6, key: "Saturday", label: "السبت" },
  { value: 0, key: "Sunday", label: "الأحد" },
  { value: 1, key: "Monday", label: "الاثنين" },
  { value: 2, key: "Tuesday", label: "الثلاثاء" },
  { value: 3, key: "Wednesday", label: "الأربعاء" },
  { value: 4, key: "Thursday", label: "الخميس" },
  { value: 5, key: "Friday", label: "الجمعة" },
];
export function dayLabel(value: number | string) { return trainingWeek.find(d => d.value === value || d.key === value)?.label ?? "—"; }

export function WeekdaysField({ name = "days", label = "أيام التدريب", initialDays = [] }: { name?: string; label?: string; initialDays?: (number | string)[] }) {
  const [selected, setSelected] = useState(() => trainingWeek.filter(d => initialDays.includes(d.value) || initialDays.includes(d.key)).map(d => d.value));
  return <fieldset className={`weekdays-field ${styles.field}`}><legend>{label}</legend><div className={`weekdays-grid ${styles.grid}`}>
    {trainingWeek.map(day => <label key={day.key} className={`weekday-choice ${styles.choice}${selected.includes(day.value) ? ` selected ${styles.selected}` : ""}`}>
      <input type="checkbox" name={name} value={day.value} checked={selected.includes(day.value)} onChange={e => setSelected(previous => e.target.checked ? [...previous, day.value] : previous.filter(value => value !== day.value))} />
      <span>{day.label}</span>
    </label>)}
  </div></fieldset>;
}
