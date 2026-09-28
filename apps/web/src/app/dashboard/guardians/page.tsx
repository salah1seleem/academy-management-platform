"use client";
import { useEffect, useState } from "react";
import { PageHeader, RowActions } from "../../../components/dashboard-shell";
type Guardian = { id: string; displayName: string; contactPhone: string };
export default function GuardiansPage() { const [items, setItems] = useState<Guardian[]>([]); useEffect(() => { fetch("/api/v1/people/guardians").then(r => r.json()).then(setItems); }, []); return <><PageHeader title="قائمة أولياء الأمور" context="أولياء الأمور / القائمة" /><section className="list-card"><div className="data-list">{items.map(x => <article key={x.id}><div><strong>{x.displayName}</strong><small dir="ltr">{x.contactPhone}</small></div><RowActions /></article>)}</div></section></>; }
