import {cleanup,fireEvent,render,screen,waitFor} from '@testing-library/react';
import {afterEach,expect,it,vi} from 'vitest';
import {AcademySettings} from './academy-settings';
let query=new URLSearchParams();
vi.mock('next/navigation',()=>({useSearchParams:()=>query,usePathname:()=>'/dashboard/settings',useRouter:()=>({replace:(url:string)=>{query=new URLSearchParams(url.split('?')[1]);}})}));
afterEach(()=>{cleanup();query=new URLSearchParams();vi.restoreAllMocks();});
it('settings tabs preserve owner-only identity and save/reload the real attendance value',async()=>{
 let enabled=true;const calls:string[]=[];
 global.fetch=vi.fn(async(input,init)=>{const path=String(input);calls.push(path);
  if(path.endsWith('/auth/csrf'))return Response.json({token:'synthetic-csrf'});
  if(path.endsWith('/manage/academy/experience')&&init?.method==='PUT'){enabled=JSON.parse(String(init.body)).coachAttendanceEnabled;return new Response(null,{status:204});}
  if(path.endsWith('/academy/experience'))return Response.json({displayName:'أكاديمية اصطناعية',coachAttendanceEnabled:enabled,currency:'EGP'});
  if(path.endsWith('/my-permissions'))return Response.json(['academy.settings.manage','evaluation.criteria.manage']);
  if(path.endsWith('/me'))return Response.json({role:'AcademyOwner'});
  if(path.endsWith('/structure/options'))return Response.json({branches:[]});
  throw Error('Unexpected '+path);
 }) as typeof fetch;
 const view=render(<AcademySettings/>);await screen.findByRole('tab',{name:'التشغيل والحضور'});
 expect(screen.getByRole('tabpanel')).toHaveTextContent('أكاديمية اصطناعية');expect(screen.queryByLabelText('رفع شعار')).not.toBeInTheDocument();
 fireEvent.click(screen.getByRole('tab',{name:'التشغيل والحضور'}));view.rerender(<AcademySettings/>);
 const toggle=await screen.findByRole('checkbox',{name:'السماح للمدرب بتسجيل حضور اللاعبين من التطبيق'});expect(toggle).toBeChecked();fireEvent.click(toggle);
 await screen.findByText('تم حفظ سياسة حضور المدرب وإعادة قراءتها.');expect(enabled).toBe(false);expect(toggle).not.toBeChecked();
 view.rerender(<AcademySettings key="reload"/>);expect(await screen.findByRole('checkbox',{name:'السماح للمدرب بتسجيل حضور اللاعبين من التطبيق'})).not.toBeChecked();
 expect(calls.filter(p=>p.endsWith('/academy/experience')).length).toBeGreaterThanOrEqual(3);
 fireEvent.click(screen.getByRole('tab',{name:'معايير التقييم'}));view.rerender(<AcademySettings key="reload"/>);expect(screen.getByRole('link',{name:/إدارة معايير التقييم/})).toHaveAttribute('href','/dashboard/evaluations/criteria');
 await waitFor(()=>expect(screen.queryByRole('heading',{name:'قوالب التنبيهات'})).not.toBeInTheDocument());
});
