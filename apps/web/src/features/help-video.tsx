"use client";
import { useEffect, useState } from "react";
export type HelpVideo = { id: string; title: string; description: string; alternativeText: string; version: number; durationSeconds: number; url: string; thumbnailUrl: string };
export function HelpVideoPlayer({ lesson, video }: { lesson: string; video: HelpVideo }) {
  const [active, setActive] = useState(true), [failed, setFailed] = useState(false);
  useEffect(() => { const change = () => setActive(!document.hidden); document.addEventListener("visibilitychange", change); return () => document.removeEventListener("visibilitychange", change); }, []);
  const root = `/api/v1/help/lessons/${encodeURIComponent(lesson)}/media/${video.id}/`;
  const valid = /^[a-f0-9-]{36}$/i.test(video.id) && video.url === `${root}video` && video.thumbnailUrl === `${root}thumbnail`;
  return <section className="help-video-block" aria-label="الشرح بالفيديو"><h3>{video.title}</h3><p>{video.description}</p>{valid && active && !failed ? <video className="help-video" src={video.url} poster={video.thumbnailUrl} controls preload="metadata" onError={() => setFailed(true)}/> : <p role="status">{failed || !valid ? "تعذر تشغيل الفيديو؛ قد تكون إتاحته تغيرت. حدّث الدليل، أو اقرأ الخطوات أدناه." : "التشغيل متوقف أثناء مغادرة الصفحة."}</p>}<p className="muted">إصدار الفيديو {video.version} · {video.durationSeconds} ثانية</p><p>{video.alternativeText}</p></section>;
}
