/**
 * Pill / status badge primitive — wraps the canon `.pill` CSS class with
 * optional semantic kind. Used for tenant chip, KPI status, sidebar
 * counts (when stylised), and ComingSoon target-sprint labels.
 */

import type { HTMLAttributes, ReactNode } from 'react';

export type PillKind = 'default' | 'ok' | 'warn' | 'bad' | 'info' | 'accent';

export interface PillProps extends HTMLAttributes<HTMLSpanElement> {
  kind?: PillKind;
  children: ReactNode;
}

const KIND_CLASS: Record<PillKind, string> = {
  default: '',
  ok: 'ok',
  warn: 'warn',
  bad: 'bad',
  info: 'info',
  accent: 'accent',
};

export function Pill({ kind = 'default', className, children, ...rest }: PillProps) {
  const classes = ['pill', KIND_CLASS[kind], className].filter(Boolean).join(' ');
  return (
    <span className={classes} {...rest}>
      {children}
    </span>
  );
}
