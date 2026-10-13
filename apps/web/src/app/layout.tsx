import type { Metadata, Viewport } from "next";
import localFont from "next/font/local";
import type { ReactNode } from "react";
import "./globals.css";

// Share the checked-in SIL-OFL Cairo asset with native; no build-time font download.
const cairo = localFont({
  src: "../../../mobile/assets/fonts/Cairo.ttf",
  weight: "400 700",
  variable: "--font-cairo",
  display: "swap",
});

export const metadata: Metadata = {
  applicationName: "منصة إدارة الأكاديمية",
  title: "منصة إدارة الأكاديمية",
  description: "الأساس التقني لمنصة عربية لإدارة الأكاديميات الرياضية.",
  manifest: "/manifest.webmanifest",
  icons: {
    icon: "/branding/icon-192.png",
    apple: "/branding/icon-180.png",
  },
};

export const viewport: Viewport = {
  width: "device-width",
  initialScale: 1,
  themeColor: "#D71935",
};

export default function RootLayout({ children }: Readonly<{ children: ReactNode }>) {
  return (
    <html lang="ar" dir="rtl" className={cairo.variable}>
      <body>{children}</body>
    </html>
  );
}
