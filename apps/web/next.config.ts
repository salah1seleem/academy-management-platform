import type { NextConfig } from "next";

const nextConfig: NextConfig = {
  distDir: process.env.ACADEMY_WEB_DIST_DIR ?? ".next",
  poweredByHeader: false,
  experimental: { proxyClientMaxBodySize: "55mb" },
  ...(process.env.ACADEMY_WEB_STANDALONE === "true" ? { output: "standalone" as const } : {}),
  async rewrites() {
    const api = process.env.API_INTERNAL_URL ?? "http://127.0.0.1:5080";
    const platformApi = process.env.PLATFORM_API_INTERNAL_URL ?? api;
    const legacyWeb = process.env.ACADEMY_LEGACY_WEB_URL;
    // Local platform delivery can retain the user's frozen testing UI and API.
    if (legacyWeb) return {
      beforeFiles: [
        { source: "/api/v1/platform/:path*", destination: `${platformApi}/api/v1/platform/:path*` },
        { source: "/api/:path((?!v1/platform).*)", destination: `${api}/api/:path` },
        { source: "/:path((?!platform|_next|api).*)", destination: `${legacyWeb}/:path` },
      ],
      fallback: [{ source: "/_next/static/:path*", destination: `${legacyWeb}/_next/static/:path*` }],
    };
    return [{ source: "/api/v1/platform/:path*", destination: `${platformApi}/api/v1/platform/:path*` }, {
      source: "/api/:path*",
      destination: `${api}/api/:path*`,
    }];
  },
};

export default nextConfig;
