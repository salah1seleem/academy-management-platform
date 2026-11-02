import {defineConfig} from '@playwright/test';
// Explicit existing isolated acceptance servers; never start/reset the manual Demo.
if(process.env.ACADEMY_V41_ISOLATED!=='true')throw Error('Explicit isolated V41 acceptance required');
export default defineConfig({testDir:'./refinement',workers:1,fullyParallel:false,retries:0,timeout:180000,expect:{timeout:15000},reporter:'line',outputDir:'../../tmp/v41-web-results',use:{baseURL:'http://127.0.0.1:3260',viewport:{width:1440,height:1000},trace:'retain-on-failure'}});
