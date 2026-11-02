import { AttendanceCorrectionDetail } from "../../../../../features/attendance-correction-detail";

export default async function Page({params}:{params:Promise<{id:string}>}) {
  const {id}=await params;
  return <AttendanceCorrectionDetail key={id} id={id}/>;
}
