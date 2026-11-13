import { GuardianShell } from "../../../components/guardian-shell";
import { AcademyActivities, type ActivitySearch } from "../../../features/academy-activities";

export default async function Page({ searchParams }: { searchParams: Promise<ActivitySearch> }) {
  return <GuardianShell><AcademyActivities guardian search={await searchParams} /></GuardianShell>;
}
