import { render, screen, waitFor } from "@testing-library/react";
import { startTransition, useEffect, useState } from "react";
import { describe, expect, it, vi } from "vitest";

import { useAfterPendingTransitions } from "./pendingTransitions";

interface Controls {
  afterPendingTransitions: (callback: () => void) => void;
  show: (text: string) => void;
}

/** Renders a line of text and hands back the hook, plus a way to change the text. */
function renderText() {
  let controls: Controls | undefined;
  function Text() {
    const [text, setText] = useState("Before");
    const afterPendingTransitions = useAfterPendingTransitions();
    useEffect(() => {
      controls = { afterPendingTransitions, show: setText };
    });
    return <p>{text}</p>;
  }

  const view = render(<Text />);
  return { ...view, controls: controls! };
}

describe("useAfterPendingTransitions", () => {
  it("calls back once a transition started earlier has rendered", async () => {
    const { container, controls } = renderText();
    let shownWhenCalled: string | null = null;

    startTransition(() => controls.show("After"));
    controls.afterPendingTransitions(() => {
      shownWhenCalled = container.textContent;
    });

    await waitFor(() => expect(shownWhenCalled).toBe("After"));
  });

  it("renders the callback's updates before anything can see the render before them", async () => {
    const { container, controls } = renderText();
    const shown: (string | null)[] = [];
    const observer = new MutationObserver(() =>
      shown.push(container.textContent),
    );
    observer.observe(container, {
      characterData: true,
      childList: true,
      subtree: true,
    });

    startTransition(() => controls.show("After"));
    controls.afterPendingTransitions(() => controls.show("Final"));

    expect(await screen.findByText("Final")).toBeInTheDocument();
    observer.disconnect();
    expect(shown).toEqual(["Final"]);
  });

  it("calls back when the component unmounts before rendering", () => {
    const { controls, unmount } = renderText();
    const callback = vi.fn();

    controls.afterPendingTransitions(callback);
    unmount();

    expect(callback).toHaveBeenCalledOnce();
  });

  it("calls back at once when asked after the component unmounted", () => {
    const { controls, unmount } = renderText();
    const callback = vi.fn();

    unmount();
    controls.afterPendingTransitions(callback);

    expect(callback).toHaveBeenCalledOnce();
  });
});
