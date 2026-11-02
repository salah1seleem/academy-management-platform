import {act,cleanup,fireEvent,render,screen} from '@testing-library/react';
import {renderToString} from 'react-dom/server';
import {afterEach,beforeEach,expect,it} from 'vitest';
import {ApprovedWorkspace} from '../components/approved-workspace';
import {saveWebAppearance,useWebAppearance} from './web-appearance';

beforeEach(()=>localStorage.clear());
afterEach(()=>cleanup());
function Choice(){const mode=useWebAppearance();return <button onClick={()=>saveWebAppearance(mode==='dark'?'light':'dark')}>تبديل المظهر</button>;}
it('one explicit profile choice updates every mounted approved surface and survives navigation',()=>{
 const view=render(<><Choice/><ApprovedWorkspace>الأول</ApprovedWorkspace><ApprovedWorkspace>الثاني</ApprovedWorkspace></>);
 fireEvent.click(screen.getByRole('button',{name:'تبديل المظهر'}));
 expect([...view.container.querySelectorAll('[data-approved-surface]')].map(e=>e.getAttribute('data-mode'))).toEqual(['light','light']);
 expect(localStorage.getItem('academy.web.appearance')).toBe('light');
 view.rerender(<ApprovedWorkspace>وجهة جديدة</ApprovedWorkspace>);
 expect(view.container.firstChild).toHaveAttribute('data-mode','light');
});
it('server render remains stable while the browser hydrates its own saved preference',()=>{
 localStorage.setItem('academy.web.appearance','light');
 expect(renderToString(<ApprovedWorkspace>قبول</ApprovedWorkspace>)).toContain('data-mode="dark"');
 const view=render(<ApprovedWorkspace>قبول</ApprovedWorkspace>);
 expect(view.container.firstChild).toHaveAttribute('data-mode','light');
});
it('a storage event updates the same shared preference without a delayed mount effect',()=>{
 const view=render(<ApprovedWorkspace>قبول</ApprovedWorkspace>);
 act(()=>{localStorage.setItem('academy.web.appearance','light');window.dispatchEvent(new StorageEvent('storage',{key:'academy.web.appearance'}));});
 expect(view.container.firstChild).toHaveAttribute('data-mode','light');
});
