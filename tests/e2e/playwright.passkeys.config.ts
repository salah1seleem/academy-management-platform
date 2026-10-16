import { defineConfig } from "@playwright/test";
import base from "./playwright.football.config";
const port=process.env.ACADEMY_E2E_WEB_PORT??"3190";
const servers=Array.isArray(base.webServer)?base.webServer:[base.webServer!];
export default defineConfig({...base,testIgnore:[],testMatch:"v3-staff-passkeys.spec.ts",use:{...base.use,baseURL:`http://localhost:${port}`},webServer:servers.map((server,index)=>index===0?{...server,command:`StaffPasskeys__Enabled=true StaffPasskeys__RpId=localhost StaffPasskeys__Origins__0=http://localhost:${port} ${server.command}`} :server)});
