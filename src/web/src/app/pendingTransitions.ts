import {
  startTransition,
  useCallback,
  useEffect,
  useRef,
  useState,
} from "react";

/**
 * Returns a function that resolves once React has rendered every transition started
 * before it was called, such as React Router's state updates. It starts a transition
 * of its own and waits for that to render, which relies on two things:
 *
 * - React currently renders pending transitions together (a documented caveat of
 *   `startTransition`), so the new transition can't render before earlier ones.
 * - React Router commits its state in a plain transition. With view transitions, which
 *   this app doesn't use, it commits in a later one that this could render before.
 *
 * The undo test that records every change while an approval is saved guards both, so
 * re-run it when upgrading React or React Router.
 */
export function useAfterPendingTransitions(): () => Promise<void> {
  const waiting = useRef(new Map<number, () => void>());
  const lastStarted = useRef(0);
  const unmounted = useRef(false);
  const [rendered, setRendered] = useState(0);

  useEffect(() => {
    for (const [started, resolve] of waiting.current) {
      if (started <= rendered) {
        waiting.current.delete(started);
        resolve();
      }
    }
  }, [rendered]);

  // Nothing is left to render once the component unmounts, so no wait outlasts it.
  useEffect(() => {
    const pending = waiting.current;
    unmounted.current = false;
    return () => {
      unmounted.current = true;
      pending.forEach((resolve) => resolve());
      pending.clear();
    };
  }, []);

  return useCallback(
    () =>
      new Promise<void>((resolve) => {
        if (unmounted.current) {
          resolve();
          return;
        }

        lastStarted.current += 1;
        const started = lastStarted.current;
        waiting.current.set(started, resolve);
        startTransition(() => setRendered(started));
      }),
    [],
  );
}
