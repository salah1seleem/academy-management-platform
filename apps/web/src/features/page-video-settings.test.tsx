import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, expect, it, vi } from 'vitest';
import { PageVideoSettings } from './page-video-settings';
const video = { role: 'Coach', screen: 'attendance', title: 'شرح الحضور الاصطناعي', url: 'https://video.example.test/attendance.mp4', isActive: true };
afterEach(() => { cleanup(); vi.restoreAllMocks(); });
async function prepare(read: () => Promise<Response>) {
  let gets = 0;
  const requests: unknown[] = [];
  global.fetch = vi.fn((url: unknown, init?: RequestInit) => {
    if (String(url).includes('/auth/csrf')) return Promise.resolve(Response.json({ token: 'test-csrf' }));
    if (init?.method === 'PUT') { requests.push(JSON.parse(String(init.body))); return Promise.resolve(new Response(null, { status: 204 })); }
    return ++gets === 1 ? Promise.resolve(Response.json({ items: [] })) : read();
  }) as typeof fetch;
  render(<PageVideoSettings />);
  await waitFor(() => expect(screen.getByRole('button', { name: 'إضافة فيديو شرح' })).toBeEnabled());
  fireEvent.click(screen.getByRole('button', { name: 'إضافة فيديو شرح' }));
  fireEvent.change(screen.getByLabelText('الدور'), { target: { value: video.role } });
  fireEvent.change(screen.getByLabelText('الصفحة'), { target: { value: video.screen } });
  fireEvent.change(screen.getByLabelText('عنوان الشرح'), { target: { value: ` ${video.title} ` } });
  fireEvent.change(screen.getByLabelText('رابط الفيديو'), { target: { value: video.url } });
  return requests;
}
it('locks the selected role/page and announces success only after exact stored metadata is read', async () => {
  let finish: (response: Response) => void = () => {};
  const saved = vi.fn(); window.addEventListener('academy-save-success', saved);
  try {
    const requests = await prepare(() => new Promise<Response>(resolve => { finish = resolve; }));
    fireEvent.click(screen.getByRole('button', { name: 'حفظ الفيديو' }));
    await screen.findByRole('button', { name: 'جارٍ الحفظ والتحقق…' });
    expect(screen.getByLabelText('الدور')).toBeDisabled(); expect(screen.getByLabelText('عنوان الشرح')).toBeDisabled();
    expect(screen.getByRole('button', { name: 'رجوع للقائمة' })).toBeDisabled(); expect(saved).not.toHaveBeenCalled();
    await waitFor(() => expect(fetch).toHaveBeenCalledTimes(4));
    await act(async () => finish(Response.json({ items: [video] })));
    expect(await screen.findByText('تم حفظ فيديو الشرح والتحقق من إعداداته.')).toBeInTheDocument();
    expect(screen.queryByLabelText('عنوان الشرح')).not.toBeInTheDocument(); expect(requests).toEqual([video]); expect(saved).toHaveBeenCalledTimes(1);
  } finally { window.removeEventListener('academy-save-success', saved); }
});
for (const key of ['role', 'screen', 'title', 'url', 'isActive'] as const) {
  it(`does not confirm a stored video with a mismatched ${key}`, async () => {
    const wrong = { ...video, [key]: key === 'isActive' ? false : 'different' };
    await prepare(() => Promise.resolve(Response.json({ items: [wrong] })));
    fireEvent.click(screen.getByRole('button', { name: 'حفظ الفيديو' }));
    expect(await screen.findByText('لم تتطابق إعدادات الفيديو المحفوظة. أعد المحاولة بنفس الاختيار أو حدّث القائمة.')).toBeInTheDocument();
    expect(screen.getByLabelText('الصفحة')).toHaveValue('attendance'); expect(screen.getByLabelText('عنوان الشرح')).toHaveValue(` ${video.title} `);
    expect(screen.queryByText('تم حفظ فيديو الشرح والتحقق من إعداداته.')).not.toBeInTheDocument();
  });
}
it('keeps a saved-but-unverified draft for a safe retry and hides technical errors', async () => {
  let reads = 0;
  const requests = await prepare(() => ++reads === 1 ? Promise.reject(new Error('INTERNAL_NETWORK_DETAILS')) : Promise.resolve(Response.json({ items: [video] })));
  fireEvent.click(screen.getByRole('button', { name: 'حفظ الفيديو' }));
  expect(await screen.findByText('تعذر حفظ فيديو الشرح أو التحقق منه. حاول مجددًا.')).toBeInTheDocument();
  expect(screen.queryByText('INTERNAL_NETWORK_DETAILS')).not.toBeInTheDocument();
  fireEvent.click(screen.getByRole('button', { name: 'حفظ الفيديو' }));
  expect(await screen.findByText('تم حفظ فيديو الشرح والتحقق من إعداداته.')).toBeInTheDocument(); expect(requests).toEqual([video, video]);
});
