import * as Dialog from '@radix-ui/react-dialog';
import { X, Flame, LoaderCircle } from 'lucide-react';
import { useState } from 'react';
import type { ReactNode, FormEvent } from 'react';

export function Brand() { return <div className="brand"><Flame size={25} fill="currentColor"/><span>hearth<span className="brand-dot">.</span></span></div>; }
export function Avatar({ name, online, small = false }: { name: string; online?: boolean; small?: boolean }) {
  const hue = [...name].reduce((v, c) => v + c.charCodeAt(0), 0) % 360;
  return <span className={`avatar ${small ? 'small' : ''}`} style={{ background: `hsl(${hue} 22% 29%)`, color: `hsl(${hue} 50% 87%)` }}>{name.slice(0, 2).toUpperCase()}{online !== undefined && <i className={online ? 'online' : ''}/>}</span>;
}
export function Modal({ title, description, children, onClose, wide = false }: { title: string; description?: string; children: ReactNode; onClose: () => void; wide?: boolean }) {
  return <Dialog.Root open onOpenChange={open => { if (!open) onClose(); }}><Dialog.Portal><Dialog.Overlay className="modal-overlay"/><Dialog.Content className={`modal ${wide ? 'wide' : ''}`}><Dialog.Title>{title}</Dialog.Title><Dialog.Description>{description || 'Manage your space on Hearth.'}</Dialog.Description><Dialog.Close className="icon-button modal-close" aria-label="Close"><X size={20}/></Dialog.Close>{children}</Dialog.Content></Dialog.Portal></Dialog.Root>;
}
export function ErrorNotice({ error }: { error: unknown }) { return error ? <div className="error" role="alert">{error instanceof Error ? error.message : String(error)}</div> : null; }
export function Loading() { return <div className="loading" role="status"><LoaderCircle className="spin" size={20}/> Getting things ready…</div>; }
export function ActionForm({ children, action, label = 'Save changes', onSuccess, danger = false }: { children?: ReactNode; action: (form: FormData) => Promise<unknown>; label?: string; onSuccess?: () => void; danger?: boolean }) {
  const [error, setError] = useState<unknown>(); const [busy, setBusy] = useState(false);
  async function submit(e: FormEvent<HTMLFormElement>) { e.preventDefault(); setBusy(true); setError(undefined); try { await action(new FormData(e.currentTarget)); onSuccess?.(); } catch (e) { setError(e); } finally { setBusy(false); } }
  return <form onSubmit={submit} className="form-stack">{children}<ErrorNotice error={error}/><button className={danger ? 'button danger' : 'button primary'} disabled={busy}>{busy ? 'Working…' : label}</button></form>;
}
export function Field({ label, name, type = 'text', value, required = true, minLength, maxLength }: { label: string; name: string; type?: string; value?: string; required?: boolean; minLength?: number; maxLength?: number }) {
  return <label className="field">{label}<input name={name} type={type} defaultValue={value} required={required} minLength={minLength} maxLength={maxLength} autoComplete={name === 'password' ? 'current-password' : name === 'email' ? 'email' : 'off'}/></label>;
}
