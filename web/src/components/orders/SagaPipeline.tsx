/**
 * SagaPipeline — Sprint-7 plan U11 / R4 / R7 / R17.
 *
 * Horizontal pipeline visualising the 8 canonical FulfillmentSaga states.
 * Pure presentation: pulls everything from props. No data fetching, no
 * mutations, no router coupling.
 *
 * Canonical state order (8 visible nodes; `Created` + `AwaitingReservation`
 * collapse into the single user-facing label "Placed"):
 *
 *   Placed → Reserved → AwaitingPick → Picked → AwaitingPack
 *           → Packed → AwaitingShip → Shipped
 *
 * The terminal `Cancelled` / mid-flight `CompensatingReservation` states
 * render as a forked failure node with `.saga-step.fail` styling, plus a
 * small caption below the pipeline showing the failure cause (the CLR
 * name of the triggering event, e.g. `StockReservationFailedV1`).
 *
 * Per-node status taxonomy:
 *   - `pending`   — not yet reached.
 *   - `active`    — `currentState === thisNode` (live saga sits here).
 *   - `completed` — saga has moved past this node.
 *   - `fail`      — currentState is Cancelled AND thisNode is where the
 *                   failure occurred (last completed state before the
 *                   `CompensatingReservation` transition).
 *
 * Doc-review design-lens finding #8: `aria-current="step"` requires a
 * list container. The pipeline node list is an `<ol role="list">` with
 * `aria-label="Saga progress"`; the active node carries
 * `aria-current="step"` and no other node does.
 *
 * Reduced-motion: the live-pulse animation on `.saga-step.active .dot`
 * is gated by `@media (prefers-reduced-motion: reduce)` in tokens.css.
 */

import { useLocale } from '../../hooks/useLocale';

/**
 * Wire-shape DTO mirroring `ShopFlow.Outbound.Domain.OrderTransition`
 * (PascalCase per Sprint-6 trade-off #6; Sprint-7+ may normalise to
 * camelCase but the saga audit DTO is read-mostly so keeping the wire
 * shape is cheap).
 */
export type OrderTransitionDto = {
  Id: string;
  OrderId: string;
  FromState: string;
  ToState: string;
  /** ISO 8601 UTC timestamp. */
  OccurredAt: string;
  /** CLR name of the triggering integration event (e.g. `StockReservedV1`). */
  EventType: string;
  CorrelationId: string;
};

export type SagaPipelineProps = {
  /**
   * One of the FulfillmentSaga state names. Includes the eight visible
   * pipeline states + `Created` + `AwaitingReservation` (which collapse
   * into "Placed") + `CompensatingReservation` + `Cancelled`.
   */
  currentState: string;
  /**
   * Append-only audit log of saga transitions, OLDEST FIRST. Used to
   * compute per-node elapsed-time badges and the failure-cause caption.
   */
  transitions: OrderTransitionDto[];
  /**
   * CLR name of the triggering event when state is `Cancelled` or
   * `CompensatingReservation`. Rendered in the failure caption.
   */
  failureCause?: string;
};

/**
 * Visible pipeline nodes in canonical order. The keys are the saga
 * state names that "own" each node — for the first node ("Placed") we
 * use `AwaitingReservation` because `Created` lasts microseconds and
 * the user-facing meaning is "the order is in the system, waiting for
 * the reservation step". The rest map 1:1.
 */
const PIPELINE_NODES = [
  'AwaitingReservation',
  'Reserved',
  'AwaitingPick',
  'Picked',
  'AwaitingPack',
  'Packed',
  'AwaitingShip',
  'Shipped',
] as const;

type PipelineNode = (typeof PIPELINE_NODES)[number];

/** States that collapse into "Placed" on the front-end node. */
const PLACED_ALIASES = new Set(['Created', 'AwaitingReservation']);

/** Terminal/failure states. */
const FAIL_STATES = new Set(['Cancelled', 'CompensatingReservation']);

type NodeStatus = 'pending' | 'active' | 'completed' | 'fail';

interface NodeLabels {
  Placed: string;
  Reserved: string;
  AwaitingPick: string;
  Picked: string;
  AwaitingPack: string;
  Packed: string;
  AwaitingShip: string;
  Shipped: string;
  Cancelled: string;
  FailedAt: string;
}

