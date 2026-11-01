"use client";
import {type ReactNode} from 'react';
import {useWebAppearance} from '../lib/web-appearance';

/** Only the explicitly approved web surfaces opt into this visual language. */
export function ApprovedWorkspace({children}:{children:ReactNode}){
 const mode=useWebAppearance();
 return <div data-approved-surface="true" data-mode={mode}>{children}</div>;
}
