import { useEffect, useState } from 'react';
import { Link, useNavigate, useSearchParams } from 'react-router-dom';
import { useQueryClient } from '@tanstack/react-query';
import { ArrowUpRight, MessageCircle, Sparkles, ShieldCheck } from 'lucide-react';
import { ActionForm, Brand, Field } from '../components/ui';
import { clearCsrf, send } from '../api/client';

export function Auth({ mode }: { mode: 'login' | 'register' | 'forgot' | 'verify' | 'reset' | 'resend' }) {
  const [message, setMessage] = useState(''); const [twoFactor, setTwoFactor] = useState(false);
  const navigate = useNavigate(); const cache = useQueryClient(); const [params] = useSearchParams();
  useEffect(() => { setMessage(''); setTwoFactor(false); }, [mode]);
  const titles = { login: 'Good to have you back.', register: 'Find your people.', forgot: 'Let’s get you back in.', verify: 'One last hello.', reset: 'A fresh start.', resend: 'Check your inbox.' };
  const subtitles = { login: 'Your conversations are right where you left them.', register: 'A place for your friends, projects, and everything in between.', forgot: 'We’ll email you a secure password reset link.', verify: 'Confirm your email address to make yourself at home.', reset: 'Choose a new password of at least 12 characters.', resend: 'Send another account verification email.' };
  async function submit(form: FormData) {
    const body = Object.fromEntries(form.entries());
    if (mode === 'login') {
      const result = await send<{ requiresTwoFactor: boolean }>('/auth/login', body);
      if (result.requiresTwoFactor) { setTwoFactor(true); return; }
      clearCsrf(); await cache.invalidateQueries({ queryKey: ['me'] }); navigate(sessionStorage.getItem('invite') ? `/invite/${sessionStorage.getItem('invite')}` : '/app');
    } else if (mode === 'register') { await send('/auth/register', body); setMessage('Check your inbox for a verification link. Then come back to sign in.'); }
    else if (mode === 'verify') { await send('/auth/verify', { userId: params.get('userId'), token: params.get('token') }); setMessage('Your email is verified. You can now sign in.'); }
    else if (mode === 'reset') { await send('/auth/reset', { ...body, userId: params.get('userId'), token: params.get('token') }); clearCsrf(); setMessage('Password reset. Sign in with your new password.'); }
    else { await send(`/auth/${mode}`, body); setMessage('If an eligible account exists, an email is on its way.'); }
  }
  return <main className="auth-page"><section className="auth-story"><Brand/><div className="auth-story-content"><div className="eyebrow"><span className="tiny-dot"/> A LITTLE CLOSER, WHEREVER YOU ARE</div><h1>Good company.<br/>Great conversations.</h1><p>A space that feels like yours. Bring your people together and let the conversation happen.</p><div className="conversation-art" aria-hidden="true"><div className="art-channel"># the-good-stuff <Sparkles size={18}/></div><div className="art-message"><span className="art-avatar">JD</span><div><b>Jamie <small>Today at 10:42</small></b><p>Same time, same place? ☕</p></div></div><div className="art-message"><span className="art-avatar purple">AL</span><div><b>Alex <small>Today at 10:43</small></b><p>Wouldn’t miss it.</p><span className="art-reaction">❤️ &nbsp;3</span></div></div><div className="art-typing"><i/><i/><i/> Someone’s got something to say</div></div></div><footer><span><ShieldCheck size={14}/> Your space. Your people.</span><span>BUILT FOR CONNECTION</span></footer></section><section className="auth-panel"><div className="auth-mobile-brand"><Brand/></div><div className="auth-box"><span className="auth-symbol"><MessageCircle size={28}/></span><h2>{titles[mode]}</h2><p className="muted">{subtitles[mode]}</p>{message ? <div className="success" role="status">{message}<Link className="button primary" to="/login">Go to sign in <ArrowUpRight size={17}/></Link></div> : <ActionForm key={mode} action={submit} label={mode === 'login' ? 'Sign in to Hearth →' : mode === 'register' ? 'Create your account →' : mode === 'verify' ? 'Verify my email' : mode === 'reset' ? 'Update password' : 'Send email'}>{mode === 'register' && <Field label="Username" name="userName" minLength={3} maxLength={24}/>} {['login', 'register', 'forgot', 'resend'].includes(mode) && <Field label="Email address" name="email" type="email"/>}{['login', 'register', 'reset'].includes(mode) && <Field label="Password" name="password" type="password" minLength={mode === 'login' ? undefined : 12} maxLength={128}/>} {twoFactor && <><Field label="Authenticator code" name="code" required={false}/><Field label="Or a recovery code" name="recoveryCode" required={false}/></>}{mode === 'login' && <Link className="small-link" to="/forgot">Forgot your password?</Link>}</ActionForm>}<div className="auth-switch">{mode === 'login' ? <>New around here? <Link to="/register">Create an account</Link><br/><Link to="/resend">Resend verification email</Link></> : <Link to="/login">Back to sign in</Link>}</div><p className="auth-note">A comfortable corner of the internet.</p></div></section></main>;
}
