import React from 'react';
import ReactDOM from 'react-dom/client';
import { BrowserRouter, Navigate, Route, Routes } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { Auth } from './features/Auth';
import { Workspace, InvitePage } from './features/Workspace';
import './styles.css';

const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false, staleTime: 10_000 }, mutations: { retry: false } } });
ReactDOM.createRoot(document.getElementById('root')!).render(<React.StrictMode><QueryClientProvider client={queryClient}><BrowserRouter><Routes><Route path="/" element={<Navigate to="/app" replace/>}/><Route path="/app/*" element={<Workspace/>}/><Route path="/invite/:token" element={<InvitePage/>}/>{(['login', 'register', 'forgot', 'verify', 'reset', 'resend'] as const).map(mode => <Route key={mode} path={`/${mode}`} element={<Auth mode={mode}/>}/>)}<Route path="*" element={<Navigate to="/app" replace/>}/></Routes></BrowserRouter></QueryClientProvider></React.StrictMode>);
