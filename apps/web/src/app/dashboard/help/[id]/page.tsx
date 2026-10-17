import { HelpCenter } from "../../../../features/help-center";
export default async function Page({ params }: { params: Promise<{ id: string }> }) { return <HelpCenter id={(await params).id} />; }
