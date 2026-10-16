import {expect,it,vi} from "vitest";
import type {APIRequestContext} from "@playwright/test";
import {demoAuthPost} from "../../../../tests/e2e/support/mobile-demo-session";
it("waits only on actual shared auth throttling and still returns the real success response",async()=>{
  const statuses=[429,429,202],pause=vi.fn(async()=>{}),post=vi.fn(async()=>{const status=statuses.shift()!;return{status:()=>status};});
  const r=await demoAuthPost({post} as unknown as APIRequestContext,"/otp",{phoneNumber:"synthetic"},pause);
  expect(r.status()).toBe(202);expect(post).toHaveBeenCalledTimes(3);expect(pause.mock.calls).toEqual([[31000],[31000]]);
});
it("never treats exhaustion, validation failure or a server error as authentication success",async()=>{
  for(const status of [429,422,500]){const pause=vi.fn(async()=>{}),post=vi.fn(async()=>({status:()=>status}));const r=await demoAuthPost({post} as unknown as APIRequestContext,"/otp",{},pause);expect(r.status()).toBe(status);expect(post).toHaveBeenCalledTimes(status===429?3:1);expect(pause).toHaveBeenCalledTimes(status===429?2:0);}
});
