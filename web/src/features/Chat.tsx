import { useEffect, useRef, useState } from 'react';
import type { FormEvent } from 'react';
import { useInfiniteQuery, useQueryClient } from '@tanstack/react-query';
import { ArrowDown, CornerUpLeft, Download, Hash, LockKeyhole, Paperclip, Pencil, Send, SmilePlus, Trash2, X } from 'lucide-react';
import ReactMarkdown from 'react-markdown';
import { api, send } from '../api/client';
import type { Attachment, Channel, Message, MessagePage, User } from '../api/types';
import { ActionForm, Avatar, ErrorNotice, Loading, Modal } from '../components/ui';

export function MessageBody({ content }: { content: string }) {
  return <ReactMarkdown skipHtml allowedElements={['p', 'strong', 'em', 'code', 'pre', 'blockquote', 'ul', 'ol', 'li', 'a', 'br', 'hr']} components={{ a: props => <a href={props.href} target="_blank" rel="noopener noreferrer">{props.children}</a> }}>{content}</ReactMarkdown>;
}
export function Chat({ channel, me, canModerate, typing, onType }: { channel: Channel; me: User; canModerate: boolean; typing: string[]; onType: () => void }) {
  const cache = useQueryClient(); const scroll = useRef<HTMLDivElement>(null); const fileInput = useRef<HTMLInputElement>(null);
  const [text, setText] = useState(''); const [reply, setReply] = useState<Message>(); const [files, setFiles] = useState<Attachment[]>([]);
  const [error, setError] = useState<unknown>(); const [busy, setBusy] = useState(false); const [uploading, setUploading] = useState(false);
  const [edit, setEdit] = useState<Message>(); const [deleting, setDeleting] = useState<Message>(); const [reactionFor, setReactionFor] = useState<number>();
  const [revealed, setRevealed] = useState(new Set<number>()); const retry = useRef<{ key: string; id: string } | undefined>(undefined);
  const lastRead = useRef(0); const previousLast = useRef(0); const atBottom = useRef(true); const [newBelow, setNewBelow] = useState(false);
  const query = useInfiniteQuery({ queryKey: ['messages', channel.id], queryFn: ({ pageParam }) => api<MessagePage>(`/conversations/${channel.id}/messages${pageParam ? `?before=${pageParam}` : ''}`), initialPageParam: undefined as number | undefined, getNextPageParam: last => last.nextCursor ?? undefined, refetchOnWindowFocus: true });
  const messages = [...(query.data?.pages ?? [])].reverse().flatMap(page => page.items);
  const latest = messages.at(-1)?.id ?? 0;
  function bottom() { scroll.current?.scrollTo({ top: scroll.current.scrollHeight, behavior: 'smooth' }); setNewBelow(false); }
  useEffect(() => {
    if (latest !== previousLast.current) {
      if (atBottom.current || previousLast.current === 0) requestAnimationFrame(() => { scroll.current?.scrollTo({ top: scroll.current.scrollHeight }); }); else setNewBelow(true);
      previousLast.current = latest;
    }
  }, [latest]);
  useEffect(() => {
    async function mark() {
      if (latest && latest > lastRead.current && document.visibilityState === 'visible' && atBottom.current) {
        lastRead.current = latest;
        try { await send(`/conversations/${channel.id}/read`, { messageId: latest }, 'PUT'); } catch { lastRead.current = 0; }
      }
    }
    void mark(); const timer = setInterval(() => void mark(), 2500); return () => clearInterval(timer);
  }, [latest, channel.id]);
  async function refresh() { await cache.invalidateQueries({ queryKey: ['messages', channel.id] }); }
  async function perform(action: () => Promise<unknown>) { setError(undefined); try { await action(); await refresh(); } catch (e) { setError(e); } }
  async function submit(e: FormEvent) {
    e.preventDefault(); if ((!text.trim() && !files.length) || busy) return;
    const key = JSON.stringify([text, reply?.id, files.map(f => f.id)]);
    if (retry.current?.key !== key) retry.current = { key, id: crypto.randomUUID() };
    setBusy(true); setError(undefined);
    try { await send(`/conversations/${channel.id}/messages`, { clientId: retry.current.id, content: text, replyToId: reply?.id, attachmentIds: files.map(f => f.id) }); setText(''); setFiles([]); setReply(undefined); retry.current = undefined; atBottom.current = true; await refresh(); }
    catch (e) { setError(e); } finally { setBusy(false); }
  }
  async function upload(selected: FileList | null) {
    if (!selected) return; setUploading(true); setError(undefined);
    try {
      if (files.length + selected.length > 5) throw new Error('Attach up to five files per message.');
      for (const file of selected) { const body = new FormData(); body.append('file', file); const uploaded = await api<Attachment>(`/conversations/${channel.id}/attachments`, { method: 'POST', body }); setFiles(previous => [...previous, uploaded]); }
    } catch (e) { setError(e); } finally { setUploading(false); if (fileInput.current) fileInput.current.value = ''; }
  }
  async function older() { const oldHeight = scroll.current?.scrollHeight ?? 0; await query.fetchNextPage(); requestAnimationFrame(() => { if (scroll.current) scroll.current.scrollTop += scroll.current.scrollHeight - oldHeight; }); }
  return <div className="chat"><div className="message-scroll" ref={scroll} onScroll={() => { const el = scroll.current!; atBottom.current = el.scrollHeight - el.scrollTop - el.clientHeight < 80; if (atBottom.current) setNewBelow(false); }}>
    {query.hasNextPage ? <button className="load-more" disabled={query.isFetchingNextPage} onClick={() => void older()}>Load earlier messages</button> : <div className="channel-welcome"><span className="welcome-hash">{channel.isPrivate ? <LockKeyhole size={32}/> : <Hash size={36}/>}</span><div className="eyebrow">THE START OF SOMETHING GOOD</div><h2>{channel.serverId ? `Welcome to #${channel.name}` : `Your conversation with ${channel.name}`}</h2><p>{channel.serverId ? 'A little space to share ideas, ask questions, and stay connected.' : 'Say hello. The best conversations start small.'}</p><div className="welcome-line"/></div>}
    {query.isLoading && <Loading/>}<ErrorNotice error={query.error}/>
    {messages.map((message, index) => {
      const showDate = index === 0 || new Date(message.createdAt).toDateString() !== new Date(messages[index - 1].createdAt).toDateString();
      const collapse = message.blocked && !revealed.has(message.id);
      return <div key={message.id}>{showDate && <div className="date-divider"><span>{new Date(message.createdAt).toLocaleDateString(undefined, { month: 'long', day: 'numeric', year: 'numeric' })}</span></div>}<article className={`message ${message.deleted ? 'deleted' : ''}`} data-testid="message"><Avatar name={message.authorName}/><div className="message-main"><div className="message-meta"><b>{message.authorName}</b>{message.authorId === me.id && <span className="you-tag">YOU</span>}<time dateTime={message.createdAt}>{new Date(message.createdAt).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}</time>{message.editedAt && !message.deleted && <small>edited</small>}</div>{collapse ? <button className="blocked-message" onClick={() => setRevealed(previous => new Set([...previous, message.id]))}>Blocked message · Click to show</button> : message.deleted ? <p className="muted"><em>This message was deleted.</em></p> : <>{message.replyToId && <div className="reply-preview"><CornerUpLeft size={13}/> Reply to message #{message.replyToId}</div>}<div className="markdown"><MessageBody content={message.content}/></div>{message.attachments.map(file => <a className={`attachment ${file.contentType.startsWith('image/') ? 'image-attachment' : ''}`} key={file.id} href={`/api/v1/attachments/${file.id}`} target="_blank" rel="noreferrer">{file.contentType.startsWith('image/') ? <img src={`/api/v1/attachments/${file.id}`} alt={file.fileName} loading="lazy"/> : <><Download size={20}/><span>{file.fileName}<small>{(file.size / 1024).toFixed(1)} KB</small></span></>}</a>)}<div className="reactions">{message.reactions.map(r => <button key={r.emoji} className={r.mine ? 'mine' : ''} aria-label={`React ${r.emoji}, ${r.count}`} onClick={() => void perform(() => send(`/conversations/${channel.id}/messages/${message.id}/reactions`, { emoji: r.emoji }, 'PUT'))}>{r.emoji} <span>{r.count}</span></button>)}</div></>}</div>{!message.deleted && !collapse && <div className="message-actions"><button aria-label="Reply" title="Reply" onClick={() => setReply(message)}><CornerUpLeft size={15}/></button><button aria-label="Add reaction" title="Add reaction" onClick={() => setReactionFor(reactionFor === message.id ? undefined : message.id)}><SmilePlus size={15}/></button>{message.authorId === me.id && <button aria-label="Edit message" title="Edit" onClick={() => setEdit(message)}><Pencil size={15}/></button>}{(message.authorId === me.id || canModerate) && <button aria-label="Delete message" title="Delete" onClick={() => setDeleting(message)}><Trash2 size={15}/></button>}</div>}{reactionFor === message.id && <div className="emoji-picker">{['👍', '❤️', '😂', '🎉', '👀', '🚀'].map(emoji => <button key={emoji} onClick={() => { setReactionFor(undefined); void perform(() => send(`/conversations/${channel.id}/messages/${message.id}/reactions`, { emoji }, 'PUT')); }}>{emoji}</button>)}</div>}</article></div>;
    })}
  </div>{newBelow && <button className="new-messages" onClick={bottom}>New messages below <ArrowDown size={15}/></button>}<div className="composer-wrap"><ErrorNotice error={error}/>{reply && <div className="replying"><CornerUpLeft size={14}/> Replying to <b>{reply.authorName}</b><button className="icon-button" aria-label="Cancel reply" onClick={() => setReply(undefined)}><X size={15}/></button></div>}{files.length > 0 && <div className="pending-files">{files.map(f => <span key={f.id}>{f.fileName}<button aria-label={`Remove ${f.fileName}`} onClick={() => void perform(async () => { await api(`/attachments/${f.id}`, { method: 'DELETE' }); setFiles(previous => previous.filter(x => x.id !== f.id)); })}><X size={13}/></button></span>)}</div>}<form className="composer" onSubmit={submit}><input ref={fileInput} className="sr-only" type="file" multiple accept="image/png,image/jpeg,image/webp,application/pdf,text/plain" onChange={e => void upload(e.target.files)}/><button type="button" className="icon-button" disabled={busy || uploading} aria-label="Attach file" onClick={() => fileInput.current?.click()}><Paperclip size={21}/></button><textarea value={text} maxLength={4000} rows={1} placeholder={`Message ${channel.serverId ? '#' : '@'}${channel.name}`} aria-label="Message" disabled={busy} onChange={e => { setText(e.target.value); onType(); }} onKeyDown={e => { if (e.key === 'Enter' && !e.shiftKey && !e.nativeEvent.isComposing) { e.preventDefault(); e.currentTarget.form?.requestSubmit(); } }}/><span className="composer-divider"/><button className="send-button" aria-label="Send message" disabled={busy || uploading || (!text.trim() && !files.length)}><Send size={18}/></button></form><div className="composer-caption"><span>{uploading ? 'Uploading…' : typing.length ? `${typing.join(', ')} ${typing.length > 1 ? 'are' : 'is'} typing…` : 'A good conversation starts with a hello.'}</span><span><kbd>Enter</kbd> to send · <kbd>Shift + Enter</kbd> for a new line</span></div></div>
  {edit && <Modal title="Edit message" onClose={() => setEdit(undefined)}><ActionForm action={form => send(`/conversations/${channel.id}/messages/${edit.id}`, { content: form.get('content') }, 'PATCH')} onSuccess={() => { setEdit(undefined); void refresh(); }}><textarea name="content" defaultValue={edit.content} required maxLength={4000} rows={5}/></ActionForm></Modal>}
  {deleting && <Modal title="Delete this message?" description="Its content and attachments will no longer be visible." onClose={() => setDeleting(undefined)}><ActionForm danger label="Delete message" action={() => api(`/conversations/${channel.id}/messages/${deleting.id}`, { method: 'DELETE' })} onSuccess={() => { setDeleting(undefined); void refresh(); }}/></Modal>}
  </div>;
}
