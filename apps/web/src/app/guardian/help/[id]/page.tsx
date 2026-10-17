import { HelpCenter } from "../../../../features/help-center";
import { GuardianShell } from "../../../../components/guardian-shell";
export default async function Page({ params }: { params: Promise<{ id: string }> }) { return <GuardianShell><HelpCenter guardian id={(await params).id} /></GuardianShell>; }
