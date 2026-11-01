"use client";

import Link from "next/link";
import { useEffect, useState } from "react";
import { AdminDataTable, EmptyState, ErrorState, LoadingState, PageHeader, SearchInput } from "../components/dashboard-shell";

type Enrollment = { id: string; player: string; playerCode: string; sport: string; group: string };

/** Selects an existing authorized enrollment; the destination rechecks access. */
export function DevelopmentEntry({ title, destination }: { title: string; destination: string }) {
  const [rows, setRows] = useState<Enrollment[] | null>(null), [search, setSearch] = useState(""), [error, setError] = useState("");
  useEffect(() => {
    const controller = new AbortController();
    fetch("/api/v1/evaluations/options", { signal: controller.signal, cache: "no-store" }).then(async response => {
      if (!response.ok) throw Error();
      const data = await response.json();
      if (!controller.signal.aborted) setRows(data.enrollments);
    }).catch(() => { if (!controller.signal.aborted) setError("تعذر تحميل اللاعبين المتاحين ضمن صلاحياتك. أعد فتح الصفحة للمحاولة."); });
    return () => controller.abort();
  }, []);
  const visible = rows?.filter(row => `${row.player} ${row.playerCode} ${row.sport} ${row.group}`.includes(search));
  return <><PageHeader title={title} context="تطور اللاعبين"/><section className="list-card">
    <div className="content-record-header"><h2>اختر اللاعب والرياضة</h2><SearchInput value={search} onChange={setSearch} label="ابحث عن اللاعب" placeholder="الاسم أو الكود أو المجموعة"/></div>
    {error ? <ErrorState message={error}/> : !visible ? <LoadingState/> : visible.length === 0 ? <EmptyState title="لا يوجد لاعب مطابق" message="غيّر البحث أو راجع التسجيلات المتاحة لحسابك."/> : <AdminDataTable label="لاعبو التطور المتاحون" rows={visible} rowKey={row=>row.id} columns={[
      {key:"player",header:"اللاعب",primary:true,render:row=><><strong>{row.player}</strong><small>{row.playerCode}</small></>},
      {key:"sport",header:"الرياضة",render:row=>row.sport},
      {key:"group",header:"المجموعة",render:row=>row.group},
      {key:"action",header:"الإجراء",render:row=><Link className="row-primary-action" href={`${destination}?enrollmentId=${encodeURIComponent(row.id)}`}>فتح</Link>},
    ]}/>}
  </section></>;
}
