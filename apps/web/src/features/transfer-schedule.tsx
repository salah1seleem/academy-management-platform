import { formatDateAr, formatTimeAr } from "../lib/formatters";
import styles from "./player-skills.module.css";

export type TransferSlot = { id: string; sessionDate?: string; startTime?: string; endTime?: string };

export function TransferSchedule({ title, sessions }: { title: string; sessions: TransferSlot[] }) {
  const dates = sessions.filter(item => item.sessionDate).slice(0, 3);
  return <div>
    <h3>{title}</h3>
    <p>{sessions.length ? `العدد: ${sessions.length}` : "لا توجد مواعيد مجدولة."}</p>
    {dates.length > 0 && <ul>{dates.map(item => <li key={item.id}>
      {formatDateAr(item.sessionDate)}
      {item.startTime && item.endTime && <> · من {formatTimeAr(item.startTime)} إلى {formatTimeAr(item.endTime)}</>}
    </li>)}</ul>}
    {sessions.length > dates.length && dates.length > 0 && <p className={styles.reviewMeta}>تظهر أقرب {dates.length} مواعيد.</p>}
  </div>;
}
