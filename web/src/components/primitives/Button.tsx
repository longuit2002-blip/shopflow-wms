/**
 * Button primitive — thin wrapper over the design canon `.btn` CSS class.
 *
 * Variants + sizes map directly to the modifier classes already declared
 * in tokens.css, so the component is a markup convenience rather than a
 * style abstraction. The canon supports primary / accent / danger /
 * ghost variants and sm / lg / xl sizes; defaults render a neutral 32 px
 * white-fill border button.
 */

import { forwardRef, type ButtonHTMLAttributes } from 'react';

export type ButtonVariant = 'default' | 'primary' | 'accent' | 'danger' | 'ghost';
export type ButtonSize = 'sm' | 'default' | 'lg' | 'xl';

export interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: ButtonVariant;
  size?: ButtonSize;
}

const VARIANT_CLASS: Record<ButtonVariant, string> = {
  default: '',
  primary: 'primary',
  accent: 'accent',
  danger: 'danger',
  ghost: 'ghost',
};

const SIZE_CLASS: Record<ButtonSize, string> = {
  sm: 'sm',
  default: '',
  lg: 'lg',
  xl: 'xl',
};

export const Button = forwardRef<HTMLButtonElement, ButtonProps>(function Button(
  { variant = 'default', size = 'default', className, type = 'button', children, ...rest },
  ref,
) {
  const classes = ['btn', VARIANT_CLASS[variant], SIZE_CLASS[size], className]
    .filter(Boolean)
    .join(' ');
  return (
    <button ref={ref} type={type} className={classes} {...rest}>
      {children}
    </button>
  );
});
