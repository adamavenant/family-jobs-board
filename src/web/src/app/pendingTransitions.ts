import {
  startTransition,
  useCallback,
  useEffect,
  useLayoutEffect,
  useRef,
  useState,
} from "react";

/**
 * Returns a function that calls back as React renders every transition started
 * before it was called, such as React Router's state updates. It starts a transition
 * of its own and calls back in a layout effect of that render, so whatever the
 * callback updates renders before the browser paints. This relies on two things:
 *
 * - React currently renders pending transitions together (a documented caveat of
 *   `startTransition`), so the new transition can't render before earlier ones.
 * - React Router commits its state in a plain transition. With view transitions, which
 *   this app doesn't use, it commits in a later one that this could render before.
 *
 * The undo test that records every change while an approval is saved guards both, so
 * re-run it when upgrading React or React Router.
 */
export function useAfterPendingTransitions(): (callback: () => void) => void {
  const waiting = useRef(new Map<number, () => void>());
  const lastStarted = useRef(0);
  const unmounted = useRef(false);
  const [rendered, setRendered] = useState(0);

  useLayoutEffect(() => {
    for (const [started, callback] of waiting.current) {
      if (started <= rendered) {
        waiting.current.delete(started);
        callback();
      }
    }
  }, [rendered]);

  // Nothing is left to render once the component unmounts, so no callback waits on it.
  useEffect(() => {
    const pending = waiting.current;
    unmounted.current = false;
    return () => {
      unmounted.current = true;
      pending.forEach((callback) => callback());
      pending.clear();
    };
  }, []);

  return useCallback((callback: () => void) => {
    if (unmounted.current) {
      callback();
      return;
    }

    lastStarted.current += 1;
    const started = lastStarted.current;
    waiting.current.set(started, callback);
    startTransition(() => setRendered(started));
  }, []);
}
