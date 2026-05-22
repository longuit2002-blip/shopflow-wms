import { describe, it, expect } from 'vitest';
import {
  MODULES,
  ORPHAN_KEYS,
  OWNER_CRITICAL_KEYS,
  PERMISSION_KEYS,
} from './admin';

/**
 * Sprint-10.5 U1 — structural pin for the permission catalog.
 *
 * Sprint-9.5 U7 originally shipped this catalog but it had drifted ~9
 * entries from the backend `ShopFlow.SharedKernel.Authorization
 * .PermissionKeys` source of truth. Sprint-10 then attached per-action
 * `[Authorize(Policy=...)]` filters to 33 controller actions using the
 * canonical names, making the drift a privilege-escalation vector once
 * Sprint-11's non-Owner role lands (a key the editor offers but the
 * backend never references can never be granted; a key the backend
 * references but the editor never offers can never be revoked through
 * UI).
 *
 * This test pins the catalog shape so any further drift is caught at
 * `npm test` time, not at first non-Owner login.
 *
 * KTD2 — exact 24-key count + per-module count + OwnerCritical mirror
 * + ORPHAN_KEYS content.
 */

describe('admin permission catalog (Sprint-10.5 U1)', () => {
  describe('PERMISSION_KEYS', () => {
    it('Length_AfterSprint10_5Catalog_Has24Entries', () => {
      expect(PERMISSION_KEYS).toHaveLength(24);
    });

    it('AuthModule_Sprint10_5Catalog_Has9Keys', () => {
      expect(PERMISSION_KEYS.filter((p) => p.module === 'Auth')).toHaveLength(9);
    });

    it('InventoryModule_Sprint10_5Catalog_Has5Keys', () => {
      expect(PERMISSION_KEYS.filter((p) => p.module === 'Inventory')).toHaveLength(5);
    });

    it('OutboundModule_Sprint10_5Catalog_Has6Keys', () => {
      expect(PERMISSION_KEYS.filter((p) => p.module === 'Outbound')).toHaveLength(6);
    });

    it('InboundModule_Sprint10_5Catalog_Has3Keys', () => {
      expect(PERMISSION_KEYS.filter((p) => p.module === 'Inbound')).toHaveLength(3);
    });

    it('HubModule_Sprint10_5Catalog_Has1Key', () => {
      expect(PERMISSION_KEYS.filter((p) => p.module === 'Hub')).toHaveLength(1);
    });

    it('Entries_Sprint10_5Catalog_AllHaveKeyAndModuleFields', () => {
      for (const entry of PERMISSION_KEYS) {
        expect(typeof entry.key).toBe('string');
        expect(entry.key.length).toBeGreaterThan(0);
        expect(typeof entry.module).toBe('string');
        expect(entry.module.length).toBeGreaterThan(0);
      }
    });

    it('Modules_Sprint10_5Catalog_AllReferenceKnownModule', () => {
      for (const entry of PERMISSION_KEYS) {
        expect(MODULES).toContain(entry.module);
      }
    });

    it('Keys_Sprint10_5Catalog_IncludesOutboundOrdersCancel', () => {
      expect(PERMISSION_KEYS.map((p) => p.key)).toContain('outbound.orders.cancel');
    });

    it('Keys_Sprint10_5Catalog_IncludesHubConnect', () => {
      expect(PERMISSION_KEYS.map((p) => p.key)).toContain('hub.connect');
    });

    it('Keys_Sprint10_5Catalog_DoesNotIncludeDriftedSprint9_5Keys', () => {
      const keys = PERMISSION_KEYS.map((p) => p.key);
      // Old drifted names that must NOT survive the rewrite.
      expect(keys).not.toContain('auth.admin.users.update');
      expect(keys).not.toContain('inventory.flash-sale.toggle');
      expect(keys).not.toContain('inventory.skus.create');
      expect(keys).not.toContain('inventory.skus.update');
      expect(keys).not.toContain('outbound.orders.confirm-pick');
      expect(keys).not.toContain('outbound.orders.confirm-pack');
      expect(keys).not.toContain('outbound.orders.confirm-ship');
      expect(keys).not.toContain('outbound.orders.mark-pick-failed');
      expect(keys).not.toContain('inbound.pos.create');
      expect(keys).not.toContain('inbound.receiving.confirm');
      expect(keys).not.toContain('hub.tenant.read');
      expect(keys).not.toContain('hub.tenant.write');
      expect(keys).not.toContain('notification.dlq.read');
    });
  });

  describe('OWNER_CRITICAL_KEYS', () => {
    it('Length_Sprint10_5Mirror_Has9Entries', () => {
      expect(OWNER_CRITICAL_KEYS).toHaveLength(9);
    });

    it('Entries_Sprint10_5Mirror_AllStartWithAuthAdminPrefix', () => {
      for (const key of OWNER_CRITICAL_KEYS) {
        expect(key.startsWith('auth.admin.')).toBe(true);
      }
    });

    it('Entries_Sprint10_5Mirror_AllExistInPermissionKeys', () => {
      const keys = PERMISSION_KEYS.map((p) => p.key);
      for (const key of OWNER_CRITICAL_KEYS) {
        expect(keys).toContain(key);
      }
    });
  });

  describe('MODULES', () => {
    it('Value_Sprint10_5Catalog_IsExactly5Entries', () => {
      expect(MODULES).toEqual(['Auth', 'Inventory', 'Outbound', 'Inbound', 'Hub']);
    });

    it('Value_Sprint10_5Catalog_DoesNotIncludeNotification', () => {
      expect(MODULES).not.toContain('Notification');
    });
  });

  describe('ORPHAN_KEYS', () => {
    it('Value_Sprint10_5Catalog_IsExactlyOutboundOrdersCancel', () => {
      expect(ORPHAN_KEYS).toEqual(['outbound.orders.cancel']);
    });

    it('Entries_Sprint10_5Catalog_AllExistInPermissionKeys', () => {
      const keys = PERMISSION_KEYS.map((p) => p.key);
      for (const key of ORPHAN_KEYS) {
        expect(keys).toContain(key);
      }
    });

    it('HubConnect_Sprint10_5Catalog_IsNotOrphanPostU3', () => {
      // hub.connect is catalogued + Sprint-10.5 U3 attaches the policy
      // to the SignalR connect handshake; therefore it is NOT orphan.
      expect(ORPHAN_KEYS).not.toContain('hub.connect');
    });
  });
});
