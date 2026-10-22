/**
 * Runtime helpers shared by every SDK. Types live in index.d.ts and are erased at build time;
 * these four functions are the only runtime surface.
 */

export const ok = (session) => ({ ok: true, session, error: null, nextStep: 'none' });

export const next = (nextStep) => ({ ok: true, session: null, error: null, nextStep });

export const fail = (error) => ({ ok: false, session: null, error, nextStep: 'none' });

export const authError = (code, message, statusCode = null) => ({ code, message, statusCode });
