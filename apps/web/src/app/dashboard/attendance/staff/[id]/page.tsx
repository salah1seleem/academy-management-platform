import { DailyCoachRecord } from "../../../../../features/daily-staff-attendance";

export default async function Page({ params, searchParams }: { params: Promise<{ id: string }>; searchParams: Promise<{ date?: string }> }) {
  const { id } = await params;
  const { date } = await searchParams;
  return <DailyCoachRecord key={id} id={id} initialDate={date} />;
}
