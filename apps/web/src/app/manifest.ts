import type { MetadataRoute } from "next";

export default function manifest(): MetadataRoute.Manifest {
  return {
    name: "منصة إدارة الأكاديمية",
    short_name: "الأكاديمية",
    description: "منصة عربية لإدارة الأكاديميات الرياضية.",
    start_url: "/login",
    display: "standalone",
    background_color: "#f4f7f6",
    theme_color: "#0f766e",
    lang: "ar",
    dir: "rtl",
    icons: [
      {
        src: "/icon.svg",
        sizes: "any",
        type: "image/svg+xml",
        purpose: "any",
      },
      {
        src: "/icon.svg",
        sizes: "any",
        type: "image/svg+xml",
        purpose: "maskable",
      },
    ],
  };
}