function labelsFor(lang: 'vi' | 'en'): NodeLabels {
  if (lang === 'en') {
    return {
      Placed: 'Placed',
      Reserved: 'Reserved',
      AwaitingPick: 'Awaiting pick',
      Picked: 'Picked',
      AwaitingPack: 'Awaiting pack',
      Packed: 'Packed',
      AwaitingShip: 'Awaiting ship',
      Shipped: 'Shipped',
      Cancelled: 'Cancelled',
      FailedAt: 'Failed at',
    };
  }
  return {
    Placed: 'Đã đặt',
    Reserved: 'Đã giữ hàng',
    AwaitingPick: 'Chờ soạn',
    Picked: 'Đã soạn',
    AwaitingPack: 'Chờ đóng gói',
    Packed: 'Đã đóng gói',
    AwaitingShip: 'Chờ giao vận',
    Shipped: 'Đã giao',
    Cancelled: 'Đã huỷ',
    FailedAt: 'Lỗi tại',
  };
}

function nodeLabel(node: PipelineNode, labels: NodeLabels): string {
  switch (node) {
    case 'AwaitingReservation':
      return labels.Placed;
    case 'Reserved':
      return labels.Reserved;
    case 'AwaitingPick':
      return labels.AwaitingPick;
    case 'Picked':
      return labels.Picked;
    case 'AwaitingPack':
      return labels.AwaitingPack;
    case 'Packed':
      return labels.Packed;
    case 'AwaitingShip':
      return labels.AwaitingShip;
    case 'Shipped':
      return labels.Shipped;
  }
}

/**
 * Normalise a raw saga state to the pipeline-node it maps to, OR `null`
 * if the state is failure-side / unmapped (Created collapses to
 * AwaitingReservation; Cancelled / CompensatingReservation return null).
 */
function nodeForState(state: string): PipelineNode | null {
  if (PLACED_ALIASES.has(state)) return 'AwaitingReservation';
  return (PIPELINE_NODES as readonly string[]).includes(state)
    ? (state as PipelineNode)
    : null;
}

/**
 * Format an elapsed duration in milliseconds for the per-node badge.
 *
 *   <= 0       → "—"
 *   < 1000     → "< 1s"
 *   < 60_000   → "1.2s"   (one decimal place)
 *   < 3.6e6    → "45m"
 *   >= 3.6e6   → "2h 15m"
 *
 * Pure function; exported for unit-test access.
 */
export function formatElapsed(ms: number | null): string {
  if (ms === null || !Number.isFinite(ms) || ms <= 0) return '—';
  if (ms < 1000) return '< 1s';
  if (ms < 60_000) return `${(ms / 1000).toFixed(1)}s`;
  if (ms < 3_600_000) return `${Math.floor(ms / 60_000)}m`;
  const hours = Math.floor(ms / 3_600_000);
  const minutes = Math.floor((ms % 3_600_000) / 60_000);
  return `${hours}h ${minutes}m`;
}

/**
 * For each pipeline node, compute its elapsed-time badge in ms — the
 * gap between the transition that ENTERED this node and the transition
 * that EXITED it (i.e. the next transition's `OccurredAt`).
 *
 * Active node uses now() − entered-at so the badge is a live elapsed
 * counter; that requires re-render to tick, which the parent's polling
 * will drive (Sprint-7 ships 2s polling, Sprint-8 swaps to SignalR).
 * Returns `null` when there is no enter event for that node yet.
 */
function computeElapsedByNode(
  transitions: OrderTransitionDto[],
  currentNode: PipelineNode | null,
  now: number,
): Record<PipelineNode, number | null> {
  const result: Record<PipelineNode, number | null> = {
    AwaitingReservation: null,
    Reserved: null,
    AwaitingPick: null,
    Picked: null,
    AwaitingPack: null,
    Packed: null,
    AwaitingShip: null,
    Shipped: null,
  };

  // Earliest enter-time per pipeline node.
  const enteredAt: Partial<Record<PipelineNode, number>> = {};
  for (const t of transitions) {
    const node = nodeForState(t.ToState);
    if (node === null) continue;
    const ts = Date.parse(t.OccurredAt);
    if (!Number.isFinite(ts)) continue;
    if (enteredAt[node] === undefined || ts < (enteredAt[node] as number)) {
      enteredAt[node] = ts;
    }
  }

  // For each node with an enter time: elapsed = next-node-enter-time −
  // this-node-enter-time. For the currently active node we use now().
  for (const node of PIPELINE_NODES) {
    const enter = enteredAt[node];
    if (enter === undefined) continue;
    const idx = PIPELINE_NODES.indexOf(node);
    let exit: number | undefined;
    for (let j = idx + 1; j < PIPELINE_NODES.length; j++) {
      const next = enteredAt[PIPELINE_NODES[j]];
      if (next !== undefined) {
        exit = next;
        break;
      }
    }
    if (exit !== undefined) {
      result[node] = exit - enter;
    } else if (currentNode === node) {
      result[node] = now - enter;
    } else {
      result[node] = null;
    }
  }
  return result;
}

