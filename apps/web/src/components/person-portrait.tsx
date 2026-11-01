"use client";
import Image from "next/image";
import { useState } from "react";

export function avatarPath(adult = false, gender?: unknown) {
  const sex = gender === 1 || gender === "Male" ? "male" : gender === 2 || gender === "Female" ? "female" : "neutral";
  return `/avatars/${adult ? "adult" : "child"}-${sex}.svg`;
}

/** Same-origin packaged/private photos only. A failed source never hides the fallback. */
export function PersonPortrait({ photo, adult = false, gender, name = "", size = 64, className }: { photo?: string; adult?: boolean; gender?: unknown; name?: string; size?: number; className?: string }) {
  const [failed, setFailed] = useState<string>();
  const safe = photo && (/^\/demo-assets\/[a-zA-Z0-9/_-]+\.(svg|png|jpe?g|webp)$/.test(photo) || /^\/api\/v1\/assets\/[a-f0-9-]{36}$/i.test(photo)) ? photo : undefined;
  const fallback = avatarPath(adult, gender);
  return <Image unoptimized src={safe && failed !== safe ? safe : fallback} onError={() => setFailed(safe)} alt={name ? `صورة ${name}` : "صورة رمزية"} width={size} height={size} className={className} style={{ objectFit: "cover", objectPosition: "50% 30%" }} />;
}
