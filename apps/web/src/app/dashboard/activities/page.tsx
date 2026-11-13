import { AcademyActivities, type ActivitySearch } from "../../../features/academy-activities";

export default async function Page({ searchParams }: { searchParams: Promise<ActivitySearch> }) {
  return <AcademyActivities search={await searchParams} />;
}
