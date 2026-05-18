import { createFileRoute } from '@tanstack/react-router';
import { Plug } from 'lucide-react';
import { ComingSoon } from '../../components/primitives/ComingSoon';
import { t, useLocale } from '../../hooks/useLocale';

export const Route = createFileRoute('/_auth/channels')({
  component: ChannelsStub,
});

function ChannelsStub() {
  useLocale();
  return (
    <ComingSoon
      icon={Plug}
      screen={t('Kênh bán', 'Channels')}
      targetLabel={t('Sprint 8', 'Sprint 8')}
      blurb={t(
        'Kết nối Shopee / Lazada / TikTok Shop / Shopify, mapping SKU, webhook health.',
        'Shopee / Lazada / TikTok Shop / Shopify connections, SKU mapping, webhook health.',
      )}
    />
  );
}