/**
 * Identify the pipeline node at which the failure occurred. When the
 * saga has compensated, the last successful node is the one whose
 * `ToState` matched a pipeline node immediately before the
 * `CompensatingReservation` transition.
 */
function failureNode(transitions: OrderTransitionDto[]): PipelineNode | null {
  const idx = transitions.findIndex((t) => t.ToState === 'CompensatingReservation');
  if (idx === -1) {
    // No compensation row yet — the failure point is the last pipeline
    // node entered (could be the initial AwaitingReservation).
    for (let i = transitions.length - 1; i >= 0; i--) {
      const node = nodeForState(transitions[i].ToState);
      if (node !== null) return node;
    }
    return 'AwaitingReservation';
  }
  for (let i = idx - 1; i >= 0; i--) {
    const node = nodeForState(transitions[i].ToState);
    if (node !== null) return node;
  }
  // Compensation fired before any forward transition (atomic-fail
  // path) → the failure visually lives on the first node.
  return 'AwaitingReservation';
}

export function SagaPipeline({
  currentState,
  transitions,
  failureCause,
}: SagaPipelineProps) {
  const { lang } = useLocale();
  const labels = labelsFor(lang);

  const isFailed = FAIL_STATES.has(currentState);
  const currentNode = isFailed ? null : nodeForState(currentState);
  const failNode = isFailed ? failureNode(transitions) : null;

  const now = Date.now();
  const elapsedByNode = computeElapsedByNode(transitions, currentNode, now);

  // Pre-compute which nodes have been entered (touched) so completed
  // status can be derived without re-walking transitions per node.
  const enteredNodes = new Set<PipelineNode>();
  for (const t of transitions) {
    const node = nodeForState(t.ToState);
    if (node !== null) enteredNodes.add(node);
  }

  return (
    <>
      <ol className="saga-pipeline" role="list" aria-label="Saga progress">
        {PIPELINE_NODES.map((node) => {
          const status = computeNodeStatus({
            node,
            currentNode,
            enteredNodes,
            isFailed,
            failNode,
          });
          const elapsedMs = elapsedByNode[node];
          const isActive = status === 'active';
          return (
            <li
              key={node}
              className={`saga-step ${status}`}
              aria-current={isActive ? 'step' : undefined}
              data-testid={`saga-step-${node}`}
              data-status={status}
            >
              <span className="dot" aria-hidden="true" />
              <span className="label">{nodeLabel(node, labels)}</span>
              {elapsedMs !== null && (
                <span className="elapsed" aria-label={`elapsed ${formatElapsed(elapsedMs)}`}>
                  {formatElapsed(elapsedMs)}
                </span>
              )}
            </li>
          );
        })}
      </ol>
      {isFailed && failNode !== null && (
        <div className="saga-failure-caption" data-testid="saga-failure-caption">
          {labels.FailedAt} {nodeLabel(failNode, labels)}
          {failureCause ? ` · ${failureCause}` : ''}
        </div>
      )}
    </>
  );
}

interface NodeStatusContext {
  node: PipelineNode;
  currentNode: PipelineNode | null;
  enteredNodes: Set<PipelineNode>;
  isFailed: boolean;
  failNode: PipelineNode | null;
}

function computeNodeStatus({
  node,
  currentNode,
  enteredNodes,
  isFailed,
  failNode,
}: NodeStatusContext): NodeStatus {
  if (isFailed) {
    if (node === failNode) return 'fail';
    // Anything reached before the failure stays completed; anything
    // after is pending.
    if (failNode === null) return enteredNodes.has(node) ? 'completed' : 'pending';
    const failIdx = PIPELINE_NODES.indexOf(failNode);
    const nodeIdx = PIPELINE_NODES.indexOf(node);
    return nodeIdx < failIdx ? 'completed' : 'pending';
  }
  if (currentNode !== null) {
    if (node === currentNode) return 'active';
    const currentIdx = PIPELINE_NODES.indexOf(currentNode);
    const nodeIdx = PIPELINE_NODES.indexOf(node);
    if (nodeIdx < currentIdx) return 'completed';
    // Node is after current — was it touched by an out-of-order
    // transition (shouldn't happen for forward sagas, but defensive)?
    if (enteredNodes.has(node)) return 'completed';
    return 'pending';
  }
  // currentState didn't map to a pipeline node and we're not failed —
  // treat as fully pending (e.g. unknown / future state).
  return enteredNodes.has(node) ? 'completed' : 'pending';
}
