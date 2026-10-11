import type { MetadataRoute } from "next";

export default function manifest(): MetadataRoute.Manifest {
  return {
    name: "The Professional Academy",
    short_name: "الأكاديمية",
    description: "منصة عربية لإدارة الأكاديميات الرياضية.",
    start_url: "/login",
    display: "standalone",
    background_color: "#171923",
    theme_color: "#D71935",
    lang: "ar",
    dir: "rtl",
    icons: [
      {
        src: "/branding/icon-192.png",
        sizes: "192x192",
        type: "image/png",
        purpose: "any",
      },
      {
        src: "/branding/icon-512.png",
        sizes: "512x512",
        type: "image/png",
        purpose: "maskable",
      },
    ],
  };
}
