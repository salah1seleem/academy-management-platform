import {afterEach,expect,it,vi} from 'vitest';
import {cleanup,fireEvent,render,screen} from '@testing-library/react';
import {GuardianContact,validGuardianChat} from './guardian-contact';
afterEach(()=>{cleanup();vi.restoreAllMocks();});
it('only opens official bounded links',()=>{expect(validGuardianChat('https://wa.me/201000000001?text=hello')).not.toBeNull();expect(validGuardianChat('https://wa.me.evil.test/201000000001')).toBeNull();expect(validGuardianChat('javascript:alert(1)')).toBeNull();});
it('loads the player scoped contacts and leaves the actual conversation to an explicit link',async()=>{
 global.fetch=vi.fn(async()=>Response.json({contacts:[{id:'a',name:'ولي خيالي أ',phone:'+201000000001',url:'https://wa.me/201000000001?text=hello'},{id:'b',name:'ولي خيالي ب',phone:'+201000000002',url:'https://wa.me/201000000002?text=hello'}]}));render(<GuardianContact playerId="child-a"/>);expect(fetch).not.toHaveBeenCalled();fireEvent.click(screen.getByRole('button',{name:'التواصل مع ولي الأمر عبر واتساب'}));await screen.findByText('اختر ولي الأمر للتواصل');expect(fetch).toHaveBeenCalledWith('/api/v1/people/players/child-a/guardian-contacts',expect.objectContaining({cache:'no-store'}));expect(screen.getByRole('link',{name:/ولي خيالي ب.*فتح المحادثة/})).toHaveAttribute('href','https://wa.me/201000000002?text=hello');
});
it('has a helpful missing-contact state and no guessed link',async()=>{
 global.fetch=vi.fn(async()=>Response.json({contacts:[{id:'a',name:'ولي خيالي',phone:null,url:null}]}));render(<GuardianContact guardianId="guardian-a"/>);fireEvent.click(screen.getByRole('button',{name:'التواصل عبر واتساب'}));await screen.findByText(/لا يوجد رقم صحيح/);expect(screen.queryByRole('link',{name:/فتح المحادثة/})).not.toBeInTheDocument();
});
